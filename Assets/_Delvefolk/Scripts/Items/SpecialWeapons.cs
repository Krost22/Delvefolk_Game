using UnityEngine;

namespace Delvefolk
{
    // ARMAS ESPECIALES (SpecialWeapons.js): no aparecen en la mazmorra. Las cuatro primeras se compran en la tienda
    // del Duende Tendero; la escopeta solo se consigue derrotándole. Siguen el mismo contrato que las demás armas.
    static class SteelColors
    {
        public static readonly Color Steel = MathUtil.Hex(0xc8d0d8);
        public static readonly Color Gold = MathUtil.Hex(0xc9a227);
    }

    // ESPADA — rápida y equilibrada. El tercer golpe seguido es un torbellino que da a todo alrededor.
    public sealed class SwordWeapon : Weapon
    {
        int combo;
        float lastAttack = -10f;
        bool spin;

        public SwordWeapon()
        {
            Id = "sword"; Name = "Espada de verdad"; Icon = "🗡️"; Style = "Rápida · 3er golpe: torbellino";
            Damage = 17; AttackRange = 2.5f; AttackArc = 110;
            AttackCooldown = 0.36f; Windup = 0.07f; AttackDuration = 0.1f; Recovery = 0.16f;
            KnockbackForce = 11; Stun = 0.35f; Hitstop = 0.06f; Shake = 0.18f;
            Lunge = 3; MoveMult = 0.55f; Color = 0xbfe0ff;
            MaxDurability = Durability = 40; BrokenName = "Empuñadura";
            HitSound = "clang";
        }

        public override void OnAttackStart(Player player, Game g)
        {
            combo = g.Time - lastAttack < 1.0f ? combo + 1 : 1;
            lastAttack = g.Time;
            spin = combo % 3 == 0;
            AttackArc = spin ? 360 : 110;
            Damage = spin ? 22 : 17;
            HitText = spin ? "¡TORBELLINO!" : null;
        }

        public override GameObject BuildModel(Transform parent) { return Build(parent, false); }
        public override GameObject BuildBrokenModel(Transform parent) { return Build(parent, true); }

        GameObject Build(Transform parent, bool broken)
        {
            var b = new MeshBuilder();
            if (broken) b.Add(ProcMesh.Box(0.1f, 0.03f, 1.5f), M(0, 0, 0.55f, 0, 0, 0, 1, 1, 0.2f), SteelColors.Steel);
            else
            {
                b.Add(ProcMesh.Box(0.1f, 0.03f, 1.5f), M(0, 0, 1.15f), SteelColors.Steel);
                b.Add(ProcMesh.Cone(0.07f, 0.25f, 4), M(0, 0, 2.0f, Mathf.PI / 2), SteelColors.Steel);
            }
            b.Add(ProcMesh.Box(0.5f, 0.08f, 0.08f), M(0, 0, 0.38f), SteelColors.Gold);
            b.Add(ProcMesh.Cylinder(0.05f, 0.05f, 0.4f, 6), M(0, 0, 0.15f, Mathf.PI / 2), MathUtil.Hex(0x5a3a22));
            b.Add(ProcMesh.Octahedron(0.08f), M(0, 0, -0.08f), SteelColors.Gold);
            return MeshModel(parent, broken ? "espadaRota" : "espada", b);
        }

