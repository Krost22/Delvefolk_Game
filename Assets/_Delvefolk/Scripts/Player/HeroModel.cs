using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Monta un personaje personalizado (modelo con esqueleto) con las mismas partes que espera el juego
    // (CharacterParts): el socket del arma va como hijo del hueso de la mano de ESE esqueleto.
    public static class HeroModel
    {
        static readonly Dictionary<GameObject, float> idleTimes = new Dictionary<GameObject, float>();

        public static CharacterParts Build(Transform model, HeroEntry e)
        {
            var holder = new GameObject("Personaje").transform;
            holder.SetParent(model, false);
            holder.localRotation = Quaternion.Euler(0f, e.Yaw, 0f);
            var go = Art.Spawn(e.Prefab, holder);
            if (!go) { Object.Destroy(holder.gameObject); return null; }
            var bones = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            Transform upper = Bone(bones, e.UpperArmBone, e), fore = Bone(bones, e.ForeArmBone, e), hand = Bone(bones, e.HandBone, e);

            // Pose de reposo: su Idle o, si no tiene, el fotograma más neutro del correr
            float idle = e.Idle ? 0f : IdleTime(e, go, bones);
            if (e.Idle) e.Idle.SampleAnimation(go, 0f); else if (e.Run) e.Run.SampleAnimation(go, idle);
            // Escala: una sola, la que lleva su altura real (malla en esa pose) a la altura común del catálogo,
            // y pies a ras de suelo. Los FBX vienen en metros reales y cada personaje mide distinto.
            var span = Span(e, go, holder);
            float s = HeroCatalog.Height / Mathf.Max(0.05f, span.y - span.x) * Mathf.Max(0.01f, e.Scale);
            go.transform.localScale *= s;
            holder.localPosition = new Vector3(0f, -span.x * s, 0f);

            var parts = new CharacterParts();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                // Los FBX de Meshy traen una esfera suelta ("Icosphere") que no es del personaje: se oculta
                if (!(r is SkinnedMeshRenderer)) { r.gameObject.SetActive(false); continue; }
                parts.Renderers.Add(r);
                var smr = r as SkinnedMeshRenderer;
                if (smr) smr.updateWhenOffscreen = true;   // los huesos se mueven fuera de su caja original
            }
            // Las piezas que el juego anima en el clásico existen, vacías (no se ven)
            parts.FootL = Dummy(model, "PieI"); parts.FootR = Dummy(model, "PieD"); parts.LHand = Dummy(model, "ManoI");
            var arm = new GameObject("Brazo").transform;
            arm.SetParent(model, false);
            arm.localPosition = upper ? model.InverseTransformPoint(upper.position) : new Vector3(-0.18f, 1.0f, 0.05f);
            parts.ArmPivot = arm;
            if (upper && fore && hand)
            {
                var socket = new GameObject("SocketArma").transform;
                socket.SetParent(hand, false);
                parts.Holder = socket;
                parts.Rig = go.AddComponent<HeroRig>();
                parts.Rig.Init(e, arm, model, go.GetComponentInChildren<Animator>(), upper, fore, hand, socket, idle);
                parts.Rig.Tick(0f);
            }
            else
            {
                // Sin los huesos de la mano: el arma va en el pivote como en el clásico (y se avisa arriba)
                var h = new GameObject("Mano").transform;
                h.SetParent(arm, false);
                h.localPosition = new Vector3(0f, 0f, 0.45f);
                parts.Holder = h;
            }
            return parts;
        }

        static readonly Dictionary<GameObject, Vector2> spans = new Dictionary<GameObject, Vector2>();

        // Altura mínima y máxima de la malla (en el espacio del soporte, a escala 1), horneada en la pose de reposo
        static Vector2 Span(HeroEntry e, GameObject go, Transform holder)
        {
            Vector2 r;
            if (spans.TryGetValue(e.Prefab, out r)) return r;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            var baked = new Mesh();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // BakeMesh(false) ya devuelve la malla a tamaño real (la escala ×100 de importación la compensan los
                // huesos): solo hay que girarla y moverla, sin volver a escalar (medido: 1,67 m el Viajero Azur)
                smr.BakeMesh(baked, false);
                var m = holder.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in baked.vertices) { float y = m.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            }
            if (Application.isPlaying) Object.Destroy(baked); else Object.DestroyImmediate(baked);
            r = float.IsInfinity(lo) ? new Vector2(0f, 1.8f) : new Vector2(lo, hi);
            spans[e.Prefab] = r;
            return r;
        }

        static Transform Bone(Dictionary<string, Transform> bones, string name, HeroEntry e)
        {
            Transform t;
            if (!string.IsNullOrEmpty(name) && bones.TryGetValue(name, out t)) return t;
            Debug.LogError("[Personajes] " + e.Name + " (" + e.Id + "): no existe el hueso '" + name + "'. Revisa Resources/Heroes.");
            return null;
        }

        static Transform Dummy(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // Fotograma de la animación de correr con los pies más juntos y a la misma altura (una vez por modelo)
        static float IdleTime(HeroEntry e, GameObject go, Dictionary<string, Transform> bones)
        {
            if (!e.Run) return 0f;
            float t;
            if (idleTimes.TryGetValue(e.Prefab, out t)) return t;
            Transform lf = null, rf = null;
            foreach (var kv in bones)
            {
                if (kv.Key.EndsWith("LeftFoot")) lf = kv.Value;
                else if (kv.Key.EndsWith("RightFoot")) rf = kv.Value;
            }
            t = 0f;
            if (lf && rf)
            {
                float best = float.PositiveInfinity;
                const int N = 24;
                for (int i = 0; i < N; i++)
                {
                    float k = e.Run.length * i / N;
                    e.Run.SampleAnimation(go, k);
                    var d = lf.position - rf.position;
                    float lift = Mathf.Abs(d.y);  // los dos pies a la misma altura (en el suelo)
                    d.y = 0f;
                    float score = d.magnitude + lift * 3f;
                    if (score < best) { best = score; t = k; }
                }
            }
            idleTimes[e.Prefab] = t;
            return t;
        }
    }
}
