using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Delvefolk.EditorTools
{
    // Prepara los modelos de la sala del Crupier (exportados de Meshy): materiales URP con su textura y su mapa
    // de normales, la rueda partida en marco fijo + centro que gira, el catálogo Resources/CasinoArt y los
    // datos de la rueda en Resources/CrupierRoulette. Se puede repetir sin problema si cambian los modelos.
    public static class CasinoSetup
    {
        public const string Models = "Assets/_Custom/Jefe final escenario models";
        public const string OutDir = ProjectSetup.Root + "/Art/JefeFinal";
        public const string CatalogPath = ProjectSetup.Root + "/Resources/CasinoArt.asset";
        public const string RoulettePath = ProjectSetup.Root + "/Resources/CrupierRoulette.asset";
        public const string WheelPrefab = OutDir + "/RuedaCrupier.prefab";

        // Radios de la rueda de Meshy (fracción del radio del marco), medidos sobre su textura:
        // centro dorado < 0,24 · segmentos de color hasta 0,64 · marco e indicador a partir de ahí
        const float HubR = 0.24f, DiscR = 0.66f;

        [MenuItem("Delvefolk/Preparar sala del Crupier")]
        public static string Setup()
        {
            if (!AssetDatabase.IsValidFolder(Models)) return "No encuentro " + Models;
            Directory.CreateDirectory(OutDir);
            AssetDatabase.Refresh();
            var log = new List<string>();
            var found = new Dictionary<string, GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                string folder = Path.GetFileName(Path.GetDirectoryName(path));
                string name = folder.Replace("Meshy_AI_", "");
                int cut = name.IndexOf("_2026");
                string key = cut > 0 ? name.Substring(0, cut) : name;
                FixMaterial(path, folder, name);
                if (!found.ContainsKey(key)) found[key] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                log.Add("Material: " + name);
            }
            var cat = AssetDatabase.LoadAssetAtPath<CasinoArt>(CatalogPath);
            bool create = !cat;
            if (create) cat = ScriptableObject.CreateInstance<CasinoArt>();
            cat.Throne = Get(found, "Crimson_Card_Throne");
            cat.Lion = Get(found, "Golden_Sentinel");
            cat.Chandelier = Get(found, "Golden_Candle_Chandelier");
            cat.Emblem = Get(found, "Royal_Card_Table");
            cat.Runner = Get(found, "Crimson_Clover_Runner");
            cat.CasinoTable = Get(found, "Royal_Casino_Table");
            cat.SlotMachine = Get(found, "slot_machine_lowpoly");
            cat.Column = Get(found, "Spire_of_Spades");
            var wheelSrc = Get(found, "Prismatic_Fortune_Wheel");
            if (wheelSrc) { cat.Wheel = BuildWheel(wheelSrc); log.Add("Rueda: " + WheelPrefab); }
            if (create) AssetDatabase.CreateAsset(cat, CatalogPath);
            EditorUtility.SetDirty(cat);
            SetupRoulette(cat.Wheel);
            AssetDatabase.SaveAssets();
            log.Add("Catálogo: " + CatalogPath);
            return string.Join("\n", log);
        }

        static GameObject Get(Dictionary<string, GameObject> d, string key) { GameObject g; return d.TryGetValue(key, out g) ? g : null; }

        // Material URP Lit con la textura base y el mapa de normales de Meshy, y reasignado en el FBX
        static void FixMaterial(string modelPath, string folder, string name)
        {
            string dir = Path.GetDirectoryName(modelPath).Replace("\\", "/");
            Texture2D baseTex = null, normal = null;
            foreach (var tg in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var tp = AssetDatabase.GUIDToAssetPath(tg);
                if (tp.EndsWith("_normal.png"))
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
                    if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                    normal = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                }
                else if (tp.EndsWith("_texture.png")) baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            }
            string matPath = OutDir + "/" + name.Replace("_20261017_texture", "") + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            if (normal) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(mat);
            var imp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "Material.001");
            Object cur;
            if (imp.GetExternalObjectMap().TryGetValue(id, out cur) && cur == mat) return;
            imp.AddRemap(id, mat);
            imp.SaveAndReimport();
        }

        // La rueda de Meshy es una sola malla: se parte por el radio de cada triángulo. El marco (y el indicador de
        // arriba) quedan fijos; el centro dorado gira. Los segmentos de color los dibuja el juego con los colores
        // de la configuración, entre los radios que se guardan en CrupierRoulette.
        static GameObject BuildWheel(GameObject src)
        {
            var mf = src.GetComponentInChildren<MeshFilter>();
            var mesh = mf.sharedMesh;
            var v = mesh.vertices;
            var tri = mesh.triangles;
            var b = mesh.bounds;
            // El disco está en el plano XZ de la malla (el FBX lo gira 270° en X): radio en x, centro en (cx, cz)
            float R = b.extents.x, cx = b.center.x, cz = b.min.z + R;
            var frame = new List<int>();
            var hub = new List<int>();
            for (int t = 0; t < tri.Length; t += 3)
            {
                var c = (v[tri[t]] + v[tri[t + 1]] + v[tri[t + 2]]) / 3f;
                float dx = c.x - cx, dz = c.z - cz, r = Mathf.Sqrt(dx * dx + dz * dz) / R;
                float ang = Mathf.Abs(Mathf.Atan2(dx, dz)) * Mathf.Rad2Deg;      // 0 = arriba (indicador)
                List<int> to = null;
                if (r < HubR) to = hub;
                else if (r >= DiscR || (ang < 9f && r > 0.5f)) to = frame;      // el indicador baja un poco sobre el disco
                if (to == null) continue;
                to.Add(tri[t]); to.Add(tri[t + 1]); to.Add(tri[t + 2]);
            }
            var frameMesh = SubMesh(mesh, frame, "RuedaMarco");
            var hubMesh = SubMesh(mesh, hub, "RuedaCentro");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutDir + "/Prismatic_Fortune_Wheel_4ec5a674.mat") ?? mf.GetComponent<MeshRenderer>().sharedMaterial;

            var root = new GameObject("RuedaCrupier");
            var model = new GameObject("Modelo").transform;
            model.SetParent(root.transform, false);
            model.localRotation = src.transform.localRotation;
            model.localScale = src.transform.localScale;
            AddMesh(model, "Marco", frameMesh, mat, Vector3.zero);
            var spin = new GameObject("Giro").transform;
            spin.SetParent(model, false);
            spin.localPosition = new Vector3(cx, 0f, cz);                         // el giro pivota en el centro del disco
            AddMesh(spin, "Centro", hubMesh, mat, new Vector3(-cx, 0f, -cz));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, WheelPrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Mesh SubMesh(Mesh src, List<int> idx, string name)
        {
            string path = OutDir + "/" + name + ".asset";
            var map = new Dictionary<int, int>();
            var sv = src.vertices; var sn = src.normals; var su = src.uv; var st = src.tangents;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<Vector4>();
            var tris = new int[idx.Count];
            for (int i = 0; i < idx.Count; i++)
            {
                int o;
                if (!map.TryGetValue(idx[i], out o))
                {
                    o = v.Count; map[idx[i]] = o;
                    v.Add(sv[idx[i]]);
                    if (sn.Length > 0) n.Add(sn[idx[i]]);
                    if (su.Length > 0) u.Add(su[idx[i]]);
                    if (st.Length > 0) t.Add(st[idx[i]]);
                }
                tris[i] = o;
            }
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = !m;
            if (create) m = new Mesh();
            m.Clear();
            m.name = name;
            m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v);
            if (n.Count == v.Count) m.SetNormals(n);
            if (u.Count == v.Count) m.SetUVs(0, u);
            if (t.Count == v.Count) m.SetTangents(t);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(m, path);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void AddMesh(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // Datos de la rueda real en la configuración de la ruleta (espacio local de "Modelo/Giro")
        static void SetupRoulette(GameObject wheel)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<RouletteConfig>(RoulettePath);
            if (!cfg || !wheel) return;
            var hub = wheel.transform.Find("Modelo/Giro/Centro").GetComponent<MeshFilter>().sharedMesh;
            var frame = wheel.transform.Find("Modelo/Marco").GetComponent<MeshFilter>().sharedMesh;
            float R = frame.bounds.extents.x;
            cfg.WheelModel = wheel;
            cfg.WheelSpinnerPath = "Modelo/Giro";
            // La cara de delante es +Y de la malla (mira a la cámara); el 0 está arriba (+Z de la malla)
            cfg.SpinAxis = Vector3.down;
            cfg.ReferenceDir = Vector3.forward;
            cfg.PointerAngle = 0f;
            cfg.WheelSegmentsInner = R * HubR * 0.92f;
            cfg.WheelSegmentsOuter = R * (DiscR + 0.05f);                        // tapa el borde del disco original
            cfg.WheelSegmentsDepth = -frame.bounds.max.y * 1.1f;
            cfg.WheelScale = 2.0f;
            EditorUtility.SetDirty(cfg);
        }
    }
}
