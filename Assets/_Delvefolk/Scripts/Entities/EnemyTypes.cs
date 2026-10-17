using System;
using UnityEngine;

namespace Delvefolk
{
    // Opciones al crear un enemigo: escala y cambios de configuración (p. ej. vida de los hijos del Rey Slime)
    public sealed class EnemyOpts
    {
        public float Scale = 1f;
        public Action<EnemyCfg> Cfg;
    }

    // SLIME: lento, vida moderada, muy fácil de empujar. Se aplasta y salta encima del jugador.
    // Modelo: el diablillo redondo del paquete de monstruos, teñido del color del slime.
    public sealed class Slime : Enemy
    {
        Transform blob;

        public Slime(Game g, float x, float z, EnemyOpts opts) : base(g, x, z, MakeCfg(opts)) { }

        static EnemyCfg MakeCfg(EnemyOpts opts)
        {
            float s = opts != null ? opts.Scale : 1f;
            var c = new EnemyCfg
            {
                Name = "Slime", Hp = 40, Speed = 2.4f, Radius = 0.55f * s, Mass = 0.85f * s,
                Damage = 10, AttackRange = 0.9f, Windup = 0.55f, AttackTime = 0.42f, Recover = 0.6f,
                AttackCooldown = 1.1f, HurtSound = "slimeHurt", DieSound = "slimeDie",
                BarY = 1.45f * s, HatY = 0.95f * s, HatScale = 1.1f * s, Color = 0x4fd06a, Scale = s,
                Lunge = 9, Bouncy = true, Coins = new[] { 1, 2 },
            };
            if (opts != null && opts.Cfg != null) opts.Cfg(c);
            return c;
        }

        protected override void BuildModel()
        {
            var c = Cfg;
            float s = c.Scale;
            var art = Art.Catalog;
            if (art != null && UseAnimatedModel(art.SlimeModel, 1.1f * s, c.Color, 0.85f))
            {
                blob = Model.GetChild(Model.childCount - 1);
                Animator.Move = "Walk Forward In Place";
                Animator.Attack = "Jump In Place";
                Animator.MoveSpeedRef = 2.4f;
                if (c.Crown && Art.Has(art.Crown)) AddCrown(art.Crown, s);
            }
            else
            {
                blob = new GameObject("Blob").transform;
                blob.SetParent(Model, false);
                var bodyB = new MeshBuilder();
                bodyB.Add(ProcMesh.Icosahedron(0.62f, 1), Matrix4x4.TRS(new Vector3(0, 0.48f, 0), Quaternion.identity, new Vector3(1, 0.78f, 1)), MathUtil.Hex(c.Color, 0.88f));
                CharacterModel.MeshObject(blob, "Cuerpo", bodyB.ToMesh("slime"), Mats.TransparentLit, Vector3.zero);
                var b = new MeshBuilder();
                b.Add(ProcMesh.Icosahedron(0.22f), new Vector3(0.1f, 0.4f, -0.1f), MathUtil.Hex(0x2a7a3a));
                foreach (var sx in new[] { -1f, 1f })
                {
                    b.Add(ProcMesh.Sphere(0.13f, 6, 5), new Vector3(sx * 0.2f, 0.65f, 0.45f), Color.white);
                    b.Add(ProcMesh.Sphere(0.065f, 5, 4), new Vector3(sx * 0.2f, 0.65f, 0.57f), MathUtil.Hex(0x111111));
                }
                b.Add(ProcMesh.Torus(0.1f, 0.025f, 3, 8, Mathf.PI), Matrix4x4.TRS(new Vector3(0, 0.45f, 0.58f), ProcMesh.EulerXYZ(0, 0, Mathf.PI), Vector3.one), MathUtil.Hex(0x111111));
                if (c.Crown)
                {
                    var gold = MathUtil.Hex(0xf2c230);
                    b.Add(ProcMesh.Cylinder(0.28f, 0.3f, 0.12f, 8), new Vector3(0, 0.98f, 0), gold);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i / 5f * Mathf.PI * 2f;
                        b.Add(ProcMesh.Cone(0.07f, 0.22f, 4), new Vector3(Mathf.Cos(a) * 0.26f, 1.12f, Mathf.Sin(a) * 0.26f), gold);
                    }
                }
                CharacterModel.MeshObject(blob, "Cara", b.ToMesh("caraSlime"), Mats.Lit, Vector3.zero);
                blob.localScale = Vector3.one * s;
            }
        }

