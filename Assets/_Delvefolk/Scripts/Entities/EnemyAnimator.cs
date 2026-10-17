using UnityEngine;

namespace Delvefolk
{
    // Animaciones de un modelo del paquete de monstruos según el estado del enemigo (solo visual).
    // Los controladores del paquete no tienen transiciones: se reproduce cada estado por su nombre.
    public sealed class EnemyAnimator
    {
        readonly Animator anim;
        readonly Renderer[] renderers;
        string current;
        float hurtT;
        bool dead;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public string Idle = "Idle", Move = "Walk Forward In Place", Windup, Attack, HurtState = "Take Damage", DieState = "Die", Hold, Stunned;
        public float MoveSpeedRef = 2.4f;

        public EnemyAnimator(GameObject go)
        {
            anim = go.GetComponentInChildren<Animator>();
            renderers = go.GetComponentsInChildren<Renderer>(true);
            if (anim)
            {
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            Play(Idle, 0f);
        }

        public bool Valid { get { return anim; } }
        public string Current { get { return current; } }

        public void Recolor(Color tint, float amount)
        {
            var lin = tint.linear;
            foreach (var r in renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetVector(Mats.RecolorId, new Vector4(lin.r, lin.g, lin.b, amount));
                r.SetPropertyBlock(mpb);
            }
        }

        void Play(string state, float fade = 0.12f)
        {
            if (!anim || string.IsNullOrEmpty(state) || state == current) return;
            current = state;
            if (fade <= 0f) anim.Play(state, 0, 0f);
            else anim.CrossFadeInFixedTime(state, fade);
        }

        public void Hurt()
        {
            if (!anim || dead) return;
            hurtT = 0.3f;
            current = HurtState;
            anim.CrossFadeInFixedTime(HurtState, 0.05f, 0, 0f);
        }

        public void Die()
        {
            if (!anim || dead) return;
            dead = true;
            anim.speed = 1f;
            current = DieState;
            anim.CrossFadeInFixedTime(DieState, 0.08f);
        }

        public void Update(Enemy e, float dt)
        {
            if (!anim || dead) return;
            hurtT -= dt;
            float speed = MathUtil.Hypot(e.Vx, e.Vz);
            string want;
            switch (e.State)
            {
                case EnemyState.Windup: want = Windup ?? Idle; break;
                case EnemyState.Attack: want = Attack ?? Move; break;
                case EnemyState.Hold: want = Hold ?? Idle; break;
                default:
                    if (e.Stun > 0.3f && Stunned != null) want = Stunned;
                    else want = speed > 0.3f ? Move : Idle;
                    break;
            }
            // El gesto de dolor se respeta un instante salvo que empiece un ataque
            if (hurtT > 0f && e.State != EnemyState.Windup && e.State != EnemyState.Attack) want = HurtState;
            Play(want);
            anim.speed = want == Move ? Mathf.Clamp(speed / Mathf.Max(0.1f, MoveSpeedRef), 0.6f, 1.8f) : 1f;
        }
    }
}
