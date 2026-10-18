using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Delvefolk.EditorTools
{
    // Prepara las armas de Meshy (Assets/_Custom/armasJugador): material URP con su textura y normales,
    // reasignado en cada FBX, y el catálogo Resources/WeaponModels con el agarre de cada una.
    // Se puede repetir: conserva los ajustes de agarre que ya se hayan hecho en el Inspector.
    public static class WeaponSetup
    {
        public const string Models = "Assets/_Custom/armasJugador";
        public const string OutDir = ProjectSetup.Root + "/Art/Armas";
        public const string CatalogPath = ProjectSetup.Root + "/Resources/WeaponModels.asset";

        // Carpeta de Meshy → id del arma en el juego (el trapeador aún no tiene modelo: usa el de código)
        static readonly Dictionary<string, string> idByModel = new Dictionary<string, string>
        {
            { "Emberbound_Skillet", "hellpan" },
            { "Vinebound_Iron_Rake", "rake" },
            { "Mushroom_Parasol", "parasol" },
            { "Arc_Reactor_Cannon", "vacuum" },
        };

        // Agarre inicial (ajustado a ojo en la partida); luego se retoca en el Inspector
        static WeaponModelEntry Default(string id)
        {
            switch (id)
            {
                case "hellpan": return new WeaponModelEntry { Id = id, Length = 1.9f, HandleBack = 0.1f };
                case "rake": return new WeaponModelEntry { Id = id, Length = 2.3f, HandleBack = 0.15f };
                case "parasol": return new WeaponModelEntry { Id = id, Length = 1.9f, HandleBack = 0.25f, FlipLength = true };
                case "vacuum": return new WeaponModelEntry { Id = id, Length = 1.3f, HandleBack = 0.35f, FlipLength = true, GripOffset = new Vector3(0f, 0.05f, 0f) };
                default: return new WeaponModelEntry { Id = id };
            }
        }

        [MenuItem("Delvefolk/Preparar armas de Meshy")]
        public static string Setup()
        {
            if (!AssetDatabase.IsValidFolder(Models)) return "No encuentro " + Models;
            Directory.CreateDirectory(OutDir);
            AssetDatabase.Refresh();
            var log = new List<string>();
            var cat = AssetDatabase.LoadAssetAtPath<WeaponModelCatalog>(CatalogPath);
            bool create = !cat;
            if (create) cat = ScriptableObject.CreateInstance<WeaponModelCatalog>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                string id = null, key = null;
                foreach (var kv in idByModel) if (path.Contains(kv.Key)) { id = kv.Value; key = kv.Key; }
                if (id == null) { log.Add("Sin uso: " + path); continue; }
                FixMaterial(path, key);
                var entry = cat.Get(id);
                if (entry == null) { entry = Default(id); cat.Entries.Add(entry); }
                entry.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                log.Add(id + " ← " + Path.GetFileName(path));
            }
            if (cat.Get("mop") == null) cat.Entries.Add(new WeaponModelEntry { Id = "mop", Length = 2.1f });
            if (create) AssetDatabase.CreateAsset(cat, CatalogPath);
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            log.Add("Catálogo: " + CatalogPath + " (mop sin modelo: asigna su FBX en Prefab cuando exista)");
            return string.Join("\n", log);
        }

        public const string ThemesPath = ProjectSetup.Root + "/Resources/FloorThemes.asset";
        public const string SlotPath = ProjectSetup.Root + "/Resources/SlotMachine.asset";

        // Crea (si no existen) los assets editables de afinidades y tragaperras con los valores por defecto
        [MenuItem("Delvefolk/Crear configuración de afinidades y tragaperras")]
        public static string SetupThemesAndSlot()
        {
            var log = new List<string>();
            if (!AssetDatabase.LoadAssetAtPath<FloorThemeCatalog>(ThemesPath))
            {
                var t = ScriptableObject.CreateInstance<FloorThemeCatalog>();
                t.Themes = FloorThemeCatalog.Defaults();
                AssetDatabase.CreateAsset(t, ThemesPath);
                log.Add("Creado " + ThemesPath);
            }
            else log.Add("Ya existe " + ThemesPath);
            if (!AssetDatabase.LoadAssetAtPath<SlotMachineConfig>(SlotPath))
            {
                var s = ScriptableObject.CreateInstance<SlotMachineConfig>();
                s.FillDefaults();
                AssetDatabase.CreateAsset(s, SlotPath);
                log.Add("Creado " + SlotPath);
            }
            else log.Add("Ya existe " + SlotPath);
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        // Material URP Lit con la textura base y el mapa de normales de Meshy, reasignado en el FBX
        static void FixMaterial(string modelPath, string name)
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
            string matPath = OutDir + "/" + name + ".mat";
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
            bool remapped = imp.GetExternalObjectMap().TryGetValue(id, out cur) && cur == mat;
            // Lectura de vértices: el juego mide el modelo para orientarlo solo y encontrar el mango
            if (remapped && imp.isReadable) return;
            imp.isReadable = true;
            if (!remapped) imp.AddRemap(id, mat);
            imp.SaveAndReimport();
        }
    }
}