        // La corona va en el hueso de la cabeza para seguir la animación
        void AddCrown(GameObject crown, float s)
        {
            Transform head = FindBone(blob, "RigHead");
            var go = Art.Spawn(crown, Model);
            if (!go) return;
            var cb = Art.LocalBounds(crown);
            float cs = 0.62f * s / Mathf.Max(0.01f, cb.size.x);
            go.transform.localScale = Vector3.one * cs;
            go.transform.localPosition = new Vector3(0, 1.0f * s, 0.05f * s);
            if (head) go.transform.SetParent(head, true);
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        protected override void OnWindupStart() { G.Audio.Play("enemyAttack"); }
        protected override void OnAttackStart() { G.Audio.Play("swing", 0.3f); }

        protected override bool UpdateAttack(float dt, float dist, out Vector2 mv)
        {
            mv = Vector2.zero;
            if (StateT < 0.3f)
            {
                TryHitPlayer(0.15f);
                mv = new Vector2(Mathf.Sin(Facing) * Cfg.Lunge, Mathf.Cos(Facing) * Cfg.Lunge);
                return true;
            }
            return false;
        }

        protected override void Animate(float dt)
        {
            float t = Anim;
            float sx = 1f, sy = 1f, y = 0f;
            var bp = blob.localPosition;
            if (State == EnemyState.Windup)
            {
                float k = Mathf.Min(1f, StateT / Cfg.Windup);
                sy = 1f - 0.38f * k; sx = 1f + 0.28f * k;
                bp.x = Mathf.Sin(t * 60f) * 0.04f * k;
                // aviso: brillo naranja creciente
                SetGlow(0.5f * k, 0.25f * k, 0f);
            }
            else if (State == EnemyState.Attack)
            {
                float k = Mathf.Min(1f, StateT / 0.3f);
                y = Mathf.Sin(k * Mathf.PI) * 0.8f;
                sy = 1.25f; sx = 0.85f;
                bp.x = 0f;
                SetGlow(0, 0, 0);
            }
            else
            {
                bp.x = 0f;
                bool moving = MathUtil.Hypot(Vx, Vz) > 0.3f;
                float f = moving ? 9f : 3.5f;
                float b = Mathf.Sin(t * f);
                sy = 1f + b * (moving ? 0.12f : 0.05f); sx = 1f - b * (moving ? 0.08f : 0.03f);
                y = moving ? Mathf.Max(0f, b) * 0.12f : 0f;
                if (State == EnemyState.Recover) SetGlow(0, 0, 0);
            }
            blob.localPosition = bp;
            if (Hop > 0f) { Hop = Mathf.Max(0f, Hop - dt * 3f); y += Mathf.Sin(Hop * Mathf.PI) * 0.6f; }
            // Aplastamiento al recibir impacto
            if (SquashT > 0f)
            {
                SquashT = Mathf.Max(0f, SquashT - dt * 4f);
                float q = Mathf.Sin(SquashT * Mathf.PI);
                sx += q * 0.35f; sy -= q * 0.35f;
            }
            // Volando por un empujón: da vueltas
            if (KnockSpeed > 5f) ModelRotZ += dt * KnockSpeed * 0.8f;
            else ModelRotZ = MathUtil.Damp(ModelRotZ, 0f, 10f, dt);
            Model.localScale = new Vector3(sx, sy, sx);
            Model.localPosition = new Vector3(0, y, 0);
        }

        public override void Die(string cause)
        {
            base.Die(cause);
            G.Effects.Burst(X, 0.5f, Z, BurstOpts.Of(26, Cfg.Color, 6f, 5f, 0.2f).WithColorVar(0.1f));
            // El rey slime se parte en dos slimes pequeños
            if (Cfg.Splits && cause != "pit")
            {
                foreach (var s in new[] { -1f, 1f })
                {
                    float a = Facing + s * Mathf.PI / 2f;
                    float ox = Mathf.Sin(a) * 0.7f, oz = Mathf.Cos(a) * 0.7f;
                    var child = G.SpawnEnemy('S', X + ox, Z + oz, new EnemyOpts { Cfg = c => c.Hp = 22 });
                    if (child != null) { child.Wake(false); child.SetState(EnemyState.Chase); child.Knock(ox * 9f, oz * 9f); child.Stun = 0.4f; }
                }
            }
        }
    }

    // GOBLIN: rápido, menos vida, agresivo, esquiva y rodea. Más difícil de controlar.
    public sealed class Goblin : Enemy
    {
        object seenAttack;
        Transform arm;
        Transform[] legs;

