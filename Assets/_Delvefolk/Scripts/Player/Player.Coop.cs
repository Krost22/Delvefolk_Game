using UnityEngine;

namespace Delvefolk
{
    // Agarrar y lanzar al compañero (Player.js)
    public sealed partial class Player
    {
        public void Grab(Player ally)
        {
            if (Carrying != null || CarryingTnt != null || ally.CarriedBy != null || ally.Carrying != null || ally.CarryingTnt != null || ally.Dead || ally.GrabbedBy != null) return;
            ally.EndJump(); ally.Struggle = 0f;
            EndJump();
            G.DropHeld(this);   // una sola cosa en las manos: el compañero
            CancelAttack(); CancelEating();
            ally.CancelAttack(); ally.CancelEating();
            ally.Thrown = null;
            Carrying = ally;
            ally.CarriedBy = this;
            ally.Body.Enabled = false;
            ally.Vx = ally.Vz = 0f;
            ally.Body.Kx = ally.Body.Kz = 0f;
            ally.CarriedT = 0f;
            Cooldown = 0.25f;
            Buffer = 0f;
            G.Audio.Play("grab");
            G.Ui.FloatText(ally.X, 2.8f, ally.Z, "¡Arriba!", TextStyle.Small);
        }

        // Suelta al compañero desde la posición propia (sin chocar con él un instante): nunca dentro de un muro
        public Player Release(float placeDist)
        {
            var ally = Carrying;
            if (ally == null) return null;
            Carrying = null;
            ally.CarriedBy = null;
            ally.Body.Enabled = true;
            ally.Body.X = X; ally.Body.Z = Z;
            ally.Body.Ignore = Body; ally.Body.IgnoreT = 0.3f;
            ally.SetRootY(0f);
            ally.Model.localRotation = Quaternion.identity;
            if (placeDist != 0f) ally.Knock(Mathf.Sin(Facing) * placeDist, Mathf.Cos(Facing) * placeDist);
            return ally;
        }

        partial void ReleaseCarriedImpl(float place) { Release(place); }

        public void PutDown()
        {
            Release(5f);
            G.Audio.Play("drop");
        }

        partial void ThrowAllyImpl(float? inputAngle)
        {
            if (inputAngle.HasValue) Facing = inputAngle.Value;
            // Un poco de puntería asistida hacia el enemigo más cercano
            var t = G.Combat.FindAimTarget(this, 7f, Facing);
            if (t != null) Facing = Mathf.Atan2(t.X - X, t.Z - Z);
            var ally = Release(0f);
            if (ally == null) return;
            float fx = Mathf.Sin(Facing), fz = Mathf.Cos(Facing);
            ally.Body.Kx = fx * 24f; ally.Body.Kz = fz * 24f;
            ally.StartThrown(this);
            ally.Facing = Facing;
            ally.Iframes = 0f;
            Squash = 1f;
            Cooldown = 0.35f;
            G.Audio.Play("throw");
            G.Effects.SwingArc(X, Z, Facing, 0.5f, 4f, Look.Ring, 0.25f);
            G.Ui.FloatText(X, 2.6f, Z, "¡Allá vaaa!", TextStyle.Small);
        }

        partial void UpdateCarried(float dt)
        {
            var c = CarriedBy;
            CarriedT += dt;
            Body.X = c.X; Body.Z = c.Z;
            SyncRoot();
            SetRootY(1.75f);
            Facing = c.Facing;
            Root.rotation = Quaternion.Euler(0, Facing * Mathf.Rad2Deg, 0);
            // Pataleo cómico
            float t = CarriedT;
            Model.localRotation = ProcMesh.EulerXYZ(-1.3f, 0, Mathf.Sin(t * 14f) * 0.25f);
            SetFeet(Mathf.Sin(t * 20f) * 0.2f, -Mathf.Sin(t * 20f) * 0.2f);
            if (!Model.gameObject.activeSelf) Model.gameObject.SetActive(true);
            // Forcejeo: machacando botones se suelta (o se escapa solo a los 6 s)
            if (StruggleStep(dt, StruggleConfig.MateNeed, StruggleConfig.MateDecay) || t > 6f)
            {
                c.Release(4f);
                PopT = 0.45f;
                G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
                G.Audio.Play("breakFree");
                G.Ui.FloatText(X, 2.6f, Z, "¡Suéltame!", TextStyle.Small);
            }
        }
    }
}
