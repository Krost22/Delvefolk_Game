using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public static class Art
    {
        static DelvefolkArt catalog;
        static bool loaded;
        static Transform templateRoot;
        static readonly Dictionary<GameObject, GameObject> templates = new Dictionary<GameObject, GameObject>();
        static readonly Dictionary<GameObject, Bounds> boundsCache = new Dictionary<GameObject, Bounds>();
        static readonly List<Collider> colliderBuf = new List<Collider>();
        static readonly List<Renderer> rendererBuf = new List<Renderer>();

        // Las pruebas pueden desactivar los modelos de assets para comprobar el camino procedural
        public static bool UseAssets = true;

        public static DelvefolkArt Catalog
        {
            get
            {
                if (!loaded) { loaded = true; catalog = Resources.Load<DelvefolkArt>("DelvefolkArt"); }
                return catalog;
            }
        }

        public static bool Has(GameObject prefab) { return UseAssets && prefab; }

        public static GameObject Pick(GameObject[] list, Rng r)
        {
            if (!UseAssets || list == null || list.Length == 0) return null;
            var p = list[(int)(r.Next() * list.Length)];
            return p ? p : null;
        }

        // Instancia una copia "limpia" del prefab: sin colisionadores (la física es nuestra) y con el shader del juego
        public static GameObject Spawn(GameObject prefab, Transform parent)
        {
            if (!Has(prefab)) return null;
            var tpl = Template(prefab);
            var go = Object.Instantiate(tpl, parent, false);
            go.name = prefab.name;
            go.SetActive(true);
            return go;
        }

        // Caja envolvente del modelo limpio en su espacio local (sin escalar)
        public static Bounds LocalBounds(GameObject prefab)
        {
            Bounds b;
            if (boundsCache.TryGetValue(prefab, out b)) return b;
            var tpl = Template(prefab);
            bool init = false;
            tpl.GetComponentsInChildren(true, rendererBuf);
            var inv = tpl.transform.worldToLocalMatrix;
            foreach (var r in rendererBuf)
            {
                if (r is ParticleSystemRenderer) continue;
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf) mesh = mf.sharedMesh;
                var smr = r as SkinnedMeshRenderer;
                if (smr) mesh = smr.sharedMesh;
                if (!mesh) continue;
                var mb = mesh.bounds;
                var m = inv * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p);
                }
            }
            boundsCache[prefab] = b;
            return b;
        }

        static GameObject Template(GameObject prefab)
        {
            GameObject t;
            if (templates.TryGetValue(prefab, out t) && t) return t;
            if (!templateRoot)
            {
                var root = new GameObject("DF_Plantillas");
                root.SetActive(false);
                if (Application.isPlaying) Object.DontDestroyOnLoad(root);
                root.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                templateRoot = root.transform;
                templates.Clear();
                boundsCache.Clear();
            }
            t = Object.Instantiate(prefab, templateRoot, false);
            t.name = prefab.name;
            t.GetComponentsInChildren(true, colliderBuf);
            foreach (var c in colliderBuf) Object.DestroyImmediate(c);
            foreach (var rb in t.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            t.GetComponentsInChildren(true, rendererBuf);
            foreach (var r in rendererBuf)
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Mats.FromAsset(mats[i]);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            t.SetActive(false);
            templates[prefab] = t;
            return t;
        }
    }
}
