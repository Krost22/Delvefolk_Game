using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public enum Element { None, Water, Fire, Plant, Poison, Future }

    // ARMAS ELEMENTALES: un golpe básico (tocar ATACAR) y una habilidad especial (mantener ATACAR y soltar)
    // con preparación visible y enfriamiento. Siguen el contrato de Weapon (poses, OnHit, durabilidad...).
    // El modelo sale de Resources/WeaponModels (Meshy) con su agarre; si falta, se usa el modelo por código.
    public abstract class ElementalWeapon : Weapon
    {
        public const float ChargeTime = 0.45f;   // cuánto hay que mantener ATACAR para el especial

        public Element Element;
        public string SpecialName = "Especial";
        public float SpecialCooldown = 7f;       // enfriamiento (empieza al soltar el especial)
        public float SpecialWindup = 0.4f;       // preparación con aviso en el suelo (sin efecto)
        public float SpecialActive = 0.35f;      // pose tras el efecto
        public float SpecialCd;                  // lo que le queda de enfriamiento
        public float SpecialMoveMult = 0.25f;
        string shownStyle;

        public bool SpecialReady { get { return SpecialCd <= 0f && !Broken; } }
        // La aspiradora: mantener ATACAR es su ataque básico (succión) y tocarlo dispara el especial
        public virtual bool HoldIsBasic { get { return false; } }

        protected ElementalWeapon()
        {
            MaxDurability = Durability = 45;
        }

        public override GameObject BuildModel(Transform parent)
        {
            return WeaponModels.Build(Id, parent) ?? BuildProcedural(parent);
        }

        public override GameObject BuildBrokenModel(Transform parent)
        {
            var m = BuildModel(parent);
            m.transform.localScale = new Vector3(1f, 1f, 0.6f);
            return m;
        }

        // Modelo de reserva hecho con primitivas (frente en +Z, mango en el origen)
        protected abstract GameObject BuildProcedural(Transform parent);

        // Cada frame mientras está en la mano
        public virtual void Tick(Player p, Game g, float dt)
        {
            SpecialCd = Mathf.Max(0f, SpecialCd - dt);
            RefreshStyle(p, g);
        }

        // El panel del arma enseña el especial y su enfriamiento (solo se escribe cuando cambia)
        protected void RefreshStyle(Player p, Game g)
        {
            string s = Broken ? Style : StyleText(p);
            if (s == shownStyle) return;
            shownStyle = s;
            Style = s;
            g.Ui.SetWeapon(p.Index, this);
        }

        protected virtual string StyleText(Player p)
        {
            string state = SpecialCd > 0f ? Mathf.CeilToInt(SpecialCd) + " s" : "¡listo!";
            return "Mantén " + p.Controls.S.AttackKey + ": " + SpecialName + " · " + state;
        }

        // Al empezar la preparación del especial (aviso visual)
        public virtual void SpecialStart(Player p, Game g) { }
        // Al acabar la preparación: el efecto en sí
        public abstract void Special(Player p, Game g);
        // Al dejar de llevarla en la mano (soltarla, cambiarla, comer...)
        public virtual void OnUnequip(Player p, Game g) { }

        public virtual Pose ChargePose(float k) { return Pose.Mix(IdlePose(), new Pose(-1.6f, -0.6f, 0f, -0.15f), MathUtil.EaseOutCubic(k)); }
        public virtual Pose SpecialPose(bool windup, float t)
        {
            var up = new Pose(-0.2f, -2.4f, 0f, -0.15f);
            var slam = new Pose(0f, 0.8f, 0f, 0.3f);
            if (windup) return Pose.Mix(IdlePose(), up, MathUtil.EaseOutBack(Mathf.Min(1f, t)));
            return Pose.Mix(slam, IdlePose(), MathUtil.EaseInOut(t));
        }

        // Pose de barrido horizontal (lo comparten varias armas)
        protected Pose Sweep(AttackPhase phase, float t, Pose back, Pose end)
        {
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(back, end, MathUtil.EaseOutCubic(t));
            return Pose.Mix(end, IdlePose(), MathUtil.EaseInOut(t));
        }

        // ¿El piso actual tiene la misma afinidad que el arma? (refuerza algunos especiales)
        protected bool OnHomeFloor(Game g) { return g.Affinity == Element && Element != Element.None; }

        // Enemigos vivos en un radio (copia: los efectos pueden matar y alterar la lista)
        protected static List<Enemy> EnemiesNear(Game g, float x, float z, float r)
        {
            var list = new List<Enemy>();
            foreach (var e in g.Enemies)
            {
                if (e.Dead || e.Removed || e.State == EnemyState.Falling || !e.Body.Enabled || e.Peaceful) continue;
                if (MathUtil.Hypot(e.X - x, e.Z - z) > r + e.Radius) continue;
                if (!g.Dungeon.LineClear(x, z, e.X, e.Z)) continue;
                list.Add(e);
            }
            return list;
        }

        // Golpe de área con empuje hacia fuera (los pesados y los jefes casi no se mueven)
        protected static void AreaHit(Game g, Player p, Enemy e, float dmg, float knock, float stun)
        {
            var o = p.Outward(e.X, e.Z);
            float k = knock * p.Gear.KnockDealtMult() * (ElementalRules.IsHeavy(e) ? ElementalRules.HeavyKnockMult : 1f);
            e.TakeHit(new Hit { Damage = Mathf.Round(dmg * p.Gear.DamageMult()), Knock = k, Stun = stun, DirX = o.x, DirZ = o.y, By = p });
        }
    }

    // Reglas compartidas de los efectos elementales
    public static class ElementalRules
    {
        public const float HeavyMass = 2f;          // a partir de esta masa un enemigo es "pesado"
        public const float MediumMass = 1.1f;       // entre esta y la anterior, "mediano"
        public const float HeavyKnockMult = 0.4f;

        public static bool IsBoss(Enemy e) { return e is Boss; }
        public static bool IsHeavy(Enemy e) { return IsBoss(e) || e.Body.Mass >= HeavyMass; }
        public static bool IsMedium(Enemy e) { return !IsHeavy(e) && e.Body.Mass > MediumMass; }
    }

    // ---------- AGUA — El Trapeador de las Profundidades ----------
    public sealed class MopWeapon : ElementalWeapon
    {
        public float SpinRadius = 3.0f, SpinDamage = 12f, SpinKnock = 16f;
        public float WetRadius = 3.2f, WetLife = 3.5f, WetSlow = 0.55f;

        public MopWeapon()
        {
            Id = "mop"; Name = "Trapeador de las Profundidades"; Icon = "🌊"; Element = Element.Water;
            SpecialName = "Torbellino de limpieza"; SpecialCooldown = 7f; SpecialWindup = 0.45f; SpecialActive = 0.4f;
            Damage = 11; AttackRange = 2.6f; AttackArc = 140; AttackCooldown = 0.55f;
            Windup = 0.12f; AttackDuration = 0.11f; Recovery = 0.22f;
            KnockbackForce = 9; Stun = 0.35f; Hitstop = 0.06f; Shake = 0.18f;
            Lunge = 2f; MoveMult = 0.45f; Color = 0x5bc8ff;
            BrokenName = "Palo del trapeador"; HitSound = "thwack"; HitText = "¡CHOF!";
        }

        protected override GameObject BuildProcedural(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.055f, 0.065f, 1.75f, 6), M(0, 0, 0.7f, Mathf.PI / 2), MathUtil.Hex(0x7a5a36));
            b.Add(ProcMesh.Cylinder(0.12f, 0.12f, 0.14f, 7), M(0, 0, 1.62f, Mathf.PI / 2), MathUtil.Hex(0x6d7a86));
            // Cabeza de tela sucia: tiras que cuelgan
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f;
                b.Add(ProcMesh.Box(0.09f, 0.07f, 0.55f), M(Mathf.Cos(a) * 0.12f, Mathf.Sin(a) * 0.12f, 1.95f, 0, 0, a), MathUtil.Hex(i % 2 == 0 ? 0x9aa6a8 : 0x7f8b7c));
            }
            return MeshModel(parent, "trapeador", b);
        }

        public override Pose IdlePose() { return new Pose(-0.8f, 0.7f); }
        public override Pose PoseFor(AttackPhase phase, float t) { return Sweep(phase, t, new Pose(-2.0f, 0.5f, 0.2f), new Pose(1.6f, 0.5f, -0.2f, 0.15f)); }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 0.6f, target.Z, BurstOpts.Of(10, 0x6fd0ff, 5f, 3f, 0.1f).Dir(hit.DirX, hit.DirZ));
        }

        public override void SpecialStart(Player p, Game g)
        {
            g.Effects.Ring(p.X, p.Z, new RingOpts { Color = 0x5bc8ff, Radius = SpinRadius, Life = SpecialWindup, Y = 0.06f, R0 = SpinRadius * 0.95f, Opacity = 0.5f });
            g.Audio.Play("swing", 0.6f);
        }

        public override void Special(Player p, Game g)
        {
            g.Audio.Play("swing", 1.4f);
            g.CameraCtl.AddShake(0.3f);
            g.Effects.SwingArc(p.X, p.Z, p.Facing, Mathf.PI * 2f, SpinRadius, 0x6fd0ff, 0.3f);
            g.Effects.Burst(p.X, 0.4f, p.Z, BurstOpts.Of(26, 0x6fd0ff, 8f, 3f, 0.12f).WithLife(0.5f));
            foreach (var e in EnemiesNear(g, p.X, p.Z, SpinRadius)) AreaHit(g, p, e, SpinDamage, SpinKnock, 0.4f);
            g.Elemental.AddWetFloor(p.X, p.Z, WetRadius, WetLife, WetSlow);
            g.Ui.FloatText(p.X, 2.8f, p.Z, "¡TORBELLINO!", TextStyle.Label);
        }

        public override Pose SpecialPose(bool windup, float t)
        {
            if (windup) return Pose.Mix(IdlePose(), new Pose(-2.6f, 0.45f), MathUtil.EaseOutCubic(t));
            return Pose.Mix(new Pose(3.4f, 0.45f, 0f, 0.2f), IdlePose(), MathUtil.EaseInOut(t));
        }
    }

    // ---------- FUEGO — La Sartén Infernal ----------
    public sealed class InfernalPanWeapon : ElementalWeapon
    {
        public float WaveRadius = 3.2f, WaveDamage = 14f, WaveKnock = 10f;
        public float BurnTime = 3f, BurnTick = 0.5f, BurnDamage = 4f;

        public InfernalPanWeapon()
        {
            Id = "hellpan"; Name = "Sartén Infernal"; Icon = "🔥"; Element = Element.Fire;
            SpecialName = "Cocina volcánica"; SpecialCooldown = 8f; SpecialWindup = 0.4f;
            Damage = 22; AttackRange = 2.2f; AttackArc = 85; AttackCooldown = 0.7f;
            Windup = 0.2f; AttackDuration = 0.09f; Recovery = 0.3f;
            KnockbackForce = 18; Stun = 0.55f; Hitstop = 0.1f; Shake = 0.35f;
            Lunge = 3f; MoveMult = 0.3f; Color = 0xff7a2a;
            BrokenName = "Mango chamuscado"; HitSound = "clang"; HitText = "¡A LA SARTÉN!";
        }

        protected override GameObject BuildProcedural(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.065f, 0.085f, 0.9f, 6), M(0, 0, 0.35f, Mathf.PI / 2), MathUtil.Hex(0x3a2a20));
            b.Add(ProcMesh.Cylinder(0.62f, 0.54f, 0.16f, 10), M(0, 0, 1.5f), MathUtil.Hex(0x2a2a2e));
            b.Add(ProcMesh.Cylinder(0.54f, 0.54f, 0.02f, 10), M(0, 0.09f, 1.5f), MathUtil.Hex(0x3d3a3a));
            // Grietas volcánicas (sin llamas permanentes)
            b.Add(ProcMesh.Box(0.5f, 0.025f, 0.05f), M(0.05f, 0.1f, 1.45f, 0, 0.6f), MathUtil.Hex(0xff6a1a));
            b.Add(ProcMesh.Box(0.35f, 0.025f, 0.05f), M(-0.12f, 0.1f, 1.62f, 0, -0.9f), MathUtil.Hex(0xff8a2a));
            return MeshModel(parent, "sartenInfernal", b);
        }

        public override Pose IdlePose() { return new Pose(-0.65f, 0.55f, 0.2f, 0); }
        public override Pose PoseFor(AttackPhase phase, float t) { return Sweep(phase, t, new Pose(-1.6f, -0.3f, 0.2f), new Pose(1.0f, 0.4f, -0.2f, 0.2f)); }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 0.9f, target.Z, BurstOpts.Of(10, 0xffa040, 6f, 3f, 0.09f).Dir(hit.DirX, hit.DirZ));
            g.Effects.Flash(target.X, 1.3f, target.Z, 0xff8a30, 2.5f, 0.1f);
        }

        public override void SpecialStart(Player p, Game g)
        {
            float x = p.X + Mathf.Sin(p.Facing) * 1.2f, z = p.Z + Mathf.Cos(p.Facing) * 1.2f;
            g.Effects.Ring(x, z, new RingOpts { Color = 0xff5a1a, Radius = WaveRadius, Life = SpecialWindup, Y = 0.06f, R0 = WaveRadius * 0.95f, Opacity = 0.5f });
        }

        public override void Special(Player p, Game g)
        {
            float x = p.X + Mathf.Sin(p.Facing) * 1.2f, z = p.Z + Mathf.Cos(p.Facing) * 1.2f;
            g.Audio.Play("clang"); g.Audio.Play("explosion", 0.5f);
            g.CameraCtl.AddShake(0.45f);
            g.RequestHitstop(0.06f);
            g.Effects.Flash(x, 1.2f, z, 0xff6a20, 5f, 0.25f);
            g.Effects.Ring(x, z, new RingOpts { Color = 0xff7a2a, Radius = WaveRadius, Life = 0.45f, Y = 0.08f, R0 = 0.3f, Opacity = 0.9f });
            g.Effects.Burst(x, 0.3f, z, BurstOpts.Of(30, 0xff8a20, 7f, 5f, 0.14f).WithColorVar(0.12f).WithLife(0.6f));
            foreach (var e in EnemiesNear(g, x, z, WaveRadius))
            {
                AreaHit(g, p, e, WaveDamage, WaveKnock, 0.3f);
                if (!e.Dead) g.Elemental.Burn(e, BurnTime, BurnTick, BurnDamage, p);
            }
            g.Ui.FloatText(x, 2.6f, z, "¡COCINA VOLCÁNICA!", TextStyle.Label);
        }
    }

    // ---------- PLANTA — El Rastrillo Traicionero ----------
    public sealed class RakeWeapon : ElementalWeapon
    {
        public float HookRange = 5f, HookArc = 40f, PullDistance = 3.5f, PullSpeed = 10f;
        public float RootTime = 1.0f, HomeRootTime = 1.6f;

        public RakeWeapon()
        {
            Id = "rake"; Name = "Rastrillo Traicionero"; Icon = "🌿"; Element = Element.Plant;
            SpecialName = "Ven aquí, maleza"; SpecialCooldown = 6f; SpecialWindup = 0.25f; SpecialActive = 0.45f;
            Damage = 13; AttackRange = 3.2f; AttackArc = 60; AttackCooldown = 0.8f;
            Windup = 0.18f; AttackDuration = 0.12f; Recovery = 0.3f;
            KnockbackForce = 8; Stun = 0.4f; Hitstop = 0.06f; Shake = 0.18f;
            Lunge = 1.5f; MoveMult = 0.4f; Color = 0x7dd65b;
            BrokenName = "Mango del rastrillo"; HitSound = "thwack"; HitText = "¡RAS!";
        }

        protected override GameObject BuildProcedural(Transform parent)
        {
            var b = new MeshBuilder();
            var wood = MathUtil.Hex(0x8a6a3a); var iron = MathUtil.Hex(0x6a6e72); var leaf = MathUtil.Hex(0x5fae3a);
            b.Add(ProcMesh.Cylinder(0.05f, 0.06f, 2.2f, 6), M(0, 0, 0.95f, Mathf.PI / 2), wood);
            b.Add(ProcMesh.Box(0.8f, 0.06f, 0.08f), M(0, 0, 2.05f), iron);
            for (int i = 0; i < 6; i++) b.Add(ProcMesh.Box(0.04f, 0.2f, 0.04f), M(-0.35f + i * 0.14f, -0.1f, 2.07f), iron);
            for (int i = 0; i < 4; i++) b.Add(ProcMesh.Octahedron(0.07f), M(0.06f * (i % 2 == 0 ? 1 : -1), 0.03f, 0.6f + i * 0.35f), leaf);
            return MeshModel(parent, "rastrillo", b);
        }

        public override Pose IdlePose() { return new Pose(-0.5f, 0.45f); }
        public override Pose PoseFor(AttackPhase phase, float t) { return Sweep(phase, t, new Pose(-0.9f, 0.3f, 0, -0.1f), new Pose(0.7f, 0.35f, 0, 0.25f)); }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 0.6f, target.Z, BurstOpts.Of(8, 0x7dd65b, 4f, 3f, 0.1f).Dir(hit.DirX, hit.DirZ));
        }

        public override Pose SpecialPose(bool windup, float t)
        {
            if (windup) return Pose.Mix(IdlePose(), new Pose(-0.1f, 0.1f, 0f, 0.9f), MathUtil.EaseOutCubic(t));
            return Pose.Mix(new Pose(0f, 0.1f, 0f, -0.5f), IdlePose(), MathUtil.EaseInOut(t));
        }

        // Engancha al enemigo más cercano dentro del alcance y delante del jugador
        public override void Special(Player p, Game g)
        {
            Enemy best = null;
            float bestD = float.PositiveInfinity;
            foreach (var e in g.Enemies)
            {
                if (e.Dead || e.Removed || e.State == EnemyState.Falling || !e.Body.Enabled || e.Peaceful) continue;
                float dx = e.X - p.X, dz = e.Z - p.Z, d = MathUtil.Hypot(dx, dz);
                if (d > HookRange + e.Radius) continue;
                if (Mathf.Abs(MathUtil.AngleDiff(p.Facing, Mathf.Atan2(dx, dz))) > HookArc * Mathf.Deg2Rad) continue;
                if (!g.Dungeon.LineClear(p.X, p.Z, e.X, e.Z)) continue;
                if (d < bestD) { bestD = d; best = e; }
            }
            float tx = best != null ? best.X : p.X + Mathf.Sin(p.Facing) * HookRange, tz = best != null ? best.Z : p.Z + Mathf.Cos(p.Facing) * HookRange;
            g.Effects.Tracer(p.X, p.Z, tx, tz, 0.8f, 0x7dd65b, 0.25f);
            g.Audio.Play("swing", 0.8f);
            if (best == null) { g.Ui.FloatText(p.X, 2.6f, p.Z, "Nada que enganchar", TextStyle.Small); return; }
            g.Effects.Burst(best.X, 0.8f, best.Z, BurstOpts.Of(12, 0x7dd65b, 4f, 3f, 0.1f));
            if (ElementalRules.IsHeavy(best))
            {
                g.Elemental.Slow(best, 0.6f, 1.2f);
                g.Ui.FloatText(best.X, 2.6f, best.Z, "¡RESISTE!", TextStyle.Label);
                return;
            }
            float root = OnHomeFloor(g) ? HomeRootTime : RootTime;
            bool medium = ElementalRules.IsMedium(best);
            g.Elemental.Pull(best, p, medium ? PullDistance * 0.55f : PullDistance, PullSpeed, medium ? 0f : root);
            if (medium) g.Elemental.Slow(best, 0.5f, 1.2f);
            g.Ui.FloatText(best.X, 2.6f, best.Z, "¡VEN AQUÍ!", TextStyle.Label);
        }
    }

    // ---------- VENENO — El Paraguas de Esporas (champiñón apaleador) ----------
    public sealed class SporeParasolWeapon : ElementalWeapon
    {
        public float CloudRadius = 3.0f, CloudLife = 4f, HomeLifeMult = 1.35f;
        public float PoisonTime = 2.5f, PoisonTick = 0.5f, PoisonDamage = 4f;
        public float HitSporeTime = 1.5f, HitSporeDamage = 2f;

        public SporeParasolWeapon()
        {
            Id = "parasol"; Name = "Paraguas de Esporas"; Icon = "🍄"; Element = Element.Poison;
            SpecialName = "Lluvia de esporas"; SpecialCooldown = 9f; SpecialWindup = 0.4f;
            Damage = 16; AttackRange = 2.3f; AttackArc = 90; AttackCooldown = 0.8f;
            Windup = 0.24f; AttackDuration = 0.1f; Recovery = 0.32f;
            KnockbackForce = 13; Stun = 0.5f; Hitstop = 0.09f; Shake = 0.25f;
            Lunge = 2.5f; MoveMult = 0.3f; Color = 0xc04ad8;
            BrokenName = "Varilla del paraguas"; HitSound = "bonk"; HitText = "¡PUF!";
        }

        protected override GameObject BuildProcedural(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.04f, 0.04f, 1.9f, 6), M(0, 0, 0.85f, Mathf.PI / 2), MathUtil.Hex(0xf0e2c8));
            b.Add(ProcMesh.Cone(0.26f, 1.2f, 8), M(0, 0, 1.25f, Mathf.PI / 2), MathUtil.Hex(0xc0307a));
            for (int i = 0; i < 4; i++) b.Add(ProcMesh.Sphere(0.05f, 5, 4), M(0.18f * Mathf.Cos(i * 1.6f), 0.18f * Mathf.Sin(i * 1.6f), 0.9f + i * 0.1f), MathUtil.Hex(0xfff2d8));
            b.Add(ProcMesh.Torus(0.1f, 0.03f, 4, 8, Mathf.PI), M(0.1f, 0, -0.05f, 0, Mathf.PI / 2), MathUtil.Hex(0x7dd65b));
            return MeshModel(parent, "paraguas", b);
        }

        public override Pose IdlePose() { return new Pose(-0.6f, 0.6f); }
        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var up = new Pose(-0.3f, -1.9f, 0, -0.1f);
            var down = new Pose(0f, 0.7f, 0, 0.25f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), up, MathUtil.EaseOutCubic(t));
            if (phase == AttackPhase.Active) return Pose.Mix(up, down, MathUtil.EaseInCubic(t));
            return Pose.Mix(down, IdlePose(), MathUtil.EaseInOut(t));
        }

        public override void OnHit(IHittable target, Hit hit, Game g)
        {
            g.Effects.Burst(target.X, 0.8f, target.Z, BurstOpts.Of(8, 0xb46ad8, 2f, 2f, 0.12f).WithLife(0.7f));
            var e = target as Enemy;
            if (e != null && !e.Dead) g.Elemental.Poison(e, HitSporeTime, PoisonTick, HitSporeDamage, hit.By);
        }

        public override void SpecialStart(Player p, Game g)
        {
            g.Effects.Ring(p.X, p.Z, new RingOpts { Color = 0x9be04a, Radius = CloudRadius, Life = SpecialWindup, Y = 0.06f, R0 = CloudRadius * 0.95f, Opacity = 0.5f });
        }

        public override void Special(Player p, Game g)
        {
            float life = CloudLife * (OnHomeFloor(g) ? HomeLifeMult : 1f);
            g.Audio.Play("bonk");
            g.CameraCtl.AddShake(0.2f);
            g.Elemental.AddSporeCloud(p.X, p.Z, CloudRadius, life, PoisonTime, PoisonTick, PoisonDamage, p);
            g.Effects.Burst(p.X, 0.6f, p.Z, BurstOpts.Of(24, 0xa860d8, 3f, 3f, 0.18f).WithColorVar(0.15f).WithLife(1f));
            g.Ui.FloatText(p.X, 2.8f, p.Z, "¡LLUVIA DE ESPORAS!", TextStyle.Label);
        }
    }

    // ---------- FUTURISTA — La Aspiradora Cuántica ----------
    // Mantener ATACAR: succión en cono (gasta energía). Tocar ATACAR: Descarga de basura (lo almacenado sale
    // disparado como proyectil; si no hay nada, una descarga de energía reducida). Energía propia del arma.
    public sealed class QuantumVacuumWeapon : ElementalWeapon
    {
        public float MaxEnergy = 100f, Energy = 100f, Drain = 28f, Regen = 18f, RegenDelay = 0.5f;
        public float SuckRange = 5.5f, SuckArc = 56f, SuckSpeedLight = 6f, SuckSpeedMedium = 2.5f, CaptureDist = 1.3f;
        public int Capacity = 3;
        public float LaunchSpeed = 24f, LaunchDamage = 10f, BlastCost = 30f, BlastDamage = 12f, BlastRange = 4f;
        public readonly List<Enemy> Stored = new List<Enemy>();
        public bool Sucking;
        float sinceUse = 10f, heavyMsgT, suckFx;

        public override bool HoldIsBasic { get { return true; } }

        public QuantumVacuumWeapon()
        {
            Id = "vacuum"; Name = "Aspiradora Cuántica"; Icon = "🌀"; Element = Element.Future;
            SpecialName = "Descarga de basura"; SpecialCooldown = 1.2f; SpecialWindup = 0.15f; SpecialActive = 0.3f;
            SpecialMoveMult = 0.5f;
            Damage = 6; AttackRange = 1.8f; AttackArc = 70; AttackCooldown = 0.5f;
            Windup = 0.1f; AttackDuration = 0.08f; Recovery = 0.2f;
            KnockbackForce = 10; Stun = 0.3f; Lunge = 1f; MoveMult = 0.5f; Color = 0x4ff0ff;
            MaxDurability = Durability = float.PositiveInfinity;   // no se rompe: su límite es la energía
            HitSound = "thwack";
        }

        protected override GameObject BuildProcedural(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Box(0.32f, 0.3f, 0.9f), M(0, 0.05f, 0.35f), MathUtil.Hex(0xefe4cc));
            b.Add(ProcMesh.Box(0.34f, 0.08f, 0.6f), M(0, 0.22f, 0.4f), MathUtil.Hex(0x3a3d44));
            b.Add(ProcMesh.Cylinder(0.11f, 0.2f, 0.5f, 8), M(0, 0.05f, 1.05f, Mathf.PI / 2), MathUtil.Hex(0x3a3d44));
            b.Add(ProcMesh.Sphere(0.11f, 6, 5), M(0, 0.2f, 0.15f), MathUtil.Hex(0x4ff0ff));
            b.Add(ProcMesh.Box(0.36f, 0.05f, 0.08f), M(0, 0.05f, 0.75f), MathUtil.Hex(0xff8a2a));
            return MeshModel(parent, "aspiradora", b);
        }

        public override Pose IdlePose() { return new Pose(-0.3f, 0.25f); }
        public override Pose PoseFor(AttackPhase phase, float t) { return Sweep(phase, t, new Pose(-0.8f, -0.2f, 0, -0.2f), new Pose(0.5f, 0.3f, 0, 0.3f)); }
        public Pose SuckPose(float t) { return new Pose(Mathf.Sin(t * 30f) * 0.03f, 0.05f, 0f, 0.25f); }
        public override Pose ChargePose(float k) { return SuckPose(k); }
        public override Pose SpecialPose(bool windup, float t)
        {
            var aim = new Pose(0f, 0.02f, 0f, 0.3f); var kick = new Pose(0.05f, -0.4f, 0f, -0.15f);
            if (windup) return Pose.Mix(IdlePose(), aim, MathUtil.EaseOutCubic(t));
            return Pose.Mix(kick, IdlePose(), MathUtil.EaseInOut(t));
        }

        protected override string StyleText(Player p)
        {
            return "Energía " + Mathf.RoundToInt(Energy) + " · dentro " + Stored.Count + "/" + Capacity + " · mantén " + p.Controls.S.AttackKey + " succiona, toca dispara";
        }

        public override void Tick(Player p, Game g, float dt)
        {
            SpecialCd = Mathf.Max(0f, SpecialCd - dt);
            heavyMsgT = Mathf.Max(0f, heavyMsgT - dt);
            // Lo almacenado puede desaparecer (piso nuevo, destruido por otro sistema): se olvida sin errores
            Stored.RemoveAll(e => e == null || e.Removed || e.Dead || !e.Root);
            if (Sucking) sinceUse = 0f; else sinceUse += dt;
            if (!Sucking && sinceUse >= RegenDelay) Energy = Mathf.Min(MaxEnergy, Energy + Regen * dt);
            RefreshStyle(p, g);
        }

        public bool CanSuck { get { return Energy > 0f && Stored.Count < Capacity; } }

        // Un frame de succión: atrae a los ligeros, poco a los medianos y nada a los pesados y jefes
        public void Suck(Player p, Game g, float dt)
        {
            if (!CanSuck)
            {
                if (heavyMsgT <= 0f) { heavyMsgT = 1.5f; g.Ui.FloatText(p.X, 2.6f, p.Z, Energy <= 0f ? "Sin energía" : "¡Depósito lleno!", TextStyle.Small); }
                return;
            }
            Energy = Mathf.Max(0f, Energy - Drain * dt);
            suckFx -= dt;
            if (suckFx <= 0f)
            {
                suckFx = 0.08f;
                float r = SuckRange * (0.5f + g.FxRand.Next() * 0.5f), a = p.Facing + (g.FxRand.Next() - 0.5f) * SuckArc * Mathf.Deg2Rad;
                float sx = p.X + Mathf.Sin(a) * r, sz = p.Z + Mathf.Cos(a) * r;
                g.Effects.Burst(sx, 0.9f, sz, BurstOpts.Of(2, 0x8ff8ff, 0f, 0f, 0.07f).Dir(p.X - sx, p.Z - sz, 0.1f).WithLife(0.3f));
            }
            foreach (var e in g.Enemies.ToArray())
            {
                if (e.Dead || e.Removed || e.State == EnemyState.Falling || !e.Body.Enabled || e.Peaceful) continue;
                float dx = e.X - p.X, dz = e.Z - p.Z, d = MathUtil.Hypot(dx, dz);
                if (d > SuckRange + e.Radius) continue;
                if (Mathf.Abs(MathUtil.AngleDiff(p.Facing, Mathf.Atan2(dx, dz))) > SuckArc * 0.5f * Mathf.Deg2Rad) continue;
                if (!g.Dungeon.LineClear(p.X, p.Z, e.X, e.Z)) continue;
                if (ElementalRules.IsHeavy(e))
                {
                    if (heavyMsgT <= 0f) { heavyMsgT = 1.5f; g.Ui.FloatText(e.X, 2.6f, e.Z, "¡DEMASIADO PESADO!", TextStyle.Small); }
                    continue;
                }
                bool medium = ElementalRules.IsMedium(e);
                e.Stun = Mathf.Max(e.Stun, 0.2f);
                if (d < CaptureDist + e.Radius && Stored.Count < Capacity) { Capture(p, g, e); continue; }
                float sp = (medium ? SuckSpeedMedium : SuckSpeedLight) * dt;
                g.Elemental.MoveSafely(e, -dx / Mathf.Max(d, 0.01f) * sp, -dz / Mathf.Max(d, 0.01f) * sp);
            }
        }

        void Capture(Player p, Game g, Enemy e)
        {
            g.Elemental.Store(e);
            Stored.Add(e);
            g.Audio.Play("pickup");
            g.Effects.Burst(e.X, 0.9f, e.Z, BurstOpts.Of(10, 0x8ff8ff, 3f, 2f, 0.1f));
            g.Ui.FloatText(p.X, 2.6f, p.Z, "¡SLURP! " + Stored.Count + "/" + Capacity, TextStyle.Label);
        }

        public override void SpecialStart(Player p, Game g) { g.Audio.Play("swing", 0.7f); }

        // Descarga: lo almacenado sale disparado; sin nada dentro, un chorro de energía si queda
        public override void Special(Player p, Game g)
        {
            float fx = Mathf.Sin(p.Facing), fz = Mathf.Cos(p.Facing);
            if (Stored.Count > 0)
            {
                for (int i = 0; i < Stored.Count; i++)
                {
                    float spread = (i - (Stored.Count - 1) / 2f) * 0.18f;
                    float a = p.Facing + spread;
                    g.Elemental.Launch(Stored[i], p, Mathf.Sin(a), Mathf.Cos(a), LaunchSpeed, LaunchDamage, i * 0.06f);
                }
                Stored.Clear();
                g.Audio.Play("shotgun");
                g.CameraCtl.AddShake(0.3f);
                g.Ui.FloatText(p.X, 2.8f, p.Z, "¡DESCARGA DE BASURA!", TextStyle.Label);
                return;
            }
            if (Energy < BlastCost) { g.Ui.FloatText(p.X, 2.6f, p.Z, "Sin energía", TextStyle.Small); return; }
            Energy -= BlastCost;
            g.Audio.Play("shotgun", 0.6f);
            ShotgunFX.Blast(g, p.X + fx * 0.9f, p.Z + fz * 0.9f, p.Facing, 0.5f, BlastRange, 0x4ff0ff);
            foreach (var e in EnemiesNear(g, p.X, p.Z, BlastRange))
            {
                if (Mathf.Abs(MathUtil.AngleDiff(p.Facing, Mathf.Atan2(e.X - p.X, e.Z - p.Z))) > 0.5f) continue;
                AreaHit(g, p, e, BlastDamage, 14f, 0.3f);
            }
        }

        // Al soltarla o cambiarla, lo que lleve dentro sale despacio a los pies del jugador
        public override void OnUnequip(Player p, Game g)
        {
            Sucking = false;
            for (int i = 0; i < Stored.Count; i++) g.Elemental.Launch(Stored[i], p, Mathf.Sin(p.Facing), Mathf.Cos(p.Facing), 4f, 0f, 0f);
            Stored.Clear();
        }
    }
}
