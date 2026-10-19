using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Delvefolk.EditorTools
{
    // Nuevo modelo del Crupier (Assets/_Custom/jefeModelo): material URP con su textura, Generic + Avatar propio,
    // andar y correr en bucle, un Idle CREADO aquí para su esqueleto (no trae ninguno) y la asignación en
    // Resources/CrupierRoulette (CrupierModel + clips). Revertir = vaciar CrupierModel en ese asset.
    public static class BossModelSetup
    {
        public const string Models = "Assets/_Custom/jefeModelo";
        public const string IdlePath = CasinoSetup.OutDir + "/Crupier_Idle.anim";
        const float IdleLength = 3.2f, Fps = 30f;

        [MenuItem("Delvefolk/Preparar modelo del Crupier")]
        public static string Setup()
        {
            string path = null;
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("_biped")) path = p;   // el *_texture_fbx es la misma malla sin esqueleto
            }
            if (path == null) return "No encuentro el FBX con esqueleto en " + Models;
            Directory.CreateDirectory(CasinoSetup.OutDir);
            var log = new List<string> { "Modelo: " + path };
            ConfigureImport(path, log);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            AnimationClip walk = null, run = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as AnimationClip;
                if (!c || c.name.StartsWith("__preview")) continue;
                if (c.name == "Walking") walk = c; else if (c.name == "Running") run = c;
            }
            log.Add("Clips del FBX: " + (walk ? "Walking " + walk.length.ToString("0.00") + " s" : "sin Walking") + " · " + (run ? "Running " + run.length.ToString("0.00") + " s" : "sin Running") + " · sin Idle");
            string why;
            var idle = BuildIdle(prefab, walk, out why);
            log.Add(idle ? "Idle creado: " + IdlePath + " (" + why + ")" : "Idle NO creado: " + why);
            var cfg = AssetDatabase.LoadAssetAtPath<RouletteConfig>(CasinoSetup.RoulettePath);
            if (cfg && idle)
            {
                cfg.CrupierModel = prefab;
                cfg.CrupierIdle = idle;
                cfg.CrupierWalk = walk;
                cfg.CrupierRun = run;
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
                log.Add("Asignado en " + CasinoSetup.RoulettePath);
            }
            return string.Join("\n", log);
        }

        static void ConfigureImport(string path, List<string> log)
        {
            string dir = Path.GetDirectoryName(path).Replace("\\", "/");
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
            string matPath = CasinoSetup.OutDir + "/Crupier_Crimson_Aristocrat.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            if (normal) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(mat);
            log.Add("Material: " + matPath + (baseTex ? " (" + baseTex.name + ")" : " ⚠ sin textura"));

            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            bool dirty = false;
            // Los materiales que trae el FBX (por nombre) pasan a ser el material con textura
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                var m = o as Material;
                if (!m) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                Object cur;
                if (!(imp.GetExternalObjectMap().TryGetValue(id, out cur) && cur == mat)) { imp.AddRemap(id, mat); dirty = true; }
            }
            if (imp.animationType != ModelImporterAnimationType.Generic) { imp.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                string want = c.takeName.EndsWith("Running") ? "Running" : c.takeName.EndsWith("Walking") ? "Walking" : c.name;
                if (c.name != want) { c.name = want; dirty = true; }
                if (!c.loopTime) { c.loopTime = true; dirty = true; }
            }
            if (dirty) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
            log.Add("Importación: Generic + Avatar propio, Walking/Running en bucle");
        }

        static Transform Find(Dictionary<string, Transform> m, string suffix)
        {
            Transform t;
            return m.TryGetValue("mixamorig:" + suffix, out t) || m.TryGetValue(suffix, out t) ? t : null;
        }

        // Idle de reposo para ESTE esqueleto: pose base = fotograma más neutro del andar (pies juntos y en el suelo),
        // y encima una respiración lenta, un leve balanceo de cadera, cabeza que mira un poco y hombros/brazos que se
        // mecen. Todo con armónicos enteros del ciclo, así el bucle cierra exacto. Se comprueba antes de guardarlo.
        static AnimationClip BuildIdle(GameObject prefab, AnimationClip walk, out string why)
        {
            if (!walk) { why = "no hay clip de andar del que sacar la pose base"; return null; }
            var inst = Object.Instantiate(prefab);
            try
            {
                var map = new Dictionary<string, Transform>();
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (!map.ContainsKey(t.name)) map[t.name] = t;
                Transform hips = Find(map, "Hips"), spine = Find(map, "Spine"), spine1 = Find(map, "Spine1"), spine2 = Find(map, "Spine2"),
                    neck = Find(map, "Neck"), head = Find(map, "Head"), shL = Find(map, "LeftShoulder"), shR = Find(map, "RightShoulder"),
                    armL = Find(map, "LeftArm"), armR = Find(map, "RightArm"), foreL = Find(map, "LeftForeArm"), foreR = Find(map, "RightForeArm"),
                    footL = Find(map, "LeftFoot"), footR = Find(map, "RightFoot");
                if (!hips || !spine || !head || !armL || !armR || !footL || !footR) { why = "faltan huesos básicos (cadera, columna, cabeza, brazos o pies)"; return null; }
                // Pose base de pie: la pose de la piel (bindpose: piernas rectas y juntas, en T) con los brazos bajados.
                // (El andar de este modelo nunca junta las piernas: su fotograma "neutro" parecía una zancada)
                var bind = HeroSetup.BindLocal(inst);
                var bones = new List<Transform>(hips.GetComponentsInChildren<Transform>(true));
                Vector3 right = inst.transform.right, up = inst.transform.up, fwd = inst.transform.forward;
                System.Action standPose = () =>
                {
                    walk.SampleAnimation(inst, 0f);   // posición de la cadera del clip (altura correcta)
                    foreach (var bn in bones) { Quaternion q; if (bind.TryGetValue(bn.name, out q)) bn.localRotation = q; }
                    // De la T a los brazos caídos junto al cuerpo, un poco hacia delante, codos algo doblados
                    Turn(armL, fwd, ArmDown(armL, foreL, fwd));
                    Turn(armR, fwd, ArmDown(armR, foreR, fwd));
                    Turn(armL, right, -8f); Turn(armR, right, -8f);
                    Turn(foreL, right, -12f); Turn(foreR, right, -12f);
                };
                standPose();
                var baseWorld = new Dictionary<Transform, Quaternion>();
                foreach (var b in bones) baseWorld[b] = b.rotation;
                var baseHip = hips.localPosition;
                float height = Mathf.Max(0.1f, head.position.y - Mathf.Min(footL.position.y, footR.position.y));

                int frames = Mathf.RoundToInt(IdleLength * Fps) + 1;
                var rot = new Dictionary<Transform, AnimationCurve[]>();
                foreach (var b in bones) rot[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                var hip = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                var last = new Dictionary<Transform, Quaternion>();
                float worstAngle = 0f, worstHip = 0f;
                for (int f = 0; f < frames; f++)
                {
                    float time = f / Fps;
                    float ph = Mathf.PI * 2f * time / IdleLength;   // un ciclo completo en IdleLength
                    standPose();
                    // Respiración (2 por ciclo) y leve vaivén de la cadera
                    hips.position += up * (0.004f * height * Mathf.Sin(2f * ph));
                    Turn(spine, right, 1.0f * Mathf.Sin(2f * ph));
                    Turn(spine1, right, -1.2f * Mathf.Sin(2f * ph + 0.5f));
                    Turn(spine2, right, -1.4f * Mathf.Sin(2f * ph + 0.7f));
                    Turn(spine, up, 1.5f * Mathf.Sin(ph));                       // el torso se gira apenas a un lado y a otro
                    Turn(neck, up, 2.0f * Mathf.Sin(ph + 1.0f));
                    Turn(head, up, 3.0f * Mathf.Sin(ph + 1.3f));                   // mira la mesa a un lado y a otro
                    Turn(head, right, 1.5f * Mathf.Sin(2f * ph + 1.2f));
                    Turn(shL, fwd, 1.5f * Mathf.Sin(2f * ph + 0.6f));             // los hombros suben al respirar
                    Turn(shR, fwd, -1.5f * Mathf.Sin(2f * ph + 0.6f));
                    Turn(armL, right, 2.0f * Mathf.Sin(ph + 0.3f));
                    Turn(armR, right, 2.0f * Mathf.Sin(ph + 2.0f));
                    Turn(foreL, right, -1.5f * Mathf.Sin(2f * ph + 0.9f));
                    Turn(foreR, right, -1.5f * Mathf.Sin(2f * ph + 2.4f));
                    foreach (var b in bones)
                    {
                        worstAngle = Mathf.Max(worstAngle, Quaternion.Angle(baseWorld[b], b.rotation));
                        var q = b.localRotation;
                        Quaternion prev;
                        if (last.TryGetValue(b, out prev) && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        last[b] = q;
                        var c = rot[b];
                        c[0].AddKey(time, q.x); c[1].AddKey(time, q.y); c[2].AddKey(time, q.z); c[3].AddKey(time, q.w);
                    }
                    var hp = hips.localPosition;
                    worstHip = Mathf.Max(worstHip, (hp - baseHip).magnitude);
                    hip[0].AddKey(time, hp.x); hip[1].AddKey(time, hp.y); hip[2].AddKey(time, hp.z);
                }
                // Comprobaciones: movimiento sutil y bucle cerrado
                if (worstAngle > 10f) { why = "giro máximo " + worstAngle.ToString("0.0") + "° (demasiado)"; return null; }
                float hipWorld = worstHip * hips.parent.lossyScale.y;
                if (hipWorld > 0.03f * height) { why = "la cadera se desplaza demasiado"; return null; }
                var clip = new AnimationClip { name = "Crupier_Idle", frameRate = Fps };
                foreach (var b in bones)
                {
                    string p = AnimationUtility.CalculateTransformPath(b, inst.transform);
                    var c = rot[b];
                    clip.SetCurve(p, typeof(Transform), "localRotation.x", c[0]);
                    clip.SetCurve(p, typeof(Transform), "localRotation.y", c[1]);
                    clip.SetCurve(p, typeof(Transform), "localRotation.z", c[2]);
                    clip.SetCurve(p, typeof(Transform), "localRotation.w", c[3]);
                }
                string hipPath = AnimationUtility.CalculateTransformPath(hips, inst.transform);
                clip.SetCurve(hipPath, typeof(Transform), "localPosition.x", hip[0]);
                clip.SetCurve(hipPath, typeof(Transform), "localPosition.y", hip[1]);
                clip.SetCurve(hipPath, typeof(Transform), "localPosition.z", hip[2]);
                clip.EnsureQuaternionContinuity();
                var st = AnimationUtility.GetAnimationClipSettings(clip);
                st.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, st);
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
                if (existing) { EditorUtility.CopySerialized(clip, existing); clip = existing; }
                else AssetDatabase.CreateAsset(clip, IdlePath);
                why = IdleLength + " s en bucle, " + bones.Count + " huesos, giro máx. " + worstAngle.ToString("0.0") + "°, cadera ±" + (hipWorld * 100f).ToString("0.0") + " cm";
                return clip;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // Ángulo (alrededor de fwd) que lleva el brazo de horizontal a colgar hacia abajo (unos 75°)
        static float ArmDown(Transform arm, Transform fore, Vector3 fwd)
        {
            if (!arm || !fore) return 0f;
            var dir = (fore.position - arm.position).normalized;
            var target = Vector3.Slerp(dir, Vector3.down, 0.82f).normalized;
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(dir, fwd), Vector3.ProjectOnPlane(target, fwd), fwd);
        }

        static void Turn(Transform t, Vector3 axis, float degrees)
        {
            if (t) t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
        }
    }
}
