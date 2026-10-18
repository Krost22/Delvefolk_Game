using UnityEngine;

namespace Delvefolk
{
    // Especial en curso de un arma elemental: preparación (aviso) → efecto → pose de vuelta
    public sealed class SpecialState
    {
        public bool Windup = true;
        public float T;
    }

    // Controles de las armas elementales con el botón de ATACAR de siempre (no se toca ninguna otra acción):
    //  - tocar ATACAR: golpe básico
    //  - mantener ATACAR ElementalWeapon.ChargeTime y soltar: habilidad especial (si no está recargando)
    //  - aspiradora: mantener = succión (básico); tocar = descarga (especial)
    public sealed partial class Player
    {
        public SpecialState Special;
        public float ChargeT = -1f;          // −1: no se está manteniendo
        bool chargeFull;

        public bool Charging { get { return ChargeT >= 0f; } }

        void CancelSpecial()
        {
            Special = null;
            if (ChargeT >= 0f) G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
            ChargeT = -1f;
            chargeFull = false;
            var v = Weapon as QuantumVacuumWeapon;
            if (v != null) v.Sucking = false;
        }

        // Llamado desde ActionButton en lugar del ataque normal cuando el arma es elemental
        void ElementalAction(ElementalWeapon w, float dt, float? inputAngle)
        {
            var c = Controls;
            Buffer = 0f;
            if (Special != null) { UpdateSpecial(w, dt); return; }
            if (Attack != null) return;   // el golpe básico sigue su curso (UpdateAttack)
            bool held = c.AttackDown();
            var vac = w as QuantumVacuumWeapon;
            if (vac != null)
            {
                // Succión mientras se mantiene; si se suelta enseguida, es un toque: descarga
                if (held && Cooldown <= 0f)
                {
                    if (ChargeT < 0f) ChargeT = 0f;
                    ChargeT += dt;
                    if (ChargeT > 0.15f) { vac.Sucking = true; vac.Suck(this, G, dt); }
                }
                else if (ChargeT >= 0f)
                {
                    bool tap = ChargeT <= 0.15f;
                    ChargeT = -1f;
                    vac.Sucking = false;
                    if (tap) TryStartSpecial(w, inputAngle, false);
                }
                return;
            }
            if (held && Cooldown <= 0f)
            {
                if (ChargeT < 0f) ChargeT = 0f;
                ChargeT += dt;
                float k = ChargeT / ElementalWeapon.ChargeTime;
                if (ChargeT > 0.12f) G.Ui.SetPlayerBar(Index, Mathf.Min(1f, k), BarKind.Charge);
                if (k >= 1f && !chargeFull)
                {
                    chargeFull = true;
                    if (w.SpecialReady) { G.Audio.Play("pickup", 1.4f); G.Effects.Burst(X, 1.2f, Z, BurstOpts.Of(6, w.Color, 2f, 2f, 0.08f)); }
                }
                return;
            }
            if (ChargeT < 0f) return;
            // Al soltar: especial si se cargó del todo (y está listo); si no, golpe básico
            bool full = ChargeT >= ElementalWeapon.ChargeTime;
            ChargeT = -1f; chargeFull = false;
            G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
            if (full && TryStartSpecial(w, inputAngle, true)) return;
            StartAttack(inputAngle);
        }

        bool TryStartSpecial(ElementalWeapon w, float? inputAngle, bool fromCharge)
        {
            if (!w.SpecialReady)
            {
                if (fromCharge && !w.Broken) G.Ui.FloatText(X, 2.6f, Z, w.SpecialName + ": " + Mathf.CeilToInt(w.SpecialCd) + " s", TextStyle.Small);
                return false;
            }
            float baseAngle = inputAngle ?? Facing;
            var t = G.Combat.FindAimTarget(this, Mathf.Max(w.AttackRange, 4f), baseAngle);
            Facing = t != null ? Mathf.Atan2(t.X - X, t.Z - Z) : baseAngle;
            Special = new SpecialState();
            w.SpecialStart(this, G);
            return true;
        }

        void UpdateSpecial(ElementalWeapon w, float dt)
        {
            var s = Special;
            s.T += dt;
            if (s.Windup && s.T >= w.SpecialWindup)
            {
                s.Windup = false; s.T = 0f;
                w.SpecialCd = w.SpecialCooldown;
                Squash = 1f;
                w.Special(this, G);
                if (Weapon != w) { Special = null; return; }
                G.Stats.Abilities++;
            }
            else if (!s.Windup && s.T >= w.SpecialActive) Special = null;
        }

        // Pose del brazo mientras se carga o se usa el especial (la llama ArmPose)
        bool ElementalPose(ref Pose p)
        {
            var w = Weapon as ElementalWeapon;
            if (w == null) return false;
            if (Special != null)
            {
                float dur = Special.Windup ? w.SpecialWindup : w.SpecialActive;
                p = w.SpecialPose(Special.Windup, Mathf.Clamp01(Special.T / Mathf.Max(1e-4f, dur)));
                return true;
            }
            if (ChargeT > 0.12f)
            {
                var vac = w as QuantumVacuumWeapon;
                p = vac != null ? vac.SuckPose(G.Time) : w.ChargePose(Mathf.Min(1f, ChargeT / ElementalWeapon.ChargeTime));
                return true;
            }
            return false;
        }

        // Cada frame con el arma en la mano (enfriamiento, energía, lo almacenado)
        void TickElemental(float dt)
        {
            var w = Weapon as ElementalWeapon;
            if (w == null) return;
            w.Tick(this, G, dt);
            if (Special != null && (Dead || StunT > 0f || CarriedBy != null || GrabbedBy != null)) Special = null;
        }
    }
}
