using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // ---------------------------------------------------------------- Sala de casino
    // Sala del trono (CasinoRoom: sus alfombras rojas son las casillas que quema el resultado ROJO), fichas del
    // jackpot y mesas que suben del suelo (resultado AZUL). Todo cuelga del piso: se va con él.
    public sealed class CasinoFloor
    {
        readonly Game G;
        public readonly List<Vector2Int> RedTiles = new List<Vector2Int>();
        public readonly HazardFloor Hazard;
        public readonly CasinoRoom Room;
        public readonly List<CasinoChip> Chips = new List<CasinoChip>();
        public readonly List<CasinoTable> Tables = new List<CasinoTable>();
        public readonly int W, H;
        int chipSeq;
        static Mesh chipMesh, tableMesh;

        public CasinoFloor(Game g)
        {
            G = g;
            var bf = g.BossFloor;
            W = bf != null ? bf.ArenaW : 19; H = bf != null ? bf.ArenaH : 15;
            Room = new CasinoRoom(g, W, H);
            RedTiles.AddRange(Room.RedTiles);
            Hazard = new HazardFloor(g, g.FloorRoot, RedTiles, 22f, 0xff7a10, 0xffe040, 0f, CasinoRoom.CarpetThickness + 0.06f);
        }

        public void SetRed(HazardFloor.CellState s) { foreach (var t in RedTiles) Hazard.Set(t.x, t.y, s); }
        public bool IsRedAt(float x, float z) { var t = new Vector2Int(Grid.TileX(x), Grid.TileZ(z)); return RedTiles.Contains(t); }

        public void Tick(float dt)
        {
            Hazard.Update(dt);
            for (int i = Chips.Count - 1; i >= 0; i--)
            {
                var c = Chips[i];
                c.T += dt;
                c.Tr.position = new Vector3(c.X, 0.45f + Mathf.Sin(c.T * 4f) * 0.12f, c.Z);
                c.Tr.rotation = Quaternion.Euler(70f, c.T * 160f, 0f);
                bool blink = c.T > CasinoChip.Life - 2f && Mathf.FloorToInt(c.T * 8f) % 2 == 0;
                if (c.Tr.gameObject.activeSelf == blink) c.Tr.gameObject.SetActive(!blink);
                Player taker = null;
                foreach (var p in G.Players) if (p.Targetable && MathUtil.Hypot(p.X - c.X, p.Z - c.Z) < 0.55f + p.Radius) { taker = p; break; }
                if (taker != null) { Give(taker, c); RemoveChip(i); }
                else if (c.T >= CasinoChip.Life) RemoveChip(i);
            }
            for (int i = Tables.Count - 1; i >= 0; i--)
            {
                var t = Tables[i];
                t.T += dt;
                float y = t.T < 0.3f ? -1.2f * (1f - t.T / 0.3f) : t.T > t.Life - 0.4f ? -1.2f * Mathf.Clamp01((t.T - (t.Life - 0.4f)) / 0.4f) : 0f;
                t.Tr.position = new Vector3(t.X, y, t.Z);
                if (t.T >= t.Life) RemoveTable(i);
            }
        }

        // ---------- Fichas del jackpot ----------
        public void SpawnChip(float x, float z, string kind)
        {
            if (!chipMesh)
            {
                var mb = new MeshBuilder();
                mb.Add(ProcMesh.Cylinder(0.4f, 0.4f, 0.12f, 16), Vector3.zero, MathUtil.Hex(0xf2c230));
                mb.Add(ProcMesh.Cylinder(0.24f, 0.24f, 0.14f, 12), Vector3.zero, Color.white);
                chipMesh = mb.ToMesh("fichaJackpot");
            }
            var go = CharacterModel.MeshObject(G.FloorRoot, "Ficha", chipMesh, Mats.Lit, new Vector3(x, 0.45f, z));
            var r = go.GetComponent<Renderer>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(Mats.BaseColorId, MathUtil.Hex(CasinoChip.Tint(kind)));
            r.SetPropertyBlock(mpb);
            Chips.Add(new CasinoChip { X = x, Z = z, Kind = kind, Tr = go.transform });
            G.Effects.Burst(x, 0.6f, z, BurstOpts.Of(8, 0xf2c230, 3f, 4f, 0.12f));
        }

        void Give(Player p, CasinoChip c)
        {
            G.Audio.Play("coin");
            G.Effects.Burst(c.X, 0.8f, c.Z, BurstOpts.Of(12, CasinoChip.Tint(c.Kind), 4f, 4f, 0.12f));
            switch (c.Kind)
            {
                case "heal": p.Heal(25f); G.Ui.FloatText(p.X, 2.4f, p.Z, "+25 ❤", TextStyle.Label); G.Audio.Play("heal"); break;
                case "haste": p.AddHaste(1.45f, 6f); G.Ui.FloatText(p.X, 2.4f, p.Z, "¡Prisa!", TextStyle.Label); break;
                case "guard": p.Iframes = Mathf.Max(p.Iframes, 3f); G.Ui.FloatText(p.X, 2.4f, p.Z, "¡Intocable!", TextStyle.Label); break;
                default: G.RewardService.ClaimReward(p, "gold_small", "crupier:ficha:" + G.Floor + ":" + (chipSeq++)); break;
            }
        }

        void RemoveChip(int i)
        {
            if (Chips[i].Tr) Object.Destroy(Chips[i].Tr.gameObject);
            Chips.RemoveAt(i);
        }

        // ---------- Mesas que suben del suelo ----------
        public void SpawnTable(float x, float z, float life)
        {
            if (!tableMesh)
            {
                var mb = new MeshBuilder();
                mb.Add(ProcMesh.Cylinder(0.35f, 0.5f, 0.9f, 10), new Vector3(0, 0.45f, 0), MathUtil.Hex(0x4a2616));
                mb.Add(ProcMesh.Cylinder(0.95f, 0.95f, 0.14f, 20), new Vector3(0, 0.95f, 0), MathUtil.Hex(0x1f7a3a));
                mb.Add(ProcMesh.Torus(0.95f, 0.07f, 6, 24), Matrix4x4.TRS(new Vector3(0, 1.02f, 0), Quaternion.Euler(90f, 0, 0), Vector3.one), MathUtil.Hex(0xe8c050));
                tableMesh = mb.ToMesh("mesaCasino");
            }
            var go = CharacterModel.MeshObject(G.FloorRoot, "MesaCasino", tableMesh, Mats.Lit, new Vector3(x, -1.2f, z));
            var t = new CasinoTable { X = x, Z = z, Life = life, Tr = go.transform };
            t.Body = G.Physics.Add(new Body { X = x, Z = z, Radius = 0.9f, IsStatic = true, Kind = BodyKind.Static, Owner = t });
            // Quien estuviera encima sale despedido
            foreach (var p in G.Players)
            {
                float dx = p.X - x, dz = p.Z - z, l = MathUtil.Hypot(dx, dz);
                if (l > 0.9f + p.Radius + 0.2f) continue;
                if (l < 0.01f) { dx = 1f; dz = 0f; l = 1f; }
                p.Knock(dx / l * 12f, dz / l * 12f);
            }
            G.Effects.Burst(x, 0.3f, z, BurstOpts.Of(10, 0x2f6fd8, 3f, 4f, 0.14f));
            G.Audio.Play("thwack");
            Tables.Add(t);
        }

        void RemoveTable(int i)
        {
            var t = Tables[i];
            G.Physics.Remove(t.Body);
            if (t.Tr) Object.Destroy(t.Tr.gameObject);
            Tables.RemoveAt(i);
        }

        public void ClearChips() { for (int i = Chips.Count - 1; i >= 0; i--) RemoveChip(i); }
        public void ClearTables() { for (int i = Tables.Count - 1; i >= 0; i--) RemoveTable(i); }

        public void ClearAll()
        {
            SetRed(HazardFloor.CellState.Safe);
            ClearChips();
            ClearTables();
        }

        public void Destroy() { ClearAll(); }   // la sala (y sus cuerpos) dura lo que el piso

        // Sitios libres dentro de la arena: suelo normal, lejos de jugadores, del jefe, de mesas y entre sí
        public List<Vector2> FreeSpots(int n, float fromPlayers, Entity avoid, float fromAvoid)
        {
            var d = G.Dungeon; var outp = new List<Vector2>();
            for (int k = 0; k < 400 && outp.Count < n; k++)
            {
                float x = G.Rand.Range(Grid.X(2), Grid.X(W - 3)), z = G.Rand.Range(Grid.Z(4), Grid.Z(H - 3));
                int tx = Grid.TileX(x), tz = Grid.TileZ(z);
                if (d.TypeAt(tx, tz) != TileType.Floor || d.Blocked[d.Idx(tx, tz)] != 0) continue;
                if (avoid != null && MathUtil.Hypot(x - avoid.X, z - avoid.Z) < fromAvoid) continue;
                bool bad = false;
                foreach (var p in G.Players) if (MathUtil.Hypot(p.X - x, p.Z - z) < fromPlayers) { bad = true; break; }
                foreach (var o in G.Physics.Bodies) if (!bad && o.IsStatic && o.Enabled && MathUtil.Hypot(o.X - x, o.Z - z) < o.Radius + 1.2f) bad = true;
                foreach (var s in outp) if (!bad && MathUtil.Hypot(s.x - x, s.y - z) < 2.6f) bad = true;
                if (!bad) outp.Add(new Vector2(x, z));
            }
            return outp;
        }
    }

    public sealed class CasinoChip
    {
        public const float Life = 9f;
        public float X, Z, T;
        public string Kind;
        public Transform Tr;
        public static int Tint(string kind)
        {
            switch (kind) { case "heal": return 0x7dff8a; case "haste": return 0x7ad0ff; case "guard": return 0xd8b0ff; default: return 0xffe070; }
        }
    }

    public sealed class CasinoTable
    {
        public float X, Z, T, Life;
        public Transform Tr;
        public Body Body;
    }

    // ---------------------------------------------------------------- Habilidades de la ruleta
    // ROJO — La casa quema: las casillas rojas avisan en naranja y luego queman un rato (saltando no quema)
    public sealed class HouseBurnsAbility : IRouletteAbility
    {
        public const float Warn = 1.6f, Burn = 3.2f;
        readonly CrupierBoss B;
        float t; bool burning;
        public HouseBurnsAbility(CrupierBoss b) { B = b; }
        public string Title { get { return "¡La casa quema! Pisad el negro"; } }
        public bool HoldsDealer { get { return t < Warn; } }
        public void Begin() { t = 0f; burning = false; B.Casino.SetRed(HazardFloor.CellState.Warn); B.G.Audio.Play("alert"); }
        public bool Tick(float dt)
        {
            t += dt;
            if (!burning && t >= Warn)
            {
                burning = true;
                B.Casino.SetRed(HazardFloor.CellState.Danger);
                B.G.Audio.Play("explosion");
                B.G.CameraCtl.AddShake(0.3f);
            }
            if (t < Warn + Burn) return false;
            B.Casino.SetRed(HazardFloor.CellState.Safe);
            return true;
        }
        public void Cancel() { B.Casino.SetRed(HazardFloor.CellState.Safe); }
    }

    // VERDE — La banca se protege: escudo que absorbe daño un rato; si se rompe, el Crupier queda aturdido
    public sealed class BankShieldAbility : IRouletteAbility
    {
        public const float Seconds = 7f, HpFrac = 0.12f;
        readonly CrupierBoss B;
        float t;
        public BankShieldAbility(CrupierBoss b) { B = b; }
        public string Title { get { return "La banca se protege"; } }
        public bool HoldsDealer { get { return t < 0.6f; } }
        public void Begin() { t = 0f; B.RaiseShield(Mathf.Round(B.MaxHp * HpFrac), Seconds); }
        public bool Tick(float dt) { t += dt; return t >= 0.6f && !B.Shielded; }
        public void Cancel() { B.DropShield(false); }
    }

    // NEGRO — Impuesto de la casa: tres ráfagas de fichas negras hacia los jugadores (se pueden devolver)
    public sealed class HouseTaxAbility : IRouletteAbility
    {
        static readonly float[] Volleys = { 0.3f, 0.9f, 1.5f };
        readonly CrupierBoss B;
        float t; int next;
        static Mesh chipMesh;
        public HouseTaxAbility(CrupierBoss b) { B = b; }
        public string Title { get { return "Impuesto de la casa"; } }
        public bool HoldsDealer { get { return true; } }
        public int Fired { get; private set; }
        public void Begin() { t = 0f; next = 0; Fired = 0; }
        public bool Tick(float dt)
        {
            t += dt;
            while (next < Volleys.Length && t >= Volleys[next])
            {
                var alive = B.AlivePlayers();
                if (alive.Count > 0 && !B.Dead) Volley(alive[next % alive.Count]);
                next++;
            }
            return t >= 2.1f;
        }

        void Volley(Player target)
        {
            B.FaceTo(target.X, target.Z);
            float a0 = Mathf.Atan2(target.X - B.X, target.Z - B.Z);
            for (int i = 0; i < 5; i++)
            {
                float a = a0 + (i - 2) * 14f * Mathf.Deg2Rad, sx = Mathf.Sin(a), sz = Mathf.Cos(a);
                B.Shoot(new Projectile
                {
                    PX = B.X + sx * 1.1f, PZ = B.Z + sz * 1.1f, Y = 1.2f, Vx = sx * 9.5f, Vz = sz * 9.5f,
                    Damage = 10, ProjRadius = 0.38f, Knock = 8, Life = 3.2f, Reflectable = true, ReflectDamage = 28, Color = 0x2a2a30,
                }, ChipModel(B.G));
                Fired++;
            }
            B.G.Audio.Play("throw");
        }

        static Transform ChipModel(Game g)
        {
            if (!chipMesh)
            {
                var mb = new MeshBuilder();
                mb.Add(ProcMesh.Cylinder(0.36f, 0.36f, 0.12f, 14), Vector3.zero, MathUtil.Hex(0x1c1c22));
                mb.Add(ProcMesh.Torus(0.3f, 0.04f, 4, 14), Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0, 0), Vector3.one), MathUtil.Hex(0xf2eee4));
                chipMesh = mb.ToMesh("fichaNegra");
            }
            return CharacterModel.MeshObject(g.FloorRoot, "FichaNegra", chipMesh, Mats.Lit, Vector3.zero).transform;
        }

        public void Cancel() { next = Volleys.Length; }
    }

    // DORADO — Jackpot: caen fichas por la sala; quien las recoge gana un premio temporal (vida, prisa, inmunidad u oro)
    public sealed class JackpotAbility : IRouletteAbility
    {
        static readonly string[] Kinds = { "heal", "haste", "guard", "gold" };
        readonly CrupierBoss B;
        float t;
        public JackpotAbility(CrupierBoss b) { B = b; }
        public string Title { get { return "¡JACKPOT! Recoged las fichas"; } }
        public bool HoldsDealer { get { return t < 0.6f; } }
        public void Begin()
        {
            t = 0f;
            B.Say("¡Vaya… la casa paga!");
            var spots = B.Casino.FreeSpots(4, 1.5f, B, 2.5f);
            for (int i = 0; i < spots.Count; i++) B.Casino.SpawnChip(spots[i].x, spots[i].y, Kinds[(i + (int)(B.G.Rand.Next() * Kinds.Length)) % Kinds.Length]);
            B.G.Audio.Play("chest");
        }
        public bool Tick(float dt) { t += dt; return t >= 0.6f && B.Casino.Chips.Count == 0; }
        public void Cancel() { B.Casino.ClearChips(); }
    }

    // AZUL — Cambio de mesa: avisos en el suelo y suben mesas de juego que cortan el paso un rato
    public sealed class TableChangeAbility : IRouletteAbility
    {
        public const float Life = 8f;
        readonly CrupierBoss B;
        float t;
        public TableChangeAbility(CrupierBoss b) { B = b; }
        public string Title { get { return "Cambio de mesa"; } }
        public bool HoldsDealer { get { return t < 1.1f; } }
        public void Begin()
        {
            t = 0f;
            foreach (var s in B.Casino.FreeSpots(5, 1.8f, B, 2.6f))
            {
                var spot = s;
                B.Telegraph(Shape.Circle(spot.x, spot.y, 1.0f), 1.0f, h => { if (!B.Dead) B.Casino.SpawnTable(spot.x, spot.y, Life); }, 0x2f6fd8);
            }
        }
        public bool Tick(float dt) { t += dt; return t >= 1.1f && B.Casino.Tables.Count == 0; }
        public void Cancel() { B.Casino.ClearTables(); }
    }

    // MORADO — Doble o nada: aparece una ilusión idéntica (a veces intercambian el sitio). Un golpe la deshace
    public sealed class DoubleOrNothingAbility : IRouletteAbility
    {
        public const float Life = 10f;
        readonly CrupierBoss B;
        float t;
        public bool Swapped { get; private set; }
        public DoubleOrNothingAbility(CrupierBoss b) { B = b; }
        public string Title { get { return "Doble o nada: ¿cuál es el bueno?"; } }
        public bool HoldsDealer { get { return t < 0.8f; } }
        public void Begin()
        {
            t = 0f; Swapped = false;
            var spots = B.Casino.FreeSpots(1, 2.5f, B, 4f);
            Vector2 s = spots.Count > 0 ? spots[0] : new Vector2(B.X + 3f, B.Z);
            var ill = new CrupierIllusion(B.G, s.x, s.y, B);
            B.Illusion = ill;
            B.G.Enemies.Add(ill);
            if (B.G.Rand.Next() < 0.5f)
            {
                // Intercambian el sitio en una nube de humo
                float bx = B.X, bz = B.Z;
                B.Body.X = ill.X; B.Body.Z = ill.Z; B.SyncRoot();
                ill.Body.X = bx; ill.Body.Z = bz; ill.SyncRoot();
                Swapped = true;
            }
            foreach (Enemy e in new Enemy[] { B, ill }) B.G.Effects.Burst(e.X, 1.2f, e.Z, BurstOpts.Of(18, 0x8a3fd0, 4f, 3f, 0.2f));
            B.G.Audio.Play("swap");
            B.Say("¿Cuál soy?");
            ill.Say("¿Cuál soy?");
        }
        public bool Tick(float dt)
        {
            t += dt;
            var ill = B.Illusion;
            if (ill == null || ill.Dead) { B.Illusion = null; return true; }
            if (t >= Life) { ill.Vanish(false); B.Illusion = null; return true; }
            return false;
        }
        public void Cancel()
        {
            if (B.Illusion != null && !B.Illusion.Dead) B.Illusion.Vanish(false);
            B.Illusion = null;
        }
    }

    // Ilusión del Crupier: igual por fuera, pero se deshace con un golpe y sus cartas (moradas) hacen menos daño
    public sealed class CrupierIllusion : Boss
    {
        CrupierLook look;
        CrupierBody body;
        bool thrown;

        public CrupierIllusion(Game g, float x, float z, CrupierBoss master) : base(g, x, z, BossCfg(c =>
        {
            c.Name = "El Crupier"; c.Hp = 1; c.Speed = 2.3f; c.Radius = 0.8f; c.Mass = 7; c.BarY = 3.0f; c.Coins = new[] { 0, 0 };
        }))
        {
            MaxHp = Hp = 1;
            IsAwake = true;
            PatternCd = 0.8f;
            AddPattern(new BossPattern
            {
                Id = "cartas", Weight = 1, Cooldown = 2.6f,
                Can = (b, pl, dist) => pl != null && dist < 16f,
                Start = b => { thrown = false; var t = b.PickTarget(0f); if (t != null) b.Telegraph(Shape.Line(b.X, b.Z, t.X, t.Z, 0.25f), 0.55f, null, 0xfff0d0); },
                Update = (b, dt, pl, dist) =>
                {
                    var t = b.Target ?? pl;
                    if (!thrown && t != null) b.FaceTo(t.X, t.Z, 8f, dt);
                    if (!thrown && b.PatternT >= 0.55f) { thrown = true; if (t != null && !b.Dead) CrupierBoss.CardFan(b, t, 3, 4f, 10f, 0xd8b0ff); }
                    return b.PatternT > 0.9f;
                },
            });
        }

        protected override void BuildModel()
        {
            var cfg = RouletteConfig.Load();
            body = CrupierBody.Build(Model, cfg);
            bool asset = body != null || UseAnimatedModel(cfg.CrupierModel, cfg.CrupierHeight, 0, 0f);
            look = new CrupierLook(Model, !asset);
        }

        protected override void Animate(float dt)
        {
            if (look == null) return;
            float aL = Mathf.Sin(Anim * 2.2f) * 0.25f, aR = Is("cartas") && PatternT < 0.55f ? -2.2f : -aL;
            look.Animate(dt, aL, aR, false, 0f);
            if (body != null) { body.Speed = MathUtil.Hypot(Vx, Vz); body.SpeedRef = Cfg.Speed; body.Acting = Current != null; }
        }

        public override void TakeHit(Hit hit)
        {
            if (Dead) return;
            Vanish(true);
        }

        public override void Damage(float amount, bool env, string label) { if (amount > 0f) Vanish(true); }

        // Se deshace en humo morado. byHit: alguien la ha descubierto
        public void Vanish(bool byHit)
        {
            if (Dead) return;
            G.Effects.Burst(X, 1.2f, Z, BurstOpts.Of(20, 0x8a3fd0, 4f, 3f, 0.2f));
            if (byHit) { G.Ui.FloatText(X, 2.8f, Z, "¡Era una ilusión!", TextStyle.Big); G.Audio.Play("swap"); }
            Die("vanish");
        }
    }
}
