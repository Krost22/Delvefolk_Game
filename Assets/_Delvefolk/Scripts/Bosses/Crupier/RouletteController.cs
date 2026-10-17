using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Delvefolk
{
    public enum RouletteState { Idle, Preparing, Spinning, Revealing, ApplyingEffect, Cooldown, Disabled }

    // Una habilidad que la ruleta puede activar. La ruleta no sabe nada de su interior: la empieza, la avanza
    // cada frame hasta que diga que ha terminado y la cancela si el combate acaba.
    public interface IRouletteAbility
    {
        string Title { get; }              // rótulo al activarse ("La casa quema")
        bool HoldsDealer { get; }          // mientras sea true, el Crupier no hace otra cosa
        void Begin();
        bool Tick(float dt);               // true = terminada (la ruleta pasa a descanso)
        void Cancel();
    }

    // Conecta cada resultado con su habilidad (por referencia, sin acoplar la ruleta a ninguna)
    public sealed class RouletteAbilityHandler
    {
        readonly Dictionary<RouletteOutcome, IRouletteAbility> map = new Dictionary<RouletteOutcome, IRouletteAbility>();
        public void Connect(RouletteOutcome o, IRouletteAbility a) { if (a == null) map.Remove(o); else map[o] = a; }
        public void Disconnect(RouletteOutcome o) { map.Remove(o); }
        public bool IsConnected(RouletteOutcome o) { return map.ContainsKey(o); }
        public IRouletteAbility Get(RouletteOutcome o) { IRouletteAbility a; return map.TryGetValue(o, out a) ? a : null; }
    }

    // Secuencia de una tirada: Idle → Preparing (anuncio) → Spinning → Revealing (pausa) → ApplyingEffect → Cooldown → Idle.
    // El resultado se elige ANTES de girar y la rueda se detiene en su segmento. Lo avanza el bucle del juego (Tick),
    // así que es determinista con la semilla de la partida y no deja coroutines colgando.
    public sealed class RouletteController
    {
        readonly RouletteConfig cfg;
        readonly RouletteWheel wheel;
        readonly Func<float> rand;
        public readonly RouletteAbilityHandler Abilities = new RouletteAbilityHandler();
        public RouletteState State { get; private set; }
        public float StateT { get; private set; }
        public int ResultIndex { get; private set; }
        public RouletteOutcome Result { get; private set; }
        public IRouletteAbility Active { get; private set; }
        public int Spins, AbilitiesFired, Mismatches;
        public float CooldownMult = 1f;
        float spinDur;
        bool fired;

        // Herramientas de prueba (no las toca la partida normal)
        public RouletteOutcome? TestForce;
        public bool TestNoAbility;

        public event Action<RouletteState> StateChanged;
        public event Action<RouletteOutcome> Announced;     // empieza a girar (resultado ya decidido, aún oculto)
        public event Action<RouletteOutcome> Revealed;      // la rueda se ha detenido en él
        public event Action<RouletteOutcome, IRouletteAbility> AbilityStarted;
        public event Action Refused;                        // todos los pesos a 0

        public RouletteController(RouletteConfig config, RouletteWheel wheel, Func<float> rand01)
        {
            cfg = config; this.wheel = wheel; rand = rand01;
            State = RouletteState.Idle;
        }

        public bool IsIdle { get { return State == RouletteState.Idle; } }
        public bool DealerBusy
        {
            get
            {
                return State == RouletteState.Preparing || State == RouletteState.Spinning || State == RouletteState.Revealing
                    || (State == RouletteState.ApplyingEffect && Active != null && Active.HoldsDealer);
            }
        }

        void Set(RouletteState s)
        {
            State = s; StateT = 0f;
            if (StateChanged != null) StateChanged(s);
        }

        // Pide una tirada. Falla si ya hay una en curso, en descanso, desactivada o sin pesos
        public bool TrySpin()
        {
            if (State != RouletteState.Idle) return false;
            var w = RouletteMath.Weights(cfg.Segments);
            int idx = RouletteMath.Pick(w, rand());
            int forced = Forced();
            if (forced >= 0) idx = forced;
            if (idx < 0)
            {
                Debug.LogWarning("[Ruleta] Todos los pesos son 0 (o negativos): no se gira. Revisa los pesos en Resources/CrupierRoulette.");
                if (Refused != null) Refused();
                Set(RouletteState.Cooldown);
                return false;
            }
            ResultIndex = idx;
            Result = cfg.Segments[idx].Outcome;
            Spins++;
            fired = false;
            Active = null;
            if (cfg.LogResults) Debug.Log(Describe(w, forced >= 0));
            int turns = Mathf.FloorToInt(Mathf.Lerp(cfg.MinTurns, cfg.MaxTurns + 0.999f, rand()));
            float target = RouletteMath.Target(wheel.Forward, wheel.AngleOf(idx), cfg.PointerAngle, wheel.Sign, Mathf.Clamp(turns, cfg.MinTurns, Mathf.Max(cfg.MinTurns, cfg.MaxTurns)));
            spinDur = cfg.InitialSpeed > 0f
                ? Mathf.Clamp((target - wheel.Forward) * RouletteMath.StartSlope(cfg.Deceleration) / cfg.InitialSpeed, 0.5f, 12f)
                : cfg.SpinDuration;
            pendingTarget = target;
            wheel.Highlight(-1);
            wheel.Show(true);
            Set(RouletteState.Preparing);
            return true;
        }
        float pendingTarget;

        int Forced()
        {
            if (TestForce.HasValue) return cfg.IndexOf(TestForce.Value);
            if (cfg.ForceAllowed) return cfg.IndexOf(cfg.ForcedOutcome);
            return -1;
        }

        string Describe(float[] w, bool forced)
        {
            var p = RouletteMath.Probabilities(cfg.Segments);
            var sb = new StringBuilder("[Ruleta] Tirada ").Append(Spins).Append(": ");
            for (int i = 0; i < w.Length; i++)
                sb.Append(cfg.Segments[i].Outcome).Append(' ').Append(w[i].ToString("0.##")).Append(" (").Append((p[i] * 100f).ToString("0.#")).Append("%)").Append(i < w.Length - 1 ? ", " : "");
            sb.Append(" → ").Append(Result);
            if (forced) sb.Append("  [PRUEBA: resultado forzado]");
            return sb.ToString();
        }

        public void Tick(float dt)
        {
            StateT += dt;
            switch (State)
            {
                case RouletteState.Preparing:
                    // Coge impulso: retrocede un poco y vuelve
                    float k = Mathf.Clamp01(StateT / Mathf.Max(0.01f, cfg.PrepareTime));
                    wheel.Windup = -18f * Mathf.Sin(k * Mathf.PI);
                    if (StateT >= cfg.PrepareTime)
                    {
                        wheel.Windup = 0f;
                        wheel.StartSpin(pendingTarget, spinDur);
                        if (Announced != null) Announced(Result);
                        Set(RouletteState.Spinning);
                    }
                    break;
                case RouletteState.Spinning:
                    if (wheel.Tick(dt))
                    {
                        int seen = wheel.IndexUnderPointer();
                        if (seen != ResultIndex)
                        {
                            Mismatches++;
                            Debug.LogError("[Ruleta] El indicador señala " + (seen >= 0 ? cfg.Segments[seen].Outcome.ToString() : "nada") + " pero el resultado es " + Result
                                + ". Revisa PointerAngle, los ángulos de los segmentos, el eje y la orientación del modelo.");
                        }
                        wheel.Highlight(ResultIndex);
                        if (Revealed != null) Revealed(Result);
                        Set(RouletteState.Revealing);
                    }
                    break;
                case RouletteState.Revealing:
                    if (StateT >= cfg.RevealPause) Apply();
                    break;
                case RouletteState.ApplyingEffect:
                    bool done = Active == null || Active.Tick(dt);
                    if (!done && StateT >= cfg.MaxEffectTime)
                    {
                        Debug.LogWarning("[Ruleta] La habilidad de " + Result + " no terminó en " + cfg.MaxEffectTime + " s: se cancela.");
                        Active.Cancel();
                        done = true;
                    }
                    if (done) { Active = null; Set(RouletteState.Cooldown); }
                    break;
                case RouletteState.Cooldown:
                    if (StateT >= 0.6f && wheel.Visible) wheel.Show(false);
                    if (StateT >= cfg.Cooldown * CooldownMult) Set(RouletteState.Idle);
                    break;
            }
        }

        // Activa la habilidad del resultado (una sola vez por tirada)
        void Apply()
        {
            if (fired) return;
            fired = true;
            wheel.Show(false);
            if (TestNoAbility || cfg.SpinWithoutAbility)
            {
                if (cfg.LogResults) Debug.Log("[Ruleta][PRUEBA] Tirada sin habilidad: " + Result);
                Set(RouletteState.Cooldown);
                return;
            }
            var a = Abilities.Get(Result);
            if (a == null)
            {
                Debug.LogWarning("[Ruleta] El resultado " + Result + " no tiene habilidad conectada: se pasa al descanso.");
                Set(RouletteState.Cooldown);
                return;
            }
            Active = a;
            AbilitiesFired++;
            Set(RouletteState.ApplyingEffect);
            try { a.Begin(); }
            catch (Exception e) { Debug.LogException(e); Active = null; Set(RouletteState.Cooldown); return; }
            if (AbilityStarted != null) AbilityStarted(Result, a);
        }

        // Corta la tirada en curso y su habilidad (muere el jefe, se descarga el piso). disable: no vuelve a girar
        public void Cancel(bool disable)
        {
            if (Active != null) { var a = Active; Active = null; a.Cancel(); }
            wheel.Stop();
            wheel.Windup = 0f;
            wheel.HideNow();
            Set(disable ? RouletteState.Disabled : RouletteState.Idle);
        }
    }
}
