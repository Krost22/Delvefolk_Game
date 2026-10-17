using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // 🔔 LA CAMPANA DEL JUICIO (BellBoss.js) — jefe inmóvil en el centro de la arena. MECÁNICA COOPERATIVA: solo se le
    // puede hacer daño mientras DOS placas doradas están pisadas A LA VEZ (un jugador en cada una; en solitario, un
    // jugador en una y un barril empujado a la otra; también cuentan los enemigos vivos). Entonces la campana se levanta
    // (5 s vulnerable, con el badajo a la vista) y baja con un ¡CLONG!; después las placas están 2 s sin funcionar.
    // En la fase 2 las placas cambian de sitio cada vez que la campana cierra.
    // Patrones (siempre activos; menos frecuentes con la campana abierta): Tañido (ondas que se esquivan saltando),
    // Eco (cono con aviso), Fantasmas (invoca ecos) y Tañido doble (solo fase 2).
    public static class BellConfig
    {
        public const float CenterZ = -15f;                       // z del centro de la arena (15 del web)
        public const float Lift = 1.6f, RiseT = 0.5f, OpenT = 4.5f, CloseT = 0.5f, LockT = 2.0f;   // abierta 5 s
        public const float PlateR = 1.0f;                        // radio de pisada de cada placa
        // Parejas de posiciones opuestas para las placas (la primera es la inicial); z ya invertida
        public static readonly Vector2[][] Pairs =
        {
            new[] { new Vector2(6, -16), new Vector2(26, -16) },
            new[] { new Vector2(8, -9), new Vector2(24, -21) },
            new[] { new Vector2(8, -22), new Vector2(24, -8) },
            new[] { new Vector2(16, -8), new Vector2(16, -22) },
        };
        public const float BarrelRespawn = 15f;                  // s sin barriles vivos hasta que cae uno nuevo
        public const float TanidoVibe = 0.8f, TanidoGap = 0.6f, TanidoDamage = 14, TanidoKnock = 10, TanidoSpeed = 6, TanidoMaxR = 16;
        public const float EcoArc = 0.9f, EcoRange = 12, EcoWarn = 1.0f, EcoDamage = 15, EcoKnock = 14;
        public const int FantasmasMax = 4, FantasmasCount = 2; public const float FantasmasWarn = 0.8f;
        public const float DobleWarn = 0.6f, DobleSpeed = 7, DobleMaxR = 16, DobleDamage = 14, DobleKnock = 10;
        public static readonly Vector2[] DoblePoints = { new Vector2(8, -9), new Vector2(24, -21) };
    }

    public sealed class BellPlate
    {
        public float X, Z;
        public Transform Group;
        public Renderer Disc;
        public bool Pressed;
        public int Color = 0xd4a72c;
    }

    public sealed class BellBoss : Boss
    {
        public string Stage = "closed";   // closed → opening → open → closing → lockout → closed
        public float StageT, LiftY, VibT, ClongT, BarrelT = BellConfig.BarrelRespawn;
        public int PairIdx;
        public readonly List<Enemy> Minions = new List<Enemy>();
        public readonly List<BellPlate> Plates = new List<BellPlate>();
        List<Telegraph> handles = new List<Telegraph>();   // avisos propios (se cancelan al morir o al destruir)
        Telegraph barrelWarn;
        Transform bell, clapper;
        Renderer runeR;
        float runeOpacity = 0.35f;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public bool IsOpen { get { return Stage == "opening" || Stage == "open" || Stage == "closing"; } }

        public BellBoss(Game g, float x, float z) : base(g, x, z, BossCfg(c =>
        {
            c.Name = "La Campana del Juicio"; c.Hp = 400; c.Speed = 0; c.Radius = 1.4f; c.Mass = 999; c.KnockDrag = 50;
            c.BarY = 3.6f; c.Coins = new[] { 35, 45 }; c.Color = 0xc9a227;
        }))
        {
            // Se coloca en el centro de la arena (la aparición está arriba)
            Body.X = Grid.X(g.BossFloor != null ? g.BossFloor.Cx : 8);
            Body.Z = BellConfig.CenterZ;
            SyncRoot();
            Facing = Mathf.PI;
            Invulnerable = true;      // cerrada hasta que se pisan las dos placas
            foreach (var p in BellConfig.Pairs[0]) Plates.Add(MakePlate(p.x, p.y));

            int fired = 0;
            AddPattern(new BossPattern
            {
                Id = "tanido", Cooldown = 4, Phases = new[] { 1, 2 },
                WeightFn = () => IsOpen ? 0.5f : 2f,
                Start = b => { fired = 0; VibT = BellConfig.TanidoVibe; b.Say("¡DONG!"); },
                Update = (b, dt, pl, dist) =>
                {
                    int n = b.Phase >= 2 ? 3 : 2;
                    if (fired < n && b.PatternT >= BellConfig.TanidoVibe + fired * BellConfig.TanidoGap)
                    {
                        fired++;
                        b.Wave(new Wave { Speed = BellConfig.TanidoSpeed, MaxR = BellConfig.TanidoMaxR, Damage = BellConfig.TanidoDamage, Knock = BellConfig.TanidoKnock });
                    }
                    return fired >= n && b.PatternT >= BellConfig.TanidoVibe + (n - 1) * BellConfig.TanidoGap + 0.3f;
                },
            });

            Shape ecoShape = null;
            AddPattern(new BossPattern
            {
                Id = "eco", Cooldown = 3, Phases = new[] { 1, 2 },
                WeightFn = () => IsOpen ? 1f : 2f,
                Start = b =>
                {
                    var t = b.Target != null && b.Target.Targetable ? b.Target : FirstAlive();
                    ecoShape = null;
                    if (t == null) return;
                    // Cono hacia el jugador elegido ahora: el aviso no le sigue
                    var shape = Shape.Cone(b.X, b.Z, Mathf.Atan2(t.X - b.X, t.Z - b.Z), BellConfig.EcoArc, BellConfig.EcoRange);
                    ecoShape = shape;
                    b.Say("¡ECO!");
                    Own(b.Telegraph(shape, BellConfig.EcoWarn, h => BossHits.HitPlayersIn(G, shape, BellConfig.EcoDamage, BellConfig.EcoKnock, b.X, b.Z)));
                },
                Update = (b, dt, pl, dist) => ecoShape == null || b.PatternT >= BellConfig.EcoWarn + 0.1f,
            });

            AddPattern(new BossPattern
            {
                Id = "fantasmas", Weight = 1, Cooldown = 8, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => AliveMinions() + BellConfig.FantasmasCount <= BellConfig.FantasmasMax,
                Start = b =>
                {
                    b.Say("¡Ecos del pasado!");
                    for (int i = 0; i < BellConfig.FantasmasCount; i++)
                    {
                        // Aparecen en los bordes de la arena, uno a cada lado
                        float fx = i % 2 == 0 ? 4.5f : 27.5f, fz = -(9f + G.Rand.Next() * 12f);
                        Own(b.Telegraph(Shape.Circle(fx, fz, 0.9f), BellConfig.FantasmasWarn, h => Summon(fx, fz)));
                    }
                },
                Update = (b, dt, pl, dist) => b.PatternT >= BellConfig.FantasmasWarn + 0.2f,
            });

            AddPattern(new BossPattern
            {
                Id = "tanido_doble", Weight = 1.5f, Cooldown = 5, Phases = new[] { 2 },
                Start = b =>
                {
                    VibT = BellConfig.DobleWarn;
                    b.Say("¡DONG DONG!");
                    // Dos ondas a la vez, desde dos puntos distintos de la arena (cada una con su aviso)
                    foreach (var pt in BellConfig.DoblePoints)
                    {
                        var p0 = pt;
                        Own(b.Telegraph(Shape.Circle(p0.x, p0.y, 1.0f), BellConfig.DobleWarn, h => G.Telegraphs.AddWave(p0.x, p0.y,
                            new Wave { Speed = BellConfig.DobleSpeed, MaxR = BellConfig.DobleMaxR, Damage = BellConfig.DobleDamage, Knock = BellConfig.DobleKnock })));
                    }
                },
                Update = (b, dt, pl, dist) => b.PatternT >= BellConfig.DobleWarn + 0.4f,
            });
        }

        Player FirstAlive() { var a = AlivePlayers(); return a.Count > 0 ? a[0] : null; }

        protected override void BuildModel()
        {
            Color stone = MathUtil.Hex(0x5b5148), bronze = MathUtil.Hex(0xb07a2a), dark = MathUtil.Hex(0x6b4a1c);
            var root = new GameObject("Campana").transform;
            root.SetParent(Model, false);
            // Soporte de piedra
            var sb = new MeshBuilder();
            sb.Add(ProcMesh.Cylinder(1.25f, 1.45f, 0.5f, 10), new Vector3(0, 0.25f, 0), stone);
            sb.Add(ProcMesh.Cylinder(1.0f, 1.25f, 0.12f, 10), new Vector3(0, 0.56f, 0), dark);
            CharacterModel.MeshObject(root, "Soporte", sb.ToMesh("soporteCampana"), Mats.Lit, Vector3.zero);
            // Campana: perfil de revolución, boca abierta abajo y techo cerrado
            var prof = new List<Vector2>
            {
                new Vector2(1.12f, 0), new Vector2(1.08f, 0.12f), new Vector2(0.98f, 0.4f), new Vector2(0.82f, 0.9f), new Vector2(0.6f, 1.45f),
                new Vector2(0.42f, 1.8f), new Vector2(0.32f, 2.0f), new Vector2(0.3f, 2.12f), new Vector2(0.0f, 2.2f),
            };
            System.Func<float, float> rAt = y =>
            {
                for (int i = 1; i < prof.Count; i++)
                    if (y <= prof[i].y)
                    {
                        float dy = prof[i].y - prof[i - 1].y; if (dy == 0f) dy = 1f;
                        return prof[i - 1].x + (prof[i].x - prof[i - 1].x) * ((y - prof[i - 1].y) / dy);
                    }
                return 0f;
            };
            bell = new GameObject("Campana").transform;
            bell.SetParent(root, false);
            bell.localPosition = new Vector3(0, 0.5f, 0);
            CharacterModel.MeshObject(bell, "Bronce", new MeshBuilder().Add(ProcMesh.Lathe(prof, 16), Vector3.zero, bronze).ToMesh("campana"), Mats.LitDouble, Vector3.zero);
            var rb = new MeshBuilder();
            rb.Add(ProcMesh.Torus(1.1f, 0.06f, 6, 20), Matrix4x4.TRS(new Vector3(0, 0.02f, 0), ProcMesh.EulerXYZ(Mathf.PI / 2f, 0, 0), Vector3.one), dark);
            rb.Add(ProcMesh.Cylinder(0.12f, 0.16f, 0.22f, 8), new Vector3(0, 2.3f, 0), dark);
            CharacterModel.MeshObject(bell, "Borde", rb.ToMesh("bordeCampana"), Mats.Lit, Vector3.zero);
            // Runas que brillan alrededor de la campana (una sola malla: se ilumina de golpe)
            var runes = new MeshBuilder();
            foreach (float ry in new[] { 0.55f, 1.2f, 1.75f })
                for (int k = 0; k < 6; k++)
                {
                    float th = k / 6f * Mathf.PI * 2f + ry, r = rAt(ry) + 0.01f;
                    runes.Add(ProcMesh.Box(0.1f, 0.22f, 0.02f), Matrix4x4.TRS(new Vector3(Mathf.Sin(th) * r, ry, Mathf.Cos(th) * r), ProcMesh.EulerXYZ(0, th, 0), Vector3.one), MathUtil.Hex(0x7ff6ff));
                }
            runeR = CharacterModel.MeshObject(bell, "Runas", runes.ToMesh("runas"), Mats.Transparent, Vector3.zero).GetComponent<Renderer>();
            Shape.SetOpacity(runeR, runeOpacity);
            // Badajo: dentro de la campana, solo se ve cuando se levanta
            clapper = new GameObject("Badajo").transform;
            clapper.SetParent(bell, false);
            CharacterModel.MeshObject(clapper, "Bola", new MeshBuilder().Add(ProcMesh.Sphere(0.28f, 10, 8), new Vector3(0, 0.3f, 0), MathUtil.Hex(0xffe9a0)).ToMesh("badajo"), Mats.Unlit, Vector3.zero);
            CharacterModel.MeshObject(clapper, "Vara", new MeshBuilder().Add(ProcMesh.Cylinder(0.05f, 0.05f, 1.6f, 5), new Vector3(0, 1.1f, 0), dark).ToMesh("varaBadajo"), Mats.Lit, Vector3.zero);
            clapper.gameObject.SetActive(false);
        }

        // ---------- Placas de presión (cuelgan del piso; se quitan en Destroy) ----------
        static Mesh discMesh, ringMesh;
        BellPlate MakePlate(float x, float z)
        {
            float R = BellConfig.PlateR;
            if (!discMesh)
            {
                discMesh = new MeshBuilder().Add(ProcMesh.Cylinder(R, R, 0.12f, 20), new Vector3(0, 0.06f, 0), Color.white).ToMesh("placa");
                ringMesh = new MeshBuilder().Add(ProcMesh.Torus(R * 0.7f, 0.04f, 4, 20), Matrix4x4.TRS(new Vector3(0, 0.13f, 0), ProcMesh.EulerXYZ(Mathf.PI / 2f, 0, 0), Vector3.one), MathUtil.Hex(0x6b4a1c)).ToMesh("aroPlaca");
            }
            var group = new GameObject("Placa").transform;
            group.SetParent(G.FloorRoot, false);
            group.position = new Vector3(x, 0, z);
            var disc = CharacterModel.MeshObject(group, "Disco", discMesh, Mats.Lit, Vector3.zero).GetComponent<Renderer>();
            CharacterModel.MeshObject(group, "Aro", ringMesh, Mats.Lit, Vector3.zero);
            var pl = new BellPlate { X = x, Z = z, Group = group, Disc = disc };
            SetColor(disc, pl.Color);
            return pl;
        }

        static void SetColor(Renderer r, int hex)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Mats.BaseColorId, MathUtil.Hex(hex));
            r.SetPropertyBlock(mpb);
        }

        // Una placa está pisada si hay encima (a menos de R) un jugador vivo que no salta, un barril o un enemigo vivo
        public bool PlatePressed(BellPlate pl)
        {
            float R = BellConfig.PlateR;
            foreach (var p in G.Players) if (!p.Dead && p.JumpY == 0f && MathUtil.Hypot(p.X - pl.X, p.Z - pl.Z) < R) return true;
            foreach (var b in G.Barrels) if (!b.Dead && !b.Removed && !b.Falling && MathUtil.Hypot(b.X - pl.X, b.Z - pl.Z) < R) return true;
            foreach (var e in G.Enemies) if (e != this && !e.Dead && !e.Removed && e.State != EnemyState.Falling && MathUtil.Hypot(e.X - pl.X, e.Z - pl.Z) < R) return true;
            return false;
        }

        void RelocatePlates()
        {
            int i = PairIdx;
            while (i == PairIdx) i = Mathf.FloorToInt(G.Rand.Next() * BellConfig.Pairs.Length);
            PairIdx = i;
            for (int k = 0; k < Plates.Count; k++)
            {
                var pl = Plates[k];
                float ox = pl.X, oz = pl.Z;
                pl.X = BellConfig.Pairs[i][k].x; pl.Z = BellConfig.Pairs[i][k].y;
                pl.Group.position = new Vector3(pl.X, 0, pl.Z);
                G.Effects.Burst(ox, 0.3f, oz, BurstOpts.Of(10, 0xffd060, 3f, 3f, 0.12f));
                G.Effects.Burst(pl.X, 0.3f, pl.Z, BurstOpts.Of(10, 0xffd060, 3f, 3f, 0.12f));
                G.Effects.Ring(pl.X, pl.Z, new RingOpts { Color = 0xffd060, Radius = 2.2f, Life = 0.5f, Y = 0.06f, R0 = 0.3f, Opacity = 0.8f });
            }
            G.Audio.Play("gate");
            G.Ui.FloatText(X, 3.2f, Z, "¡LAS PLACAS SE MUEVEN!", TextStyle.Label);
        }

        // ---------- Apertura y cierre de la campana ----------
        void SetStage(string s) { Stage = s; StageT = 0f; }

        void BeginOpen()
        {
            SetStage("opening");
            Invulnerable = false;
            LiftY = 0f;
            G.Audio.Play("alert");
            G.CameraCtl.AddShake(0.5f);
            G.Effects.Ring(X, Z, new RingOpts { Color = 0xffe9a0, Radius = 3f, Life = 0.5f, Y = 0.06f, R0 = 0.3f, Opacity = 0.8f });
            G.Ui.FloatText(X, 3.2f, Z, "¡AHORA!", TextStyle.Big);
        }

        void StartClose() { SetStage("closing"); Invulnerable = true; }

        void EndClose()
        {
            LiftY = 0f;
            ClongT = 0.25f;
            SetStage("lockout");
            G.Audio.Play("crash");
            G.CameraCtl.AddShake(0.5f);
            G.Effects.Burst(X, 0.4f, Z, BurstOpts.Of(14, 0xc9a227, 4f, 3f, 0.2f));
            G.Ui.FloatText(X, 3.0f, Z, "¡CLONG!", TextStyle.Big);
            if (Phase >= 2) RelocatePlates();
        }

        // Barriles para jugar en solitario: sin barriles vivos, cae uno nuevo cerca de una placa cada 15 s
        void DropBarrel()
        {
            var pl = Plates[Mathf.FloorToInt(G.Rand.Next() * Plates.Count)];
            // Cae a 1,8 de la placa, hacia el centro, para que se pueda empujar encima
            float dx = X - pl.X, dz = Z - pl.Z, l = MathUtil.Hypot(dx, dz); if (l == 0f) l = 1f;
            float bx = pl.X + dx / l * 1.8f, bz = pl.Z + dz / l * 1.8f;
            barrelWarn = Own(Telegraph(Shape.Circle(bx, bz, 0.9f), 1.5f, h =>
            {
                barrelWarn = null;
                G.Barrels.Add(new Barrel(G, bx, bz, false));
                BarrelT = BellConfig.BarrelRespawn;
            }));
        }

        void UpdateBell(float dt)
        {
            if (!IsAwake || Dead) return;
            foreach (var pl in Plates)
            {
                pl.Pressed = PlatePressed(pl);
                int col = pl.Pressed ? 0x3cff6a : 0xd4a72c;
                if (pl.Color != col) { pl.Color = col; SetColor(pl.Disc, col); }
            }
            if (Plates.TrueForAll(pl => pl.Pressed) && Stage == "closed") BeginOpen();

            bool alive = G.Barrels.Exists(b => !b.Dead && !b.Removed);
            if (alive) BarrelT = BellConfig.BarrelRespawn;
            else if (barrelWarn == null) { BarrelT -= dt; if (BarrelT <= 0f) DropBarrel(); }

            StageT += dt;
            if (Stage == "opening")
            {
                float k = Mathf.Min(1f, StageT / BellConfig.RiseT);
                LiftY = BellConfig.Lift * (1f - Mathf.Pow(1f - k, 3f));
                if (k >= 1f) SetStage("open");
            }
            else if (Stage == "open")
            {
                LiftY = BellConfig.Lift + Mathf.Sin(StageT * 4f) * 0.03f;
                if (StageT >= BellConfig.OpenT) StartClose();
            }
            else if (Stage == "closing")
            {
                float k = Mathf.Min(1f, StageT / BellConfig.CloseT);
                LiftY = BellConfig.Lift * (1f - k * k);
                if (k >= 1f) EndClose();
            }
            else if (Stage == "lockout")
            {
                LiftY = 0f;
                if (StageT >= BellConfig.LockT) SetStage("closed");
            }
        }

        // ---------- Sin movimiento, mecánica de placas y patrones ----------
        protected override Vector2 Idle(float dt, Player player, float dist, float dx, float dz) { return Vector2.zero; }

        protected override bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv)
        {
            UpdateBell(dt);
            return base.UpdateState(dt, player, dist, dx, dz, out mv);
        }

        public int AliveMinions()
        {
            Minions.RemoveAll(e => e.Dead || e.Removed);
            return Minions.Count;
        }

        public Enemy Summon(float x, float z)
        {
            if (AliveMinions() >= BellConfig.FantasmasMax) return null;
            var e = SpawnMinion('S', x, z, new EnemyOpts { Scale = 0.7f, Cfg = c => { c.Hp = 16; c.Damage = 6; c.Color = 0xc0d0ff; } });
            if (e != null) Minions.Add(e);
            return e;
        }

        Telegraph Own(Telegraph h) { handles.RemoveAll(x => x.Removed); handles.Add(h); return h; }
        void CancelOwn() { foreach (var h in handles) if (h != null) h.Cancel(); handles.Clear(); }

        protected override void Animate(float dt)
        {
            if (!bell) return;
            bell.localPosition = new Vector3(0, 0.5f + LiftY, 0);
            bool cl = LiftY > 0.05f;
            if (clapper.gameObject.activeSelf != cl) clapper.gameObject.SetActive(cl);
            // Vibración al tañer y aplastamiento al cerrar
            VibT = Mathf.Max(0f, VibT - dt);
            bell.localRotation = ProcMesh.EulerXYZ(0, 0, VibT > 0f ? Mathf.Sin(Anim * 60f) * 0.05f * (0.4f + VibT) : 0f);
            ClongT = Mathf.Max(0f, ClongT - dt);
            bell.localScale = new Vector3(1f, ClongT > 0f ? 1f - Mathf.Sin(ClongT / 0.25f * Mathf.PI) * 0.06f : 1f, 1f);
            // Brillo: fuerte al tañer, pulsante con la campana abierta, tenue cerrada
            float glow = VibT > 0f ? 1f : IsOpen ? 0.75f + Mathf.Sin(Anim * 7f) * 0.2f : 0.35f;
            runeOpacity += (glow - runeOpacity) * Mathf.Min(1f, dt * 10f);
            Shape.SetOpacity(runeR, runeOpacity);
        }

        public override void Die(string cause)
        {
            CancelOwn();
            base.Die(cause);
        }

        public override void Destroy()
        {
            if (Removed) return;
            CancelOwn();
            foreach (var pl in Plates) if (pl.Group) Object.Destroy(pl.Group.gameObject);
            Plates.Clear();
            base.Destroy();
        }
    }

    public static partial class BossRegistrations
    {
        static partial void Bell()
        {
            Bosses.Register("bell", new BossEntry
            {
                Name = "La Campana del Juicio", Title = "Tañe por vosotros", Icon = "🔔",
                Hint = "Pisad las dos placas doradas a la vez para levantarla y pegadle al badajo mientras esté abierta. Solos: empujad un barril a una placa. Saltad las ondas del tañido.",
                ArenaW = 17, ArenaH = 15,
                Decorate = (put, W, H) =>
                {
                    // Barriles junto a cada placa (izquierda x≈6, derecha x≈26) y dos púas en las esquinas
                    put(3, 6, 'b'); put(5, 9, 'b');
                    put(13, 6, 'b'); put(11, 9, 'b');
                    put(2, 4, '^'); put(14, 11, '^');
                },
                Spawn = (g, x, z) => new List<Boss> { new BellBoss(g, x, z) },
            });
        }
    }
}
