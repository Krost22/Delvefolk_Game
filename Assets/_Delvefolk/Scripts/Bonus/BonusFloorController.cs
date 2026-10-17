using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Ciclo de vida de los pisos bonus (BonusFloorController.js): aparición → introducción → desafío → resultado → descenso.
    // Integración con Game:
    //   Descend() pregunta ShouldTrigger(); si toca, Enter() carga el piso del desafío en lugar del siguiente.
    //   Durante la intro y el resultado State = Bonus (simulación congelada, solo se leen confirmaciones).
    //   Al salir, Exit() devuelve el control al juego con lo que lleva cada jugador.
    // Perder un bonus nunca termina la partida: quien cae se levanta con BonusConfig.ReviveHp.
    public sealed class BonusFloorController
    {
        public sealed class Result
        {
            public string Id, Reason;
            public bool Success;
            public Dictionary<int, List<BonusRewardService.Granted>> Granted;
            public Dictionary<int, BonusChallenge.Part> Part;
        }

        public readonly Game G;
        public BonusChallenge Challenge;
        public string Phase;               // null | "intro" | "running" | "resolving" | "result"
        public readonly List<string> History = new List<string>();   // desafíos jugados en esta partida
        readonly HashSet<int> seenAfter = new HashSet<int>();         // pisos tras los que ya salió el bonus obligatorio
        public string ForceNext;           // depuración / pruebas: id del próximo desafío
        public int Serial;
        public uint Seed = 1;
        public int NextFloor, Depth;
        public float OverlayT, ResultDelay;
        public readonly HashSet<int> Confirmed = new HashSet<int>();
        public Result LastResult;

        static BonusFloorController()
        {
            BonusChallenges.Register("minefield", c => new MinefieldChallenge(c));
            BonusChallenges.Register("bridge", c => new BridgeChallenge(c));
            BonusChallenges.Register("loot", c => new CursedLootChallenge(c));
            BonusChallenges.Register("feast", c => new FeastChallenge(c));
            BonusChallenges.Register("arena", c => new EchoArenaChallenge(c));
            BonusChallenges.Register("breath", c => new LastBreathChallenge(c));
        }

        public BonusFloorController(Game g) { G = g; }

        public bool Active { get { return Challenge != null; } }
        public bool Running { get { return Phase == "running" && Challenge != null && Challenge.Status == "running"; } }

        public void ResetRun()
        {
            History.Clear(); seenAfter.Clear(); Serial = 0;
            Challenge = null; Phase = null;
        }

        // ¿Hay piso bonus al bajar desde fromFloor?
        public bool ShouldTrigger(int fromFloor)
        {
            string mode = G.Settings.BonusMode;
            if (ForceNext != null) return true;
            if (mode == "never" || fromFloor < BonusConfig.MinFloor) return false;
            if (mode == "always") return true;
            if (System.Array.IndexOf(BonusConfig.AfterFloors, fromFloor) >= 0 && !seenAfter.Contains(fromFloor)) return true;
            return G.Rand.Next() < BonusConfig.Chance;
        }

        string PickChallenge()
        {
            if (ForceNext != null && BonusChallenges.Has(ForceNext)) { string id = ForceNext; ForceNext = null; return id; }
            ForceNext = null;
            string last = History.Count > 0 ? History[History.Count - 1] : null;
            var items = new List<KeyValuePair<string, float>>();
            foreach (var kv in BonusConfig.Weights) if (BonusChallenges.Has(kv.Key) && kv.Value > 0f) items.Add(kv);
            if (BonusConfig.NoBackToBack && items.Count > 1) items.RemoveAll(it => it.Key == last);
            if (items.Count == 0) return "minefield";
            return G.Rand.Weighted(items, it => it.Value).Key;
        }

        public void Enter(int fromFloor, int nextFloor, CarryState[] carry)
        {
            seenAfter.Add(fromFloor);
            string id = PickChallenge();
            Serial++;
            Seed = (uint)(G.Rand.NextDouble() * 1e9);
            NextFloor = nextFloor;
            Depth = fromFloor;
            var ch = BonusChallenges.List[id](this);
            ch.Id = id;
            ch.Source = "bonus" + Serial + ":" + id;   // prefijo de las claves de recompensa
            Challenge = ch;
            History.Add(id);
            G.LoadLevel(ch.BuildLevel(fromFloor), fromFloor, carry, new LoadOpts { Bonus = true });
            ch.Setup();
            Phase = "intro";
            OverlayT = BonusConfig.IntroTimeout;
            Confirmed.Clear();
            G.State = GameState.Bonus;
            G.Ui.Fade(false);
            G.Ui.ShowBonusIntro(ch.Info, G.Players);
            G.Audio.Play("bonus");
        }

        public bool AbilitiesBlockedFor(Player p) { return Active && Challenge.IsOut(p); }
        public bool AllowRevive() { return !Running || Challenge.AllowRevive(); }

        // ---- Pantallas de intro / resultado (simulación congelada) ----
        public void UpdateOverlay(float dt)
        {
            OverlayT -= dt;
            foreach (var p in G.Players)
            {
                if (p.Controls.ConsumeAttack() || p.Controls.ConsumeInteract())
                {
                    Confirmed.Add(p.Index);
                    G.Audio.Play("pickup");
                }
            }
            G.Ui.UpdateBonusOverlay(Confirmed, G.Players, Mathf.Max(0f, OverlayT));
            bool all = true;
            foreach (var p in G.Players) if (!Confirmed.Contains(p.Index)) { all = false; break; }
            if (Phase == "intro" && (all || OverlayT <= 0f)) BeginChallenge();
            else if (Phase == "result" && (all || OverlayT <= 0f)) Exit();
        }

        void BeginChallenge()
        {
            Phase = "running";
            G.Ui.HideBonusOverlay();
            G.State = GameState.Play;
            G.Input.Clear();
            Challenge.Start();
            G.Ui.ShowBonusHud(true);
            G.Ui.Banner(Challenge.Info.Icon + " " + Challenge.Info.Name, "¡Empieza el desafío!");
        }

        public void PreUpdate(float dt) { if (Running) Challenge.PreUpdate(dt); }

        public void Update(float dt)
        {
            if (Challenge == null) return;
            if (Running) Challenge.Update(dt);
            if (Challenge != null) G.Ui.UpdateBonusHud(Challenge.Hud());
        }

        public bool HandleInteraction(Player p) { return Running && Challenge.HandleInteraction(p); }
        public void OnPlayerDown(Player p) { if (Running) Challenge.OnPlayerDown(p); }
        // Un jugador se une a mitad del desafío (J2 pulsa ENTER): participa desde ese momento
        public void OnPlayerJoined(Player p) { if (Running) Challenge.Participant(p); }

        // Lo llama el desafío al terminar (una sola vez)
        public void OnChallengeFinished(BonusChallenge ch)
        {
            G.RewardService.GrantPlan(ch.RewardPlan());
            var granted = G.RewardService.Summary(ch.Source);
            LastResult = new Result { Id = ch.Id, Success = ch.Status == "success", Reason = ch.Reason, Granted = granted, Part = ch.Parts };
            G.Audio.Play(ch.Status == "success" ? "win" : "lose");
            G.Ui.Banner(ch.Status == "success" ? "¡DESAFÍO SUPERADO!" : "DESAFÍO FALLIDO", ch.Reason);
            // Un instante para ver el desenlace antes de la pantalla de resultados
            ResultDelay = 1.6f;
            Phase = "resolving";
        }

        // Avanza el retardo de "resolving" (en tiempo de juego)
        public void UpdateResolving(float dt)
        {
            if (Phase != "resolving") return;
            ResultDelay -= dt;
            if (ResultDelay > 0f) return;
            Phase = "result";
            OverlayT = BonusConfig.ResultTimeout;
            Confirmed.Clear();
            G.State = GameState.Bonus;
            G.Ui.ShowBonusHud(false);
            G.Ui.ShowBonusResult(Challenge, LastResult, G.Players);
        }

        void Exit()
        {
            var ch = Challenge;
            G.Ui.HideBonusOverlay();
            // Todos vuelven: quien cayó o quedó fuera se levanta; nadie pierde la partida por un bonus
            foreach (var p in G.Players)
            {
                ch.ClearSpectator(p);
                if (p.Dead) p.Revive(BonusConfig.ReviveHp);
                p.StunT = 0f; p.ConfusedT = 0f;
            }
            ch.Dispose();
            Challenge = null;
            Phase = null;
            G.State = GameState.Transition;
            var carry = G.CollectCarry();
            int next = NextFloor;
            G.Transition(() => G.AfterBonus(next, carry));
        }
    }
}
