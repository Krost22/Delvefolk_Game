using UnityEngine;

namespace Delvefolk
{
    // Coger y lanzar la TNT (barril de pólvora): en la mano arde una mecha de 3 s. Ataque = lanzar lejos
    // (hacia el enemigo más cercano por delante, o hacia donde se mire: también contra un compañero).
    public sealed partial class Player
    {
        public Barrel CarryingTnt;

        public void GrabTnt(Barrel b)
        {
            if (Carrying != null || CarryingTnt != null || b == null || !b.CanBeGrabbed) return;
            EndJump();
            G.DropHeld(this);   // una sola cosa en las manos: la TNT
            CancelAttack(); CancelEating();
            CarryingTnt = b;
            b.PickUp(this);
            Cooldown = 0.25f;
            Buffer = 0f;
            G.Audio.Play("grab");
            G.Ui.FloatText(X, 2.9f, Z, "¡Mecha encendida!", TextStyle.Small);
        }

        public void ThrowTnt(float? inputAngle)
        {
            var b = CarryingTnt;
            if (b == null) return;
            if (inputAngle.HasValue) Facing = inputAngle.Value;
            // Puntería asistida hacia el enemigo más cercano dentro del cono (más alcance que con el compañero)
            var t = G.Combat.FindAimTarget(this, 11f, Facing);
            if (t != null) Facing = Mathf.Atan2(t.X - X, t.Z - Z);
            b.Throw(this, Facing, Barrel.ThrowSpeed);
            Squash = 1f;
            Cooldown = 0.35f;
            G.Audio.Play("throw");
            G.Effects.SwingArc(X, Z, Facing, 0.5f, 4f, Look.Ring, 0.25f);
            G.Ui.FloatText(X, 2.6f, Z, "¡Ahí va la TNT!", TextStyle.Small);
        }

        // Suelta la TNT a sus pies (sigue ardiendo)
        public void DropTnt(float place)
        {
            if (CarryingTnt == null) return;
            CarryingTnt.Drop(place);
            CarryingTnt = null;
            G.Audio.Play("drop");
        }
    }
}
