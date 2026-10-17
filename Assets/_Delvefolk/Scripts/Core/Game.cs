using System;
using UnityEngine;

namespace Delvefolk
{
    public enum GameState { Title, Lobby, Play, Paused, Bonus, Transition, Dead, Win }

    // Orquestador (DF.Game): bucle principal, estados, jugadores, carga de pisos, salas e interacción.
    // Todo temporizador de jugabilidad cuenta tiempo de juego (el dt de Frame).
    public sealed partial class Game : IDisposable
    {
        public readonly GameLoop Host;
        public readonly InputHub Input = new InputHub();
        // Aleatoriedad de juego: determinista con la semilla (pruebas reproducibles)
        public readonly Rng Rand;
        public readonly AudioSynth Audio = new AudioSynth();

        public GameState State = GameState.Title;
        public int Floor = 1;
        public float Time;
        public float Hitstop;
        public int NumPlayers = 1;
        public int FrameCount;

        // Raíz de la escena del juego (todo lo creado por código cuelga de aquí)
        public readonly Transform SceneRoot;

        // Fundido entre escenas (tiempo real)
        Action pendingTransition;
        float transitionLeft;
        // Las pruebas pueden ejecutar las transiciones al instante
        public bool InstantTransitions;

        public Game(GameLoop host, uint seed)
        {
            Host = host;
            Rand = new Rng(seed);
            SceneRoot = new GameObject("Delvefolk").transform;
            if (host) SceneRoot.SetParent(host.transform, false);
            Input.PadDisconnected += OnPadDisconnect;
            Init();
        }

        // Inicialización ampliada por los demás archivos de la clase (escena, piso de fondo...)
        partial void Init();
        partial void OnDispose();

        public void Dispose()
        {
            OnDispose();
            if (SceneRoot) UnityEngine.Object.Destroy(SceneRoot.gameObject);
        }

        // Pausa de impacto (hitstop): como mucho 0.16 s
        public void RequestHitstop(float t) { Hitstop = Mathf.Min(0.16f, Mathf.Max(Hitstop, t)); }

        // ---------- Transiciones (el único temporizador en tiempo real del juego) ----------
        public void Transition(Action fn)
        {
            OnFade(true);
            if (InstantTransitions) { fn(); return; }
            pendingTransition = fn;
            transitionLeft = 0.65f;
        }

        public bool TransitionPending { get { return pendingTransition != null; } }

        // Ejecuta ya la transición pendiente (pruebas)
        public void FlushTransition()
        {
            var fn = pendingTransition;
            pendingTransition = null;
            if (fn != null) fn();
        }

        public void UpdateRealTime(float unscaledDt)
        {
            if (pendingTransition == null) return;
            transitionLeft -= unscaledDt;
            if (transitionLeft <= 0f) FlushTransition();
        }

        partial void OnFade(bool on);

        // Visuales que no son simulación (interpolaciones de UI en tiempo real)
        public void LateVisuals(float unscaledDt) { OnLateVisuals(unscaledDt); }
        partial void OnLateVisuals(float unscaledDt);

        void OnPadDisconnect(int i)
        {
            Toast("🎮 Mando " + (i + 1) + " desconectado. Partida en pausa: vuelve a conectarlo o sigue con el teclado.", 6f);
            if (State == GameState.Play) TogglePause();
        }

        public void TogglePause()
        {
            if (State == GameState.Play) { State = GameState.Paused; OnPauseChanged(true); }
            else if (State == GameState.Paused) { State = GameState.Play; OnPauseChanged(false); }
        }

        partial void OnPauseChanged(bool paused);

        public void Toast(string text, float seconds) { OnToast(text, seconds); }
        partial void OnToast(string text, float seconds);

        static readonly string[] PauseKeys = { "Escape", "KeyP", "GP0:START", "GP1:START" };

        // ---------- Bucle ----------
        public void Frame(float dt)
        {
            FrameCount++;
            Input.Poll();
            // Pausa solo en partida (en la sala de preparación, Esc vuelve al título)
            if ((State == GameState.Play || State == GameState.Paused) && Input.ConsumeAny(PauseKeys)) TogglePause();
            if (Input.Consume("KeyM"))
            {
                bool m = Audio.ToggleMute();
                Toast(m ? "Sonido desactivado" : "Sonido activado", 1.5f);
            }
            FrameBody(dt);
        }

        // Cuerpo del frame según el estado (lo completan las fases siguientes)
        void FrameBody(float dt)
        {
            bool handled = false;
            FrameStates(dt, ref handled);
            if (handled) return;
            if (State == GameState.Paused || State == GameState.Win) { Input.EndFrame(); return; }

            // Pausa de impacto: se congela la simulación, la cámara sigue vibrando
            if (Hitstop > 0f)
            {
                Hitstop -= dt;
                HitstopFrame(dt);
                return; // no se limpian las pulsaciones: se procesan al reanudar
            }

            Time += dt;
            Simulate(dt);
            Input.EndFrame();
        }

        partial void FrameStates(float dt, ref bool handled);
        partial void HitstopFrame(float dt);
        partial void Simulate(float dt);
    }
}
