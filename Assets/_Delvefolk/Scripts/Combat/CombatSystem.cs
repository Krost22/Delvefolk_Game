using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Golpe que recibe un objetivo (del jugador, de un pisotón, de un compañero lanzado...)
    public sealed class Hit
    {
        public float Damage, Knock, Stun, DirX, DirZ, Hitstop, Shake;
        public string Text, Sound;
        public Player By;
        public bool Stomp, Reflected, Blocked;
        public string BlockText;   // rótulo cuando un jefe bloquea el golpe
        public Hit Clone() { return (Hit)MemberwiseClone(); }

        public static Hit Of(float damage, float knock, float stun, float dirX, float dirZ, Player by = null)
        {
            return new Hit { Damage = damage, Knock = knock, Stun = stun, DirX = dirX, DirZ = dirZ, By = by };
        }
    }

    // Algo que el ataque del jugador puede golpear: enemigos, barriles, muebles, proyectiles devolvibles
    public interface IHittable
    {
        float X { get; }
        float Z { get; }
        float Radius { get; }
        void TakeHit(Hit hit);
    }

    // Detección de golpes, aplicación de daño/empuje, feedback y explosiones (CombatSystem.js)
    public sealed class CombatSystem
    {
        readonly Game g;
        readonly List<IHittable> targets = new List<IHittable>();

        public CombatSystem(Game game) { g = game; }

        public List<IHittable> Targets()
        {
            targets.Clear();
            foreach (var e in g.Enemies) if (!e.Dead && e.State != EnemyState.Falling && !e.Removed) targets.Add(e);
            foreach (var b in g.Barrels) if (!b.Dead && !b.Falling && !b.Held) targets.Add(b);
            foreach (var b in g.Breakables) if (!b.Dead) targets.Add(b);
            g.AddReflectableTargets(targets);
            return targets;
        }

        // Asistencia de puntería: el enemigo más prometedor en un cono delante del jugador
        public Enemy FindAimTarget(Player player, float attackRange, float baseAngle)
        {
            Enemy best = null;
            float bestScore = float.PositiveInfinity;
            foreach (var e in g.Enemies)
            {
                if (e.Dead || e.State == EnemyState.Falling || e.Peaceful) continue;
                float dx = e.X - player.X, dz = e.Z - player.Z;
                float d = MathUtil.Hypot(dx, dz);
                if (d > attackRange + e.Radius + 1.0f) continue;
                float ang = Mathf.Abs(MathUtil.AngleDiff(baseAngle, Mathf.Atan2(dx, dz)));
                if (ang > 0.85f) continue;
                if (!g.Dungeon.LineClear(player.X, player.Z, e.X, e.Z)) continue;
                float score = d + ang * 2.5f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        // Se llama cada frame durante la ventana activa. Cada objetivo solo puede recibir un golpe por ataque.
        public void PlayerAttack(Player player, Weapon w, AttackState a)
        {
            float fx = Mathf.Sin(player.Facing), fz = Mathf.Cos(player.Facing);
            var list = Targets();
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (a.HitSet.Contains(t)) continue;
                float dx = t.X - player.X, dz = t.Z - player.Z;
                float dist = MathUtil.Hypot(dx, dz);
                if (dist > w.AttackRange + t.Radius) continue;
                float ang = Mathf.Abs(MathUtil.AngleDiff(player.Facing, Mathf.Atan2(dx, dz)));
                float tol = w.ArcRad / 2f + Mathf.Atan2(t.Radius, Mathf.Max(dist, 0.3f));
                if (dist < t.Radius + player.Radius + 0.3f) tol = Mathf.Max(tol, 1.6f);
                if (ang > tol) continue;
                // Las armas no atraviesan muros
                if (!g.Dungeon.LineClear(player.X, player.Z, t.X, t.Z)) continue;
                a.HitSet.Add(t);
                ApplyPlayerHit(player, w, a, t, fx, fz, dx, dz);
                if (player.Weapon != w) return;   // el arma cambió (se rompió) en pleno ataque
            }
        }

        void ApplyPlayerHit(Player player, Weapon w, AttackState a, IHittable t, float fx, float fz, float dx, float dz)
        {
            var dir = w.KnockDirection(fx, fz, dx, dz);
            // Equipo del jugador (mejoras e ídolo / guantes): multiplica daño y empuje
            var hit = new Hit
            {
                Damage = w.Damage * player.Gear.DamageMult(), Knock = w.KnockbackForce * player.Gear.KnockDealtMult(), Stun = w.Stun,
                DirX = dir.x, DirZ = dir.y, Hitstop = w.Hitstop, Shake = w.Shake,
                Text = w.HitText, Sound = w.HitSound, By = player,
            };
            if (!w.Broken) w.ModifyHit(hit, new HitCtx { Time = g.Time, FirstHitOfAttack = a.Hits == 0, Dist = MathUtil.Hypot(dx, dz), Game = g });
            a.Hits++;
            t.TakeHit(hit);
            // Feedback
            float cx = t.X - dir.x * t.Radius * 0.8f, cz = t.Z - dir.y * t.Radius * 0.8f;
            g.Effects.Burst(cx, 0.9f, cz, BurstOpts.Of(9, 0xfff4c0, 7f, 3f, 0.09f).Dir(dir.x, dir.y, 1.6f).WithLife(0.3f));
            g.Effects.Ring(cx, cz, new RingOpts { Color = 0xffffff, Radius = 1.0f, Life = 0.15f, Y = 0.9f, R0 = 0.3f, Opacity = 0.8f });
            if (!w.Broken) w.OnHit(t, hit, g);
            g.Audio.Play(hit.Sound);
            g.RequestHitstop(hit.Hitstop);
            g.CameraCtl.AddShake(hit.Shake);
            if (hit.Text != null && a.Hits == 1) g.Ui.FloatText(t.X, 2.3f, t.Z, hit.Text, TextStyle.Label);
            // Durabilidad: un uso por ataque que acierta
            if (a.Hits == 1 && w.Wear()) WeaponBroke(player, w);
            else if (a.Hits == 1 && w.HasDurability && !w.Broken) g.Ui.SetWeapon(player.Index, w);
        }

        void WeaponBroke(Player player, Weapon w)
        {
            g.Audio.Play("break");
            g.CameraCtl.AddShake(0.25f);
            g.Effects.Burst(player.X, 1.1f, player.Z, BurstOpts.Of(16, 0x9a7a55, 5f, 5f, 0.14f).WithColorVar(0.1f));
            g.Ui.FloatText(player.X, 2.9f, player.Z, "¡CRAC! Se ha roto", TextStyle.Big);
            player.RefreshWeaponModel();
        }

        static Mesh scorchMesh;

        public void Explosion(float x, float z)
        {
            const float R = 3.9f;
            g.Audio.Play("explosion");
            g.CameraCtl.AddShake(0.85f);
            g.RequestHitstop(0.1f);
            g.Effects.Flash(x, 2f, z, 0xff8a30, 7f, 0.35f);
            g.Effects.Ring(x, z, new RingOpts { Color = 0xffb040, Radius = R, Life = 0.4f, Y = 0.06f, R0 = 0.3f, Opacity = 0.8f });
            g.Effects.Burst(x, 0.8f, z, BurstOpts.Of(40, 0xff8a20, 10f, 6f, 0.28f).WithColorVar(0.15f).WithLife(0.6f));
            g.Effects.Burst(x, 0.8f, z, BurstOpts.Of(20, 0x444444, 5f, 7f, 0.4f).WithLife(1.0f).WithGravity(4f));
            g.Ui.FloatText(x, 2.8f, z, "¡BUUM!", TextStyle.Big);
            // Mancha de hollín
            if (!scorchMesh) scorchMesh = new MeshBuilder().Add(ProcMesh.Circle(1.8f, 12).RotateX(-Mathf.PI / 2f), Vector3.zero, new Color(0, 0, 0, 0.45f)).ToMesh("hollin");
            if (g.FloorRoot) CharacterModel.MeshObject(g.FloorRoot, "Hollin", scorchMesh, Mats.Transparent, new Vector3(x, 0.015f, z));
            for (int i = 0; i < g.Enemies.Count; i++)
            {
                var e = g.Enemies[i];
                if (e.Dead || e.State == EnemyState.Falling) continue;
                float dx = e.X - x, dz = e.Z - z, d = MathUtil.Hypot(dx, dz);
                if (d > R + e.Radius) continue;
                float f = 1f - Mathf.Min(1f, d / (R + e.Radius)) * 0.5f;
                float l = d == 0f ? 1f : d;
                e.Wake(false);
                e.Knock(dx / l * 24f * f / e.Body.Mass, dz / l * 24f * f / e.Body.Mass);
                e.Stun = Mathf.Max(e.Stun, 0.9f);
                e.EnvDamage(45f * f, "¡BUM!");
            }
            for (int i = 0; i < g.Barrels.Count; i++)
            {
                var b = g.Barrels[i];
                if (b.Dead) continue;
                float dx = b.X - x, dz = b.Z - z, d = MathUtil.Hypot(dx, dz);
                if (d > R + 0.5f) continue;
                float l = d == 0f ? 1f : d;
                b.Knock(dx / l * 10f, dz / l * 10f);
                if (b.Explosive) { if (b.Fuse < 0f || b.Fuse > 0.18f) b.Fuse = 0.18f; }
                else b.EnvHit(40f);
            }
            for (int i = 0; i < g.Breakables.Count; i++)
            {
                var b = g.Breakables[i];
                // Una explosión destroza cualquier mueble
                if (!b.Dead && MathUtil.Hypot(b.X - x, b.Z - z) < R + b.Radius) b.EnvHit(99f);
            }
            for (int i = 0; i < g.Players.Count; i++)
            {
                var p = g.Players[i];
                float pd = MathUtil.Hypot(p.X - x, p.Z - z);
                if (p.Targetable && pd < R)
                {
                    if (p.Hurt(Mathf.Round(26f * (1f - pd / R * 0.5f)), x, z, 16f))
                        g.Ui.FloatText(p.X, 2.6f, p.Z, "¡Me he chamuscado!", TextStyle.Small);
                }
            }
        }
    }

    // Contexto de ModifyHit / OnHit de un arma
    public struct HitCtx
    {
        public float Time, Dist;
        public bool FirstHitOfAttack;
        public Game Game;
    }
}
