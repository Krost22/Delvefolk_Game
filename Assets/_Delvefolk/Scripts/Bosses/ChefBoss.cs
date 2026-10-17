using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // 🍳 MONSIEUR BOCADO, EL CHEF CARNICERO (ChefBoss.js) — jefe de la cocina.
    // Mecánicas cooperativas:
    //  - AGARRAR: te agarra con la mano y te lleva al CALDERO. Dentro te cueces (daño cada segundo).
    //    Sales machacando botones, o tu compañero le vuelca el caldero a golpes (3 golpes junto a él).
    //  - PLATOS: lanza platos hacia donde estáis. Al caer dejan comida: muslo de pollo o QUESO PODRIDO
    //    (si te lo comes, te confunde y te hace daño). Dar de comer al compañero es lo que le cura.
    //  - CUCHILLAS: lanza 3 cuchillas que se pueden devolver; si una le golpea, queda aturdido.
    //  - TAJO: cuchillada de cerca, con aviso en abanico.
    //  - FASE 2 (mitad de vida): '¡SE ME QUEMA LA COCINA!': fogones en los bordes que se encienden por turnos.
    public static class ChefConfig
    {
        public const float CauldronX = 9f, CauldronZ = -17f;   // (9, 17) del web
        public const float CarrySpeed = 3.0f;      // velocidad al llevar al jugador hacia el caldero
        public const float CarryMax = 2.5f;        // segundos máximos caminando con el jugador
        public const float CauldronReach = 10f;    // solo agarra si el caldero está a menos de esta distancia
        public const float CookDps = 5f;           // daño por segundo dentro del caldero
        public const int RescueHits = 3;           // golpes junto al caldero para volcarlo
        public const float TipTime = 6f;           // segundos volcado tras un rescate
        public const float PlateVy = 9f, PlateG = 18f, PlateY0 = 2.0f;
        // Tiempo de vuelo de un plato lanzado en parábola hasta el suelo (y = 0,15)
        public static readonly float Flight = (PlateVy + Mathf.Sqrt(PlateVy * PlateVy + 2f * PlateG * (PlateY0 - 0.15f))) / PlateG;
        public static readonly Vector2Int[] FireTiles = { new Vector2Int(1, 4), new Vector2Int(1, 8), new Vector2Int(1, 12), new Vector2Int(15, 4), new Vector2Int(15, 8), new Vector2Int(15, 12) };
        public const float FireCycle = 8f;         // aviso 1,5 s, fuego 2,5 s, apagado 4 s; grupos desfasados 4 s
    }

    // ---------------------------------------------------------------- Caldero
    // Hace de "agarrador" del jugador que se cuece (mismo contrato que un enemigo que agarra)
    public sealed class ChefCauldron : IGrabHolder
    {
        readonly ChefBoss boss;
        readonly Game G;
        public readonly float X, Z;
        public Player Held;
        public bool Dead, Removed;
        public float BoilAcc, TipT, TipK, BubbleT;
        public int Rescues;
        HashSet<AttackState> seen = new HashSet<AttackState>();
        readonly Transform group, pivot;
        readonly List<Transform> bubbles = new List<Transform>();
        readonly Body body;

        Player IGrabHolder.Held { get { return Held; } }
        bool IGrabHolder.Dead { get { return Dead; } }
        bool IGrabHolder.Removed { get { return Removed; } }
        float IGrabHolder.Facing { get { return 0f; } }
        float IGrabHolder.EscapePresses { get { return 10f; } }
        float IGrabHolder.EscapeDecay { get { return 1.0f; } }

        public ChefCauldron(ChefBoss boss, float x, float z)
        {
            this.boss = boss; G = boss.G; X = x; Z = z;
            group = new GameObject("Caldero").transform;
            group.SetParent(G.FloorRoot, false);
            group.position = new Vector3(x, 0, z);
            pivot = new GameObject("Vuelco").transform;   // lo que se vuelca
            pivot.SetParent(group, false);
            Color black = MathUtil.Hex(0x1e1d22), green = MathUtil.Hex(0x6fcf3a);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(1.05f, 0.85f, 1.15f, 12), new Vector3(0, 0.7f, 0), black);
            b.Add(ProcMesh.Torus(1.0f, 0.09f, 6, 14), new Vector3(0, 1.28f, 0), black);
            b.Add(ProcMesh.Cylinder(0.97f, 0.97f, 0.08f, 12), new Vector3(0, 1.15f, 0), green);
            for (int i = 0; i < 3; i++) { float a = i * 2.1f; b.Add(ProcMesh.Cylinder(0.07f, 0.09f, 0.2f, 5), new Vector3(Mathf.Cos(a) * 0.7f, 0.1f, Mathf.Sin(a) * 0.7f), black); }
            CharacterModel.MeshObject(pivot, "Olla", b.ToMesh("caldero"), Mats.Lit, Vector3.zero);
            // Burbujas que suben por el líquido
            var bm = new MeshBuilder().Add(ProcMesh.Sphere(0.12f, 6, 4), Vector3.zero, MathUtil.Hex(0xc8ff9a)).ToMesh("burbujaCaldero");
            for (int i = 0; i < 5; i++) bubbles.Add(CharacterModel.MeshObject(pivot, "Burbuja", bm, Mats.Unlit, Vector3.zero).transform);
            // Cuerpo estático: los jugadores y el jefe chocan con él
            body = G.Physics.Add(new Body { X = x, Z = z, Radius = 1.0f, IsStatic = true, Kind = BodyKind.Static, Owner = this });
        }

        // Punto donde queda el jugador dentro: encima del caldero
        public Vector3 HoldPos() { return new Vector3(X, 0.9f, Z); }

        // Mete al jugador en el caldero (lo llama el jefe al llegar con él)
        public void Receive(Player p)
        {
            boss.Held = null;
            Held = p;
            p.OnGrabbedBy(this);
            BoilAcc = 0f;
            Rescues = 0;
            seen = new HashSet<AttackState>();
            G.Audio.Play("grab");
            G.Effects.Burst(X, 1.2f, Z, BurstOpts.Of(12, 0x9dff5a, 3f, 5f, 0.12f));
            G.Ui.FloatText(X, 2.6f, Z, "¡Se está cociendo!", TextStyle.Big);
        }

        // Suelta al jugador que está dentro. why: 'rescue' (volcado), 'escape' (forcejeo), 'dead', 'lost'
        public void ReleaseHeld(string why)
        {
            var p = Held;
            if (p == null) return;
            Held = null;
            // Sale por el lado contrario de donde venía el jefe, con un empujón hacia fuera
            float dx = X - boss.X, dz = Z - boss.Z, l = MathUtil.Hypot(dx, dz); if (l == 0f) l = 1f;
            dx /= l; dz /= l;
            if (p.GrabbedBy == this)
            {
                p.Body.X = X + dx * 2.2f; p.Body.Z = Z + dz * 2.2f;
                p.Body.Ignore = boss.Body; p.Body.IgnoreT = 0.4f;
                p.FreeFromGrab();
                p.SyncRoot();
            }
            if (p.Dead || why == "dead" || why == "lost") return;
            p.Knock(dx * 9f, dz * 9f);
            p.PopT = 0.5f;
            if (why == "rescue")
            {
                G.Audio.Play("clang");
                G.CameraCtl.AddShake(0.4f);
                G.Ui.FloatText(p.X, 2.8f, p.Z, "¡RESCATADO!", TextStyle.Big);
            }
            else if (why == "escape")
            {
                G.Audio.Play("breakFree");
                G.Ui.FloatText(p.X, 2.6f, p.Z, "¡Se escapa del caldero!", TextStyle.Label);
            }
        }

        // Vuelca el caldero tras el rescate: queda fuera de juego un rato
        void Tip()
        {
            TipT = ChefConfig.TipTime;
            ReleaseHeld("rescue");
            G.Effects.Burst(X, 1.0f, Z, BurstOpts.Of(18, 0x9dff5a, 4f, 4f, 0.14f));
        }

        // Cada frame: burbujeo, volcado, cocción y golpes de rescate
        public void Tick(float dt)
        {
            BubbleT += dt;
            for (int i = 0; i < bubbles.Count; i++)
            {
                float k = (BubbleT * 0.7f + i * 0.2f) % 1f;
                bubbles[i].localPosition = new Vector3(Mathf.Cos(i * 2.3f) * 0.5f, 1.12f + k * 0.3f, Mathf.Sin(i * 2.3f) * 0.5f);
                bubbles[i].localScale = Vector3.one * (0.4f + 0.6f * Mathf.Sin(k * Mathf.PI));
            }
            if (TipT > 0f)
            {
                TipT -= dt;
                if (TipT <= 0f) { TipT = 0f; Rescues = 0; seen = new HashSet<AttackState>(); }
            }
            TipK += ((TipT > 0f ? 1f : 0f) - TipK) * Mathf.Min(1f, dt * 6f);
            pivot.localRotation = ProcMesh.EulerXYZ(0, 0, TipK * 1.25f);
            pivot.localPosition = new Vector3(0, TipK * 0.45f, 0);

            var p = Held;
            if (p == null) return;
            if (p.Dead || p.GrabbedBy != this) { Held = null; return; }
            // Cocción: 5 de daño por segundo (daño entero acumulado)
            BoilAcc += ChefConfig.CookDps * dt;
            if (BoilAcc >= 1f)
            {
                int n = Mathf.FloorToInt(BoilAcc);
                BoilAcc -= n;
                p.Hurt(n, X, Z, 0f, new HurtOpts { Pierce = true });
                if (p.Dead) return;
            }
            // Rescate: golpes del OTRO jugador junto al caldero (cada ataque cuenta una vez)
            foreach (var q in G.Players)
            {
                var a = q.Attack;
                if (q == p || q.Dead || a == null || a.Phase != AttackPhase.Active || seen.Contains(a)) continue;
                if (MathUtil.Hypot(q.X - X, q.Z - Z) >= 2.2f) continue;
                seen.Add(a);
                Rescues++;
                G.Audio.Play("hit");
                G.Ui.FloatText(q.X, 2.6f, q.Z, "Golpe " + Rescues + "/" + ChefConfig.RescueHits, TextStyle.Small);
                G.Effects.Burst(X, 1.2f, Z, BurstOpts.Of(6, 0xffffff, 3f, 3f, 0.1f));
                if (Rescues >= ChefConfig.RescueHits) { Tip(); break; }
            }
        }

        public void Destroy()
        {
            if (Removed) return;
            ReleaseHeld("lost");
            Removed = true;
            G.Physics.Remove(body);
            if (group) Object.Destroy(group.gameObject);
        }
    }

    // ---------------------------------------------------------------- Queso podrido
    // Comida de la cocina: se come como el queso, pero no cura, hace daño y confunde (también si se lo das al compañero)
    public sealed class RottenCheeseFood : FoodItem
    {
        public RottenCheeseFood()
        {
            Id = "rottencheese"; Name = "Queso podrido"; Icon = "🤢";
            Heal = 0; EatTime = 0.9f;
            Style = "Huele fatal... no cura nada";
        }

        public override bool OnEaten(Player p, Game g)
        {
            if (p.Dead) return true;
            p.Hurt(12, p.X, p.Z, 0f, new HurtOpts { Pierce = true });
            if (p.Dead) return true;
            p.ConfusedT = 3f;
            g.Audio.Play("hit");
            g.Ui.FloatText(p.X, 2.8f, p.Z, "¡PUAJ!", TextStyle.Big);
            return true;
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.42f, 0.42f, 0.3f, 3, 1, false, 0, Mathf.PI / 2.2f), M(0, 0, 0.25f), MathUtil.Hex(0x7a4f9a));
            for (int i = 0; i < 3; i++) b.Add(ProcMesh.Sphere(0.07f + i * 0.02f, 5, 4), M(0.1f + i * 0.1f, 0.17f, 0.3f + (i % 2) * 0.1f), MathUtil.Hex(0x6cc04a));
            return MeshModel(parent, "quesoPodrido", b);
        }
    }

    // ---------------------------------------------------------------- Jefe
    public sealed class ChefBoss : Boss, IGrabHolder
    {
        public Player Held;               // jugador que lleva en alto (patrón agarrar)
        public readonly ChefCauldron Cauldron;
        public readonly BossPattern PAgarrar;
        public HazardFloor Hazard;
        sealed class GrabSt { public string Phase = "aviso"; public float T; }
        GrabSt grabSt;
        float fireT;
        List<Vector2Int>[] fireGroups;
        Transform armBlade, armLadle, body3d;
        float rxA, lxA;

        Player IGrabHolder.Held { get { return Held; } }
        bool IGrabHolder.Dead { get { return Dead; } }
        bool IGrabHolder.Removed { get { return Removed; } }
        float IGrabHolder.Facing { get { return Facing; } }
        float IGrabHolder.EscapePresses { get { return 6f; } }
        float IGrabHolder.EscapeDecay { get { return 1.4f; } }

        public ChefBoss(Game g, float x, float z) : base(g, x, z, BossCfg(c =>
        {
            c.Name = "Monsieur Bocado"; c.Hp = 460; c.Speed = 2.6f; c.Radius = 1.0f; c.Mass = 9; c.BarY = 3.6f;
            c.Coins = new[] { 35, 45 }; c.Color = 0xf2efe6;
        }))
        {
            Cauldron = new ChefCauldron(this, ChefConfig.CauldronX, ChefConfig.CauldronZ);

            PAgarrar = AddPattern(new BossPattern
            {
                Id = "agarrar", Weight = 2, Cooldown = 4.5f,
                Can = (b, pl, dist) => pl != null && dist < 7f && CanGrab(),
                Start = b =>
                {
                    var t = Nearest();
                    if (t != null) b.FaceTo(t.X, t.Z);
                    var st = new GrabSt();
                    grabSt = st;
                    // Aviso de línea corta delante del jefe (0,8 s)
                    var shape = Shape.Line(b.X, b.Z, b.X + Mathf.Sin(b.Facing) * 2.4f, b.Z + Mathf.Cos(b.Facing) * 2.4f, 1.4f);
                    b.Telegraph(shape, 0.8f, h => TryGrab(st, h.Shape), 0xff6a9a);
                },
                Update = (b, dt, pl, dist) => UpdateGrab(dt),
                End = b => { grabSt = null; ReleaseHeld("drop"); },
            });

            var plates = new List<Vector3>();   // (x, z, momento) de cada plato
            var platesDone = new List<bool>();
            float platesEnd = 0f;
            AddPattern(new BossPattern
            {
                Id = "platos", Weight = 2, Cooldown = 4,
                Can = (b, pl, dist) => pl != null,
                Start = b =>
                {
                    var alive = b.AlivePlayers(); int n = b.Phase > 1 ? 6 : 4;
                    plates.Clear(); platesDone.Clear();
                    for (int i = 0; i < n; i++)
                    {
                        var p = alive[i % alive.Count];
                        plates.Add(new Vector3(
                            Mathf.Clamp(p.X + (G.Rand.Next() - 0.5f) * 3f, 3f, 28f),
                            Mathf.Clamp(p.Z + (G.Rand.Next() - 0.5f) * 3f, -24f, -8f),
                            0.2f + i * 0.45f));
                        platesDone.Add(false);
                    }
                    platesEnd = plates[plates.Count - 1].z + ChefConfig.Flight + 0.3f;
                    b.Say("¡PLATOS AL AIRE!");
                },
                Update = (b, dt, pl, dist) =>
                {
                    for (int i = 0; i < plates.Count; i++)
                        if (!platesDone[i] && b.PatternT >= plates[i].z) { platesDone[i] = true; ThrowPlate(plates[i].x, plates[i].y); }
                    return b.PatternT > platesEnd;
                },
            });

            bool knivesDone = false;
            AddPattern(new BossPattern
            {
                Id = "cuchillos", Weight = 2, Cooldown = 3.2f,
                Can = (b, pl, dist) => pl != null && dist > 3f,
                Start = b =>
                {
                    knivesDone = false;
                    var t = Nearest();
                    if (t != null) b.FaceTo(t.X, t.Z);
                    b.Say("¡CUCHILLAS!");
                },
                Update = (b, dt, pl, dist) =>
                {
                    if (!knivesDone && b.PatternT >= 0.5f) { knivesDone = true; ThrowKnives(); }
                    return knivesDone && b.PatternT > 1.0f;
                },
            });

            AddPattern(new BossPattern
            {
                Id = "tajo", Weight = 2, Cooldown = 2,
                Can = (b, pl, dist) => pl != null && dist < 3.2f,
                Start = b =>
                {
                    var t = Nearest();
                    if (t != null) b.FaceTo(t.X, t.Z);
                    b.Telegraph(Shape.Cone(b.X, b.Z, b.Facing, 2.0f, 3.0f), 0.7f,
                        h => { if (!b.Dead) BossHits.HitPlayersIn(G, h.Shape, 14, 12, b.X, b.Z, true); }, 0xffb040);
                    b.Say("¡TAJO!");
                },
                Update = (b, dt, pl, dist) => b.PatternT > 0.9f,
            });
        }

        protected override void BuildModel()
        {
            Color white = MathUtil.Hex(0xf2efe6), apron = MathUtil.Hex(0xf4f1e8), stain = MathUtil.Hex(0x8a3b1c), dark = MathUtil.Hex(0x3a2a1e);
            Color skin = MathUtil.Hex(0xf0c9a0), steel = MathUtil.Hex(0xc8d0d8), wood = MathUtil.Hex(0x6a4426), iron = MathUtil.Hex(0x6b6b72), trousers = MathUtil.Hex(0x3a3f4c);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Icosahedron(0.92f, 1), Matrix4x4.TRS(new Vector3(0, 1.3f, 0), Quaternion.identity, new Vector3(1.05f, 1.05f, 0.95f)), white);
            b.Add(ProcMesh.Box(1.1f, 1.2f, 0.12f), new Vector3(0, 1.0f, 0.86f), apron);
            b.Add(ProcMesh.Box(0.2f, 0.15f, 0.03f), new Vector3(-0.25f, 1.2f, 0.94f), stain);
            b.Add(ProcMesh.Box(0.22f, 0.12f, 0.03f), new Vector3(0.3f, 0.8f, 0.94f), stain);
            b.Add(ProcMesh.Box(0.16f, 0.1f, 0.03f), new Vector3(0.1f, 0.5f, 0.94f), stain);
            b.Add(ProcMesh.Icosahedron(0.46f, 1), new Vector3(0, 2.1f, 0.02f), skin);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Add(ProcMesh.Box(0.32f, 0.09f, 0.12f), Matrix4x4.TRS(new Vector3(s * 0.15f, 1.98f, 0.44f), ProcMesh.EulerXYZ(0, 0, -s * 0.35f), Vector3.one), dark);
                b.Add(ProcMesh.Cylinder(0.2f, 0.22f, 0.6f, 7), new Vector3(s * 0.36f, 0.3f, 0), trousers);
                b.Add(ProcMesh.Box(0.32f, 0.18f, 0.46f), new Vector3(s * 0.36f, 0.09f, 0.1f), dark);
            }
            // Gorro alto de chef
            b.Add(ProcMesh.Cylinder(0.42f, 0.46f, 0.36f, 10), new Vector3(0, 2.6f, 0), white);
            b.Add(ProcMesh.Cylinder(0.56f, 0.42f, 0.6f, 10), new Vector3(0, 3.1f, 0), white);
            body3d = new GameObject("Chef").transform;
            body3d.SetParent(Model, false);
            CharacterModel.MeshObject(body3d, "Cuerpo", b.ToMesh("chef"), Mats.Lit, Vector3.zero);
            var eyes = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f }) eyes.Add(ProcMesh.Sphere(0.06f, 5, 4), new Vector3(s * 0.16f, 2.2f, 0.42f), MathUtil.Hex(0x1a1a1a));
            CharacterModel.MeshObject(body3d, "Ojos", eyes.ToMesh("ojosChef"), Mats.Unlit, Vector3.zero);
            armBlade = Arm(1f, white, skin);     // mano de la cuchilla
            armLadle = Arm(-1f, white, skin);    // mano del cucharón
            var bl = new MeshBuilder();
            bl.Add(ProcMesh.Cylinder(0.05f, 0.05f, 0.25f, 6), new Vector3(0, -1.2f, 0), wood);
            bl.Add(ProcMesh.Box(0.14f, 0.9f, 0.025f), new Vector3(0, -1.7f, 0), steel);
            CharacterModel.MeshObject(armBlade, "Cuchilla", bl.ToMesh("cuchillaChef"), Mats.Lit, Vector3.zero);
            var ld = new MeshBuilder();
            ld.Add(ProcMesh.Cylinder(0.035f, 0.035f, 0.8f, 6), new Vector3(0, -1.4f, 0), wood);
            ld.Add(ProcMesh.Cylinder(0.2f, 0.2f, 0.08f, 10), new Vector3(0, -1.85f, 0), iron);
            CharacterModel.MeshObject(armLadle, "Cucharon", ld.ToMesh("cucharonChef"), Mats.Lit, Vector3.zero);
        }

        Transform Arm(float sx, Color white, Color skin)
        {
            var pv = new GameObject("Brazo").transform;
            pv.SetParent(body3d, false);
            pv.localPosition = new Vector3(sx * 0.9f, 1.7f, 0);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.13f, 0.16f, 0.95f, 6), new Vector3(0, -0.48f, 0), white);
            b.Add(ProcMesh.Icosahedron(0.2f), new Vector3(0, -1.0f, 0), skin);
            CharacterModel.MeshObject(pv, "Brazo", b.ToMesh("brazoChef"), Mats.Lit, Vector3.zero);
            return pv;
        }

        protected override void Animate(float dt)
        {
            body3d.localPosition = new Vector3(0, Mathf.Sin(Anim * 2.4f) * 0.03f, 0);
            float rx = 0.15f, lx = 0.15f;
            if (Is("cuchillos")) rx = -2.0f;
            else if (Is("tajo")) rx = PatternT < 0.5f ? -2.6f : 1.1f;
            else if (Is("platos")) lx = -1.5f + Mathf.Sin(PatternT * 9f) * 0.4f;
            else if (Is("agarrar")) { rx = -1.4f; lx = -1.4f; }
            rxA = MathUtil.Damp(rxA, rx, 10f, dt);
            lxA = MathUtil.Damp(lxA, lx, 10f, dt);
            armBlade.localRotation = ProcMesh.EulerXYZ(rxA, 0, 0);
            armLadle.localRotation = ProcMesh.EulerXYZ(lxA, 0, 0);
        }

        public override void OnAwake() { Say("¡Ya está la cena!"); }

        // Fase 2: más velocidad, agarrar más a menudo y la cocina en llamas
        protected override void OnPhase(int n)
        {
            if (n < 2) return;
            Cfg.Speed = 3.0f;
            PAgarrar.Cooldown = 2.5f;
            Say("¡SE ME QUEMA LA COCINA!");
            BuildFires();
        }

        // ---------- Agarrar ----------
        float DistTo(float x, float z) { return MathUtil.Hypot(x - X, z - Z); }

        // Jugador atacable más cercano AHORA (Target puede ir con retraso)
        public Player Nearest()
        {
            Player best = null; float bd = float.PositiveInfinity;
            foreach (var p in AlivePlayers()) { float d = DistTo(p.X, p.Z); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        public bool CanGrab()
        {
            var C = Cauldron;
            return Held == null && C.Held == null && C.TipT <= 0f && DistTo(C.X, C.Z) < ChefConfig.CauldronReach;
        }

        public Vector3 HoldPos()
        {
            float f = Facing;
            float x = X + Mathf.Sin(f) * 1.2f, z = Z + Mathf.Cos(f) * 1.2f;
            if (G.Dungeon.IsWallLike(Grid.TileX(x), Grid.TileZ(z))) { x = X; z = Z; }
            return new Vector3(x, 1.6f, z);
        }

        void TryGrab(GrabSt st, Shape shape)
        {
            if (Dead || grabSt != st || st.Phase != "aviso" || Held != null) return;
            var p = AlivePlayers().Find(q => q.JumpY <= JumpConfig.DodgeHeight && shape.Contains(q.X, q.Z, q.Radius));
            if (p == null) return;
            p.OnGrabbedBy(this);
            Held = p;
            st.Phase = "carga"; st.T = 0f;
            G.Audio.Play("grab");
            G.CameraCtl.AddShake(0.3f);
            G.Ui.FloatText(X, 3.8f, Z, "¡TE PILLÉ!", TextStyle.Big);
            if (!G.SeenHints.ContainsKey("chef"))
            {
                G.SeenHints["chef"] = true;
                G.Ui.Toast("¡Te lleva al caldero! Machaca botones para soltarte, o que tu compañero le pegue junto a él.", 5.5f);
            }
        }

        // Patrón agarrar, fase de llevar: devuelve true al terminar
        bool UpdateGrab(float dt)
        {
            var st = grabSt;
            if (st == null) return true;
            if (st.Phase == "aviso") return PatternT > 0.95f;   // el aviso ya ha caído (agarró o no)
            if (Held == null) return true;                      // soltado (escapó o le aturdieron)
            st.T += dt;
            var C = Cauldron;
            if (DistTo(C.X, C.Z) < 2.4f) { C.Receive(Held); return true; }
            if (st.T >= ChefConfig.CarryMax) { ReleaseHeld("drop"); return true; }   // no llegó a tiempo
            float dx = C.X - X, dz = C.Z - Z, l = MathUtil.Hypot(dx, dz); if (l == 0f) l = 1f;
            Mv = new Vector2(dx / l * ChefConfig.CarrySpeed, dz / l * ChefConfig.CarrySpeed);
            Facing = Mathf.Atan2(dx, dz);
            return false;
        }

        // Suelta al jugador que lleva en alto. why: 'escape' (forcejeo), 'hit', 'stun', 'drop', 'dead', 'lost'
        public void ReleaseHeld(string why)
        {
            var p = Held;
            if (p == null) return;
            Held = null;
            float f = Facing;
            var h = HoldPos();
            if (p.GrabbedBy == this)
            {
                p.Body.X = h.x; p.Body.Z = h.z;
                p.Body.Ignore = Body; p.Body.IgnoreT = 0.4f;
                p.FreeFromGrab();
                p.SyncRoot();
            }
            if (p.Dead) return;
            if (why == "escape")
            {
                p.Knock(Mathf.Sin(f) * 8f, Mathf.Cos(f) * 8f);
                p.PopT = 0.5f;
                Abilities.StunEnemy(this, 3.5f);
                G.Audio.Play("breakFree");
                G.Ui.FloatText(X, 2.9f, Z, "¡Se le escapa!", TextStyle.Label);
            }
            else if (why == "hit" || why == "stun")
            {
                p.Knock(Mathf.Sin(f) * 5f, Mathf.Cos(f) * 5f);
                G.Ui.FloatText(p.X, 2.6f, p.Z, "¡Libre!", TextStyle.Heal);
            }
            else if (why == "drop") G.Ui.FloatText(p.X, 2.6f, p.Z, "¡Se le cae!", TextStyle.Small);
        }

        // ---------- Platos ----------
        static Transform PlateModel()
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.42f, 0.42f, 0.07f, 10), Vector3.zero, MathUtil.Hex(0xf2f2f6));
            b.Add(ProcMesh.Cylinder(0.26f, 0.26f, 0.08f, 10), Vector3.zero, MathUtil.Hex(0xd8dce6));
            var go = new GameObject("Plato");
            CharacterModel.MeshObject(go.transform, "Plato", b.ToMesh("plato"), Mats.Lit, Vector3.zero);
            return go.transform;
        }

        static Transform KnifeModel()
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Box(0.1f, 0.03f, 0.7f), new Vector3(0, 0, 0.2f), MathUtil.Hex(0xd0d8e0));
            b.Add(ProcMesh.Box(0.08f, 0.06f, 0.22f), new Vector3(0, 0, -0.25f), MathUtil.Hex(0x6a4426));
            var go = new GameObject("Cuchilla");
            CharacterModel.MeshObject(go.transform, "Cuchilla", b.ToMesh("cuchilla"), Mats.Lit, Vector3.zero);
            return go.transform;
        }

        public void ThrowPlate(float tx, float tz)
        {
            float x0 = X + Mathf.Sin(Facing) * 0.8f, z0 = Z + Mathf.Cos(Facing) * 0.8f, fl = ChefConfig.Flight;
            Shoot(new Projectile
            {
                PX = x0, PZ = z0, Y = ChefConfig.PlateY0, Vx = (tx - x0) / fl, Vz = (tz - z0) / fl,
                Damage = 10, Knock = 9, ProjRadius = 0.4f, Life = fl + 0.6f, HasArc = true, ArcVy = ChefConfig.PlateVy, ArcG = ChefConfig.PlateG,
            }, PlateModel());
            // Aviso del punto de caída; al caer golpea y deja comida (65 % pollo, 35 % queso podrido)
            Telegraph(Shape.Circle(tx, tz, 1.2f), fl, h =>
            {
                if (Dead) return;
                BossHits.HitPlayersIn(G, h.Shape, 10, 6, tx, tz);
                G.SpawnItemPickup(WeaponRegistry.Create(G.Rand.Next() < 0.35f ? "rottencheese" : "chicken"), tx, tz);
            }, 0xffd060);
        }

        // ---------- Cuchillas ----------
        public void ThrowKnives()
        {
            float bas = Facing, sp = 9f;
            foreach (int k in new[] { -1, 0, 1 })
            {
                float a = bas + k * 0.28f;
                Shoot(new Projectile
                {
                    PX = X + Mathf.Sin(a) * 1.1f, PZ = Z + Mathf.Cos(a) * 1.1f, Y = 1.3f,
                    Vx = Mathf.Sin(a) * sp, Vz = Mathf.Cos(a) * sp,
                    Damage = 12, Knock = 10, ProjRadius = 0.32f, Life = 2.4f, Color = 0xd0d8e0, Reflectable = true,
                    OnHitEnemy = (p, e) =>
                    {
                        if (e != this || Dead) return;
                        Stun = Mathf.Max(Stun, 1.0f);
                        G.Ui.FloatText(X, 2.8f, Z, "¡MI CUCHILLA!", TextStyle.Big);
                    },
                }, KnifeModel());
            }
        }

        // ---------- Fogones (fase 2) ----------
        void BuildFires()
        {
            var t = ChefConfig.FireTiles;
            fireGroups = new[] { new List<Vector2Int>(), new List<Vector2Int>() };
            for (int i = 0; i < t.Length; i++) fireGroups[i % 2].Add(t[i]);
            Hazard = new HazardFloor(G, G.FloorRoot, t, 16f, 0xe0501a, 0xffb040, 0.8f);
        }

        void UpdateFires(float dt)
        {
            if (Hazard == null) return;
            fireT += dt;
            for (int g = 0; g < 2; g++)
            {
                float k = (((fireT - g * 4f) % ChefConfig.FireCycle) + ChefConfig.FireCycle) % ChefConfig.FireCycle;
                var st = k < 1.5f ? HazardFloor.CellState.Warn : k < 4f ? HazardFloor.CellState.Danger : HazardFloor.CellState.Safe;
                foreach (var tt in fireGroups[g]) Hazard.Set(tt.x, tt.y, st);
            }
            Hazard.Update(dt);
        }

        // ---------- Ciclo ----------
        protected override bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv)
        {
            Cauldron.Tick(dt);
            UpdateFires(dt);
            if (Held != null && !Controllable) ReleaseHeld("stun");
            return base.UpdateState(dt, player, dist, dx, dz, out mv);
        }

        public override void TakeHit(Hit hit)
        {
            float hp0 = Hp;
            base.TakeHit(hit);
            // Cualquier golpe que le haga daño suelta a quien lleva en alto
            if (Held != null && !Dead && Hp < hp0) ReleaseHeld("hit");
        }

        public override void Die(string cause)
        {
            if (Dead) return;
            Cauldron.ReleaseHeld("dead");
            base.Die(cause);
        }

        public override void Destroy()
        {
            if (Removed) return;
            ReleaseHeld("lost");
            Cauldron.Destroy();
            Hazard = null;   // su malla cuelga del piso
            base.Destroy();
        }
    }

    public static partial class BossRegistrations
    {
        static partial void Chef()
        {
            Bosses.Register("chef", new BossEntry
            {
                Name = "Monsieur Bocado", Title = "El Chef Carnicero", Icon = "🍳",
                Hint = "Te agarra y te mete en el caldero: machaca para salir, o que tu compañero le pegue 3 veces junto a él. Los platos traen comida; ¡ojo con el queso podrido!",
                ArenaW = 17, ArenaH = 15,
                Decorate = (put, W, H) => { put(5, 12, 'm'); put(11, 10, 'm'); put(12, 13, 'v'); put(2, 8, 'v'); },
                Spawn = (g, x, z) => new List<Boss> { new ChefBoss(g, x, z) },
            });
        }
    }
}