        public override Pose IdlePose() { return new Pose(-0.5f, 0.4f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = spin ? new Pose(-2.8f, 0.2f) : new Pose(-1.4f, -0.2f, -0.3f, -0.1f);
            var end = spin ? new Pose(2.8f, 0.2f, 0, 0.2f) : new Pose(1.1f, 0.35f, 0.3f, 0.2f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, end, MathUtil.EaseOutCubic(t));
            return Pose.Mix(end, IdlePose(), MathUtil.EaseInOut(t));
        }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 1.0f, target.Z, BurstOpts.Of(8, 0xdff0ff, 6f, 2f, 0.07f).Dir(hit.DirX, hit.DirZ));
        }
    }

    // MAZO DE GUERRA — lentísimo, brutal, y la onda del golpe empuja a todo lo que haya cerca del objetivo.
    public sealed class MaulWeapon : Weapon
    {
        public MaulWeapon()
        {
            Id = "maul"; Name = "Mazo de guerra"; Icon = "🔨"; Style = "Lentísimo · onda expansiva";
            Damage = 34; AttackRange = 2.4f; AttackArc = 75;
            AttackCooldown = 1.1f; Windup = 0.42f; AttackDuration = 0.1f; Recovery = 0.45f;
            KnockbackForce = 30; Stun = 1.0f; Hitstop = 0.16f; Shake = 0.6f;
            Lunge = 4; MoveMult = 0.15f; Color = 0xffb060;
            MaxDurability = Durability = 30; BrokenName = "Palo del mazo";
            HitSound = "clang"; HitText = "¡BONK!";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f }) b.Add(ProcMesh.Box(0.1f, 0.52f, 0.56f), M(s * 0.4f, 0, 1.55f), MathUtil.Hex(0x3a3c40));
            b.Add(ProcMesh.Cylinder(0.06f, 0.08f, 1.6f, 6), M(0, 0, 0.7f, Mathf.PI / 2), MathUtil.Hex(0x5a3a22));
            b.Add(ProcMesh.Box(0.75f, 0.45f, 0.5f), M(0, 0, 1.55f), MathUtil.Hex(0x55595f));
            b.Add(ProcMesh.Box(0.8f, 0.12f, 0.54f), M(0, 0, 1.55f), SteelColors.Gold);
            return MeshModel(parent, "mazo", b);
        }

        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.06f, 0.08f, 1.4f, 6), M(0, 0, 0.6f, Mathf.PI / 2), MathUtil.Hex(0x5a3a22));
            return MeshModel(parent, "mazoRoto", b);
        }

        public override Pose IdlePose() { return new Pose(-0.7f, 0.7f, 0.3f, -0.1f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var up = new Pose(-0.1f, -2.6f, 0, -0.2f);
            var slam = new Pose(0, 0.8f, 0, 0.3f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), up, MathUtil.EaseOutBack(Mathf.Min(1f, t)));
            if (phase == AttackPhase.Active) return Pose.Mix(up, slam, MathUtil.EaseInCubic(t));
            return Pose.Mix(slam, IdlePose(), MathUtil.EaseInOut(Mathf.Max(0f, (t - 0.3f) / 0.7f)));
        }

        // Onda expansiva: lo que esté cerca del objetivo sale despedido
        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Ring(target.X, target.Z, new RingOpts { Color = 0xffb060, Radius = 2.4f, Life = 0.35f, Y = 0.06f, R0 = 0.3f, Opacity = 0.8f });
            g.Effects.Burst(target.X, 0.2f, target.Z, BurstOpts.Of(18, 0x8a7b68, 6f, 5f, 0.16f));
            foreach (var e in g.Enemies.ToArray())
            {
                if (ReferenceEquals(e, target) || e.Dead || e.State == EnemyState.Falling || !e.Body.Enabled) continue;
                float dx = e.X - target.X, dz = e.Z - target.Z, d = MathUtil.Hypot(dx, dz);
                if (d > 2.4f + e.Radius) continue;
                float l = d > 0f ? d : 1f;
                e.Knock(dx / l * 12f / e.Body.Mass, dz / l * 12f / e.Body.Mass);
                e.Stun = Mathf.Max(e.Stun, 0.4f);
                e.EnvDamage(9, null);
            }
        }
    }

    // LANZA — mucho alcance en línea recta; atraviesa a todo lo que tenga delante.
    public sealed class SpearWeapon : Weapon
    {
        public SpearWeapon()
        {
            Id = "spear"; Name = "Lanza de guardia"; Icon = "🔱"; Style = "Larguísima · atraviesa en línea";
            Damage = 15; AttackRange = 4.3f; AttackArc = 22;
            AttackCooldown = 0.45f; Windup = 0.1f; AttackDuration = 0.1f; Recovery = 0.2f;
            KnockbackForce = 14; Stun = 0.4f; Hitstop = 0.05f; Shake = 0.15f;
            Lunge = 2.5f; MoveMult = 0.5f; Color = 0xd0f0a0;
            MaxDurability = Durability = 40; BrokenName = "Palo de lanza";
            HitSound = "thwack";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.045f, 0.055f, 3.2f, 6), M(0, 0, 1.2f, Mathf.PI / 2), MathUtil.Hex(0x8a6a3a));
            b.Add(ProcMesh.Cone(0.12f, 0.5f, 4), M(0, 0, 3.0f, Mathf.PI / 2), SteelColors.Steel);
            b.Add(ProcMesh.Cone(0.08f, 0.25f, 5), M(0, -0.12f, 2.65f, Mathf.PI), MathUtil.Hex(0xb3261e));
            return MeshModel(parent, "lanza", b);
        }

        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.045f, 0.055f, 1.6f, 6), M(0, 0, 0.6f, Mathf.PI / 2), MathUtil.Hex(0x8a6a3a));
            return MeshModel(parent, "lanzaRota", b);
        }

        public override Pose IdlePose() { return new Pose(-0.35f, 0.25f, 0, -0.2f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = new Pose(-0.15f, 0.1f, 0, -0.6f);
            var thrust = new Pose(-0.05f, 0.08f, 0, 1.1f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, thrust, MathUtil.EaseOutCubic(t));
            return Pose.Mix(thrust, IdlePose(), MathUtil.EaseInOut(t));
        }

        public override Vector2 KnockDirection(float fx, float fz, float dx, float dz) { return new Vector2(fx, fz); }
    }

    // HACHA DOBLE — arco enorme: barre a todo lo que tengas delante y a los lados.
    public sealed class AxeWeapon : Weapon
    {
        public AxeWeapon()
        {
            Id = "axe"; Name = "Hacha doble"; Icon = "🪓"; Style = "Barrido enorme";
            Damage = 22; AttackRange = 2.4f; AttackArc = 160;
            AttackCooldown = 0.62f; Windup = 0.16f; AttackDuration = 0.12f; Recovery = 0.26f;
            KnockbackForce = 15; Stun = 0.5f; Hitstop = 0.08f; Shake = 0.3f;
            Lunge = 2.5f; MoveMult = 0.35f; Color = 0xff8a60;
            MaxDurability = Durability = 35; BrokenName = "Mango de hacha";
            HitSound = "thwack";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f })
                b.Add(ProcMesh.Cylinder(0.42f, 0.42f, 0.05f, 8, 1, false, 0f, Mathf.PI), M(s * 0.05f, 0, 1.35f, 0, s > 0 ? 0 : Mathf.PI, Mathf.PI / 2), SteelColors.Steel);
            b.Add(ProcMesh.Cylinder(0.06f, 0.07f, 1.5f, 6), M(0, 0, 0.65f, Mathf.PI / 2), MathUtil.Hex(0x6a4426));
            return MeshModel(parent, "hacha", b);
        }

        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.06f, 0.07f, 1.4f, 6), M(0, 0, 0.6f, Mathf.PI / 2), MathUtil.Hex(0x6a4426));
            return MeshModel(parent, "hachaRota", b);
        }

        public override Pose IdlePose() { return new Pose(-0.6f, 0.55f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = new Pose(-2.0f, -0.1f);
            var end = new Pose(1.7f, 0.3f, 0, 0.15f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, end, MathUtil.EaseOutCubic(t));
            return Pose.Mix(end, IdlePose(), MathUtil.EaseInOut(t));
        }
    }

    // ESCOPETA DEL DUENDE — solo la suelta el Duende Tendero al ser derrotado. No se rompe nunca.
    // Dispara un cono de perdigones (más daño cuanto más cerca) y gasta una bala por disparo.
    // Sin balas golpea con la culata. Cada enemigo derrotado mientras la llevas en la mano da +1 bala.
    public static class ShotgunConfig
    {
        public const int StartAmmo = 6, MaxAmmo = 12;
        public const float Range = 8.5f, Arc = 34f, Damage = 30f, MinDamage = 9f;
    }

    public sealed class ShotgunWeapon : Weapon
    {
        public int Ammo = ShotgunConfig.StartAmmo;
        public bool Firing;
        bool shotPending;

        public ShotgunWeapon()
        {
            Id = "shotgun"; Name = "Escopeta del Duende"; Icon = "🔫";
            AttackCooldown = 0.85f; Hitstop = 0.08f; Color = 0xffd27a;
            ApplyMode();
        }

        // Con balas: disparo en cono a distancia. Sin balas: culatazo.
        void ApplyMode() { SetStats(Ammo > 0); }

        void SetStats(bool gun)
        {
            if (gun)
            {
                Damage = ShotgunConfig.Damage; AttackRange = ShotgunConfig.Range; AttackArc = ShotgunConfig.Arc; Windup = 0.1f; AttackDuration = 0.06f;
                Recovery = 0.45f; KnockbackForce = 20; Stun = 0.7f; Shake = 0.5f; Lunge = -4; MoveMult = 0.5f; HitSound = "hit"; HitText = null;
            }
            else
            {
                Damage = 7; AttackRange = 1.9f; AttackArc = 80; Windup = 0.12f; AttackDuration = 0.08f;
                Recovery = 0.2f; KnockbackForce = 10; Stun = 0.3f; Shake = 0.12f; Lunge = 2; MoveMult = 0.5f; HitSound = "thwack"; HitText = null;
            }
            UpdateStyle();
        }

        void UpdateStyle()
        {
            Style = Ammo > 0 ? "Balas: " + Ammo + "/" + ShotgunConfig.MaxAmmo + " · no se rompe" : "Sin balas: culatazo · mata para recargar";
        }

        public int AddAmmo(int n)
        {
            int before = Ammo;
            Ammo = Mathf.Min(ShotgunConfig.MaxAmmo, Ammo + n);
            ApplyMode();
            return Ammo - before;
        }

        public override ItemState GetState() { return new ItemState { id = Id, ammo = Ammo }; }
        public override void LoadState(ItemState s) { if (s != null && s.ammo >= 0) { Ammo = s.ammo; ApplyMode(); } }

        // Cada ataque decide al empezar si es disparo (gasta una bala) o culatazo
        public override void OnAttackStart(Player player, Game g)
        {
            Firing = Ammo > 0;
            if (Firing) Ammo--;
            SetStats(Firing);
            shotPending = Firing;
        }

        // Al empezar la fase activa: perdigones, fogonazo y retroceso
        public override void OnActive(Player player, Game g)
        {
            if (shotPending)
            {
                shotPending = false;
                float f = player.Facing, x = player.X + Mathf.Sin(f) * 0.9f, z = player.Z + Mathf.Cos(f) * 0.9f;
                ShotgunFX.Blast(g, x, z, f, ShotgunConfig.Arc * Mathf.Deg2Rad, ShotgunConfig.Range, 0xffe08a);
            }
            UpdateStyle();
            g.Ui.SetWeapon(player.Index, this);
        }

        // Más daño a bocajarro
        public override void ModifyHit(Hit hit, HitCtx ctx)
        {
            if (!Firing) return;
            float k = Mathf.Clamp01(1f - ctx.Dist / ShotgunConfig.Range);
            hit.Damage = MathUtil.RoundJs(ShotgunConfig.MinDamage + (hit.Damage - ShotgunConfig.MinDamage) * k);
            hit.Knock *= 0.5f + k * 0.7f;
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            var metal = MathUtil.Hex(0x3a3c40);
            foreach (float s in new[] { -1f, 1f }) b.Add(ProcMesh.Cylinder(0.055f, 0.055f, 1.1f, 8), M(s * 0.06f, 0.06f, 0.95f, Mathf.PI / 2), metal);
            b.Add(ProcMesh.Box(0.2f, 0.16f, 0.35f), M(0, 0.02f, 0.35f), metal);
            b.Add(ProcMesh.Box(0.16f, 0.22f, 0.6f), M(0, -0.06f, -0.05f, -0.25f), MathUtil.Hex(0x7a4a2a));
            b.Add(ProcMesh.Box(0.2f, 0.06f, 0.08f), M(0, 0.06f, 0.7f), SteelColors.Gold);
            return MeshModel(parent, "escopeta", b);
        }

        public override GameObject BuildBrokenModel(Transform parent) { return BuildModel(parent); }

        public override Pose IdlePose() { return new Pose(-0.25f, 0.2f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            if (!Firing)
            {
                // Culatazo
                var back = new Pose(-0.8f, -0.3f, 0, -0.2f); var end = new Pose(0.6f, 0.3f, 0, 0.3f);
                if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
                if (phase == AttackPhase.Active) return Pose.Mix(back, end, MathUtil.EaseOutCubic(t));
                return Pose.Mix(end, IdlePose(), MathUtil.EaseInOut(t));
            }
            var aim = new Pose(0, 0.02f, 0, 0.25f); var kick = new Pose(0.05f, -0.45f, 0, -0.15f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), aim, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(aim, kick, MathUtil.EaseOutCubic(t));
            return Pose.Mix(kick, IdlePose(), MathUtil.EaseInOut(t));
        }
    }

    // Efecto de disparo de escopeta (lo usan el jugador y el Duende Tendero)
    public static class ShotgunFX
    {
        public static void Blast(Game g, float x, float z, float facing, float arc, float range, int color)
        {
            var fx = g.Effects;
            g.Audio.Play("shotgun");
            g.CameraCtl.AddShake(0.35f);
            fx.Flash(x, 1.2f, z, 0xffc060, 5f, 0.12f);
            fx.Burst(x, 1.1f, z, BurstOpts.Of(10, 0xffd080, 7f, 1f, 0.1f).Dir(Mathf.Sin(facing), Mathf.Cos(facing), 0.6f).WithLife(0.2f));
            fx.Burst(x, 1.2f, z, BurstOpts.Of(6, 0x888888, 1.5f, 1.5f, 0.25f).WithLife(0.7f).WithGravity(-1f));
            for (int k = 0; k < 7; k++)
            {
                float a = facing + ((float)g.FxRand.NextDouble() - 0.5f) * arc;
                float r = range * (0.7f + (float)g.FxRand.NextDouble() * 0.3f);
                fx.Tracer(x, z, x + Mathf.Sin(a) * r, z + Mathf.Cos(a) * r, 1.0f, color);
            }
        }
    }
}
