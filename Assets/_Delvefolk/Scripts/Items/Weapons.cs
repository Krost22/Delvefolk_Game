using UnityEngine;

namespace Delvefolk
{
    // ESCOBA — barrido amplio: poco daño, mucho empuje, golpea a varios.
    public sealed class BroomWeapon : Weapon
    {
        public BroomWeapon()
        {
            Id = "broom"; Name = "Escoba"; Icon = "🧹"; Style = "Barrido amplio · mucho empuje";
            Damage = 7; AttackRange = 3.0f; AttackArc = 170; AttackCooldown = 0.5f;
            Windup = 0.13f; AttackDuration = 0.12f; Recovery = 0.22f;
            KnockbackForce = 15; Stun = 0.35f; Hitstop = 0.06f; Shake = 0.2f;
            Lunge = 2.5f; MoveMult = 0.45f; Color = 0xe8d27a;
            MaxDurability = Durability = 14; BrokenName = "Palo de escoba";
            HitSound = "thwack"; HitText = "¡FUSH!";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.06f, 0.07f, 1.9f, 6), M(0, 0, 0.75f, Mathf.PI / 2), MathUtil.Hex(0x9a6a3a));
            b.Add(ProcMesh.Cylinder(0.16f, 0.55f, 0.75f, 7), M(0, 0, 2.05f, Mathf.PI / 2, 0, 0, 1.25f, 1, 0.45f), MathUtil.Hex(0xe0c060));
            b.Add(ProcMesh.Cylinder(0.19f, 0.2f, 0.12f, 7), M(0, 0, 1.72f, Mathf.PI / 2), MathUtil.Hex(0xa83a2a));
            return MeshModel(parent, "escoba", b);
        }

        // Rota: solo queda medio palo astillado
        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.06f, 0.07f, 0.9f, 6), M(0, 0, 0.3f, Mathf.PI / 2), MathUtil.Hex(0x9a6a3a));
            b.Add(ProcMesh.Cone(0.07f, 0.25f, 4), M(0, 0, 0.85f, Mathf.PI / 2), MathUtil.Hex(0x9a6a3a));
            return MeshModel(parent, "escobaRota", b);
        }

        public override Pose IdlePose() { return new Pose(-0.85f, 0.75f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = new Pose(-2.0f, 0.42f, 0.3f, 0);
            var end = new Pose(1.65f, 0.42f, -0.3f, 0.15f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, end, MathUtil.EaseOutCubic(t));
            return Pose.Mix(end, IdlePose(), MathUtil.EaseInOut(t));
        }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 0.3f, target.Z, BurstOpts.Of(8, 0xd9c38a, 4f, 2f, 0.12f).Dir(hit.DirX, hit.DirZ));
        }
    }

    // SARTÉN — golpe pesado: lento, muchísimo daño y empuje. El enemigo sale disparado.
    public sealed class PanWeapon : Weapon
    {
        public PanWeapon()
        {
            Id = "pan"; Name = "Sartén"; Icon = "🍳"; Style = "Golpe pesado · sale disparado";
            Damage = 28; AttackRange = 2.3f; AttackArc = 85; AttackCooldown = 0.85f;
            Windup = 0.3f; AttackDuration = 0.09f; Recovery = 0.38f;
            KnockbackForce = 26; Stun = 0.6f; Hitstop = 0.14f; Shake = 0.5f;
            Lunge = 4; MoveMult = 0.2f; Color = 0xffa040;
            MaxDurability = Durability = 8; BrokenName = "Mango de sartén";
            HitSound = "clang"; HitText = "¡CLANG!";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.07f, 0.09f, 0.95f, 6), M(0, 0, 0.35f, Mathf.PI / 2), MathUtil.Hex(0x5a3820));
            b.Add(ProcMesh.Cylinder(0.06f, 0.06f, 0.35f, 5), M(0, 0, 0.95f, Mathf.PI / 2), MathUtil.Hex(0x2e3036));
            // Una sartén absurdamente grande
            b.Add(ProcMesh.Cylinder(0.72f, 0.62f, 0.18f, 12), M(0, 0, 1.75f), MathUtil.Hex(0x2e3036));
            b.Add(ProcMesh.Cylinder(0.62f, 0.62f, 0.02f, 12), M(0, 0.1f, 1.75f), MathUtil.Hex(0x55585f));
            b.Add(ProcMesh.Cylinder(0.25f, 0.28f, 0.03f, 9), M(0.12f, 0.12f, 1.68f), UnityEngine.Color.white);
            b.Add(ProcMesh.Sphere(0.1f, 6, 4), M(0.12f, 0.14f, 1.68f, 0, 0, 0, 1, 0.5f, 1), MathUtil.Hex(0xffb020));
            return MeshModel(parent, "sarten", b);
        }

        // Rota: el mango con un trozo de cuello doblado
        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.07f, 0.09f, 0.95f, 6), M(0, 0, 0.35f, Mathf.PI / 2), MathUtil.Hex(0x5a3820));
            b.Add(ProcMesh.Cylinder(0.06f, 0.06f, 0.3f, 5), M(0, 0.08f, 0.92f, Mathf.PI / 2 + 0.7f), MathUtil.Hex(0x2e3036));
            return MeshModel(parent, "sartenRota", b);
        }

        public override Pose IdlePose() { return new Pose(-0.65f, 0.55f, 0.2f, 0); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var up = new Pose(-0.15f, -2.35f, 0, -0.15f);
            var slam = new Pose(0, 0.75f, 0, 0.3f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), up, MathUtil.EaseOutBack(Mathf.Min(1f, t)));
            if (phase == AttackPhase.Active) return Pose.Mix(up, slam, MathUtil.EaseInCubic(t) * 0.3f + t * 0.7f);
            // se queda un instante clavada en el suelo y vuelve
            float k = Mathf.Max(0f, (t - 0.35f) / 0.65f);
            return Pose.Mix(slam, IdlePose(), MathUtil.EaseInOut(k));
        }

        // La sartén manda al enemigo casi recto hacia donde mira el jugador: fácil de apuntar
        public override Vector2 KnockDirection(float fx, float fz, float dx, float dz)
        {
            float d = MathUtil.Hypot(dx, dz); if (d == 0f) d = 1f;
            float x = dx / d * 0.3f + fx * 0.7f, z = dz / d * 0.3f + fz * 0.7f;
            float l = MathUtil.Hypot(x, z); if (l == 0f) l = 1f;
            return new Vector2(x / l, z / l);
        }

        // Al final del golpe, onda en el suelo aunque no dé a nadie
        public override void OnAttackStart(Player player, Game g) { player.PendingSlam = true; }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Ring(target.X, target.Z, new RingOpts { Color = 0xffd080, Radius = 2.4f, Life = 0.3f, Y = 0.06f, R0 = 0.3f, Opacity = 0.8f });
            g.Effects.Burst(target.X, 0.8f, target.Z, BurstOpts.Of(14, 0xfff0b0, 9f, 4f, 0.12f).Dir(hit.DirX, hit.DirZ, 1.4f));
            g.Effects.Flash(target.X, 1.5f, target.Z, 0xffd27a, 4f, 0.15f);
        }
    }

    // CUBO — golpe de control: empuja, interrumpe y deja al enemigo aturdido con el cubo en la cabeza.
    public sealed class BucketWeapon : Weapon
    {
        public BucketWeapon()
        {
            Id = "bucket"; Name = "Cubo"; Icon = "🪣"; Style = "Aturde · interrumpe ataques";
            Damage = 10; AttackRange = 2.4f; AttackArc = 110; AttackCooldown = 0.6f;
            Windup = 0.16f; AttackDuration = 0.1f; Recovery = 0.26f;
            KnockbackForce = 11; Stun = 1.7f; Hitstop = 0.08f; Shake = 0.25f;
            Lunge = 2.5f; MoveMult = 0.4f; Color = 0x8fc8ff;
            MaxDurability = Durability = 10; BrokenName = "Asa del cubo";
            HitSound = "bonk"; HitText = "¡BONK!";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Torus(0.42f, 0.035f, 4, 10, Mathf.PI), M(0, 0, 0.55f, 0, Mathf.PI / 2), MathUtil.Hex(0x5d6873));
            b.AddDouble(ProcMesh.Cylinder(0.5f, 0.38f, 0.7f, 9, 1, true), M(0, -0.25f, 0.55f), MathUtil.Hex(0x8c9aa6));
            b.AddDouble(ProcMesh.Circle(0.38f, 9), M(0, -0.6f, 0.55f, Mathf.PI / 2), MathUtil.Hex(0x8c9aa6));
            b.Add(ProcMesh.Torus(0.5f, 0.04f, 4, 12), M(0, 0.1f, 0.55f, Mathf.PI / 2), MathUtil.Hex(0x5d6873));
            b.Add(ProcMesh.Circle(0.46f, 9), M(0, 0, 0.55f, -Mathf.PI / 2), MathUtil.Hex(0x3f7fc0));
            return MeshModel(parent, "cubo", b);
        }

        // Roto: el cubo se ha caído y solo queda el asa
        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Torus(0.42f, 0.035f, 4, 10, Mathf.PI), M(0, 0, 0.55f, 0, Mathf.PI / 2), MathUtil.Hex(0x5d6873));
            return MeshModel(parent, "asa", b);
        }

        public override Pose IdlePose() { return new Pose(-1.3f, 0.2f, 0, 0.1f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = new Pose(-2.1f, -0.1f, 0.5f, 0.2f);
            var end = new Pose(1.1f, 0.0f, -0.6f, 0.4f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, end, MathUtil.EaseOutCubic(t));
            return Pose.Mix(end, IdlePose(), MathUtil.EaseInOut(t));
        }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            // Salpicadura de agua y el enemigo acaba con un cubo en la cabeza
            g.Effects.Burst(target.X, 1.0f, target.Z, BurstOpts.Of(14, 0x5fa8ff, 5f, 5f, 0.1f).WithColorVar(0.1f));
            var e = target as Enemy;
            if (e != null) e.SetBucketHat(hit.Stun);
        }
    }

    // PALO — estocadas rápidas y largas. Cada tercer golpe seguido es un "¡ZAS!" con más empuje.
    public sealed class StickWeapon : Weapon
    {
        int combo;
        float lastHitTime = -10f;

        public StickWeapon()
        {
            Id = "stick"; Name = "Palo"; Icon = "🪵"; Style = "Rápido · mucho alcance · combo x3";
            Damage = 6; AttackRange = 3.4f; AttackArc = 40; AttackCooldown = 0.26f;
            Windup = 0.05f; AttackDuration = 0.08f; Recovery = 0.12f;
            KnockbackForce = 8; Stun = 0.25f; Hitstop = 0.04f; Shake = 0.12f;
            Lunge = 1.5f; MoveMult = 0.7f; Color = 0xb6e07a;
            MaxDurability = Durability = 18; BrokenName = "Palito";
            HitSound = "thwack"; HitText = null;
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            var wood = MathUtil.Hex(0x7b5a36);
            b.Add(ProcMesh.Cylinder(0.05f, 0.09f, 2.7f, 5), M(0, 0, 1.0f, Mathf.PI / 2), wood);
            b.Add(ProcMesh.Dodecahedron(0.1f), M(0.06f, 0, 1.4f), wood);
            b.Add(ProcMesh.Cylinder(0.02f, 0.03f, 0.4f, 4), M(0.12f, 0.08f, 1.9f, Mathf.PI / 2, 0, -0.8f), wood);
            b.Add(ProcMesh.Tetrahedron(0.12f), M(0.25f, 0.15f, 2.0f), MathUtil.Hex(0x5f9a3a));
            return MeshModel(parent, "palo", b);
        }

        // Roto: un palito ridículo
        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            var wood = MathUtil.Hex(0x7b5a36);
            b.Add(ProcMesh.Cylinder(0.04f, 0.06f, 0.7f, 5), M(0, 0, 0.25f, Mathf.PI / 2), wood);
            b.Add(ProcMesh.Cone(0.07f, 0.25f, 4), M(0, 0, 0.65f, Mathf.PI / 2), wood);
            return MeshModel(parent, "palito", b);
        }

        public override Pose IdlePose() { return new Pose(-0.45f, 0.3f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = new Pose(-0.2f, 0.12f, 0, -0.45f);
            var thrust = new Pose(-0.05f, 0.1f, 0.3f, 1.0f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, thrust, MathUtil.EaseOutCubic(t));
            return Pose.Mix(thrust, IdlePose(), MathUtil.EaseInOut(t));
        }

        public override Vector2 KnockDirection(float fx, float fz, float dx, float dz) { return new Vector2(fx, fz); }

        public override void ModifyHit(Hit hit, HitCtx ctx)
        {
            if (Broken) return;
            if (ctx.Time - lastHitTime > 1.0f) combo = 0;
            if (!ctx.FirstHitOfAttack) return;
            lastHitTime = ctx.Time;
            combo++;
            if (combo >= 3)
            {
                combo = 0;
                hit.Knock *= 2.2f; hit.Damage += 6; hit.Stun = 0.6f;
                hit.Text = "¡ZAS!"; hit.Hitstop = 0.09f; hit.Shake = 0.3f;
            }
        }
    }

    // TOMO PESADO — sale de las estanterías. Golpe de canto lento que deja al enemigo muy aturdido ("¡Lee esto!").
    public sealed class BookWeapon : Weapon
    {
        public BookWeapon()
        {
            Id = "book"; Name = "Tomo pesado"; Icon = "📕"; Style = "Lento · aturde mucho";
            Damage = 14; AttackRange = 2.0f; AttackArc = 75; AttackCooldown = 0.6f;
            Windup = 0.18f; AttackDuration = 0.09f; Recovery = 0.28f;
            KnockbackForce = 11; Stun = 1.1f; Hitstop = 0.09f; Shake = 0.25f;
            Lunge = 2.5f; MoveMult = 0.35f; Color = 0xc04040;
            MaxDurability = Durability = 10; BrokenName = "Tapas sueltas";
            HitSound = "thwack"; HitText = "¡LEE ESTO!";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Box(0.75f, 0.32f, 1.0f), M(0, 0, 0.75f), MathUtil.Hex(0x8a2a2a));
            b.Add(ProcMesh.Box(0.68f, 0.26f, 0.96f), M(0.05f, 0, 0.75f), MathUtil.Hex(0xf0e6c8));
            b.Add(ProcMesh.Box(0.08f, 0.34f, 0.2f), M(0.38f, 0, 0.75f), MathUtil.Hex(0xd8b040));
            b.Add(ProcMesh.Octahedron(0.1f), M(0, 0.17f, 0.75f), MathUtil.Hex(0xd8b040));
            return MeshModel(parent, "tomo", b);
        }

        // Roto: las tapas colgando sin páginas
        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Box(0.75f, 0.04f, 1.0f), M(0, 0.12f, 0.7f, 0, 0, 0.4f), MathUtil.Hex(0x8a2a2a));
            b.Add(ProcMesh.Box(0.75f, 0.04f, 1.0f), M(0, -0.12f, 0.7f, 0, 0, -0.4f), MathUtil.Hex(0x8a2a2a));
            return MeshModel(parent, "tapas", b);
        }

        public override Pose IdlePose() { return new Pose(-0.55f, 0.5f, 1.4f, -0.1f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var up = new Pose(-0.4f, -1.9f, 1.57f, -0.1f);
            var down = new Pose(0, 0.65f, 1.57f, 0.25f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), up, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(up, down, MathUtil.EaseInCubic(t) * 0.4f + t * 0.6f);
            return Pose.Mix(down, IdlePose(), MathUtil.EaseInOut(t));
        }

        // Vuelan unas cuantas páginas
        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 1.3f, target.Z, BurstOpts.Of(10, 0xf0e6c8, 3f, 4f, 0.12f).WithGravity(4f).WithLife(0.9f));
        }
    }

    // PATA DE SILLA — sale de las mesas y los armeros. Garrote equilibrado: golpe en arco a buen ritmo.
    public sealed class ChairLegWeapon : Weapon
    {
        public ChairLegWeapon()
        {
            Id = "chairleg"; Name = "Pata de silla"; Icon = "🪑"; Style = "Garrote equilibrado";
            Damage = 11; AttackRange = 2.2f; AttackArc = 100; AttackCooldown = 0.42f;
            Windup = 0.1f; AttackDuration = 0.1f; Recovery = 0.18f;
            KnockbackForce = 13; Stun = 0.4f; Hitstop = 0.06f; Shake = 0.18f;
            Lunge = 2.2f; MoveMult = 0.5f; Color = 0xd0a060;
            MaxDurability = Durability = 12; BrokenName = "Astilla";
            HitSound = "thwack"; HitText = null;
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.07f, 0.11f, 1.3f, 6), M(0, 0, 0.6f, Mathf.PI / 2), MathUtil.Hex(0x9a6a3a));
            b.Add(ProcMesh.Cylinder(0.13f, 0.13f, 0.1f, 6), M(0, 0, 0.9f, Mathf.PI / 2), MathUtil.Hex(0x6a4426));
            b.Add(ProcMesh.Cylinder(0.015f, 0.015f, 0.25f, 4), M(0, 0.12f, 1.15f), MathUtil.Hex(0x9aa3ad));
            return MeshModel(parent, "pata", b);
        }

        public override GameObject BuildBrokenModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.05f, 0.1f, 0.5f, 5), M(0, 0, 0.2f, Mathf.PI / 2), MathUtil.Hex(0x9a6a3a));
            b.Add(ProcMesh.Cone(0.05f, 0.25f, 4), M(0, 0, 0.55f, Mathf.PI / 2), MathUtil.Hex(0x9a6a3a));
            return MeshModel(parent, "astilla", b);
        }

        public override Pose IdlePose() { return new Pose(-0.6f, 0.5f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = new Pose(-1.6f, -0.3f, 0, -0.1f);
            var end = new Pose(1.0f, 0.35f, 0, 0.15f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, end, MathUtil.EaseOutCubic(t));
            return Pose.Mix(end, IdlePose(), MathUtil.EaseInOut(t));
        }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 0.9f, target.Z, BurstOpts.Of(6, 0x9a6a3a, 4f, 3f, 0.08f));
        }
    }

    // Comida: ocupa la mano igual que un arma. Al "atacar" con ella, el jugador se la come:
    // tarda un rato, va más lento y si le golpean pierde el bocado (la comida sigue en la mano).
    public class FoodItem : Weapon
    {
        public FoodItem()
        {
            IsFood = true; Heal = 30; EatTime = 1.4f; EatMoveMult = 0.35f;
            AttackCooldown = 0.3f; Color = 0xffd36a;
        }

        public override Pose IdlePose() { return new Pose(-0.5f, 0.5f, 0, -0.05f); }

        // Pose mientras se come: a la boca, con mordiscos
        public override Pose EatPose(float t)
        {
            float bite = Mathf.Abs(Mathf.Sin(t * 18f)) * 0.18f;
            return new Pose(0.15f, -0.55f - bite, 0.2f, -0.3f + bite * 0.4f);
        }
    }

    public sealed class ChickenFood : FoodItem
    {
        public ChickenFood()
        {
            Id = "chicken"; Name = "Muslo de pollo"; Icon = "🍗";
            Heal = 35; EatTime = 1.6f; Style = "+35 vida · ataca para comer";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Icosahedron(0.3f), M(0, 0, 0.55f, 0, 0, 0, 1, 0.85f, 1.25f), MathUtil.Hex(0xb5652e));
            b.Add(ProcMesh.Cylinder(0.06f, 0.06f, 0.45f, 5), M(0, 0, 0.12f, Mathf.PI / 2), MathUtil.Hex(0xe8e0c8));
            b.Add(ProcMesh.Sphere(0.1f, 5, 4), M(0, 0, -0.1f), MathUtil.Hex(0xe8e0c8));
            return MeshModel(parent, "pollo", b);
        }
    }

    public sealed class CheeseFood : FoodItem
    {
        public CheeseFood()
        {
            Id = "cheese"; Name = "Cuña de queso"; Icon = "🧀";
            Heal = 20; EatTime = 0.9f; Style = "+20 vida · se come rápido";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.42f, 0.42f, 0.3f, 3, 1, false, 0, Mathf.PI / 2.2f), M(0, 0, 0.25f), MathUtil.Hex(0xf2c94c));
            for (int i = 0; i < 3; i++)
                b.Add(ProcMesh.Circle(0.04f + i * 0.015f, 6), M(0.1f + i * 0.08f, 0.152f, 0.3f + (i % 2) * 0.08f, -Mathf.PI / 2), MathUtil.Hex(0xc89a20));
            return MeshModel(parent, "queso", b);
        }
    }
}
