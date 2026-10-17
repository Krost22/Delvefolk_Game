using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Ataque en curso (uno nuevo por ataque: los goblins reconocen cada golpe que ven venir)
    public sealed class AttackState
    {
        public AttackPhase Phase = AttackPhase.Windup;
        public float T;
        public int Hits;
        public readonly HashSet<IHittable> HitSet = new HashSet<IHittable>();
    }

    public sealed class EatState
    {
        public float T, Crumb;
    }

    // Ataque, comida y pisotones del jugador (Player.js)
    public sealed partial class Player
    {
        public AttackState Attack;
        public EatState Eating;
        public bool PendingSlam;     // sartén: onda en el suelo al acabar la fase activa

        partial void IsEatingImpl(ref bool v) { v = Eating != null; }
        partial void IsAttackingImpl(ref bool v) { v = Attack != null; }
        partial void CancelAttackImpl() { Attack = null; }

        partial void OnWeaponChanged()
        {
            Attack = null;
            Eating = null;
        }

        partial void MoveMultiplierImpl(ref float m)
        {
            if (Attack != null && Attack.Phase != AttackPhase.Recovery) m = Weapon.MoveMult;
            if (Eating != null) m = Weapon.EatMoveMult;
            if (Carrying != null || CarryingTnt != null) m = 0.75f;
        }

        partial void AttackAllowsTurningImpl(ref bool v) { v = Attack == null || Attack.Phase == AttackPhase.Recovery; }

        // ---- Acción principal: atacar / comer / lanzar ----
        partial void ActionButton(float dt, float? inputAngle)
        {
            var c = Controls;
            if (Carrying != null)
            {
                if (Buffer > 0f) { Buffer = 0f; ThrowAlly(inputAngle); }
            }
            else if (CarryingTnt != null)
            {
                if (Buffer > 0f) { Buffer = 0f; ThrowTnt(inputAngle); }
            }
            else if (Spectator) Buffer = 0f;   // fuera del desafío: puede moverse, pero no actuar
            else if (Weapon.IsFood)
            {
                if (Buffer > 0f && Eating == null && Cooldown <= 0f) { Buffer = 0f; StartEating(); }
            }
            else if ((Buffer > 0f || c.AttackDown()) && Cooldown <= 0f && Attack == null) StartAttack(inputAngle);
            if (Attack != null) UpdateAttack(dt);
            if (Eating != null) UpdateEating(dt);
        }

        partial void ThrowAllyImpl(float? inputAngle);
        void ThrowAlly(float? inputAngle) { ThrowAllyImpl(inputAngle); }

        // ---------- Ataque ----------
        public void StartAttack(float? inputAngle)
        {
            var w = Weapon;
            // Asistencia de puntería: girar hacia el enemigo más cercano dentro de un cono
            float baseAngle = inputAngle ?? Facing;
            var t = G.Combat.FindAimTarget(this, w.AttackRange, baseAngle);
            Facing = t != null ? Mathf.Atan2(t.X - X, t.Z - Z) : baseAngle;
            Attack = new AttackState();
            Cooldown = w.AttackCooldown;
            Buffer = 0f;
            if (!w.Broken) w.OnAttackStart(this, G);
        }

        void UpdateAttack(float dt)
        {
            var w = Weapon;
            var a = Attack;
            a.T += dt;
            if (a.Phase == AttackPhase.Windup && a.T >= w.Windup)
            {
                a.Phase = AttackPhase.Active; a.T -= w.Windup;
                float fx = Mathf.Sin(Facing), fz = Mathf.Cos(Facing);
                Knock(fx * w.Lunge, fz * w.Lunge);
                if (!w.Broken) w.OnActive(this, G);
                G.Audio.Play("swing", w.Damage / 20f);
                G.Effects.SwingArc(X, Z, Facing, w.ArcRad, w.AttackRange, w.Color);
                Squash = 1f;
            }
            if (a.Phase == AttackPhase.Active)
            {
                G.Combat.PlayerAttack(this, w, a);
                if (Attack == null || Weapon != w) return;   // el arma se rompió y se canceló el ataque
                if (a.T >= w.AttackDuration)
                {
                    a.Phase = AttackPhase.Recovery; a.T -= w.AttackDuration;
                    if (PendingSlam)
                    {
                        PendingSlam = false;
                        float sx = X + Mathf.Sin(Facing) * 1.8f, sz = Z + Mathf.Cos(Facing) * 1.8f;
                        G.Effects.Ring(sx, sz, new RingOpts { Color = 0xffe0a0, Radius = 1.8f, Life = 0.3f, Y = 0.06f, R0 = 0.3f, Opacity = 0.8f });
                        G.Effects.Burst(sx, 0.1f, sz, BurstOpts.Of(10, 0x8a7b68, 3f, 3f, 0.14f));
                        G.CameraCtl.AddShake(0.18f);
                        if (a.Hits == 0) G.Audio.Play("thwack");
                    }
                }
            }
            if (a.Phase == AttackPhase.Recovery && a.T >= w.Recovery) Attack = null;
        }

        // ---------- Comer ----------
        void StartEating()
        {
            if (Hp >= MaxHp)
            {
                G.Ui.FloatText(X, 2.4f, Z, "Estoy lleno...", TextStyle.Small);
                Cooldown = 0.6f;
                return;
            }
            Eating = new EatState();
            G.Audio.Play("eat");
        }

        void UpdateEating(float dt)
        {
            var e = Eating;
            var food = Weapon;
            e.T += dt; e.Crumb -= dt;
            if (e.Crumb <= 0f)
            {
                e.Crumb = 0.28f;
                G.Audio.Play("eat");
                float fx = Mathf.Sin(Facing) * 0.4f, fz = Mathf.Cos(Facing) * 0.4f;
                G.Effects.Burst(X + fx, 1.3f, Z + fz, BurstOpts.Of(4, food.Id == "cheese" ? 0xf2c94c : 0xb5652e, 2f, 2f, 0.07f));
            }
            G.Ui.SetPlayerBar(Index, e.T / food.EatTime, BarKind.Eat);
            if (e.T >= food.EatTime)
            {
                Eating = null;
                G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
                Heal(food.Heal);
                if (!food.OnEaten(this, G))
                {
                    G.Audio.Play("heal");
                    G.Ui.FloatText(X, 2.4f, Z, "+" + food.Heal + " ¡Ñam!", TextStyle.Heal);
                    G.Effects.Burst(X, 1.0f, Z, BurstOpts.Of(10, 0x7dff8a, 2f, 5f, 0.1f));
                }
                SetWeapon(WeaponRegistry.Create("fists"));
            }
        }

        partial void CancelEatingImpl()
        {
            if (Eating == null) return;
            Eating = null;
            G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
            G.Ui.FloatText(X, 2.7f, Z, "¡Se me cae el bocado!", TextStyle.Small);
            Cooldown = 0.5f;
        }

        // ---------- Pose del brazo ----------
        partial void ArmPose(ref Pose p, ref bool handled)
        {
            if (Carrying != null || CarryingTnt != null)
            {
                p = new Pose(0, -1.6f, 0, -0.2f);   // brazos arriba sujetando al compañero (o la TNT)
                Parts.LHand.localPosition = new Vector3(0.3f, 1.9f, 0.0f);
                handled = true;
                return;
            }
            Parts.LHand.localPosition = new Vector3(0.42f, 0.8f, 0.1f);
            var w = Weapon;
            if (Eating != null) { p = w.EatPose(Eating.T); handled = true; }
            else if (Attack != null)
            {
                float dur = Attack.Phase == AttackPhase.Windup ? w.Windup : Attack.Phase == AttackPhase.Active ? w.AttackDuration : w.Recovery;
                p = w.PoseFor(Attack.Phase, Mathf.Clamp01(Attack.T / Mathf.Max(1e-4f, dur)));
                handled = true;
            }
        }

        // ---------- Pisotón a enemigos y barriles (uno por frame, cada objetivo como mucho cada 0.35 s) ----------
        partial void CheckStompTargets(ref bool done)
        {
            foreach (var e in G.Enemies)
            {
                if (e.Dead || e.Removed || e.State == EnemyState.Falling || !e.Body.Enabled || StompCd.ContainsKey(e)) continue;
                if (MathUtil.Hypot(e.X - X, e.Z - Z) > e.Radius + 0.35f) continue;
                float top = e.StompTop();
                if (JumpY > top + 0.2f || JumpY < top - 0.8f) continue;
                StompHittable(e, e.X, e.Z, top, e.Cfg.Bouncy);
                done = true;
                return;
            }
            // (los compañeros se comprueban en CheckStomp, entre los enemigos y los barriles)
        }

        partial void CheckStompBarrels(ref bool done)
        {
            foreach (var b in G.Barrels)
            {
                if (b.Dead || b.Falling || b.Held || StompCd.ContainsKey(b)) continue;
                if (MathUtil.Hypot(b.X - X, b.Z - Z) > b.Radius + 0.3f || JumpY > 1.3f || JumpY < 0.4f) continue;
                StompHittable(b, b.X, b.Z, 1.1f, false);
                done = true;
                return;
            }
        }

        // Dirección hacia fuera desde el jugador hasta el objetivo (DF.Abilities.outward)
        public Vector2 Outward(float tx, float tz)
        {
            float dx = tx - X, dz = tz - Z;
            float l = MathUtil.Hypot(dx, dz);
            if (l < 1e-4f) return new Vector2(Mathf.Sin(Facing), Mathf.Cos(Facing));
            return new Vector2(dx / l, dz / l);
        }

        void StompHittable(IHittable t, float tx, float tz, float top, bool bouncy)
        {
            var o = Outward(tx, tz);
            t.TakeHit(new Hit { Damage = Mathf.Round(JumpConfig.StompDamage * Gear.DamageMult()), Knock = 3f, Stun = JumpConfig.StompStun, DirX = o.x, DirZ = o.y, Stomp = true, By = this });
            var e = t as Enemy;
            if (e != null) e.SquashT = 1f;
            AfterStomp(t, tx, tz, top, bouncy);
        }
    }
}
