using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Delvefolk.EditorTools
{
    // Herramientas de preparación del proyecto: escena Bootstrap, catálogo de arte y recursos de TextMeshPro.
    // Todo se puede lanzar desde el menú Delvefolk o con `unity command run_script`.
    public static class ProjectSetup
    {
        public const string Root = "Assets/_Delvefolk";
        public const string ScenePath = Root + "/Scenes/Bootstrap.unity";
        public const string CatalogPath = Root + "/Resources/DelvefolkArt.asset";
        const string Dungeon = "Assets/LowPolyDungeons/Prefabs/";

        [MenuItem("Delvefolk/Preparar todo")]
        public static string SetupAll()
        {
            var log = new List<string>();
            log.Add(ImportTmpEssentials());
            log.Add(BuildCatalog());
            log.Add(CreateBootstrapScene());
            return string.Join("\n", log);
        }

        // La escena solo contiene un objeto Bootstrap: todo lo demás se construye por código
        [MenuItem("Delvefolk/Crear escena Bootstrap")]
        public static string CreateBootstrapScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!File.Exists(ScenePath))
            {
                var prev = EditorSceneManager.GetActiveScene().path;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var go = new GameObject("Bootstrap");
                go.AddComponent<GameLoop>();
                SceneManagerMove(go, scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorSceneManager.CloseScene(scene, true);
            }
            // Primera en la compilación
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            return "Escena: " + ScenePath;
        }

        static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        [MenuItem("Delvefolk/Importar recursos de TextMeshPro")]
        public static string ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return "TMP: ya importado";
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            return "TMP: importando recursos esenciales";
        }

        static GameObject P(string rel)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Dungeon + rel + ".prefab");
            if (!go) Debug.LogWarning("Delvefolk: falta el prefab " + rel);
            return go;
        }

        static GameObject[] PS(params string[] rels) { return rels.Select(P).Where(g => g).ToArray(); }

        // Rellena el catálogo con los modelos de los paquetes instalados (rutas fijas, se revisan a mano)
        [MenuItem("Delvefolk/Reconstruir catálogo de arte")]
        public static string BuildCatalog()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            var cat = AssetDatabase.LoadAssetAtPath<DelvefolkArt>(CatalogPath);
            bool create = !cat;
            if (create) cat = ScriptableObject.CreateInstance<DelvefolkArt>();
            cat.FlatShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/DelvefolkFlat.shader");
            bool hasDungeon = AssetDatabase.IsValidFolder("Assets/LowPolyDungeons");
            if (hasDungeon)
            {
                cat.Floors = PS("Dungeon/Ground_05", "Dungeon/Ground_06", "Dungeon/Ground_11", "Dungeon/Ground_12");
                cat.FloorsRare = PS("Dungeon/Ground_03", "Dungeon/Ground_04");
                cat.Walls = PS("Dungeon/Wall_01", "Dungeon/Wall_02", "Dungeon/Wall_03", "Dungeon/Wall_04", "Dungeon/Wall_05", "Dungeon/Wall_06");
                cat.Pillar = P("Dungeon/Column_05");
                cat.ArchPost = P("Dungeon/Column_01");
                cat.ArchLintel = P("Dungeon/WallPart_02");
                cat.Crates = PS("Props/Box_01", "Props/Box_02", "Props/Box_03");
                cat.Torch = P("Props/Light_05");
                cat.EntranceDoor = P("Dungeon/Wall_08_Door_01");
                cat.GateBars = P("Dungeon/MetalWall_01");
                cat.Spikes = P("Dungeon/Ground_16");
                cat.Rubble = PS("Dungeon/Brick_01", "Dungeon/Brick_02", "Dungeon/Brick_03", "Dungeon/Brick_04");
                cat.Bones = PS("Props/Bone_01", "Props/Bone_02");
                cat.Skull = P("Props/Skull");
                cat.Webs = PS("Props/SpiderWeb_01", "Props/SpiderWeb_02");
                cat.Banners = PS("Props/WallDecor_05", "Props/WallDecor_06", "Props/WallDecor_07", "Props/WallDecor_08");
                cat.Chains = PS("Props/Chain_01", "Props/Chain_02", "Props/Chain_03");
                cat.Crown = P("Gold/Crown_01");
                cat.Barrel = P("Props/Barrel_01");
                cat.Bookshelves = PS("Props/LibraryWithBooks_01", "Props/LibraryWithBooks_02", "Props/LibraryWithBooks_03", "Props/LibraryWithBooks_04", "Props/LibraryWithBooks_05");
                cat.Tables = PS("Props/Table_01", "Props/Table_02");
                cat.Vases = PS("Props/Jug_01", "Props/Jug_02", "Props/Jug_03", "Props/Jug_04", "Props/Jug_05");
            }
            const string Monsters = "Assets/Monsters Ultimate Pack 09 Cute Series/";
            if (AssetDatabase.IsValidFolder("Assets/Monsters Ultimate Pack 09 Cute Series"))
            {
                cat.SlimeModel = AssetDatabase.LoadAssetAtPath<GameObject>(Monsters + "Imp Mischief Cute Series/Prefabs/Imp Mischief.prefab");
                cat.GoblinModel = AssetDatabase.LoadAssetAtPath<GameObject>(Monsters + "Imp Demon Cute Series/Prefabs/Imp Demon.prefab");
                cat.GrabberModel = AssetDatabase.LoadAssetAtPath<GameObject>(Monsters + "Treant Forest Cute Series/Prefabs/Treant Forest Autumn.prefab");
            }
            if (create) AssetDatabase.CreateAsset(cat, CatalogPath);
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            return "Catálogo: " + CatalogPath + (hasDungeon ? " (con LowPolyDungeons)" : " (sin paquetes: modelos por código)");
        }
    }
}
