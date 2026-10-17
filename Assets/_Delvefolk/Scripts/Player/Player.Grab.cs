using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Vuelo de un jugador lanzado (por su compañero o por un Abrazón)
    public sealed class ThrownState
    {
        public Player By;
        public float T;
        public bool WallHit;
        public readonly HashSet<object> HitSet = new HashSet<object>();
    }

    // Agarres de enemigos, forcejeo y vuelo tras un lanzamiento (Player.js)
    public sealed partial class Player
    {
        public ThrownState Thrown;

        partial void IsThrownImpl(ref bool v) { v = Thrown != null; }

        public void StartThrown(Player by)
        {
            Thrown = new ThrownState { By = by };
        }

        // ---------- Forcejeo (soltarse de un compañero o de un enemigo) ----------
        // need: pulsaciones necesarias · decay: pulsaciones que se pierden por segundo. Devuelve true al soltarse.
        public bool StruggleStep(float dt, float need, float decay)
        {
            var c = Controls;
            int n = 0;
            if (c.ConsumeAttack()) n++;
            if (c.ConsumeInteract()) n++;
            if (c.ConsumeJump()) n++;
            float power = Gear.Has("item_gloves") ? StruggleConfig.Gloves : 1f;   // los guantes de fuerza ayudan
            Struggle = Mathf.Max(0f, Struggle - decay * dt) + n * power;
            if (n > 0)
            {
                Squash = 0.7f;
                G.Audio.Play("struggle");
                G.Effects.Burst(X, Root.position.y + 1.2f, Z, BurstOpts.Of(3, 0xffffff, 2f, 2f, 0.07f));
            }
            G.Ui.SetPlayerBar(Index, Mathf.Min(1f, Struggle / need), BarKind.Struggle);
            if (Struggle >= need)
            {
                Struggle = 0f;
                G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
                return true;
            }
            return false;
        }

        // ---------- Agarrado por un enemigo ----------
        public Grabber GrabbedByGrabber { get { return GrabbedBy as Grabber; } }

        public void OnGrabbedBy(IGrabHolder e)
        {
            ReleaseCarriedForGrab();
            CancelAttack(); CancelEating(); Ability.Finish(); EndJump();
            GrabbedBy = e; GrabT = 0f; Struggle = 0f;
            Body.Enabled = false;
            Vx = Vz = 0f; Body.Kx = Body.Kz = 0f; Body.Mvx = Body.Mvz = 0f;
        }

        partial void ReleaseCarriedImpl(float place);
        void ReleaseCarriedForGrab() { if (Carrying != null) ReleaseCarriedImpl(1f); }

        partial void UpdateGrabbed(float dt)
        {
            var e = GrabbedBy as IGrabHolder;
            if (e == null || e.Dead || e.Removed || e.Held != this) { FreeFromGrab(); return; }
            var h = e.HoldPos();
            Body.X = h.x; Body.Z = h.z;
            SyncRoot();
            SetRootY(h.y);
            Facing = e.Facing + Mathf.PI;
            Root.rotation = Quaternion.Euler(0, Facing * Mathf.Rad2Deg, 0);
            GrabT += dt;
            float t = GrabT;
            Model.localRotation = ProcMesh.EulerXYZ(0.15f, 0, Mathf.Sin(t * 24f) * 0.2f);
            SetFeet(Mathf.Sin(t * 26f) * 0.25f, -Mathf.Sin(t * 26f) * 0.25f);
            if (!Model.gameObject.activeSelf) Model.gameObject.SetActive(true);
            if (StruggleStep(dt, e.EscapePresses, e.EscapeDecay)) e.ReleaseHeld("escape");
        }

        // Vuelve a tierra tras un agarre (lo llama el enemigo al soltarle, o él mismo si el enemigo desaparece)
        public void FreeFromGrab()
        {
            GrabbedBy = null;
            Body.Enabled = true;
            SetRootY(0f);
            Model.localRotation = Quaternion.identity;
            Struggle = 0f;
            GrabT = 0f;
            Iframes = Mathf.Max(Iframes, 0.6f);
            G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
        }

        // ---------- Volando tras un lanzamiento ----------
        float thrownSpin;

        partial void UpdateThrown(float dt)
        {
            var th = Thrown;
            th.T += dt;
            Body.Mvx = Body.Mvz = 0f;
            Vx = Vz = 0f;
            float sp = KnockSpeed;
            SetRootY(MathUtil.Damp(Root.position.y, Mathf.Clamp((sp - 5f) * 0.08f, 0f, 1.0f), 14f, dt));
            thrownSpin += dt * sp * 0.9f;
            Model.localRotation = ProcMesh.EulerXYZ(thrownSpin, 0, 0);
            Root.rotation = Quaternion.Euler(0, Facing * Mathf.Rad2Deg, 0);
            if ((th.T > 0.15f && sp < 4f) || th.T > 1.3f)
            {
                Thrown = null;
                thrownSpin = 0f;
                Model.localRotation = Quaternion.identity;
                SetRootY(0f);
                Squash = 1f;
                G.Effects.Burst(X, 0.1f, Z, BurstOpts.Of(8, 0x8a7b68, 3f, 3f, 0.14f));
            }
        }

        // Un jugador lanzado es un proyectil: daña al monstruo y se hace daño él mismo
        partial void OnBodyImpactImpl(Body other, float speed, float nx, float nz)
        {
            if (Thrown == null || speed < 5f) return;
            var o = other.Owner;
            var th = Thrown;
            var e = o as Enemy;
            var b = o as Barrel;
            var br = o as Breakable;
            if (e != null && !e.Dead && !th.HitSet.Contains(e))
            {
                th.HitSet.Add(e);
                float l = KnockSpeed; if (l == 0f) l = 1f;
                float dirX = Body.Kx / l, dirZ = Body.Kz / l;
                e.TakeHit(new Hit { Damage = 22, Knock = 17, Stun = 0.9f, DirX = -nx != 0f ? -nx : dirX, DirZ = -nz != 0f ? -nz : dirZ });
                G.Ui.FloatText(e.X, 2.5f, e.Z, "¡COMPAÑERAZO!", TextStyle.Label);
                G.Audio.Play("hit");
                G.RequestHitstop(0.1f);
                G.CameraCtl.AddShake(0.4f);
                G.Effects.Burst(e.X, 0.9f, e.Z, BurstOpts.Of(14, 0xfff4c0, 7f, 4f, 0.1f));
                Iframes = 0f;
                Hurt(10f, e.X, e.Z, 3f);
            }
            else if (b != null && !th.HitSet.Contains(b))
            {
                th.HitSet.Add(b);
                b.TakeHit(new Hit { Damage = 20, Knock = 12, Stun = 0, DirX = -nx, DirZ = -nz });
            }
            else if (br != null && !th.HitSet.Contains(br))
            {
                th.HitSet.Add(br);
                br.EnvHit(20f);
                OnWallImpactImpl(speed);
            }
            else if (other.IsStatic) OnWallImpactImpl(speed);
        }

        partial void OnWallImpactImpl(float speed)
        {
            if (Thrown == null || speed < 6f || Thrown.WallHit) return;
            Thrown.WallHit = true;
            if (Gear.Has("item_boots"))
            {
                // Botas de rebote: rebota sin hacerse daño
                Body.Kx *= 1.3f; Body.Kz *= 1.3f;
                G.Audio.Play("bonk");
                G.Ui.FloatText(X, 2.6f, Z, "¡BOING!", TextStyle.Label);
                return;
            }
            Iframes = 0f;
            G.Audio.Play("crash");
            G.CameraCtl.AddShake(0.25f);
            if (Hurt(6f, X, Z, 0f)) G.Ui.FloatText(X, 2.6f, Z, "¡PLAF!", TextStyle.Label);
        }

        partial void OnDieImpl()
        {
            if (Carrying != null) ReleaseCarriedImpl(2f);
            Eating = null;
            Thrown = null;
            var gb = GrabbedBy as IGrabHolder;
            if (gb != null && gb.Held == this) gb.ReleaseHeld("dead");
        }
    }
}
