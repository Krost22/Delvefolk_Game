using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Delvefolk
{
    // Vista previa 3D de un aventurero en la sala de preparación (CharacterPreview.js): el mismo modelo que
    // en partida, girando despacio, con la demostración del efecto de su habilidad cada 2.4 s. Cada vista es
    // un pequeño escenario lejos del mapa, en su propia capa, con su cámara pintando a una textura.
    public sealed class CharacterPreview
    {
        public const int Layer = 31;
        public static readonly HashSet<Camera> Cameras = new HashSet<Camera>();

        readonly Game g;
        readonly Transform stage, root, ring;
        readonly MeshRenderer ringR;
        readonly Camera cam;
        public readonly RenderTexture Texture;
        public readonly Effects Fx;
        public readonly Light Key;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        Transform model;
        CharacterParts parts;
        string shownKey;
        AbilityDef ability;
        float t, fxT = 1f, cosT;
        public int Demos { get; private set; }

        public CharacterPreview(Game game, int slot, int width, int height)
        {
            g = game;
            stage = new GameObject("VistaPrevia" + (slot + 1)).transform;
            stage.SetParent(game.SceneRoot, false);
            stage.position = new Vector3(slot * 60f, -1000f, 0f);
            stage.gameObject.layer = Layer;
            // Suelo redondo y anillo del color del jugador
            var floor = new MeshBuilder().Add(ProcMesh.Circle(2.2f, 24).RotateX(-Mathf.PI / 2f), Vector3.zero, MathUtil.Hex(0x3a3028)).ToMesh("sueloVista");
            CharacterModel.MeshObject(stage, "Suelo", floor, Mats.Lit, Vector3.zero).layer = Layer;
            var ringMesh = new MeshBuilder().Add(ProcMesh.Ring(0.55f, 0.68f, 24).RotateX(-Mathf.PI / 2f), Vector3.zero, new Color(1, 1, 1, 0.75f)).ToMesh("anilloVista");
            var rg = CharacterModel.MeshObject(stage, "Anillo", ringMesh, Mats.Transparent, new Vector3(0, 0.02f, 0));
            rg.layer = Layer;
            ring = rg.transform;
            ringR = rg.GetComponent<MeshRenderer>();
            root = new GameObject("Raiz").transform;
            root.SetParent(stage, false);
            root.gameObject.layer = Layer;
            Fx = new Effects(stage, new Rng(0xbeefu + (uint)slot), Layer);
            // Luz principal cálida del original (solo se enciende mientras pinta esta cámara)
            var lg = new GameObject("LuzVista");
            lg.layer = Layer;
            lg.transform.SetParent(stage, false);
            lg.transform.localPosition = new Vector3(-3f, 6f, 4f);
            lg.transform.rotation = Quaternion.LookRotation(-lg.transform.localPosition.normalized);
            Key = lg.AddComponent<Light>();
            Key.type = LightType.Directional;
            Key.color = MathUtil.Hex(0xffe0b0);
            Key.intensity = 0.9f * Lighting.DirScale;
            Key.shadows = LightShadows.None;
            Key.enabled = false;
            // Cámara: FOV 30 desde (0, 2.3, 4.8) mirando a (0, 0.95, 0) (cerca: el modelo es lo principal de la ficha)
            Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "vistaPrevia" + (slot + 1), antiAliasing = 4 };
            var cg = new GameObject("CamaraVista");
            cg.transform.SetParent(stage, false);
            cg.transform.localPosition = new Vector3(0f, 2.3f, 4.8f);
            cg.transform.LookAt(stage.TransformPoint(new Vector3(0f, 0.95f, 0f)));
            cam = cg.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.targetTexture = Texture;
            cam.depth = -10;
            var data = cg.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            cam.enabled = false;
            Cameras.Add(cam);
        }

        public bool Active { get { return cam && cam.enabled; } }
        public bool IsCamera(Camera c) { return c == cam; }
        public void SetActive(bool on) { if (cam) cam.enabled = on; }

        // look: aspecto del jugador (color del anillo); cosmetics: selección; abilityId: habilidad a demostrar
        public void Set(PlayerLook look, Dictionary<string, string> cosmetics, string abilityId, string hero = null)
        {
            ability = Abilities.Get(abilityId);
            var c = MathUtil.Hex(look.Ring).linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(c.r, c.g, c.b, 0.75f));
            ringR.SetPropertyBlock(mpb);
            string key = KeyOf(cosmetics) + (hero ?? HeroCatalog.Classic);
            if (key == shownKey) return;
            shownKey = key;
            if (model) Object.Destroy(model.gameObject);
            model = new GameObject("Modelo").transform;
            model.SetParent(root, false);
            parts = CharacterModel.Build(model, cosmetics, hero);
            // Los personajes son más altos que el clásico: la vista previa los encuadra a su altura
            if (parts.Rig != null) model.localScale = Vector3.one * (1.85f / HeroCatalog.Height);
            SetLayer(model, Layer);
            fxT = 0.6f;
        }

        static string KeyOf(Dictionary<string, string> c)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in Cosmetics.Slots) { string v; c.TryGetValue(s, out v); sb.Append(v).Append('|'); }
            return sb.ToString();
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        public string ModelKey { get { return shownKey; } }
        public CharacterParts Parts { get { return parts; } }

        public void Update(float dt)
        {
            if (!model) return;
            t += dt;
            // Gira despacio de un lado a otro (como en el web, visto desde el mismo lado de la pantalla)
            root.localRotation = Quaternion.Euler(0, -(Mathf.Sin(t * 0.6f) * 0.7f + 0.4f) * Mathf.Rad2Deg, 0);
            // Pequeña animación de "idle" para que se vean las piezas en movimiento
            float s = Mathf.Sin(t * 6f);
            model.localPosition = new Vector3(0, Mathf.Abs(s) * 0.04f, 0);
            SetZ(parts.FootL, s * 0.08f); SetZ(parts.FootR, -s * 0.08f);
            parts.ArmPivot.localRotation = ProcMesh.EulerYXZ(0.6f + Mathf.Sin(t * 2f) * 0.1f, -0.6f, 0f);
            if (parts.Cape) parts.Cape.localRotation = Quaternion.Euler((0.25f + Mathf.Sin(t * 3f) * 0.08f) * Mathf.Rad2Deg, 0, 0);
            var fx = parts.Fx;
            if (fx != null)
            {
                cosT -= dt;
                if (cosT <= 0f)
                {
                    cosT = fx.Every;
                    Fx.Burst((Random.value - 0.5f) * 0.6f, fx.Y, (Random.value - 0.5f) * 0.6f,
                        BurstOpts.Of(1, fx.Color, 0.6f, fx.Up, fx.Size).WithGravity(2f).WithLife(0.9f));
                }
            }
            // Demostración del efecto de la habilidad cada pocos segundos
            fxT -= dt;
            if (fxT <= 0f && ability != null && ability.Preview != null)
            {
                fxT = 2.4f;
                Demos++;
                ability.Preview(Fx, t);
            }
            Fx.Update(dt);
        }

        static void SetZ(Transform tr, float z) { if (!tr) return; var p = tr.localPosition; p.z = z; tr.localPosition = p; }

        public void Dispose()
        {
            Cameras.Remove(cam);
            if (stage) Object.Destroy(stage.gameObject);
            if (Texture) { Texture.Release(); Object.Destroy(Texture); }
        }
    }
}
