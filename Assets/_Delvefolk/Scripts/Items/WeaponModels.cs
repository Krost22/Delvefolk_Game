using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Agarre de un modelo de arma importado (Meshy u otro): cómo se coloca en la mano.
    // El modelo se normaliza solo (eje largo hacia +Z, mango en el origen, cara plana hacia arriba) y estos
    // valores lo ajustan encima. Todo se puede tocar en el Inspector, también en Play: se ve al momento.
    [System.Serializable]
    public sealed class WeaponModelEntry
    {
        public string Id;
        public GameObject Prefab;
        [Tooltip("Largo del arma en la mano (unidades del juego; las armas de código miden 1,6–2,3)")]
        public float Length = 1.9f;
        [Tooltip("Cuánto sobresale el mango por detrás de la mano")]
        public float HandleBack = 0.12f;
        [Tooltip("El mango está en el otro extremo (si la normalización automática lo pone al revés)")]
        public bool FlipLength;
        [Tooltip("Desplazamiento local del agarre (x derecha, y arriba, z adelante)")]
        public Vector3 GripOffset;
        [Tooltip("Giro local del agarre en grados (z = girar sobre el eje del arma)")]
        public Vector3 GripEuler;
        [Tooltip("Escala extra (1 = el largo de arriba)")]
        public float Scale = 1f;
    }

    public static class WeaponModels
    {
        static WeaponModelCatalog catalog;
        static bool loaded;

        // Orientación automática de cada modelo (se calcula una vez a partir de sus vértices)
        sealed class Fit
        {
            public Quaternion Rot;          // eje largo → +Z, eje más fino → +Y
            public Vector3 MinEnd, MaxEnd;  // centro de cada extremo del eje largo (espacio del padre, ya girado)
            public float Size;              // largo original
            public bool HandleAtMax;        // el extremo más estrecho (el mango) está en +Z
        }

        static readonly Dictionary<GameObject, Fit> fits = new Dictionary<GameObject, Fit>();

        public static WeaponModelCatalog Catalog
        {
            get
            {
                if (!loaded) { loaded = true; catalog = Resources.Load<WeaponModelCatalog>("WeaponModels"); }
                return Art.UseAssets ? catalog : null;
            }
        }

        public static WeaponModelEntry Entry(string id) { var c = Catalog; return c != null ? c.Get(id) : null; }

        public static bool Has(string id) { var e = Entry(id); return e != null && Art.Has(e.Prefab); }

        // Monta el modelo importado del arma bajo parent con el agarre de su entrada. null si no hay modelo.
        // La copia sale de Art.Spawn: sin colisionadores ni Rigidbody, así que nunca se cae ni empuja a nadie.
        public static GameObject Build(string id, Transform parent)
        {
            var e = Entry(id);
            if (e == null || !Art.Has(e.Prefab)) return null;
            // raíz (libre: el objeto del suelo la centra) → agarre (WeaponGrip) → normalizado → modelo
            var root = new GameObject(id);
            root.transform.SetParent(parent, false);
            var grip = new GameObject("Agarre");
            grip.transform.SetParent(root.transform, false);
            var norm = new GameObject("Normalizado").transform;
            norm.SetParent(grip.transform, false);
            var model = Art.Spawn(e.Prefab, norm);
            if (!model) { Object.Destroy(root); return null; }
            grip.AddComponent<WeaponGrip>().Init(e, norm);
            return root;
        }

        internal static void Apply(WeaponModelEntry e, Transform grip, Transform norm)
        {
            var f = FitOf(e.Prefab, norm.childCount > 0 ? norm.GetChild(0) : null);
            grip.localPosition = e.GripOffset;
            grip.localRotation = Quaternion.Euler(e.GripEuler);
            grip.localScale = Vector3.one * Mathf.Max(0.01f, e.Scale);
            if (f == null) return;
            bool handleMax = f.HandleAtMax != e.FlipLength;
            float s = e.Length / Mathf.Max(1e-5f, f.Size);
            // Si el mango queda en +Z, se da la vuelta (180° sobre Y) para que la cabeza apunte adelante
            var flip = handleMax ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            var rot = flip * f.Rot;
            var handle = flip * (handleMax ? f.MaxEnd : f.MinEnd);
            norm.localRotation = rot;
            norm.localScale = Vector3.one * s;
            norm.localPosition = -handle * s + new Vector3(0f, 0f, -e.HandleBack);
        }

        static Fit FitOf(GameObject prefab, Transform instance)
        {
            Fit f;
            if (fits.TryGetValue(prefab, out f)) return f;
            if (!instance) return null;
            // Vértices en el espacio del padre del modelo (con su giro y escala de importación)
            var pts = new List<Vector3>();
            var toParent = Matrix4x4.TRS(instance.localPosition, instance.localRotation, instance.localScale) * instance.worldToLocalMatrix;
            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var m = toParent * mf.transform.localToWorldMatrix;
                var mesh = mf.sharedMesh;
                if (mesh.isReadable)
                {
                    var v = mesh.vertices;
                    for (int i = 0; i < v.Length; i++) pts.Add(m.MultiplyPoint3x4(v[i]));
                }
                else
                {
                    // Sin lectura de vértices (Read/Write desactivado): las 8 esquinas de su caja. El mango no se
                    // puede adivinar; se ajusta con FlipLength
                    var mb = mesh.bounds;
                    for (int i = 0; i < 8; i++)
                        pts.Add(m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                }
            }
            if (pts.Count == 0) return null;
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            // Eje largo → Z; el más fino → Y; el otro → X
            var size = b.size;
            int la = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int a1 = (la + 1) % 3, a2 = (la + 2) % 3;
            int thin = size[a1] <= size[a2] ? a1 : a2;
            var fwd = Axis(la);
            var up = Axis(thin);
            var rot = Quaternion.Inverse(Quaternion.LookRotation(fwd, up));
            // ¿Qué extremo es el mango? El más estrecho: anchura máxima de cada rodaja del 12 % de cada punta
            float lo = b.min[la], len = size[la];
            Vector3 cMin = Vector3.zero, cMax = Vector3.zero; int nMin = 0, nMax = 0;
            float wMin = 0f, wMax = 0f;
            var center = b.center;
            foreach (var p in pts)
            {
                float t = (p[la] - lo) / Mathf.Max(1e-6f, len);
                var d = p - center; d[la] = 0f;
                if (t < 0.12f) { wMin = Mathf.Max(wMin, d.magnitude); cMin += p; nMin++; }
                else if (t > 0.88f) { wMax = Mathf.Max(wMax, d.magnitude); cMax += p; nMax++; }
            }
            cMin = nMin > 0 ? cMin / nMin : center; cMax = nMax > 0 ? cMax / nMax : center;
            cMin[la] = b.min[la]; cMax[la] = b.max[la];
            f = new Fit
            {
                Rot = rot,
                MinEnd = rot * cMin,
                MaxEnd = rot * cMax,
                Size = len,
                HandleAtMax = wMax < wMin,
            };
            // Tras el giro, el extremo "min" debe quedar en −Z; si no, se intercambian
            if (f.MinEnd.z > f.MaxEnd.z) { var t = f.MinEnd; f.MinEnd = f.MaxEnd; f.MaxEnd = t; f.HandleAtMax = !f.HandleAtMax; }
            fits[prefab] = f;
            return f;
        }

        static Vector3 Axis(int i) { return i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward; }
    }
}
