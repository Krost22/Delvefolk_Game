using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Delvefolk
{
    // Cuerpo visual del Crupier con el modelo de Resources/CrupierRoulette (CrupierModel + clips). SOLO visual: cuelga
    // del "Modelo" del jefe (la raíz, el cuerpo de colisión, la IA, los ataques y sus puntos de salida no se tocan).
    //  - reposo (CrupierIdle) quieto; andar/correr según la velocidad; transiciones suaves (mezclador de Playables)
    //  - durante un patrón de ataque sigue en su animación de base (no hay clips de ataque): el reposo nunca
    //    interrumpe nada, los avisos del ataque (suelo, ruleta) son los de siempre
    // Nunca mueve la raíz ni decide nada del combate: lee la velocidad y el patrón, no los cambia.
    public sealed class CrupierBody : MonoBehaviour
    {
        public float Speed;          // velocidad actual (u/s), la escribe el jefe
        public float SpeedRef = 2.3f;
        public bool Acting;          // hay un patrón de ataque en curso (solo informativo: no cambia la animación)

        Animator anim;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable idleP, walkP, runP;
        float idleT, walkT, runT, wIdle = 1f, wWalk, wRun;
        RouletteConfig cfg;

        public float IdleWeight { get { return wIdle; } }
        public float MoveWeight { get { return wWalk + wRun; } }

        // Monta el modelo bajo model. null si no hay modelo con reposo (se usa el camino antiguo)
        public static CrupierBody Build(Transform model, RouletteConfig cfg)
        {
            if (cfg == null || !Art.Has(cfg.CrupierModel) || !cfg.CrupierIdle) return null;
            var holder = new GameObject("CuerpoCrupier").transform;
            holder.SetParent(model, false);
            holder.localRotation = Quaternion.Euler(0f, cfg.CrupierYaw, 0f);
            var go = Art.Spawn(cfg.CrupierModel, holder);
            if (!go) { Object.Destroy(holder.gameObject); return null; }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!(r is SkinnedMeshRenderer)) { r.gameObject.SetActive(false); continue; }   // restos sueltos del FBX
                ((SkinnedMeshRenderer)r).updateWhenOffscreen = true;
            }
            // Pose de reposo, altura CrupierHeight y pies a ras de suelo
            cfg.CrupierIdle.SampleAnimation(go, 0f);
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            var baked = new Mesh();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.BakeMesh(baked, false);
                var m = holder.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in baked.vertices) { float y = m.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            }
            if (Application.isPlaying) Object.Destroy(baked); else Object.DestroyImmediate(baked);
            if (float.IsInfinity(lo)) { lo = 0f; hi = 1.7f; }
            float s = cfg.CrupierHeight / Mathf.Max(0.05f, hi - lo);
            go.transform.localScale *= s;
            holder.localPosition = new Vector3(0f, -lo * s, 0f);
            var body = go.AddComponent<CrupierBody>();
            body.Init(go, model, cfg);
            return body;
        }

        void Init(GameObject go, Transform model, RouletteConfig c)
        {
            cfg = c;
            anim = go.GetComponentInChildren<Animator>();
            if (!anim) anim = go.AddComponent<Animator>();
            anim.applyRootMotion = false;   // la posición la lleva el jefe, nunca la animación
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Crupier");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            idleP = Clip(c.CrupierIdle, 0);
            walkP = Clip(c.CrupierWalk ? c.CrupierWalk : c.CrupierIdle, 1);
            runP = Clip(c.CrupierRun ? c.CrupierRun : (c.CrupierWalk ? c.CrupierWalk : c.CrupierIdle), 2);
            var output = AnimationPlayableOutput.Create(graph, "Salida", anim);
            output.SetSourcePlayable(mixer);
            graph.Play();
            Tick(0f);
        }

        AnimationClipPlayable Clip(AnimationClip clip, int slot)
        {
            var p = AnimationClipPlayable.Create(graph, clip);
            p.SetApplyFootIK(false);
            graph.Connect(p, 0, mixer, slot);
            return p;
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        void LateUpdate() { Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (!graph.IsValid() || !anim || !anim.isActiveAndEnabled) return;
            // Pesos objetivo: quieto → reposo; andando → andar; rápido (fase 2, empujones) → correr
            float k = Speed / Mathf.Max(0.1f, SpeedRef);
            float tIdle = k < 0.15f ? 1f : 0f;
            float tRun = k > 1.15f ? Mathf.Clamp01((k - 1.15f) / 0.5f) : 0f;
            float tWalk = 1f - tIdle - tRun;
            float rate = dt * 6f;   // ≈0,17 s de transición
            wIdle = Mathf.MoveTowards(wIdle, tIdle, rate);
            wRun = Mathf.MoveTowards(wRun, tRun, rate);
            wWalk = Mathf.Max(0f, 1f - wIdle - wRun);
            idleT += dt;
            float mv = Mathf.Clamp(k, 0.6f, 1.8f);
            walkT += dt * mv; runT += dt * mv;
            idleP.SetTime(idleT % Mathf.Max(0.01f, cfg.CrupierIdle.length));
            walkP.SetTime(walkT % Mathf.Max(0.01f, walkP.GetAnimationClip().length));
            runP.SetTime(runT % Mathf.Max(0.01f, runP.GetAnimationClip().length));
            mixer.SetInputWeight(0, wIdle);
            mixer.SetInputWeight(1, wWalk);
            mixer.SetInputWeight(2, wRun);
            graph.Evaluate(0f);
        }
    }
}
