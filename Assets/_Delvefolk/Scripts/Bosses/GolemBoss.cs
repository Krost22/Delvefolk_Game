using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // 🗿 GÓLEM DE ESCOMBROS (GolemBoss.js) — gólem enorme y lento de piedra con musgo. Mecánica cooperativa: uno le
    // distrae por DELANTE (su armadura le protege casi del todo) mientras el otro le pega en el NÚCLEO naranja de la
    // espalda (ahí recibe el doble). Además se obsesiona con el último que le golpeó (rótulo "¡TE ODIA!").
    //  Fase 1: pisotón sísmico (la onda se esquiva saltando) y roca lanzada (si la devuelves, le aturde).
    //  Fase 2 (a la mitad de vida, más rápido y con el núcleo brillante): además embiste en línea recta
    //  (si choca con un muro o columna, queda aturdido), una segunda onda y barriles explosivos.
    public sealed class GolemBoss : Boss
    {
        Transform armL, armR, core, halo, bodyG;
        Renderer coreR, haloR;
        Player tauntFor;
        WorldLabel taunt;
        public Projectile LastRock;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public GolemBoss(Game g, float x, float z) : base(g, x, z, BossCfg(c =>
        {
            c.Name = "Gólem de Escombros"; c.Hp = 520; c.Speed = 1.9f; c.Radius = 1.15f; c.Mass = 12;
            c.BarY = 3.2f; c.Coins = new[] { 35, 45 };
        }))
        {
            PhaseThresholds = new[] { 0.5f };

            // 1) Pisotón sísmico: aviso circular, golpe en el área y onda expansiva (en fase 2, una segunda onda)
            bool w2 = false;
            AddPattern(new BossPattern
            {
                Id = "pisoton", Weight = 2, Cooldown = 3.5f, Phases = new[] { 1, 2 },
                Start = b =>
                {
                    w2 = false;
                    b.Telegraph(Shape.Circle(b.X, b.Z, 2.6f), 0.8f, h =>
                    {
                        if (b.Dead) return;
                        BossHits.HitPlayersIn(G, h.Shape, 18, 12, b.X, b.Z);
                        b.Wave(new Wave { Speed = 7, MaxR = 14, Damage = 12 });
                        G.CameraCtl.AddShake(0.6f);
                        G.Audio.Play("stomp");
                    });
                },
                Update = (b, dt, pl, dist) =>
                {
                    if (b.Phase == 2 && !w2 && b.PatternT >= 1.3f)   // 0,5 s después del primer golpe
                    {
                        w2 = true;
                        if (!b.Dead) b.Wave(new Wave { Speed = 7, MaxR = 14, Damage = 12 });
                    }
                    return b.PatternT > 1.4f;
                },
            });

            // 2) Lanzar roca: se gira hacia su objetivo 0,7 s (aviso de línea) y lanza una roca reflejable
            bool fired = false;
            AddPattern(new BossPattern
            {
                Id = "roca", Weight = 2, Cooldown = 4, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => pl != null && dist < 14f,
                Start = b =>
                {
                    fired = false;
                    var t = b.PickTarget(0f) ?? FirstAlive();
                    if (t != null) b.Telegraph(Shape.Line(b.X, b.Z, t.X, t.Z, 0.25f), 0.7f, null, 0xffa040);
                },
                Update = (b, dt, pl, dist) =>
                {
                    var t = b.Target ?? pl;
                    if (!fired && b.PatternT < 0.7f && t != null) b.FaceTo(t.X, t.Z, 6f, dt);
                    if (!fired && b.PatternT >= 0.7f)
                    {
                        fired = true;
                        if (t != null && !b.Dead)
                        {
                            float dx = t.X - b.X, dz = t.Z - b.Z, l = MathUtil.Hypot(dx, dz); if (l == 0f) l = 1f;
                            float sx = dx / l, sz = dz / l, v = 9f;
                            LastRock = b.Shoot(new Projectile
                            {
                                PX = b.X + sx * 1.5f, PZ = b.Z + sz * 1.5f, Y = 1.9f, Vx = sx * v, Vz = sz * v,
                                Damage = 16, ProjRadius = 0.6f, Knock = 14, Life = 4.5f, Reflectable = true, Color = 0x8a7a66,
                                OnHitEnemy = (p, e) =>
                                {
                                    if (e != b) return;
                                    // Quien la devolvió pasa a ser el objetivo de su rabia
                                    if (p.Reflector != null) { b.LastHitter = p.Reflector; b.ForcedTarget = p.Reflector; }
                                    b.CancelPattern();
                                    b.Stun = 2.2f;
                                    G.Ui.FloatText(b.X, b.Cfg.BarY + 0.4f, b.Z, "¡ATURDIDO!", TextStyle.Big);
                                },
                            });
                        }
                    }
                    return b.PatternT > 1.1f;
                },
            });

            // 3) Embestida (fase 2): aviso de línea hacia el objetivo y carga recta. Muro o columna = aturdido
            float cdx = 0f, cdz = 1f;
            var chargeHit = new HashSet<Player>();
            AddPattern(new BossPattern
            {
                Id = "embestida", Weight = 2, Cooldown = 5, Phases = new[] { 2 },
                Can = (b, pl, dist) => pl != null && dist > 3.5f && dist < 13f,
                Start = b =>
                {
                    var t = b.PickTarget(0f) ?? FirstAlive();   // objetivo al día (respeta la rabia)
                    float dx = t != null ? t.X - b.X : 0f, dz = t != null ? t.Z - b.Z : 1f;
                    float l = MathUtil.Hypot(dx, dz); if (l == 0f) l = 1f;
                    cdx = dx / l; cdz = dz / l;
                    chargeHit.Clear();
                    b.FaceTo(b.X + cdx, b.Z + cdz);
                    b.Telegraph(Shape.Line(b.X, b.Z, b.X + cdx * 10f, b.Z + cdz * 10f, 2f), 1.0f, null, 0xff5030);
                },
                Update = (b, dt, pl, dist) =>
                {
                    if (b.PatternT < 1.0f) return false;                 // aviso: quieto
                    if (b.PatternT >= 1.8f) return true;                 // fin de la carga
                    // Choque contra un muro o una columna (el hueco de delante)
                    float ax = b.X + cdx * (b.Radius + 0.9f), az = b.Z + cdz * (b.Radius + 0.9f);
                    if (BlockedAt(ax, az))
                    {
                        b.Stun = 1.5f;
                        G.Audio.Play("crash");
                        G.CameraCtl.AddShake(0.5f);
                        G.RequestHitstop(0.06f);
                        G.Ui.FloatText(b.X, 2.6f, b.Z, "¡PAF! Se ha estrellado", TextStyle.Big);
                        return true;
                    }
                    b.Mv = new Vector2(cdx * 14f, cdz * 14f);
                    BossHits.HitPlayersIn(G, Shape.Line(b.X, b.Z, b.X + cdx * 2.2f, b.Z + cdz * 2.2f, 2.4f), 20, 18, b.X, b.Z, false, chargeHit);
                    return false;
                },
            });

            // 4) Barriles (fase 2): dos avisos en el suelo; caen dos barriles explosivos en sitios libres
            AddPattern(new BossPattern
            {
                Id = "barriles", Weight = 1, Cooldown = 9, Phases = new[] { 2 },
                Start = b =>
                {
                    foreach (var s in FreeSpots(2))
                    {
                        var spot = s;
                        b.Telegraph(Shape.Circle(spot.x, spot.y, 1.3f), 1.0f, h =>
                        {
                            if (b.Dead) return;
                            G.Barrels.Add(new Barrel(G, spot.x, spot.y, true));
                            G.Effects.Burst(spot.x, 0.6f, spot.y, BurstOpts.Of(10, 0x9a8c7a, 3f, 3f, 0.2f));
                            G.Audio.Play("thwack");
                        }, 0xc0302a);
                    }
                },
                Update = (b, dt, pl, dist) => b.PatternT > 1.4f,
            });
        }

        Player FirstAlive() { var a = AlivePlayers(); return a.Count > 0 ? a[0] : null; }

        // ---------- Modelo: roca apilada con musgo, brazos de piedra y núcleo naranja en la espalda (−Z) ----------
        protected override void BuildModel()
        {
            Color stone = MathUtil.Hex(0x7a7364), stone2 = MathUtil.Hex(0x5c574b), moss = MathUtil.Hex(0x4e7a3a), mossDark = MathUtil.Hex(0x3a5c2a);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.4f, 0.5f, 0.9f, 7), new Vector3(-0.55f, 0.45f, 0), stone2);
            b.Add(ProcMesh.Cylinder(0.4f, 0.5f, 0.9f, 7), new Vector3(0.55f, 0.45f, 0), stone2);
            b.Add(ProcMesh.Dodecahedron(0.95f), Matrix4x4.TRS(new Vector3(0, 1.55f, 0), Quaternion.identity, new Vector3(1.1f, 1.05f, 0.95f)), stone);
            b.Add(ProcMesh.Icosahedron(0.6f), new Vector3(0, 1.0f, 0.25f), stone2);
            b.Add(ProcMesh.Icosahedron(0.42f), new Vector3(-0.5f, 2.25f, 0.5f), moss);
            b.Add(ProcMesh.Icosahedron(0.38f), new Vector3(0.45f, 2.0f, 0.6f), moss);
            b.Add(ProcMesh.Icosahedron(0.34f), new Vector3(0.1f, 2.5f, -0.4f), moss);
            b.Add(ProcMesh.Icosahedron(0.3f), new Vector3(0.25f, 1.1f, -0.6f), mossDark);
            // Cabeza pequeña con ojos brillantes (mira hacia +Z)
            b.Add(ProcMesh.Icosahedron(0.42f), new Vector3(0, 2.95f, 0.15f), stone);
            b.Add(ProcMesh.Dodecahedron(0.2f), new Vector3(0, 3.2f, 0.35f), stone2);
            bodyG = new GameObject("Golem").transform;
            bodyG.SetParent(Model, false);
            CharacterModel.MeshObject(bodyG, "Cuerpo", b.ToMesh("golem"), Mats.Lit, Vector3.zero);
            var eyes = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f }) eyes.Add(ProcMesh.Sphere(0.07f, 6, 4), new Vector3(s * 0.16f, 3.0f, 0.5f), MathUtil.Hex(0xffe14a));
            CharacterModel.MeshObject(bodyG, "Ojos", eyes.ToMesh("ojosGolem"), Mats.Unlit, Vector3.zero);
            // Núcleo brillante en la espalda (lado −Z) con un halo aditivo
            core = CharacterModel.MeshObject(bodyG, "Nucleo", new MeshBuilder().Add(ProcMesh.Sphere(0.36f, 9, 7), Vector3.zero, Color.white).ToMesh("nucleo"), Mats.Unlit, new Vector3(0, 1.75f, -1.05f)).transform;
            coreR = core.GetComponent<Renderer>();
            SetColor(coreR, 0xff8a2a, 1f);
            halo = CharacterModel.MeshObject(bodyG, "Halo", new MeshBuilder().Add(ProcMesh.Sphere(0.62f, 10, 8), Vector3.zero, MathUtil.Hex(0xff7a20)).ToMesh("halo"), Mats.Additive, new Vector3(0, 1.75f, -1.05f)).transform;
            haloR = halo.GetComponent<Renderer>();
            Shape.SetOpacity(haloR, 0.3f);
            // Brazos gruesos: pivotan en el hombro; el puño es una roca
            armL = MakeArm(stone2, stone, -1.15f);
            armR = MakeArm(stone2, stone, 1.15f);
        }

        Transform MakeArm(Color armC, Color fistC, float x)
        {
            var group = new GameObject("Brazo").transform;
            group.SetParent(bodyG, false);
            group.localPosition = new Vector3(x, 2.2f, 0);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.3f, 0.36f, 1.5f, 7), new Vector3(0, -0.75f, 0), armC);
            b.Add(ProcMesh.Dodecahedron(0.45f), new Vector3(0, -1.6f, 0.1f), fistC);
            CharacterModel.MeshObject(group, "Piedra", b.ToMesh("brazoGolem"), Mats.Lit, Vector3.zero);
            return group;
        }

        static void SetColor(Renderer r, int hex, float a)
        {
            r.GetPropertyBlock(mpb);
            var c = MathUtil.Hex(hex); c.a = a;
            mpb.SetColor(Mats.BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }

        // ---------- Animación según el patrón en curso ----------
        protected override void Animate(float dt)
        {
            float t = Anim, pt = PatternT;
            float aL = Mathf.Sin(t * 2f) * 0.2f, aR = -aL;          // balanceo al andar
            if (Is("pisoton"))
            {
                if (pt < 0.8f) aL = aR = -2.3f * Mathf.Clamp01(pt / 0.8f);                       // levanta los brazos
                else if (pt < 1.05f) aL = aR = -2.3f + 3.2f * Mathf.Clamp01((pt - 0.8f) / 0.25f);  // golpe
                else aL = aR = 0.9f * (1f - Mathf.Clamp01((pt - 1.05f) / 0.3f));
            }
            else if (Is("roca"))
            {
                aL = 0f;
                aR = pt < 0.7f ? -1.9f * Mathf.Clamp01(pt / 0.7f) : -1.9f * (1f - Mathf.Clamp01((pt - 0.7f) / 0.4f));
            }
            else if (Is("embestida")) aL = aR = pt < 1.0f ? -0.6f : -1.0f;   // se inclina para cargar
            else if (Is("barriles")) aL = aR = -1.2f * (1f - Mathf.Clamp01((pt - 0.9f) / 0.4f));
            armL.localRotation = ProcMesh.EulerXYZ(aL, 0, 0);
            armR.localRotation = ProcMesh.EulerXYZ(aR, 0, 0);
            // Núcleo que late; en fase 2 brilla más
            bool f2 = Phase >= 2;
            float pulse = 1f + 0.12f * Mathf.Sin(t * 5f) + (f2 ? 0.12f : 0f);
            core.localScale = Vector3.one * pulse;
            halo.localScale = Vector3.one * pulse * 1.1f;
            Shape.SetOpacity(haloR, f2 ? 0.55f : 0.3f);
            UpdateTaunt();
        }

        // ---------- Rabia: rótulo sobre el jugador al que persigue ----------
        public string TauntText { get { return taunt != null && taunt.Alive ? taunt.Text : null; } }
        public Player TauntFor { get { return tauntFor; } }

        void UpdateTaunt()
        {
            var f = ForcedTarget;
            if (f != tauntFor)
            {
                ClearTaunt();
                if (f != null)
                {
                    tauntFor = f;
                    taunt = G.Ui.Label("¡TE ODIA!", new Vector3(f.X, 2.4f, f.Z), 0xff6a5a);
                }
            }
            if (taunt != null && tauntFor != null)
            {
                taunt.Pos = new Vector3(tauntFor.X, 2.4f + tauntFor.JumpY, tauntFor.Z);
                bool vis = !tauntFor.Dead && !Dead;
                if (taunt.R && taunt.R.gameObject.activeSelf != vis) taunt.R.gameObject.SetActive(vis);
            }
        }

        void ClearTaunt()
        {
            if (taunt != null) taunt.Remove();
            taunt = null;
            tauntFor = null;
        }

        // ---------- Reglas del jefe ----------
        public override void OnAwake() { Say("¡Nadie se mueve!"); }

        protected override void OnPhase(int n)
        {
            if (n == 2)
            {
                Cfg.Speed = 2.4f;
                Say("¡GRRRAAAH!");
                SetColor(coreR, 0xffe08a, 1f);
            }
        }

        // Armadura: el golpe que viene de DELANTE (ángulo con su frente < 100°) casi no le hace daño.
        // Desde la espalda o los lados recibe el doble. Los golpes sin jugador (proyectil devuelto) usan la dirección inversa.
        protected override Hit ModifyIncoming(Hit hit)
        {
            if (hit.Reflected) return hit;
            float ax = hit.By != null ? hit.By.X - X : -hit.DirX;
            float az = hit.By != null ? hit.By.Z - Z : -hit.DirZ;
            float ang = Mathf.Abs(MathUtil.AngleDiff(Facing, Mathf.Atan2(ax, az)));
            if (ang < 100f * Mathf.Deg2Rad)
            {
                hit.Damage *= 0.1f;
                if (hit.Damage < 1f) { hit.Blocked = true; hit.BlockText = "¡CLANG! Por delante"; }
            }
            else
            {
                hit.Damage *= 2f;
                G.Ui.FloatText(X, Cfg.BarY + 0.2f, Z, "¡NÚCLEO!", TextStyle.Label);
            }
            return hit;
        }

        // Golpe de jugador: además de contarlo, la rabia apunta a quien le golpea
        public override void TakeHit(Hit hit)
        {
            base.TakeHit(hit);
            if (LastHitter != null) ForcedTarget = LastHitter;
        }

        // ¿Hay muro o columna (o un cuerpo estático) en ese punto? Lo usa la embestida
        public bool BlockedAt(float x, float z)
        {
            if (G.Dungeon.IsWallLike(Grid.TileX(x), Grid.TileZ(z))) return true;
            foreach (var o in G.Physics.Bodies)
            {
                if (!o.IsStatic || !o.Enabled || o == Body) continue;
                if (MathUtil.Hypot(o.X - x, o.Z - z) < o.Radius + 0.5f) return true;
            }
            return false;
        }

        // Sitios libres de la arena para los barriles (suelo normal, sin púas, lejos de jefe, barriles y jugadores)
        List<Vector2> FreeSpots(int n)
        {
            var d = G.Dungeon; var outp = new List<Vector2>();
            for (int k = 0; k < 300 && outp.Count < n; k++)
            {
                float x = G.Rand.Range(4f, 28f), z = -G.Rand.Range(8f, 24f);
                int tx = Grid.TileX(x), tz = Grid.TileZ(z);
                if (d.TypeAt(tx, tz) != TileType.Floor || d.Spike[d.Idx(tx, tz)] != 0) continue;
                if (MathUtil.Hypot(x - X, z - Z) < 3.5f) continue;
                bool bad = false;
                foreach (var o in G.Physics.Bodies) if (o.IsStatic && MathUtil.Hypot(o.X - x, o.Z - z) < o.Radius + 1.0f) { bad = true; break; }
                if (bad) continue;
                foreach (var br in G.Barrels) if (!br.Dead && MathUtil.Hypot(br.X - x, br.Z - z) < 2.2f) { bad = true; break; }
                if (bad) continue;
                foreach (var p in G.Players) if (MathUtil.Hypot(p.X - x, p.Z - z) < 1.8f) { bad = true; break; }
                if (bad) continue;
                foreach (var s in outp) if (MathUtil.Hypot(s.x - x, s.y - z) < 3f) { bad = true; break; }
                if (bad) continue;
                outp.Add(new Vector2(x, z));
            }
            return outp;
        }

        public override void Die(string cause)
        {
            base.Die(cause);
            if (taunt != null && taunt.R) taunt.R.gameObject.SetActive(false);
        }

        public override void Destroy()
        {
            ClearTaunt();
            base.Destroy();
        }
    }

    public static partial class BossRegistrations
    {
        static partial void Golem()
        {
            Bosses.Register("golem", new BossEntry
            {
                Name = "Gólem de Escombros", Title = "El que nunca olvida un golpe", Icon = "🗿",
                Hint = "Por delante es casi invulnerable: que uno le distraiga y el otro le pegue en el núcleo de la ESPALDA. Persigue al último que le golpea.",
                ArenaW = 17, ArenaH = 15,
                Decorate = (put, W, H) =>
                {
                    put(3, 4, 'o'); put(13, 4, 'o'); put(3, 9, 'o'); put(13, 9, 'o');   // columnas: la embestida choca contra ellas
                    put(3, 12, 'e'); put(13, 12, 'e');                                  // barriles de pólvora junto a la entrada
                },
                Spawn = (g, x, z) => new List<Boss> { new GolemBoss(g, x, z) },
            });
        }
    }
}
