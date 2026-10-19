using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Armas elementales, afinidad del piso y tragaperras de los pisos de exploración
    public sealed partial class Game
    {
        // Las pruebas antiguas la apagan (GameTestBase) para no cambiar sus pisos; las nuevas la encienden
        public static bool SlotMachinesEnabled = true;

        ElementalSystem elemental;
        public ElementalSystem Elemental { get { return elemental ?? (elemental = new ElementalSystem(this)); } }
        FloorThemeApplier theme;
        public FloorThemeApplier Theme { get { return theme ?? (theme = new FloorThemeApplier(this)); } }
        public readonly List<SlotMachine> SlotMachines = new List<SlotMachine>();
        SlotOverlay slotUi;
        public SlotOverlay SlotUi { get { return slotUi ?? (slotUi = new SlotOverlay(this)); } }

        // Afinidad del piso actual (la decide la tragaperras; None = aspecto de siempre)
        public Element Affinity { get; private set; }
        // Resultados de la tragaperras que duran lo que dura el piso
        public string SlotBounty;          // tipo de enemigo que suelta monedas extra ("all" = todos)
        public float SlotHealOnKill;       // vida por enemigo derrotado

        public void SetAffinity(Element e)
        {
            Affinity = e;
            if (e == Element.None) Theme.Clear(); else Theme.Apply(e);
        }

        void UpdateElemental(float dt)
        {
            Elemental.Update(dt);
            for (int i = 0; i < SlotMachines.Count; i++) SlotMachines[i].Update(dt);
        }

        // Al vaciar el piso: estados, zonas, enemigos almacenados, máquinas y afinidad no pasan al siguiente
        void ClearElemental()
        {
            SlotUi.Abort();
            Elemental.Clear();
            Theme.Clear();
            foreach (var m in SlotMachines) m.Destroy();
            SlotMachines.Clear();
            Affinity = Element.None;
            SlotBounty = null;
            SlotHealOnKill = 0f;
        }

        // ---------- Aparición de la tragaperras ----------
        partial void AfterLevelLoadedMore(LoadOpts opts)
        {
            if (!SlotMachinesEnabled || opts.Preview || opts.Bonus || opts.Shop || opts.Boss) return;
            var cfg = SlotMachineConfig.Load();
            if (Floor < cfg.MinFloor || Floor > cfg.MaxFloor || (cfg.ExcludeFirstFloor && Floor <= 1)) return;
            // Generador propio por piso: no gasta números del azar de la partida (los pisos siguen igual)
            var r = SlotRng(Seed, Floor);
            for (int k = 0; k < cfg.MaxPerFloor; k++)
            {
                if (r.Next() >= cfg.SpawnChance) continue;
                Vector3 facing;
                var t = FindSlotTile(r, out facing);
                if (t.x < 0) break;
                SlotMachines.Add(new SlotMachine(this, t.x, t.y, facing, r));
            }
            if (SlotMachines.Count > 0 && !SeenHints.ContainsKey("slot"))
            {
                SeenHints["slot"] = true;
                Ui.Toast("¡Una tragaperras! Gírala para decidir el tema del piso, una mejora y un arma elemental.", 5f);
            }
        }

        // Semilla de la máquina de cada piso, bien mezclada (con una semilla casi lineal los primeros números salían
        // correlacionados y la probabilidad de aparición no se cumplía)
        public static Rng SlotRng(uint seed, int floor)
        {
            uint h = unchecked(seed ^ ((uint)floor * 0x9E3779B9u));
            h ^= h >> 16; h = unchecked(h * 0x85EBCA6Bu); h ^= h >> 13; h = unchecked(h * 0xC2B2AE35u); h ^= h >> 16;
            var r = new Rng(h);
            r.Next(); r.Next();
            return r;
        }

        // Casilla de la sala inicial junto a un muro, lejos del inicio y de la salida, que no corte el paso
        Vector2Int FindSlotTile(Rng r, out Vector3 facing)
        {
            facing = Vector3.forward;
            var d = Dungeon;
            Spawn ps = default(Spawn);
            foreach (var s in d.Spawns) if (s.Ch == 'P') { ps = s; break; }
            var room = d.RoomAt(ps.X, ps.Z);
            var used = new HashSet<int>();
            foreach (var s in d.Spawns) used.Add(d.Idx(s.Tx, s.Tz));
            int ex = Grid.TileX(d.ExitX), ez = Grid.TileZ(d.ExitZ);
            int[] ox = { 1, -1, 0, 0 }, oz = { 0, 0, 1, -1 };
            var cands = new List<Vector2Int>();
            var dirs = new List<Vector3>();
            for (int tz = 1; tz < d.H - 1; tz++)
                for (int tx = 1; tx < d.W - 1; tx++)
                {
                    if (d.TypeAt(tx, tz) != TileType.Floor || d.CharAt(tx, tz) != '.') continue;
                    int i = d.Idx(tx, tz);
                    if (used.Contains(i) || d.Spike[i] != 0 || d.Blocked[i] != 0) continue;
                    if (room != null && d.RoomAt(Grid.X(tx), Grid.Z(tz)) != room) continue;
                    if (Mathf.Abs(tx - ps.Tx) + Mathf.Abs(tz - ps.Tz) < 2 || Mathf.Abs(tx - ex) + Mathf.Abs(tz - ez) < 3) continue;
                    // Exactamente un muro al lado (no esquinas ni pasillos) y suelo libre delante
                    int walls = 0, wk = -1;
                    for (int k = 0; k < 4; k++) if (d.TypeAt(tx + ox[k], tz + oz[k]) == TileType.Wall) { walls++; wk = k; }
                    if (walls != 1) continue;
                    int fx = tx - ox[wk], fz = tz - oz[wk];
                    if (d.TypeAt(fx, fz) != TileType.Floor || d.Blocked[d.Idx(fx, fz)] != 0) continue;
                    // Que sus dos vecinos laterales sean suelo: así no tapa una puerta ni un hueco estrecho
                    int sx = oz[wk], sz = ox[wk];
                    if (!Dungeon.IsFloorLike(d.TypeAt(tx + sx, tz + sz)) || !Dungeon.IsFloorLike(d.TypeAt(tx - sx, tz - sz))) continue;
                    cands.Add(new Vector2Int(tx, tz));
                    dirs.Add(Grid.Dir(-ox[wk], -oz[wk]));
                }
            if (cands.Count == 0) return new Vector2Int(-1, -1);
            int pick = (int)(r.Next() * cands.Count);
            facing = dirs[pick];
            return cands[pick];
        }

        // ---------- Interacción ----------
        public SlotMachine SlotNear(Player p, float range = 2.0f)
        {
            foreach (var m in SlotMachines) if (MathUtil.Hypot(m.X - p.X, m.Z - p.Z) < range) return m;
            return null;
        }

        // INTERACTUAR junto a la máquina: girar. true si se quedó con el botón
        bool SlotInteraction(Player p)
        {
            if (p.Carrying != null || p.CarryingTnt != null) return false;
            var m = SlotNear(p);
            if (m == null) return false;
            string prompt = m.Prompt(p);
            if (prompt == null) return false;
            Ui.SetPrompt(p.Index, prompt);
            if (p.Controls.ConsumeInteract()) m.Spin(p);
            return true;
        }

        // Obligatoria: no se baja hasta haberla girado
        public bool SlotBlocksExit()
        {
            if (!SlotMachineConfig.Load().Mandatory) return false;
            foreach (var m in SlotMachines) if (m.State != SlotState.Done) return true;
            return false;
        }

        // Recompensa del primer rodillo y curación por eliminación del segundo
        void OnSlotKill(Enemy e, string cause)
        {
            if (cause != "pit" && SlotBounty != null)
            {
                int extra = 0;
                if (SlotBounty == "all") extra = 1;
                else if (SlotBounty == "slime" && e is Slime) extra = 2;
                else if (SlotBounty == "goblin" && e is Goblin) extra = 2;
                else if (SlotBounty == "grabber" && e is Grabber) extra = 5;
                if (extra > 0) SpawnCoins(e.X, e.Z, extra);
            }
            if (SlotHealOnKill > 0f)
                foreach (var p in Players) if (!p.Dead && p.Hp < p.MaxHp) { p.Heal(SlotHealOnKill); Ui.FloatText(p.X, 2.4f, p.Z, "+" + SlotHealOnKill, TextStyle.Heal); }
        }
    }
}
