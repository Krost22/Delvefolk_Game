using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Delvefolk
{
    // Viñetas ilustradas de los pisos bonus (BonusIllustrations.js): pequeñas escenas 3D montadas con los
    // modelos del juego (los aventureros con el aspecto que eligió cada jugador, cofres, minas, slimes...) que
    // se renderizan UNA vez a textura con una cámara fuera de pantalla y se guardan en caché.
    // En los pies de foto, {E} {A} {J} se sustituyen por las teclas de cada jugador.
    public static class Illustrator
    {
        public const int W = 480, H = 340;   // se muestra a la mitad: nítido en pantallas de alta densidad
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        public static int Rendered { get; private set; }

        public static Texture2D Render(string key, Action<IllustrationKit> build, List<Dictionary<string, string>> looks)
        {
            Texture2D tex;
            if (cache.TryGetValue(key, out tex) && tex) return tex;
            var stage = new GameObject("Vineta").transform;
            stage.position = new Vector3(0f, -2000f, 0f);
            var camGo = new GameObject("CamaraVineta");
            camGo.transform.SetParent(stage, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << CharacterPreview.Layer;
            cam.enabled = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            // Sol cálido de la viñeta (solo mientras se pinta)
            var sunGo = new GameObject("SolVineta");
            sunGo.transform.SetParent(stage, false);
            sunGo.transform.localPosition = new Vector3(4f, 8f, -3f);
            sunGo.transform.rotation = Quaternion.LookRotation(-sunGo.transform.localPosition.normalized);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = MathUtil.Hex(0xffe6c0);
            sun.intensity = 0.9f * Lighting.DirScale;
            sun.shadows = LightShadows.None;
            var kit = new IllustrationKit(stage, cam, looks);
            build(kit);
            kit.Frame();
            SetLayer(stage, CharacterPreview.Layer);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "vineta" };
            // Luz y ambiente propios: la luna del mapa se apaga un instante
            var lights = UnityEngine.Object.FindObjectsByType<Light>();
            var turnedOff = new List<Light>();
            foreach (var l in lights) if (l != sun && l.enabled && l.type == LightType.Directional) { l.enabled = false; turnedOff.Add(l); }
            Mats.SetAmbient(MathUtil.Hex(0x9a90b0), 0.75f, MathUtil.Hex(0x8090b0), MathUtil.Hex(0x2a1d14), 0.35f);
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try
            {
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                // La primera viñeta de la sesión sale sin luz (URP aún no tiene preparada la del sol): se pinta dos veces
                int passes = Rendered == 0 ? 2 : 1;
                for (int pass = 0; pass < passes; pass++)
                {
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "vineta_" + key, wrapMode = TextureWrapMode.Clamp };
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
            }
            finally
            {
                RenderSettings.fog = fog;
                foreach (var l in turnedOff) if (l) l.enabled = true;
                Lighting.ApplyAmbient();
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                // Destroy espera al final del frame: se apaga ya para que no salga en la siguiente viñeta
                stage.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(stage.gameObject);
            }
            Rendered++;
            cache[key] = tex;
            return tex;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }

    // Kit de piezas para montar viñetas (DF.IllustrationKit). Coordenadas en casillas de 1.6 u como en el web
    // (x, z); se guardan con z invertida como el resto del juego, y los ángulos del web θ pasan a π − θ.
    public sealed class IllustrationKit
    {
        public const float T = 1.6f;
        readonly Transform stage;
        readonly Camera cam;
        readonly List<Dictionary<string, string>> looks;
        float fx, fz, dist = 7.5f;

        public IllustrationKit(Transform stage, Camera cam, List<Dictionary<string, string>> looks)
        {
            this.stage = stage; this.cam = cam; this.looks = looks;
        }

        static float Yaw(float webYaw) { return (Mathf.PI - webYaw) * Mathf.Rad2Deg; }

        Transform At(Transform obj, float x, float z, float y = 0f)
        {
            obj.SetParent(stage, false);
            obj.localPosition = new Vector3(x * T, y, -z * T);
            return obj;
        }

        Transform Obj(string name, Mesh mesh, Material mat)
        {
            return CharacterModel.MeshObject(stage, name, mesh, mat, Vector3.zero).transform;
        }

        static Mesh M(MeshData d, Color c, string name) { return new MeshBuilder().Add(d, Vector3.zero, c).ToMesh(name); }

        // Centro y distancia de la cámara
        public void View(float x, float z, float d = 7.5f) { fx = x * T; fz = -z * T; dist = d; }

        public void Frame()
        {
            float d = dist;
            cam.transform.localPosition = new Vector3(fx + d * 0.62f, d * 0.82f, fz - d * 0.62f);
            cam.transform.LookAt(stage.TransformPoint(new Vector3(fx, 0.5f, fz)));
            FaceLabels();
        }

        public void Floor(float w = 5f, float h = 4f, int color = 0x3d352d)
        {
            var o = Obj("Suelo", M(ProcMesh.Box(w * T, 0.2f, h * T), MathUtil.Hex(color), "sueloVineta"), Mats.Lit);
            o.localPosition = new Vector3(0, -0.1f, 0);
        }

        public Transform Tile(float x, float z, int color, float h = 0.16f, float y = 0f)
        {
            return At(Obj("Losa", M(ProcMesh.Box(T * 0.92f, h, T * 0.92f), MathUtil.Hex(color), "losaVineta"), Mats.Lit), x, z, y + h / 2f - 0.02f);
        }

        // Losa del puente (con grietas y puntos de desgaste opcionales)
        public Transform Slab(float x, float z, int color = 0x8a7d6a, bool cracks = false, int pips = 0, float y = 0f)
        {
            var g = new GameObject("LosaPuente").transform;
            var b = CharacterModel.MeshObject(g, "Losa", M(ProcMesh.Box(T * 0.88f, 0.35f, T * 0.88f), MathUtil.Hex(color), "losaPuenteV"), Mats.Lit, new Vector3(0, -0.15f, 0));
            if (cracks)
                for (int k = 0; k < 3; k++)
                {
                    var c = CharacterModel.MeshObject(g, "Grieta", M(ProcMesh.Box(0.06f, 0.03f, 0.95f), MathUtil.Hex(0x111111), "grietaV"), Mats.Unlit, new Vector3((k - 1) * 0.25f, 0.04f, -((k % 2) * 0.2f - 0.1f)));
                    c.transform.localRotation = Quaternion.Euler(0, Yaw(k * 1.1f + 0.3f), 0);
                }
            for (int k = 0; k < pips; k++)
                CharacterModel.MeshObject(g, "Uso", M(ProcMesh.Circle(0.13f, 6).RotateX(-Mathf.PI / 2f), MathUtil.Hex(0xf0d080), "usoV"), Mats.Unlit, new Vector3((k - (pips - 1) / 2f) * 0.38f, 0.04f, -0.45f));
            return At(g, x, z, y);
        }

        // Losa oculta del buscaminas / losa ya destapada con su número
        public Transform Plate(float x, float z) { return Tile(x, z, 0x6d7888, 0.18f); }

        public void Revealed(float x, float z, int n)
        {
            Tile(x, z, 0x2e2822, 0.06f);
            if (n > 0) Number(x, z, n);
        }

        public Transform Number(float x, float z, int n)
        {
            string[] cols = { null, "#5bb0ff", "#7dd65b", "#ff5a4a", "#c080ff" };
            var go = new GameObject("Numero");
            var t = go.AddComponent<TextMeshPro>();
            t.font = Fonts.Body;
            t.text = "<b>" + n + "</b>";
            t.fontSize = 10f;
            t.alignment = TextAlignmentOptions.Center;
            Color c;
            if (!(n < cols.Length && cols[n] != null && ColorUtility.TryParseHtmlString(cols[n], out c))) c = Color.white;
            t.color = c;
            Delvefolk.Hud.Outline(t, Color.black, 0.3f);
            t.rectTransform.sizeDelta = new Vector2(1.1f, 1.1f);
            go.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            return At(go.transform, x, z, 0.06f);
        }

        public Transform Cursor(float x, float z, int color = 0x7dd65b)
        {
            var o = Obj("Cursor", M(ProcMesh.Ring(1.0f, 1.2f, 4, 1, Mathf.PI / 4f).RotateX(-Mathf.PI / 2f), MathUtil.Hex(color), "cursorV"), Mats.UnlitDouble);
            o.localScale = Vector3.one * (T / 2f);
            return At(o, x, z, 0.22f);
        }

        public Transform Mine(float x, float z, bool ghost = false)
        {
            var m = BonusProps.Mine(stage);
            if (ghost)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector(Mats.BaseColorId, new Vector4(1, 1, 1, 0.5f));
                foreach (var r in m.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.sharedMaterial == Mats.Lit) r.sharedMaterial = Mats.TransparentLit;
                    r.SetPropertyBlock(mpb);
                }
            }
            return At(m, x, z, ghost ? 0.2f : 0.05f);
        }

        public Transform Flag(float x, float z, int color = 0xe2533b) { return At(BonusProps.Flag(stage, color), x, z, 0.16f); }

        public Transform Chest(float x, float z, bool open = false, bool gold = false, float? face = null)
        {
            var c = BonusProps.Chest(stage, gold ? 0xffd040 : 0x8a8f96, gold ? 1.3f : 1f);
            if (open) c.Lid.localRotation = Quaternion.Euler(-1.6f * Mathf.Rad2Deg, 0, 0);
            c.Root.localRotation = Quaternion.Euler(0, Yaw(face ?? Mathf.PI / 4f), 0);
            return At(c.Root, x, z);
        }

        public void Coins(float x, float z)
        {
            var r = new Rng(7u);
            var mesh = M(ProcMesh.Cylinder(0.12f, 0.12f, 0.04f, 8), MathUtil.Hex(0xf2c230), "monedaV");
            for (int k = 0; k < 7; k++)
            {
                var c = Obj("Moneda", mesh, Mats.Lit);
                float rx = (float)r.NextDouble() * 3f, rz = (float)r.NextDouble() * 3f;
                c.localRotation = ProcMesh.EulerXYZ(rx, 0f, rz);
                float px = x + ((float)r.NextDouble() - 0.5f) * 0.4f, pz = z + ((float)r.NextDouble() - 0.5f) * 0.4f, py = 0.8f + (float)r.NextDouble() * 0.7f;
                At(c, px, pz, py);
            }
        }

        public Transform Slime(float x, float z, int color = 0x4fd06a, bool mouth = false, float scale = 1f, float? face = null, float y = 0f)
        {
            var g = new GameObject("Slime").transform;
            var b = new MeshBuilder();
            b.Add(ProcMesh.Icosahedron(0.55f, 1), Matrix4x4.TRS(new Vector3(0, 0.45f, 0), Quaternion.identity, new Vector3(1f, 0.78f, 1f)), MathUtil.Hex(color));
            CharacterModel.MeshObject(g, "Cuerpo", b.ToMesh("slimeV"), Mats.Lit, Vector3.zero);
            var eyes = new MeshBuilder();
            foreach (float sx in new[] { -1f, 1f })
            {
                eyes.Add(ProcMesh.Sphere(0.12f, 6, 5), new Vector3(sx * 0.18f, 0.62f, 0.4f), Color.white);
                eyes.Add(ProcMesh.Sphere(0.06f, 5, 4), new Vector3(sx * 0.18f, 0.62f, 0.51f), MathUtil.Hex(0x111111));
            }
            if (mouth)
                eyes.Add(ProcMesh.Sphere(0.2f, 8, 6, 0f, Mathf.PI * 2f, 0f, Mathf.PI / 2f), Matrix4x4.TRS(new Vector3(0, 0.36f, 0.44f), ProcMesh.EulerXYZ(Mathf.PI / 2f + 0.3f, 0, 0), Vector3.one), MathUtil.Hex(0x5a1010));
            CharacterModel.MeshObject(g, "Cara", eyes.ToMesh("caraSlimeV"), Mats.UnlitDouble, Vector3.zero);
            g.localScale = Vector3.one * scale;
            g.localRotation = Quaternion.Euler(0, Yaw(face ?? Mathf.PI / 4f), 0);
            return At(g, x, z, y);
        }

        public Transform Table(float x, float z, bool big = false)
        {
            Transform food;
            var t = FeastChallenge.BuildTable(stage, big, new Rng((uint)MathUtil.RoundJs(x * 31f + z * 7f + 3f)), out food);
            return At(t, x, z);
        }

        public Transform Roller(float x, float z)
        {
            return At(Obj("Rodillo", M(ProcMesh.Cylinder(0.75f, 0.75f, 1.5f, 10).RotateX(Mathf.PI / 2f), MathUtil.Hex(0x7a7266), "rodilloV"), Mats.Lit), x, z, 0.75f);
        }

        // Casilla de peligro (lava, gas, aviso)
        public Transform Hazard(float x, float z, int color, float opacity = 0.55f)
        {
            var c = MathUtil.Hex(color, opacity);
            return At(Obj("Peligro", M(ProcMesh.Plane(T * 0.98f, T * 0.98f).RotateX(-Mathf.PI / 2f), c, "peligroV"), Mats.Transparent), x, z, 0.03f);
        }

        // Marco de zona segura (lado en casillas)
        public Transform Zone(float x, float z, float size = 2f)
        {
            var o = Obj("Zona", M(ProcMesh.Ring(0.9f, 1.05f, 4, 1, Mathf.PI / 4f).RotateX(-Mathf.PI / 2f), MathUtil.Hex(0x7dff8a), "zonaV"), Mats.UnlitDouble);
            o.localScale = Vector3.one * (size * T / 2f * Mathf.Sqrt(2f));
            return At(o, x, z, 0.06f);
        }

        // Flecha plana en el suelo (ángulo del web: 0 = hacia +z)
        public Transform Arrow(float x, float z, float ang, int color = 0xffe14a, float len = 1.2f, float y = 0.08f)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Box(0.2f, 0.04f, len), new Vector3(0, 0, len / 2f), MathUtil.Hex(color));
            b.Add(ProcMesh.Cone(0.32f, 0.5f, 3), Matrix4x4.TRS(new Vector3(0, 0, len + 0.18f), ProcMesh.EulerXYZ(Mathf.PI / 2f, 0, 0), Vector3.one), MathUtil.Hex(color));
            var g = Obj("Flecha", b.ToMesh("flechaV"), Mats.UnlitDouble);
            g.localRotation = Quaternion.Euler(0, Yaw(ang), 0);
            return At(g, x, z, y);
        }

        public void Boom(float x, float z, float y = 0.6f)
        {
            int[] cols = { 0xff8a20, 0xffc040, 0xff5020, 0x555555 };
            var r = new Rng(3u);
            for (int k = 0; k < 16; k++)
            {
                float rad = 0.18f + (float)r.NextDouble() * 0.25f;
                float a = k * 2.4f, d = 0.3f + (float)r.NextDouble() * 0.8f;
                var o = Obj("Estallido", M(ProcMesh.Icosahedron(rad, 0), MathUtil.Hex(cols[k % 4]), "boomV"), Mats.Unlit);
                At(o, x + Mathf.Cos(a) * d / T, z + Mathf.Sin(a) * d / T, y + (float)r.NextDouble() * 1.0f);
            }
        }

        public void Sparkle(float x, float z, float y = 1.2f, int color = 0xfff4a0)
        {
            var mesh = M(ProcMesh.Octahedron(0.09f), MathUtil.Hex(color), "brilloV");
            for (int k = 0; k < 6; k++)
            {
                float a = k * 1.05f;
                At(Obj("Brillo", mesh, Mats.Unlit), x + Mathf.Cos(a) * 0.3f, z + Mathf.Sin(a) * 0.3f, y + (k % 2) * 0.3f);
            }
        }

        // Rótulo con fondo oscuro y borde del color del texto, de cara a la cámara
        public Transform Label(string text, float x, float z, float y = 2.2f, string color = "#ffe2a0")
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(color, out c)) c = MathUtil.Hex(0xffe2a0);
            var root = new GameObject("Rotulo").transform;
            var go = new GameObject("Texto");
            go.transform.SetParent(root, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = Fonts.Body;
            t.text = "<b>" + text + "</b>";
            t.fontSize = 4.2f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = c;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            var pref = t.GetPreferredValues(t.text);
            t.rectTransform.sizeDelta = pref + new Vector2(0.1f, 0.05f);
            var bg = new MeshBuilder();
            bg.Add(ProcMesh.Plane(pref.x + 0.32f, pref.y + 0.16f), new Vector3(0, 0, 0.02f), c);
            bg.Add(ProcMesh.Plane(pref.x + 0.24f, pref.y + 0.08f), new Vector3(0, 0, 0.01f), new Color(20 / 255f, 14 / 255f, 10 / 255f, 1f));
            CharacterModel.MeshObject(root, "Fondo", bg.ToMesh("rotuloV"), Mats.UnlitDouble, Vector3.zero);
            At(root, x, z, y);
            // De cara a la cámara (se orienta al encuadrar)
            pendingLabels.Add(root);
            return root;
        }

        readonly List<Transform> pendingLabels = new List<Transform>();

        void FaceLabels()
        {
            foreach (var l in pendingLabels) if (l) l.rotation = Quaternion.LookRotation(l.position - cam.transform.position);
        }

        public Transform Item(string id, Transform parent)
        {
            var w = WeaponRegistry.Create(id);
            var go = w.BuildDisplayModel(parent);
            return go ? go.transform : null;
        }

        public static float Face(float x, float z, float tx, float tz) { return Mathf.Atan2(tx - x, tz - z); }

        // Aventurero con el aspecto elegido. pose: idle | probe | raise | eat | offer | swing | jump | fall | run | hurt | cheer
        public Transform Hero(int i, float x, float z, string pose = null, string item = null, float? face = null, float y = 0f)
        {
            var look = Player.Looks[i];
            var root = new GameObject("Heroe" + (i + 1)).transform;
            var model = new GameObject("Modelo").transform;
            model.SetParent(root, false);
            var parts = CharacterModel.Build(model, looks != null && i < looks.Count ? looks[i] : Cosmetics.Defaults(i));
            var ring = CharacterModel.MeshObject(root, "Anillo", M(ProcMesh.Ring(0.55f, 0.68f, 24).RotateX(-Mathf.PI / 2f), MathUtil.Hex(look.Ring, 0.85f), "anilloV"), Mats.Transparent, new Vector3(0, 0.03f, 0)).transform;
            if (item != null) Item(item, parts.Holder);
            float[] P;
            switch (pose)
            {
                case "probe": P = new[] { 0.55f, -0.05f, 0.2f, 0.55f }; break;
                case "raise": P = new[] { -2.3f, -0.2f, 0f, 0.1f }; break;
                case "eat": P = new[] { -0.55f, 0.15f, 0.2f, -0.3f }; break;
                case "offer": P = new[] { -0.15f, 0.1f, 0.2f, 0.45f }; break;
                case "swing": P = new[] { 0.35f, 0.9f, 0f, 0.15f }; break;
                default: P = new[] { 0.6f, -0.6f, 0f, 0f }; break;
            }
            parts.ArmPivot.localRotation = ProcMesh.EulerYXZ(P[0], P[1], P[2]);
            var hp = parts.Holder.localPosition; hp.z = 0.45f + P[3]; parts.Holder.localPosition = hp;
            float baseY = y;
            if (pose == "jump") { y += 1.0f; SetZ(parts.FootL, 0.12f); SetZ(parts.FootR, -0.12f); model.localRotation = ProcMesh.EulerXYZ(-0.15f, 0, 0); }
            if (pose == "fall")
            {
                y -= 0.35f;
                model.localRotation = ProcMesh.EulerXYZ(0.45f, 0, 0.3f);
                parts.ArmPivot.localRotation = ProcMesh.EulerYXZ(-2.8f, 0.3f, 0f);
                if (parts.LHand) parts.LHand.localPosition = new Vector3(0.35f, 1.95f, 0.1f);
                ring.gameObject.SetActive(false);
            }
            if (pose == "run") { SetZ(parts.FootL, 0.22f); SetZ(parts.FootR, -0.22f); model.localRotation = ProcMesh.EulerXYZ(0.18f, 0, 0); }
            if (pose == "hurt") { model.localRotation = ProcMesh.EulerXYZ(0, 0, 0.35f); if (parts.Stars) parts.Stars.gameObject.SetActive(true); }
            if (pose == "cheer")
            {
                y += 0.25f;
                parts.ArmPivot.localRotation = ProcMesh.EulerYXZ(-2.6f, 0f, 0f);
                if (parts.LHand) parts.LHand.localPosition = new Vector3(0.35f, 1.9f, 0f);
            }
            var sh = Entity.BlobShadow(root, 0.5f);
            sh.localPosition = new Vector3(0, 0.02f - (y - baseY), 0);
            ring.localPosition = new Vector3(0, 0.03f - (y - baseY), 0);
            root.localRotation = Quaternion.Euler(0, Yaw(face ?? Mathf.PI / 4f), 0);
            return At(root, x, z, y);
        }

        static void SetZ(Transform t, float z) { if (!t) return; var p = t.localPosition; p.z = z; t.localPosition = p; }
    }

    // Viñetas de cada desafío (DF.BonusArt)
    public static class BonusArt
    {
        public sealed class PanelDef { public Func<string> Cap; public string Mark; public Action<IllustrationKit> Build; }
        public sealed class Def { public Func<string[]> Goals; public PanelDef[] Panels; }   // Goals: { victoria, derrota, recompensa }
        public struct Panel { public Texture2D Tex; public string Cap, Mark; }

        public static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>();
        public static Def Get(string id) { Def d; return id != null && Defs.TryGetValue(id, out d) ? d : null; }

        static PanelDef P(string cap, string mark, Action<IllustrationKit> build) { return new PanelDef { Cap = () => cap, Mark = mark, Build = build }; }
        static PanelDef P(Func<string> cap, string mark, Action<IllustrationKit> build) { return new PanelDef { Cap = cap, Mark = mark, Build = build }; }

        // Viñetas con el aspecto actual de los jugadores (null si ese desafío no tiene)
        public static List<Panel> Panels(string id, List<Dictionary<string, string>> looks)
        {
            var def = Get(id);
            if (def == null) return null;
            var sb = new System.Text.StringBuilder();
            if (looks != null)
                foreach (var l in looks) foreach (var s in Cosmetics.Slots) { string v; l.TryGetValue(s, out v); sb.Append(v).Append(','); }
            string lk = sb.ToString();
            var outp = new List<Panel>();
            for (int k = 0; k < def.Panels.Length; k++)
            {
                var p = def.Panels[k];
                outp.Add(new Panel { Tex = Illustrator.Render(id + ":" + k + ":" + lk, p.Build, looks), Cap = p.Cap(), Mark = p.Mark });
            }
            return outp;
        }

        const float PI = Mathf.PI;

        static BonusArt()
        {
            Defs["minefield"] = new Def
            {
                Goals = () => new[] { "Destapa todas las losas seguras", BonusConfig.Minefield.Strikes + " minas = derrota", "Botín para quien siga en pie" },
                Panels = new[]
                {
                    P("El número = minas que tiene alrededor", null, S =>
                    {
                        S.Floor(4, 4);
                        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) if (x != 0 || z != 0) S.Plate(x, z);
                        S.Revealed(0, 0, 2);
                        S.Mine(1, -1, true); S.Mine(-1, 1, true);
                        S.View(0, 0, 6.2f);
                    }),
                    P("Mira una losa y pulsa {E}: sondear", "ok", S =>
                    {
                        S.Floor(4, 4);
                        S.Revealed(0, 1, 1); S.Revealed(1, 1, 1); S.Revealed(-1, 1, 0);
                        S.Plate(0, 0); S.Plate(-1, 0); S.Plate(1, 0); S.Plate(0, -1);
                        S.Cursor(0, 0);
                        S.Hero(0, 0, 1, "probe", "stick", PI);
                        S.View(0, 0.4f, 6.5f);
                    }),
                    P("Pulsa {A}: bandera donde haya mina", "ok", S =>
                    {
                        S.Floor(4, 4);
                        S.Revealed(0, 1, 1); S.Revealed(-1, 1, 1); S.Revealed(1, 1, 0);
                        S.Plate(0, 0); S.Plate(-1, 0); S.Plate(1, 0); S.Plate(0, -1);
                        S.Flag(0, 0);
                        S.Hero(0, 0, 1, "raise", null, PI);
                        S.View(0, 0.4f, 6.5f);
                    }),
                    P("¡Pisar una mina te elimina!", "bad", S =>
                    {
                        S.Floor(4, 4);
                        S.Plate(-1, 0); S.Plate(1, 0); S.Plate(0, -1); S.Plate(-1, -1); S.Revealed(0, 0, 0);
                        S.Boom(0, 0, 0.2f);
                        S.Hero(0, 0, 0, "hurt", null, null, 0.9f);
                        S.View(0, 0, 6.2f);
                    }),
                },
            };
            Defs["bridge"] = new Def
            {
                Goals = () => new[] { "Llega a la otra orilla", "Nadie cruza en " + BonusConfig.Bridge.TimeLimit + " s", "Botín al cruzar (+1 al primero)" },
                Panels = new[]
                {
                    P("Cruza de losa en losa", "ok", S =>
                    {
                        S.Floor(4, 4, 0x0c0614);
                        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) S.Slab(x, z);
                        S.Hero(0, 0, 0.9f, "run", null, PI);
                        S.Arrow(0, 0.3f, PI, 0xffe14a, 1.3f);
                        S.View(0, 0, 6.5f);
                    }),
                    P("Si cruje... ¡atrás, rápido!", "warn", S =>
                    {
                        S.Floor(4, 4, 0x0c0614);
                        S.Slab(0, 1); S.Slab(0, -1); S.Slab(-1, 0); S.Slab(1, 0); S.Slab(-1, 1); S.Slab(1, 1);
                        S.Slab(0, 0, 0x8a7d6a, true, 0, -0.12f);
                        S.Hero(0, 0, 0.2f, "run", null, 0f);
                        S.Arrow(0, 0.35f, 0f, 0x7dff8a, 0.8f, 0.25f);
                        S.Label("*crac*", 0, -0.3f, 1.0f, "#ff9a2a");
                        S.View(0, 0.2f, 6.2f);
                    }),
                    P("Las del centro aguantan a 1; las de los lados, a 2", null, S =>
                    {
                        S.Floor(4, 3, 0x0c0614);
                        S.Slab(0, 0, 0x8a7d6a, false, 1); S.Slab(-1.15f, 0, 0x8a7d6a, false, 2); S.Slab(1.15f, 0, 0x8a7d6a, false, 2);
                        S.Label("×1", 0, 0, 1.0f, "#ffd36a"); S.Label("×2", -1.15f, 0, 1.0f, "#7dff8a"); S.Label("×2", 1.15f, 0, 1.0f, "#7dff8a");
                        S.View(0, 0, 6.2f);
                    }),
                    P("Caer al foso: fuera de la ronda", "bad", S =>
                    {
                        S.Floor(4, 4, 0x0c0614);
                        S.Slab(-1, 0); S.Slab(1, 0); S.Slab(0, 1); S.Slab(-1, 1); S.Slab(1, -1);
                        S.Hero(0, 0, 0, "fall", null, PI / 4f + 0.5f);
                        S.Label("¡AAAH!", 0, 0, 1.4f, "#ff6a5a");
                        S.View(0, 0.3f, 6.2f);
                    }),
                },
            };
            Defs["loot"] = new Def
            {
                Goals = () => new[] { "Sal con vida", "Que caigáis todos", "Lo que saques es tuyo" },
                Panels = new[]
                {
                    P(() => "Mantén {E}: abrir (" + BonusConfig.Loot.MaxOpensPerPlayer + " cofres cada uno)", "ok", S =>
                    {
                        S.Floor(4, 3);
                        S.Chest(0, 0, true, false, PI / 2f);
                        S.Sparkle(0, 0, 1.0f);
                        S.Hero(0, 0.95f, 0, "offer", null, -PI / 2f);
                        S.View(0.4f, 0, 5.6f);
                    }),
                    P("Mantén {A}: examinar. La pista la ven todos", "ok", S =>
                    {
                        S.Floor(4, 3);
                        S.Chest(0, 0, false, false, PI / 2f);
                        S.Label("⏰ Tic-tac", 0, 0, 1.6f);
                        S.Hero(0, 0.95f, 0, null, null, -PI / 2f);
                        S.View(0.4f, 0, 5.6f);
                    }),
                    P("Dentro: botín... o una sorpresa", "warn", S =>
                    {
                        S.Floor(5, 3);
                        S.Chest(-0.8f, 0, true); S.Coins(-0.8f, 0);
                        S.Chest(0.9f, 0, true); S.Slime(0.9f, 0, 0x4fd06a, true, 0.75f, null, 0.55f);
                        S.View(0, 0, 6.2f);
                    }),
                    P("Cofre dorado: premio doble... o desastre", "warn", S =>
                    {
                        S.Floor(4, 3);
                        S.Chest(0, 0, false, true);
                        S.Label("¿?", 0, 0, 2.0f, "#ffd040");
                        S.Sparkle(0, 0, 1.5f, 0xffd040);
                        S.View(0, 0, 5.4f);
                    }),
                },
            };
            Defs["arena"] = new Def
            {
                Goals = () => new[] { "Aguanta " + BonusConfig.Arena.Duration + " s", "Que caigáis todos", "Más tiempo en pie = más botín" },
                Panels = new[]
                {
                    P("Aguanta las oleadas con lo que pilles", "ok", S =>
                    {
                        S.Floor(5, 4);
                        S.Hero(0, -0.5f, 0.3f, "swing", "pan", IllustrationKit.Face(-0.5f, 0.3f, 0.8f, -0.5f));
                        S.Slime(0.8f, -0.5f); S.Slime(1.1f, 0.35f, 0x4fd06a, false, 0.8f);
                        S.View(0.2f, 0, 6.8f);
                    }),
                    P("El borde se vuelve lava (naranja = aviso)", "warn", S =>
                    {
                        S.Floor(5, 5);
                        for (int x = -2; x <= 2; x++)
                            for (int z = -2; z <= 2; z++)
                            {
                                int r = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                                if (r == 2) S.Hazard(x, z, 0xd0302a, 0.75f); else if (r == 1) S.Hazard(x, z, 0xffa030, 0.45f);
                            }
                        S.Hero(0, 0, 0);
                        S.View(0, 0, 8.2f);
                    }),
                    P("Empuja a los monstruos a la lava", "ok", S =>
                    {
                        S.Floor(5, 3);
                        S.Hazard(1.5f, 0, 0xd0302a, 0.75f); S.Hazard(2.5f, 0, 0xd0302a, 0.75f);
                        S.Hazard(1.5f, 1, 0xd0302a, 0.75f); S.Hazard(1.5f, -1, 0xd0302a, 0.75f);
                        S.Hero(0, -1, 0, "swing", "broom", PI / 2f);
                        S.Slime(1.3f, 0); S.Arrow(0.0f, 0, PI / 2f, 0xffe14a, 0.7f);
                        S.View(0.5f, 0, 6.6f);
                    }),
                },
            };
            Defs["feast"] = new Def
            {
                Goals = () => new[] { "Sal con vida", "Que caigáis todos", "Mejoras para quien come" },
                Panels = new[]
                {
                    P(() => "Mantén {E}: comer (" + BonusConfig.Feast.MaxBitesPerPlayer + " bocados cada uno)", "ok", S =>
                    {
                        S.Floor(4, 3);
                        S.Table(0, 0);
                        S.Hero(0, 0.85f, 0.2f, "eat", "chicken", IllustrationKit.Face(0.85f, 0.2f, 0, 0));
                        S.View(0.3f, 0, 5.4f);
                    }),
                    P("Mantén {A}: olfatear. La pista la ven todos", "ok", S =>
                    {
                        S.Floor(4, 3);
                        S.Table(0, 0);
                        S.Label("✨ Huele bien", 0, 0, 1.6f);
                        S.Hero(0, 0.85f, 0.2f, null, null, IllustrationKit.Face(0.85f, 0.2f, 0, 0));
                        S.View(0.3f, 0, 5.6f);
                    }),
                    P("¡Algunos platos muerden!", "bad", S =>
                    {
                        S.Floor(4, 3);
                        S.Table(0, 0);
                        S.Slime(0, 0, 0xc08040, true, 0.6f, PI / 2f, 0.85f);
                        S.Hero(0, 0.85f, 0, "hurt", null, -PI / 2f);
                        S.View(0.3f, 0, 5.4f);
                    }),
                    P("Festín prohibido: premio para ti, castigo para el resto", "warn", S =>
                    {
                        S.Floor(5, 3);
                        S.Table(0, 0, true);
                        S.Hero(0, -1.15f, 0.3f, "cheer");
                        S.Hero(1, 1.2f, 0.3f, "hurt");
                        S.Label("😋", -1.15f, 0.3f, 2.7f); S.Label("💀", 1.2f, 0.3f, 2.6f, "#ff6a5a");
                        S.View(0, 0, 6.8f);
                    }),
                },
            };
            Defs["breath"] = new Def
            {
                Goals = () => new[] { "Sigue en pie " + BonusConfig.Breath.Duration + " s", "Que caigáis todos", "Botín (+1 al último en pie)" },
                Panels = new[]
                {
                    P("Quédate en lo verde: fuera hay gas", "ok", S =>
                    {
                        S.Floor(5, 4);
                        for (int x = -2; x <= 2; x++)
                            for (float z = -1.5f; z <= 1.5f; z++)
                                if (!(x >= 0 && x <= 1 && z >= -0.5f && z <= 0.5f)) S.Hazard(x, z, 0x60c040, 0.5f);
                        S.Zone(0.5f, 0);
                        S.Hero(0, 0.5f, 0);
                        S.View(0.3f, 0, 7.4f);
                    }),
                    P("¡Naranja! Corre a la zona nueva", "warn", S =>
                    {
                        S.Floor(5, 3);
                        for (int x = -2; x <= 0; x++) for (int z = -1; z <= 1; z++) S.Hazard(x, z, 0xffa030, 0.5f);
                        S.Zone(1.5f, 0, 1.6f);
                        S.Hero(0, -1, 0, "run", null, PI / 2f);
                        S.Arrow(-0.4f, 0, PI / 2f, 0x7dff8a, 1.2f);
                        S.View(0.2f, 0, 7f);
                    }),
                    P("Esquiva el rodillo (¡o sáltalo con {J}!)", "warn", S =>
                    {
                        S.Floor(5, 3);
                        for (int x = -2; x <= 2; x++) S.Hazard(x, 0, 0xff5a4a, 0.25f);
                        S.Roller(-1.3f, 0);
                        S.Arrow(-0.6f, 0, PI / 2f, 0xff5a4a, 0.9f);
                        S.Hero(0, 1, 0, "jump", null, -PI / 2f);
                        S.View(0, 0.2f, 7f);
                    }),
                },
            };
        }
    }
}
