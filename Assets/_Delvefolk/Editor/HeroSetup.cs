using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Delvefolk.EditorTools
{
    // Prepara los personajes de Meshy (Assets/_Custom/Characters/*/..._biped): material URP con su textura y
    // normales, Avatar Generic creado desde el propio modelo (cada uno conserva su esqueleto), animación de
    // correr en bucle, y el catálogo Resources/Heroes con los huesos de la mano de cada uno.
    // Los *_texture_fbx son el mismo personaje sin esqueleto: no se usan. Se puede repetir: conserva los ajustes.
    public static class HeroSetup
    {
        public const string Models = "Assets/_Custom/Characters";
        public const string OutDir = ProjectSetup.Root + "/Art/Personajes";
        public const string CatalogPath = ProjectSetup.Root + "/Resources/Heroes.asset";

        static readonly Dictionary<string, KeyValuePair<string, string>> names = new Dictionary<string, KeyValuePair<string, string>>
        {
            { "Blossom_Elf_Maiden", new KeyValuePair<string, string>("Doncella Élfica", "🌸") },
            { "Crimson_Elven_Rogue", new KeyValuePair<string, string>("Pícara Carmesí", "🗡️") },
            { "Low_Poly_Pink_Elf_Gir", new KeyValuePair<string, string>("Elfa Rosa", "🎀") },
            { "Rosewind_Rogue", new KeyValuePair<string, string>("Pícara Rosaviento", "🌹") },
            { "Azure_Wayfarer", new KeyValuePair<string, string>("Viajero Azur", "🧭") },
            { "Crimson_Shadow_Rogue", new KeyValuePair<string, string>("Sombra Carmesí", "🥷") },
            { "Embercloak_Alchemist", new KeyValuePair<string, string>("Alquimista de Ascuas", "🧪") },
            { "Feathered_Outlaw", new KeyValuePair<string, string>("Forajido Emplumado", "🪶") },
        };

        // Habilidad de cada personaje (7 habilidades para 8: la Carga se repite). Editable en Resources/Heroes
        static readonly Dictionary<string, string> abilities = new Dictionary<string, string>
        {
            { "azure_wayfarer", "quake" }, { "embercloak_alchemist", "blast" }, { "feathered_outlaw", "magnet" },
            { "crimson_shadow_rogue", "swap" }, { "rosewind_rogue", "charge" }, { "low_poly_pink_elf_gir", "shout" },
            { "blossom_elf_maiden", "guard" }, { "crimson_elven_rogue", "charge" },
        };

        [MenuItem("Delvefolk/Preparar personajes de Meshy")]
        public static string Setup()
        {
            if (!AssetDatabase.IsValidFolder(Models)) return "No encuentro " + Models;
            Directory.CreateDirectory(OutDir);
            AssetDatabase.Refresh();
            var log = new List<string>();
            var cat = AssetDatabase.LoadAssetAtPath<HeroCatalog>(CatalogPath);
            bool create = !cat;
            if (create) cat = ScriptableObject.CreateInstance<HeroCatalog>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("_biped")) continue;    // los *_texture no tienen esqueleto
                string folder = Path.GetFileName(Path.GetDirectoryName(path)).Replace("Meshy_AI_", "").Replace("_biped", "");
                ConfigureImport(path, folder);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                AnimationClip clip = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip && !o.name.StartsWith("__preview")) clip = (AnimationClip)o;
                var id = folder.ToLowerInvariant();
                HeroEntry e = null;
                foreach (var h in cat.Heroes) if (h.Id == id) e = h;
                if (e == null)
                {
                    KeyValuePair<string, string> nm;
                    if (!names.TryGetValue(folder, out nm)) nm = new KeyValuePair<string, string>(folder.Replace("_", " "), "🧝");
                    e = new HeroEntry { Id = id, Name = nm.Key, Icon = nm.Value };
                    string ab;
                    if (abilities.TryGetValue(id, out ab)) e.Ability = ab;
                    cat.Heroes.Add(e);
                }
                e.Prefab = prefab;
                e.Run = clip;
                // Huesos de la mano elegida en este esqueleto (se comprueban, no se suponen)
                var side = e.Hand == HeroHand.Left ? "Left" : "Right";
                string up = Find(prefab, side + "Arm"), fore = Find(prefab, side + "ForeArm"), hand = Find(prefab, side + "Hand");
                if (up != null) e.UpperArmBone = up;
                if (fore != null) e.ForeArmBone = fore;
                if (hand != null) e.HandBone = hand;
                log.Add(e.Name + " (" + id + "): mano " + e.HandBone + (hand == null ? "  ⚠ NO ENCONTRADA" : "") + " · clip " + (clip ? clip.length.ToString("0.00") + " s" : "ninguno"));
            }
            if (create) AssetDatabase.CreateAsset(cat, CatalogPath);
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            log.Add("Catálogo: " + CatalogPath);
            return string.Join("\n", log);
        }

        // ---------- Reposo (Idle) ----------
        // Solo un personaje trae Idle (archivo *_Animation_Idle*.fbx). Su dueño lo usa tal cual; para los demás se
        // hornea una copia adaptada a su esqueleto: misma ROTACIÓN relativa a la pose de reposo de cada hueso
        // (nunca las posiciones ni escalas del original, que deformarían otras proporciones) y la altura de la
        // cadera proporcional. Después se comprueba: si las direcciones de brazos, piernas, columna y cabeza se
        // separan más de MaxAngle de media (o MaxPeak en algún momento) del original, ese personaje NO recibe el Idle (sigue con su pose de correr).
        public const float MaxAngle = 20f, MaxPeak = 30f;   // media y pico (un brazo muy desviado ya se ve mal)
        static readonly string[] CoreBones = { "Hips", "Spine", "Head", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg" };
        static readonly string[][] Limbs =
        {
            new[] { "Hips", "Spine" }, new[] { "Spine", "Head" }, new[] { "LeftArm", "LeftForeArm" }, new[] { "LeftForeArm", "LeftHand" },
            new[] { "RightArm", "RightForeArm" }, new[] { "RightForeArm", "RightHand" }, new[] { "LeftUpLeg", "LeftLeg" }, new[] { "LeftLeg", "LeftFoot" },
            new[] { "RightUpLeg", "RightLeg" }, new[] { "RightLeg", "RightFoot" },
        };

        [MenuItem("Delvefolk/Integrar Idle en los personajes")]
        public static string SetupIdle()
        {
            var cat = AssetDatabase.LoadAssetAtPath<HeroCatalog>(CatalogPath);
            if (!cat) return "Primero: Delvefolk/Preparar personajes de Meshy";
            var log = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).Contains("_Animation_Idle")) continue;
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
                bool dirty = false;
                foreach (var c in clips) if (!c.loopTime) { c.loopTime = true; dirty = true; }
                if (dirty) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
                var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                AnimationClip src = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is AnimationClip && !o.name.StartsWith("__preview")) src = (AnimationClip)o;
                if (!src) { log.Add("Sin clip en " + path); continue; }
                string owner = Path.GetFileName(Path.GetDirectoryName(path)).Replace("Meshy_AI_", "").Replace("_biped", "").ToLowerInvariant();
                log.Add("Idle: " + Path.GetFileName(path) + " · clip \"" + src.name + "\" " + src.length.ToString("0.00") + " s · rig " + imp.animationType + " · dueño " + owner);
                foreach (var h in cat.Heroes)
                {
                    if (!h.Prefab) continue;
                    if (h.Id == owner) { h.Idle = src; log.Add("  " + h.Name + ": su propio Idle"); continue; }
                    string why;
                    var baked = Retarget(src, srcPrefab, h, out why);
                    h.Idle = baked;
                    log.Add("  " + h.Name + ": " + (baked ? "adaptado (" + why + ")" : "INCOMPATIBLE — " + why + " · sigue con su pose de correr"));
                }
            }
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        // Rotación local de cada hueso en la pose de la piel (bindpose), en el espacio de su padre
        internal static Dictionary<string, Quaternion> BindLocal(GameObject go)
        {
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var bones = smr.bones; var bp = smr.sharedMesh.bindposes;
            var meshRot = new Dictionary<Transform, Quaternion>();   // rotación de cada hueso en el espacio de la malla
            for (int i = 0; i < bones.Length && i < bp.Length; i++) if (bones[i]) meshRot[bones[i]] = bp[i].inverse.rotation;
            var inv = Quaternion.Inverse(smr.transform.rotation);
            var r = new Dictionary<string, Quaternion>();
            foreach (var kv in meshRot)
            {
                var t = kv.Key;
                Quaternion parent;
                if (!t.parent || !meshRot.TryGetValue(t.parent, out parent)) parent = t.parent ? inv * t.parent.rotation : Quaternion.identity;
                r[t.name] = Quaternion.Inverse(parent) * kv.Value;
            }
            return r;
        }

        static Dictionary<string, Transform> Map(GameObject go)
        {
            var d = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (!d.ContainsKey(t.name)) d[t.name] = t;
            return d;
        }

        static Transform Core(Dictionary<string, Transform> m, string suffix)
        {
            Transform t;
            return m.TryGetValue("mixamorig:" + suffix, out t) || m.TryGetValue(suffix, out t) ? t : null;
        }

        static AnimationClip Retarget(AnimationClip src, GameObject srcPrefab, HeroEntry h, out string why)
        {
            var S = Object.Instantiate(srcPrefab); var T = Object.Instantiate(h.Prefab);
            try
            {
                var sm = Map(S); var tm = Map(T);
                foreach (var c in CoreBones)
                    if (!Core(sm, c) || !Core(tm, c)) { why = "le falta el hueso " + c; return null; }
                // Huesos comunes (por nombre) bajo la cadera, con su pose de reposo (la del FBX, antes de animar)
                var sHips = Core(sm, "Hips"); var tHips = Core(tm, "Hips");
                var common = new List<string>();
                foreach (var t in tHips.GetComponentsInChildren<Transform>(true)) if (sm.ContainsKey(t.name)) common.Add(t.name);
                // Pose de reposo REAL: la de la piel (bindposes). La pose por defecto de los FBX de Meshy es un fotograma
                // cualquiera (distinta incluso entre dos archivos del mismo personaje) y no sirve de referencia
                var sBind = BindLocal(S); var tBind = BindLocal(T);
                common.RemoveAll(n => !sBind.ContainsKey(n) || !tBind.ContainsKey(n));
                Vector3 sHipBind = sHips.localPosition, tHipBind = tHips.localPosition;
                float hipRatio = Mathf.Abs(sHipBind.y) > 1e-5f ? tHipBind.y / sHipBind.y : 1f;
                const float Fps = 30f;
                int frames = Mathf.Max(2, Mathf.RoundToInt(src.length * Fps) + 1);
                var curves = new Dictionary<string, AnimationCurve[]>();
                var hipCurves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                foreach (var n in common) curves[n] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                var last = new Dictionary<string, Quaternion>();
                float worst = 0f, sum = 0f; int count = 0;
                for (int f = 0; f < frames; f++)
                {
                    float time = Mathf.Min(src.length, f / Fps);
                    src.SampleAnimation(S, time);
                    foreach (var n in common)
                    {
                        var q = tBind[n] * (Quaternion.Inverse(sBind[n]) * sm[n].localRotation);
                        Quaternion prev;
                        if (last.TryGetValue(n, out prev) && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        last[n] = q;
                        var c = curves[n];
                        c[0].AddKey(time, q.x); c[1].AddKey(time, q.y); c[2].AddKey(time, q.z); c[3].AddKey(time, q.w);
                        tm[n].localRotation = q;
                    }
                    var hp = tHipBind + (sHips.localPosition - sHipBind) * hipRatio;
                    hipCurves[0].AddKey(time, hp.x); hipCurves[1].AddKey(time, hp.y); hipCurves[2].AddKey(time, hp.z);
                    tHips.localPosition = hp;
                    // Comprobación: direcciones de las extremidades en el espacio de la cadera
                    foreach (var l in Limbs)
                    {
                        Transform sa = Core(sm, l[0]), sb = Core(sm, l[1]), ta = Core(tm, l[0]), tb = Core(tm, l[1]);
                        if (!sa || !sb || !ta || !tb) continue;
                        var ds = S.transform.InverseTransformDirection(sb.position - sa.position);
                        var dt = T.transform.InverseTransformDirection(tb.position - ta.position);
                        float a = Vector3.Angle(ds, dt);
                        worst = Mathf.Max(worst, a); sum += a; count++;
                    }
                }
                float avg = count > 0 ? sum / count : 0f;
                if (avg > MaxAngle || worst > MaxPeak) { why = "desviación media " + avg.ToString("0") + "°, máx. " + worst.ToString("0") + "° (límites " + MaxAngle + "° / " + MaxPeak + "°)"; return null; }
                var clip = new AnimationClip { name = "Idle_" + h.Id, frameRate = Fps };
                foreach (var n in common)
                {
                    string p = AnimationUtility.CalculateTransformPath(tm[n], T.transform);
                    var c = curves[n];
                    clip.SetCurve(p, typeof(Transform), "localRotation.x", c[0]);
                    clip.SetCurve(p, typeof(Transform), "localRotation.y", c[1]);
                    clip.SetCurve(p, typeof(Transform), "localRotation.z", c[2]);
                    clip.SetCurve(p, typeof(Transform), "localRotation.w", c[3]);
                }
                string hipPath = AnimationUtility.CalculateTransformPath(tHips, T.transform);
                clip.SetCurve(hipPath, typeof(Transform), "localPosition.x", hipCurves[0]);
                clip.SetCurve(hipPath, typeof(Transform), "localPosition.y", hipCurves[1]);
                clip.SetCurve(hipPath, typeof(Transform), "localPosition.z", hipCurves[2]);
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                string outPath = OutDir + "/Idle_" + h.Id + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                if (existing) { EditorUtility.CopySerialized(clip, existing); clip = existing; }
                else AssetDatabase.CreateAsset(clip, outPath);
                why = common.Count + " huesos, desviación media " + avg.ToString("0.0") + "°, máx. " + worst.ToString("0") + "°";
                return clip;
            }
            finally { Object.DestroyImmediate(S); Object.DestroyImmediate(T); }
        }

        // Hueso cuyo nombre acaba en ":" + suffix (Mixamo) o es exactamente suffix
        static string Find(GameObject prefab, string suffix)
        {
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                if (t.name == suffix || t.name.EndsWith(":" + suffix)) return t.name;
            return null;
        }

        static void ConfigureImport(string modelPath, string name)
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
                else if (tp.EndsWith("_texture_0.png")) baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            }
            string matPath = OutDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            if (normal) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(mat);

            var imp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            bool dirty = false;
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "Material.001");
            Object cur;
            if (!(imp.GetExternalObjectMap().TryGetValue(id, out cur) && cur == mat)) { imp.AddRemap(id, mat); dirty = true; }
            // Generic con su propio Avatar: cada personaje conserva su esqueleto (no se renombra ni se borra nada)
            if (imp.animationType != ModelImporterAnimationType.Generic) { imp.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            // Legible: el juego hornea la malla para medir su altura (en una build, sin esto BakeMesh falla)
            if (!imp.isReadable) { imp.isReadable = true; dirty = true; }
            if (imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            foreach (var c in clips) if (!c.loopTime) { c.loopTime = true; dirty = true; }
            if (dirty) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
        }
    }
}