        public Goblin(Game g, float x, float z, EnemyOpts opts) : base(g, x, z, MakeCfg(opts)) { }

        static EnemyCfg MakeCfg(EnemyOpts opts)
        {
            var c = new EnemyCfg
            {
                Name = "Goblin", Hp = 32, Speed = 4.5f, Radius = 0.45f, Mass = 1.35f,
                Damage = 8, AttackRange = 1.05f, Windup = 0.32f, AttackTime = 0.28f, Recover = 0.42f,
                AttackCooldown = 0.75f, Strafe = 0.65f, StunMult = 0.7f, WindupTrack = 7,
                HurtSound = "goblinHurt", DieSound = "goblinDie", BarY = 1.75f, HatY = 1.38f, HatScale = 0.9f,
                KnockDrag = 4.6f, Color = 0x7da345, Coins = new[] { 2, 3 },
            };
            if (opts != null && opts.Cfg != null) opts.Cfg(c);
            return c;
        }

        protected override void BuildModel()
        {
            var art = Art.Catalog;
            if (art != null && UseAnimatedModel(art.GoblinModel, 1.45f, 0x7da345, 0.55f))
            {
                Animator.Move = "Fly Forward In Place";
                Animator.Windup = "Stab Attack";
                Animator.Attack = "Stab Attack";
                Animator.MoveSpeedRef = 4.5f;
                return;
            }
            Color skin = MathUtil.Hex(0x7da345), vest = MathUtil.Hex(0x6b4a2a), dark = MathUtil.Hex(0x3b2a1a);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.26f, 0.36f, 0.62f, 7), new Vector3(0, 0.6f, 0), vest);
            b.Add(ProcMesh.Icosahedron(0.32f), Matrix4x4.TRS(new Vector3(0, 1.15f, 0), Quaternion.identity, new Vector3(1.05f, 0.95f, 1)), skin);
            b.Add(ProcMesh.Cone(0.08f, 0.35f, 4), Matrix4x4.TRS(new Vector3(0, 1.1f, 0.42f), ProcMesh.EulerXYZ(Mathf.PI / 2, 0, 0), Vector3.one), skin);
            legs = new Transform[2];
            int li = 0;
            foreach (var sx in new[] { -1f, 1f })
            {
                b.Add(ProcMesh.Cone(0.1f, 0.55f, 4), Matrix4x4.TRS(new Vector3(sx * 0.48f, 1.25f, 0), ProcMesh.EulerXYZ(0, 0, sx * -1.25f), Vector3.one), skin);
                b.Add(ProcMesh.Sphere(0.07f, 5, 4), new Vector3(sx * 0.13f, 1.22f, 0.27f), MathUtil.Hex(0xffe14a));
                var leg = CharacterModel.MeshObject(Model, "Pierna", new MeshBuilder().Add(ProcMesh.Box(0.14f, 0.32f, 0.16f), Vector3.zero, dark).ToMesh("piernaGoblin"), Mats.Lit, new Vector3(sx * 0.14f, 0.16f, 0));
                legs[li++] = leg.transform;
            }
            CharacterModel.MeshObject(Model, "Cuerpo", b.ToMesh("goblin"), Mats.Lit, Vector3.zero);
            arm = new GameObject("Brazo").transform;
            arm.SetParent(Model, false);
            arm.localPosition = new Vector3(-0.36f, 0.75f, 0);
            var ab = new MeshBuilder();
            ab.Add(ProcMesh.Cylinder(0.06f, 0.13f, 0.85f, 6), new Vector3(0, 0.38f, 0), MathUtil.Hex(0x7a5230));
            ab.Add(ProcMesh.Cone(0.05f, 0.14f, 4), Matrix4x4.TRS(new Vector3(0.1f, 0.65f, 0), ProcMesh.EulerXYZ(0, 0, -Mathf.PI / 2), Vector3.one), MathUtil.Hex(0xcccccc));
            CharacterModel.MeshObject(arm, "Garrote", ab.ToMesh("garrote"), Mats.Lit, Vector3.zero);
            arm.localRotation = ProcMesh.EulerXYZ(0.4f, 0, 0);
        }

        float armX = 0.4f;

        public override void Update(float dt)
        {
            // Esquiva: a veces salta hacia atrás cuando ve venir un golpe
            foreach (var p in G.Players)
            {
                if (!(p.Attack != null && p.Attack != seenAttack && Controllable && State == EnemyState.Chase)) continue;
                seenAttack = p.Attack;
                float dx = X - p.X, dz = Z - p.Z, d = MathUtil.Hypot(dx, dz);
                if (d < 3.2f && G.Rand.Next() < 0.28f)
                {
                    Knock(dx / d * 6.5f, dz / d * 6.5f);
                    Hop = 1f;
                    G.Ui.FloatText(X, 2.0f, Z, "¡je!", TextStyle.Small);
                }
            }
            base.Update(dt);
        }

        protected override void OnWindupStart() { G.Audio.Play("enemyAttack"); }
        protected override void OnAttackStart() { G.Audio.Play("swing", 0.4f); }

        protected override bool UpdateAttack(float dt, float dist, out Vector2 mv)
        {
            mv = Vector2.zero;
            if (StateT > 0.06f) TryHitPlayer(0.2f);
            if (StateT < 0.14f) { mv = new Vector2(Mathf.Sin(Facing) * 6f, Mathf.Cos(Facing) * 6f); return true; }
            return false;
        }

        protected override void Animate(float dt)
        {
            float t = Anim;
            bool moving = MathUtil.Hypot(Vx, Vz) > 0.3f;
            float y = moving ? Mathf.Abs(Mathf.Sin(t * 14f)) * 0.08f : 0f;
            if (legs != null)
                for (int i = 0; i < legs.Length; i++)
                {
                    var lp = legs[i].localPosition;
                    lp.z = moving ? Mathf.Sin(t * 14f + i * Mathf.PI) * 0.15f : 0f;
                    legs[i].localPosition = lp;
                }
            if (State == EnemyState.Windup)
            {
                float k = Mathf.Min(1f, StateT / Cfg.Windup);
                armX = Mathf.Lerp(0.4f, -2.3f, MathUtil.EaseOutCubic(k));
                SetGlow(0.45f * k, 0.1f * k, 0f);
            }
            else if (State == EnemyState.Attack)
            {
                float k = Mathf.Min(1f, StateT / 0.12f);
                armX = Mathf.Lerp(-2.3f, 1.6f, MathUtil.EaseOutCubic(k));
                SetGlow(0, 0, 0);
            }
            else armX = MathUtil.Damp(armX, 0.4f + Mathf.Sin(t * 6f) * 0.1f, 8f, dt);
            if (arm) arm.localRotation = ProcMesh.EulerXYZ(armX, 0, 0);
            if (Hop > 0f) { Hop = Mathf.Max(0f, Hop - dt * 3.5f); y += Mathf.Sin(Hop * Mathf.PI) * 0.5f; }
            float sx = 1f, sy = 1f;
            if (SquashT > 0f)
            {
                SquashT = Mathf.Max(0f, SquashT - dt * 4f);
                float q = Mathf.Sin(SquashT * Mathf.PI);
                sx += q * 0.25f; sy -= q * 0.25f;
            }
            if (KnockSpeed > 5f) ModelRotX = MathUtil.Damp(ModelRotX, -1.1f, 10f, dt);
            else ModelRotX = MathUtil.Damp(ModelRotX, 0f, 8f, dt);
            Model.localScale = new Vector3(sx, sy, sx);
            Model.localPosition = new Vector3(0, y, 0);
        }

        public override void Die(string cause)
        {
            base.Die(cause);
            G.Effects.Burst(X, 0.8f, Z, BurstOpts.Of(18, 0x7da345, 6f, 5f, 0.16f));
            G.Effects.Burst(X, 0.8f, Z, BurstOpts.Of(6, 0x6b4a2a, 4f, 6f, 0.2f));
        }
    }

    public static class EnemyFactory
    {
        public static Enemy Create(Game g, char ch, float x, float z, EnemyOpts opts = null)
        {
            if (ch == 'S') return new Slime(g, x, z, opts);
            if (ch == 'G') return new Goblin(g, x, z, opts);
            if (ch == 'A') return new Grabber(g, x, z, opts);
            if (ch == 'L')
            {
                var extra = opts != null ? opts.Cfg : null;
                return new Slime(g, x, z, new EnemyOpts
                {
                    Scale = 1.75f,
                    Cfg = c =>
                    {
                        c.Name = "Rey Slime"; c.Hp = 110; c.Speed = 1.8f; c.Damage = 16; c.AttackRange = 1.0f; c.Windup = 0.75f;
                        c.Color = 0x3ec0b0; c.Crown = true; c.Splits = true; c.Lunge = 8; c.PlayerKnock = 13; c.StunMult = 0.6f; c.Coins = new[] { 8, 10 };
                        if (extra != null) extra(c);
                    },
                });
            }
            return null;
        }
    }
}
