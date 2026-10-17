using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Ficha para la introducción de un desafío
    public sealed class BonusInfo
    {
        public string Id, Name, Icon, Tagline, Win, Lose, Risk, Reward, Coop;
        public string[] Rules;
    }

    // Estado del HUD del minijuego (arriba, centrado)
    public sealed class BonusHudState
    {
        public string Title, Icon;
        public float? Timer;
        public string ProgressLabel;
        public int ProgressValue, ProgressMax;
        public bool HasProgress;
        public readonly List<string> Lines = new List<string>();
        public readonly List<BonusHudPlayer> Players = new List<BonusHudPlayer>();
    }

    public struct BonusHudPlayer { public string Name, Extra; public int Color; public bool Out, Down; }

    // Registro de acciones resueltas (útil para pruebas)
    public sealed class BonusEvent
    {
        public string Type, How, Reason, Why;
        public float T;
        public int Player = -1, C = -1, R = -1, Chest = -1;
        public bool Mine, On, Success;
    }

    // Contrato común de los minijuegos de los pisos bonus (BonusChallenge.js).
    // Ciclo de vida (lo dirige BonusFloorController):
    //   BuildLevel()  → mapa ASCII del piso del desafío
    //   Setup()       → tras cargar el piso: tablero, cofres, etc.
    //   PreUpdate(dt) → antes de mover a los jugadores (para capturar botones propios del minijuego)
    //   Update(dt)    → después de la física
    //   HandleInteraction(p) → true si el minijuego gestiona el aviso/botón de interactuar de ese jugador
    //   OnPlayerDown(p), Hud(), Dispose()
    // Para terminar, el minijuego llama a Finish(éxito, motivo). Las recompensas se calculan en RewardPlan()
    // y las reparte BonusRewardService (que impide duplicados).
    public abstract class BonusChallenge
    {
        public sealed class Part { public bool Out; public string Why = ""; public int Score, Strikes; }

        public readonly BonusFloorController Ctl;
        public readonly Game G;
        public float Time;
        public string Status = "pending";   // pending → running → success | failed
        public string Reason = "";
        public readonly Dictionary<int, Part> Parts = new Dictionary<int, Part>();
        public readonly List<BonusEvent> Events = new List<BonusEvent>();
        public readonly Rng Rnd;
        public string Id, Source;

        protected BonusChallenge(BonusFloorController ctl)
        {
            Ctl = ctl;
            G = ctl.G;
            Rnd = new Rng(ctl.Seed);
        }

        public abstract BonusInfo Info { get; }

        public Part Participant(Player p)
        {
            Part st;
            if (!Parts.TryGetValue(p.Index, out st)) Parts[p.Index] = st = new Part();
            return st;
        }

        public bool IsOut(Player p) { Part st; return Parts.TryGetValue(p.Index, out st) && st.Out; }

        public List<Player> ActivePlayers()
        {
            var l = new List<Player>();
            foreach (var p in G.Players) if (!p.Dead && !IsOut(p)) l.Add(p);
            return l;
        }

        public BonusEvent Log(string type)
        {
            var e = new BonusEvent { Type = type, T = Mathf.Round(Time * 100f) / 100f };
            Events.Add(e);
            return e;
        }

        public abstract LevelDef BuildLevel(int depth);
        public virtual void Setup() { }

        public virtual void Start()
        {
            Status = "running";
            foreach (var p in G.Players) Participant(p);
        }

        public virtual void PreUpdate(float dt) { }
        public virtual void Update(float dt) { Time += dt; }
        public virtual bool HandleInteraction(Player p) { return false; }
        // Por defecto, caer derribado en el bonus te elimina del desafío
        public virtual void OnPlayerDown(Player p) { Eliminate(p, "derribado"); }
        public virtual bool AllowRevive() { return false; }

        // Fuera del desafío: pasa a espectador (no recibe daño ni puede actuar; se le levanta al terminar)
        public void Eliminate(Player p, string why)
        {
            var st = Participant(p);
            if (st.Out) return;
            st.Out = true; st.Why = why;
            var e = Log("eliminated"); e.Player = p.Index; e.Why = why;
            if (!p.Dead) MakeSpectator(p);
            G.Ui.Toast(p.Look.Name + " queda fuera del desafío (" + why + ").", 3f);
            if (Status == "running" && ActivePlayers().Count == 0) Finish(false, "Nadie sigue en pie");
        }

        readonly Dictionary<Renderer, Material> ghostMats = new Dictionary<Renderer, Material>();
        static readonly MaterialPropertyBlock ghostMpb = new MaterialPropertyBlock();

        public void MakeSpectator(Player p)
        {
            p.Spectator = true;
            p.Body.Ghost = true;
            p.Attack = null; p.CancelEating();
            if (p.Carrying != null) p.Release(1f);
            p.Ability.Finish();
            // Semitransparente al 35 %: solo las piezas del muñeco (no el arma ni el caparazón)
            foreach (var r in p.Model.GetComponentsInChildren<Renderer>(true))
            {
                if (p.Parts.Holder && r.transform.IsChildOf(p.Parts.Holder)) continue;
                if (p.Parts.Shell && r.transform.IsChildOf(p.Parts.Shell)) continue;
                if (ghostMats.ContainsKey(r)) continue;
                ghostMats[r] = r.sharedMaterial;
                r.sharedMaterial = Mats.TransparentLit;
                r.GetPropertyBlock(ghostMpb);
                ghostMpb.SetVector(Mats.BaseColorId, new Vector4(1f, 1f, 1f, 0.35f));
                r.SetPropertyBlock(ghostMpb);
            }
        }

        public void ClearSpectator(Player p)
        {
            if (!p.Spectator) return;
            p.Spectator = false;
            p.Body.Ghost = p.Dead;
            foreach (var kv in ghostMats)
            {
                if (!kv.Key || !kv.Key.transform.IsChildOf(p.Model)) continue;
                kv.Key.sharedMaterial = kv.Value;
                kv.Key.GetPropertyBlock(ghostMpb);
                ghostMpb.SetVector(Mats.BaseColorId, Vector4.one);
                kv.Key.SetPropertyBlock(ghostMpb);
            }
        }

        public void Finish(bool success, string reason)
        {
            if (Status != "running") return;     // solo se resuelve una vez
            Status = success ? "success" : "failed";
            Reason = reason ?? "";
            var e = Log("finish"); e.Success = success; e.Reason = Reason;
            Ctl.OnChallengeFinished(this);
        }

        // Recompensas a repartir (clave única → sin duplicados)
        public virtual List<BonusRewardService.PlanItem> RewardPlan() { return new List<BonusRewardService.PlanItem>(); }

        protected static BonusRewardService.PlanItem Plan(Player p, string reward, string key, string note = "")
        {
            return new BonusRewardService.PlanItem { Player = p, Reward = reward, Key = key, Note = note };
        }

        public virtual BonusHudState Hud() { return new BonusHudState { Title = Info.Name, Icon = Info.Icon }; }

        protected void HudPlayers(BonusHudState h, System.Func<Player, string> extra = null)
        {
            foreach (var p in G.Players)
                h.Players.Add(new BonusHudPlayer { Name = p.Look.Name, Color = p.Look.Ring, Out = IsOut(p), Down = p.Dead, Extra = extra != null ? extra(p) : "" });
        }

        public virtual void Dispose() { }

        // Raíz para los visuales del desafío (se borra con el piso)
        Transform root;
        protected Transform Root
        {
            get
            {
                if (!root) { root = new GameObject("Desafio_" + Id).transform; root.SetParent(G.FloorRoot, false); }
                return root;
            }
        }

        // ---- Utilidades para construir mapas ----
        public static string[] Frame(int w, int h, char fill = '.')
        {
            var rows = new string[h];
            for (int z = 0; z < h; z++)
            {
                var sb = new System.Text.StringBuilder(w);
                for (int x = 0; x < w; x++) sb.Append(x == 0 || z == 0 || x == w - 1 || z == h - 1 ? '#' : fill);
                rows[z] = sb.ToString();
            }
            return rows;
        }

        public static void Put(string[] rows, int x, int z, char ch)
        {
            rows[z] = rows[z].Substring(0, x) + ch + rows[z].Substring(x + 1);
        }

        // Centro de una casilla en el mundo
        public static Vector2 TileCenter(int tx, int tz) { return new Vector2(Grid.X(tx), Grid.Z(tz)); }
    }

    // Registro de minijuegos
    public static class BonusChallenges
    {
        public static readonly Dictionary<string, System.Func<BonusFloorController, BonusChallenge>> List =
            new Dictionary<string, System.Func<BonusFloorController, BonusChallenge>>();

        public static void Register(string id, System.Func<BonusFloorController, BonusChallenge> make) { List[id] = make; }
        public static bool Has(string id) { return id != null && List.ContainsKey(id); }
    }
}
