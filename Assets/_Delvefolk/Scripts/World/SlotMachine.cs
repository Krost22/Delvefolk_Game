using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Delvefolk
{
    // Una opción de un rodillo, con su peso (los negativos cuentan como 0; con 0 no sale)
    [System.Serializable]
    public sealed class SlotOption
    {
        public string Id;
        public string Label;
        public string Icon;
        public float Weight = 1f;
        [TextArea] public string Explain;
    }

    // Selección ponderada (sin estado; se prueba sola)
    public static class SlotMachineMath
    {
        public static float Clean(float w) { return float.IsNaN(w) || float.IsInfinity(w) || w <= 0f ? 0f : w; }

        public static float Total(IList<SlotOption> opts)
        {
            float t = 0f;
            if (opts != null) foreach (var o in opts) if (o != null) t += Clean(o.Weight);
            return t;
        }

        // Índice elegido con r en [0, 1). −1 si ningún peso es positivo
        public static int Pick(IList<SlotOption> opts, double r)
        {
            float total = Total(opts);
            if (total <= 0f) return -1;
            double x = r * total;
            int last = -1;
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i] == null) continue;
                float w = Clean(opts[i].Weight);
                if (w <= 0f) continue;
                last = i;
                if (x < w) return i;
                x -= w;
            }
            return last;
        }

        public static int IndexOf(IList<SlotOption> opts, string id)
        {
            if (string.IsNullOrEmpty(id) || opts == null) return -1;
            for (int i = 0; i < opts.Count; i++) if (opts[i] != null && opts[i].Id == id) return i;
            return -1;
        }
    }

    public enum SlotState { Idle, Spinning, Revealing, Done }

    // La tragaperras del piso: se gira una vez con INTERACTUAR. Los tres resultados se eligen ANTES de la
    // animación y los rodillos se paran en ellos; después se aplican una sola vez.
    public sealed class SlotMachine
    {
        public readonly Game G;
        public readonly float X, Z;
        public readonly int Tx, Tz;
        public SlotState State { get; private set; }
        public SlotOption EnemyResult, AbilityResult, AffinityResult;
        public bool Applied { get; private set; }
        public float T;
        readonly SlotMachineConfig cfg;
        readonly Transform root;
        readonly TextMeshPro reels, caption;
        readonly Body body;
        readonly Rng rng;
        int spins;

        public SlotMachine(Game g, int tx, int tz, Vector3 facing, Rng rng)
        {
            G = g; Tx = tx; Tz = tz; this.rng = rng;
            cfg = SlotMachineConfig.Load();
            X = Grid.X(tx); Z = Grid.Z(tz);
            root = new GameObject("Tragaperras").transform;
            root.SetParent(g.FloorRoot, false);
            root.localPosition = new Vector3(X, 0f, Z);
            root.localRotation = Quaternion.LookRotation(facing);
            BuildModel();
            reels = Label(new Vector3(0f, 3.0f, 0f), 3.2f, "<b>❔ ❔ ❔</b>");
            caption = Label(new Vector3(0f, 2.45f, 0f), 1.6f, "TRAGAPERRAS");
            g.Dungeon.Blocked[g.Dungeon.Idx(tx, tz)] = 1;
            body = g.Physics.Add(new Body { X = X, Z = Z, Radius = 0.75f, IsStatic = true, Kind = BodyKind.Static });
        }

        void BuildModel()
        {
            var art = CasinoArt.Load();
            if (art != null && Art.Has(art.SlotMachine))
            {
                var holder = new GameObject("Modelo").transform;
                holder.SetParent(root, false);
                holder.localRotation = Quaternion.Euler(0f, art.SlotYaw, 0f);
                var go = Art.Spawn(art.SlotMachine, holder);
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float s = 2.3f / Mathf.Max(0.01f, b.size.y);
                go.transform.localScale *= s;
                rs = go.GetComponentsInChildren<Renderer>();
                b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += new Vector3(root.position.x - b.center.x, -b.min.y, root.position.z - b.center.z);
                return;
            }
            // Mueble de reserva hecho por código (frente en +Z)
            var mb = new MeshBuilder();
            mb.Add(ProcMesh.Box(1.2f, 1.9f, 0.9f), new Vector3(0, 0.95f, 0), MathUtil.Hex(0x8a1f2a));
            mb.Add(ProcMesh.Box(1.0f, 0.5f, 0.05f), new Vector3(0, 1.35f, 0.46f), MathUtil.Hex(0x1a1420));
            mb.Add(ProcMesh.Box(1.3f, 0.25f, 1.0f), new Vector3(0, 2.0f, 0), MathUtil.Hex(0xc9a227));
            mb.Add(ProcMesh.Cylinder(0.04f, 0.04f, 0.7f, 6), new Vector3(0.72f, 1.35f, 0), MathUtil.Hex(0xc8d0d8));
            mb.Add(ProcMesh.Sphere(0.11f, 6, 5), new Vector3(0.72f, 1.75f, 0), MathUtil.Hex(0xe23b3b));
            CharacterModel.MeshObject(root, "Mueble", mb.ToMesh("tragaperras"), Mats.Lit, Vector3.zero);
        }

        TextMeshPro Label(Vector3 pos, float size, string text)
        {
            var go = new GameObject("Texto");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            t.font = Fonts.Body;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = MathUtil.Hex(0xffe2a0);
            Hud.Outline(t, Color.black, 0.25f);
            t.rectTransform.sizeDelta = new Vector2(6f, 1.2f);
            return t;
        }

        public bool CanUse { get { return State == SlotState.Idle || (State == SlotState.Done && cfg.RespinEnabled); } }
        public bool Busy { get { return State == SlotState.Spinning || State == SlotState.Revealing; } }

        public string Prompt(Player p)
        {
            if (State == SlotState.Idle) return "Girar la tragaperras";
            if (State == SlotState.Done && cfg.RespinEnabled) return "Volver a girar (" + cfg.RespinCost + " monedas)";
            return null;
        }

        // Elige los tres resultados (una sola vez por giro) y empieza la animación. false si no se puede
        public bool Spin(Player p)
        {
            if (!CanUse) return false;
            if (SlotMachineMath.Total(cfg.Enemies) <= 0f || SlotMachineMath.Total(cfg.Abilities) <= 0f || SlotMachineMath.Total(cfg.Affinities) <= 0f)
            {
                G.Ui.Toast("La tragaperras no tiene premios configurados (todos los pesos a 0).", 3f);
                Debug.LogWarning("[Tragaperras] Todos los pesos de un rodillo son 0: no se gira.");
                return false;
            }
            if (State == SlotState.Done)
            {
                if (p.Gear.Gold < cfg.RespinCost) { G.Ui.FloatText(p.X, 2.6f, p.Z, "Faltan monedas", TextStyle.Small); return false; }
                p.Gear.Gold -= cfg.RespinCost;
                G.Ui.SetGear(p.Index, p.Gear);
                Undo();
            }
            EnemyResult = Choose(cfg.Enemies, cfg.DebugForce ? cfg.ForceEnemies : null);
            AbilityResult = Choose(cfg.Abilities, cfg.DebugForce ? cfg.ForceAbility : null);
            AffinityResult = Choose(cfg.Affinities, cfg.DebugForce ? cfg.ForceAffinity : null);
            spinner = p;
            spins++;
            State = SlotState.Spinning;
            T = 0f;
            Applied = false;
            G.Audio.Play("pickup");
            return true;
        }

        Player spinner;
        bool abilityGiven;

        SlotOption Choose(List<SlotOption> opts, string force)
        {
            int i = SlotMachineMath.IndexOf(opts, force);
            if (i < 0) i = SlotMachineMath.Pick(opts, rng.NextDouble());
            return opts[i];
        }

        public float SpinTotal { get { return cfg.ReelSpin + cfg.ReelStagger * 2f; } }

        public void Update(float dt)
        {
            // Los rótulos miran siempre a la cámara
            var cam = G.CameraCtl.Cam.transform.rotation;
            reels.transform.rotation = cam; caption.transform.rotation = cam;
            if (State == SlotState.Spinning)
            {
                T += dt;
                // Cada rodillo enseña opciones al azar hasta su momento de parar; entonces, el resultado elegido
                string a = T >= cfg.ReelSpin ? EnemyResult.Icon : Flicker(cfg.Enemies, 0);
                string b = T >= cfg.ReelSpin + cfg.ReelStagger ? AbilityResult.Icon : Flicker(cfg.Abilities, 1);
                string c = T >= SpinTotal ? AffinityResult.Icon : Flicker(cfg.Affinities, 2);
                reels.text = "<b>" + a + " " + b + " " + c + "</b>";
                if (T >= SpinTotal)
                {
                    State = SlotState.Revealing; T = 0f;
                    G.Audio.Play("gate");
                    caption.text = AffinityResult.Label.ToUpperInvariant();
                    G.Ui.Banner("🎰 " + EnemyResult.Icon + " " + AbilityResult.Icon + " " + AffinityResult.Icon,
                        AbilityResult.Label + " · " + AffinityResult.Label + " · " + EnemyResult.Label);
                }
            }
            else if (State == SlotState.Revealing)
            {
                T += dt;
                if (T >= cfg.RevealPause) { State = SlotState.Done; ApplyResults(); }
            }
        }

        string Flicker(List<SlotOption> opts, int reel)
        {
            int n = opts.Count;
            int i = (int)(T * 14f + reel * 3) % Mathf.Max(1, n);
            return opts[i] != null ? opts[i].Icon : "?";
        }

        // Aplica los tres resultados una sola vez
        void ApplyResults()
        {
            if (Applied) return;
            Applied = true;
            var lines = new List<string>();
            // Rodillo 1: recompensa por tipo de enemigo (no cambia a los enemigos)
            G.SlotBounty = EnemyResult.Id;
            lines.Add(EnemyResult.Explain);
            // Rodillo 2: mejora temporal para todos los jugadores (con los límites del equipo de siempre)
            // (al volver a girar no se da otra: nunca se acumulan dos mejoras de la misma máquina)
            if (!abilityGiven)
            {
                abilityGiven = true;
                if (AbilityResult.Id == "heal_kill") G.SlotHealOnKill = 3f;
                else foreach (var p in G.Players) if (p.Gear.Apply(AbilityResult.Id) != null) G.Ui.SetGear(p.Index, p.Gear);
                lines.Add(AbilityResult.Label + ": " + AbilityResult.Explain);
            }
            else lines.Add("(La mejora de esta máquina ya se dio.)");
            // Rodillo 3: afinidad del piso (materiales) y arma relacionada junto a la máquina
            Element el;
            if (System.Enum.TryParse(AffinityResult.Id, out el) && el != Element.None)
            {
                G.SetAffinity(el);
                var theme = FloorThemeCatalog.Load().Get(el);
                if (theme != null)
                {
                    lines.Add(theme.Explain);
                    if (!string.IsNullOrEmpty(theme.WeaponId) && WeaponRegistry.Has(theme.WeaponId) && rng.Next() < cfg.AffinityWeaponChance)
                    {
                        var spot = WeaponSpot();
                        G.SpawnItemPickup(WeaponRegistry.Create(theme.WeaponId), spot.x, spot.y, new Vector2(X, Z));
                    }
                }
            }
            G.Ui.Toast(string.Join("\n", lines), 6f);
            G.Effects.Burst(X, 2.4f, Z, BurstOpts.Of(24, 0xffd36a, 5f, 6f, 0.12f).WithColorVar(0.2f));
        }

        // Casilla libre al lado de la máquina (sin muros ni fosos) para el arma
        Vector2 WeaponSpot()
        {
            var fwd = root.forward;
            var d = G.Dungeon;
            foreach (var o in new[] { new Vector2(fwd.x, fwd.z) * 1.6f, new Vector2(fwd.z, -fwd.x) * 1.6f, new Vector2(-fwd.z, fwd.x) * 1.6f })
            {
                float x = X + o.x, z = Z + o.y;
                if (!d.IsWallLike(Grid.TileX(x), Grid.TileZ(z)) && !d.IsPitAt(x, z)) return new Vector2(x, z);
            }
            return new Vector2(X + fwd.x * 1.2f, Z + fwd.z * 1.2f);
        }

        // Volver a girar: se retira el resultado anterior antes de aplicar el nuevo (nunca se suman)
        void Undo()
        {
            G.SlotBounty = null;
            G.SetAffinity(Element.None);
        }

        public void Destroy()
        {
            if (root) Object.Destroy(root.gameObject);
            if (G.Physics != null) G.Physics.Remove(body);
        }
    }
}
