using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    public enum TextStyle { Dmg, Env, Label, Big, Small, Alert, Hurt, Heal, Pickup }

    // Interfaz (UIManager.js) con uGUI + TextMeshPro, construida por código.
    // Solo escribe en los componentes cuando un valor cambia.
    public sealed partial class Hud
    {
        public const float Px = 1.3f;   // px del CSS original → unidades del lienzo (referencia 1920×1080)
        public static readonly Color Gold = MathUtil.Hex(0xc9a227);
        public static readonly Color Ink = MathUtil.Hex(0xf3e6c8);
        public static readonly Color TitleColor = MathUtil.Hex(0xffcf6a);
        public static readonly Color PanelBorder = MathUtil.Hex(0x6b5132);

        readonly Game g;
        public readonly Canvas Canvas;
        public readonly RectTransform Root;
        readonly RectTransform worldLayer, screenLayer, topLayer;

        sealed class Floater
        {
            public TextMeshProUGUI T;
            public RectTransform R;
            public float X, Y, Z, Age, Life;
            public bool Active;
        }

        readonly List<Floater> floaters = new List<Floater>();
        TextMeshProUGUI toastText; Image toastBg; float toastT;
        TextMeshProUGUI bannerTitle, bannerSub; float bannerT;
        TextMeshProUGUI floorBadge; string floorShown;
        Image fadeImg; float fadeTarget, fadeValue;
        Image hurtImg; float hurtT;
        Image vignette;

        public Hud(Game game, Transform parent)
        {
            g = game;
            var go = new GameObject("Interfaz", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)go.transform;
            // Clics del ratón en los botones (con el Input System)
            if (!UnityEngine.EventSystems.EventSystem.current)
            {
                var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                es.transform.SetParent(parent, false);
            }

            vignette = MakeImage(Root, "Vineta", new Color(0, 0, 0, 0f));
            Stretch(vignette.rectTransform);
            vignette.sprite = VignetteSprite();
            vignette.color = new Color(0, 0, 0, 0.55f);
            vignette.raycastTarget = false;

            worldLayer = MakeLayer("Mundo");
            screenLayer = MakeLayer("Pantalla");

            hurtImg = MakeImage(screenLayer, "DanoRojo", new Color(0.7f, 0f, 0f, 0f));
            Stretch(hurtImg.rectTransform);
            hurtImg.sprite = vignette.sprite;
            hurtImg.raycastTarget = false;

            // Distintivo de piso (arriba a la izquierda del centro)
            floorBadge = MakeText(screenLayer, "Piso", "", 22, Fonts.Serif, Gold);
            Anchor(floorBadge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -24 * Px), new Vector2(400, 40));
            floorBadge.fontStyle = FontStyles.Bold;

            // Aviso (toast) arriba
            toastBg = MakeImage(screenLayer, "Aviso", new Color(0.1f, 0.07f, 0.05f, 0.85f));
            Anchor(toastBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -78 * Px), new Vector2(900, 56));
            toastText = MakeText(toastBg.rectTransform, "Texto", "", 18, Fonts.Body, Ink);
            Stretch(toastText.rectTransform, 16);
            toastText.textWrappingMode = TextWrappingModes.Normal;
            toastBg.gameObject.SetActive(false);

            // Banner de sala (serifa grande con subtítulo)
            bannerTitle = MakeText(screenLayer, "Banner", "", 54, Fonts.Serif, MathUtil.Hex(0xffe2a0));
            Anchor(bannerTitle.rectTransform, new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(1400, 90));
            bannerTitle.fontStyle = FontStyles.Bold;
            Outline(bannerTitle, MathUtil.Hex(0x3a1a08), 0.25f);
            bannerSub = MakeText(screenLayer, "Subtitulo", "", 24, Fonts.Serif, Ink);
            Anchor(bannerSub.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(0, -62), new Vector2(1400, 50));
            bannerSub.fontStyle = FontStyles.Italic;
            bannerTitle.gameObject.SetActive(false);
            bannerSub.gameObject.SetActive(false);

            topLayer = MakeLayer("Encima");
            fadeImg = MakeImage(topLayer, "Fundido", new Color(0, 0, 0, 0));
            Stretch(fadeImg.rectTransform);
            fadeImg.raycastTarget = false;

            InitExtra();
        }

        partial void InitExtra();

        // ---------- Construcción ----------
        RectTransform MakeLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(Root, false);
            Stretch(r);
            return r;
        }

        public static Image MakeImage(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI MakeText(Transform parent, string name, string text, float sizePx, TMP_FontAsset font, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.text = text;
            t.fontSize = sizePx * Px;
            t.color = c;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        // Contorno de texto con un material compartido por (fuente, color, grosor): sin una instancia de
        // material por texto y válido también en objetos que aún no se han activado
        static readonly Dictionary<string, Material> outlineMats = new Dictionary<string, Material>();

        public static void Outline(TMP_Text t, Color c, float width)
        {
            var font = t.font;
            if (!font || !font.material) return;
            string key = font.GetHashCode() + "|" + ColorUtility.ToHtmlStringRGBA(c) + "|" + Mathf.RoundToInt(width * 1000f);
            Material m;
            if (!outlineMats.TryGetValue(key, out m) || !m)
            {
                m = new Material(font.material) { name = font.name + " contorno " + key };
                m.SetColor(ShaderUtilities.ID_OutlineColor, c);
                m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
                m.EnableKeyword(ShaderUtilities.Keyword_Outline);
                outlineMats[key] = m;
            }
            t.fontSharedMaterial = m;
        }

        public static void Stretch(RectTransform r, float inset = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
        }

        public static void Anchor(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            r.anchorMin = anchor; r.anchorMax = anchor;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        // Marco de 9 cortes (borde opaco de 2 px y centro transparente) para bordes de paneles
        static Sprite frameSprite;
        public static Sprite FrameSprite()
        {
            if (frameSprite) return frameSprite;
            const int s = 8;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "marco" };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = (x < 2 || y < 2 || x >= s - 2 || y >= s - 2) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px); tex.Apply();
            frameSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
            return frameSprite;
        }

        // Convierte una imagen en marco: el borde mide "width" unidades del lienzo
        public static void AsFrame(Image img, float width)
        {
            img.sprite = FrameSprite();
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            // El corte mide 3 px del sprite y 2 de ellos son opacos
            img.pixelsPerUnitMultiplier = 2f / width;
        }

        // Cambia el pivote al centro sin mover el rectángulo (para escalar desde el centro, como el CSS)
        public static void CenterPivot(RectTransform r)
        {
            var size = r.rect.size;
            var d = new Vector2(0.5f, 0.5f) - r.pivot;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition += new Vector2(d.x * size.x, d.y * size.y);
        }

        static Sprite vignetteSprite;
        static Sprite VignetteSprite()
        {
            if (vignetteSprite) return vignetteSprite;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "vineta" };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.8f);
                    float a = Mathf.Clamp01((d - 0.55f) / 0.6f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            vignetteSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return vignetteSprite;
        }

        // ---------- Textos flotantes en el mundo ----------
        static void StyleOf(TextStyle st, out Color c, out float px, out bool italic, out float life)
        {
            italic = false; life = 0.8f;
            switch (st)
            {
                case TextStyle.Env: c = MathUtil.Hex(0xff9a2a); px = 26; break;
                case TextStyle.Label: c = MathUtil.Hex(0xffe14a); px = 24; italic = true; life = 1.0f; break;
                case TextStyle.Big: c = MathUtil.Hex(0xff6a2a); px = 30; italic = true; life = 1.0f; break;
                case TextStyle.Small: c = MathUtil.Hex(0xe8e0d0); px = 15; life = 1.2f; break;
                case TextStyle.Alert: c = MathUtil.Hex(0xff3030); px = 34; break;
                case TextStyle.Hurt: c = MathUtil.Hex(0xff4a4a); px = 24; break;
                case TextStyle.Heal: c = MathUtil.Hex(0x7dff8a); px = 22; break;
                case TextStyle.Pickup: c = MathUtil.Hex(0xffd36a); px = 22; break;
                default: c = Color.white; px = 22; break;
            }
        }

        public void FloatText(float x, float y, float z, string text, TextStyle style = TextStyle.Dmg)
        {
            Floater f = null;
            for (int i = 0; i < floaters.Count; i++) if (!floaters[i].Active) { f = floaters[i]; break; }
            if (f == null)
            {
                if (floaters.Count >= 60) { f = floaters[0]; }
                else
                {
                    var t = MakeText(worldLayer, "Flotante", "", 22, Fonts.Body, Color.white);
                    t.fontStyle = FontStyles.Bold;
                    Outline(t, new Color(0, 0, 0, 0.9f), 0.22f);
                    t.rectTransform.sizeDelta = new Vector2(600, 60);
                    f = new Floater { T = t, R = t.rectTransform };
                    floaters.Add(f);
                }
            }
            Color c; float px; bool italic; float life;
            StyleOf(style, out c, out px, out italic, out life);
            f.T.text = text;
            f.T.color = c;
            f.T.fontSize = px * Px;
            f.T.fontStyle = italic ? (FontStyles.Bold | FontStyles.Italic) : FontStyles.Bold;
            f.X = x + (g.FxRand.Next() - 0.5f) * 0.4f; f.Y = y; f.Z = z + (g.FxRand.Next() - 0.5f) * 0.4f;
            f.Age = 0f; f.Life = life;
            f.Active = true;
            f.T.gameObject.SetActive(true);
            f.R.SetAsLastSibling();
            PlaceAt(f.R, new Vector3(x, y, z));
        }

        public int ActiveFloatTexts
        {
            get { int n = 0; foreach (var f in floaters) if (f.Active) n++; return n; }
        }

        // Último texto flotante mostrado que contiene "fragment" (pruebas)
        public bool HasFloatText(string fragment)
        {
            foreach (var f in floaters) if (f.Active && f.T.text.Contains(fragment)) return true;
            return false;
        }

        public void ClearFloats()
        {
            foreach (var f in floaters) { f.Active = false; f.T.gameObject.SetActive(false); }
        }

        // Proyecta un punto del mundo al lienzo; false si queda detrás de la cámara
        public bool PlaceAt(RectTransform r, Vector3 world)
        {
            var cam = g.CameraCtl != null ? g.CameraCtl.Cam : null;
            if (!cam) return false;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0) { r.anchoredPosition = new Vector2(-10000, -10000); return false; }
            Vector2 local;
            var uiCam = Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, sp, uiCam, out local);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = local;
            return true;
        }

        // ---------- Avisos, banner y piso ----------
        public string ToastText { get { return toastBg.gameObject.activeSelf ? toastText.text : null; } }
        public string BannerText { get { return bannerTitle.gameObject.activeSelf ? bannerTitle.text : null; } }

        public void Toast(string text, float seconds = 3f)
        {
            if (toastText.text != text) toastText.text = text;
            toastT = seconds;
            toastBg.gameObject.SetActive(true);
            toastText.ForceMeshUpdate();
            var pref = toastText.GetPreferredValues(text, 1400f, 0f);
            toastBg.rectTransform.sizeDelta = new Vector2(Mathf.Min(1440f, pref.x + 48f), Mathf.Max(56f, pref.y + 26f));
        }

        public void ClearToast() { toastT = 0f; toastBg.gameObject.SetActive(false); }

        public void Banner(string title, string sub = "")
        {
            bannerTitle.text = title;
            bannerSub.text = sub ?? "";
            bannerT = 2.6f;
            bannerTitle.gameObject.SetActive(true);
            bannerSub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
        }

        // Distintivo "PISO n" (n puede ser BONUS, TIENDA o JEFE, con su color)
        public string FloorShown { get { return floorShown; } }
        public void SetFloor(string n)
        {
            if (floorShown == n) return;
            floorShown = n;
            int border = 0x6b5132, label = 0xb39a72;
            if (n == "BONUS") { border = 0xc9a227; label = 0xffd36a; }
            else if (n == "TIENDA") { border = 0x7dd65b; label = 0x9fd36a; }
            else if (n == "JEFE") { border = 0xe2533b; label = 0xff9a6a; }
            // 13 y 20 px del CSS a la escala del lienzo (×1.3)
            floorBadge.text = "<size=17><color=#" + label.ToString("x6") + "><cspace=0.3em>PISO</cspace></color></size> <size=26><color=#ffe2a0>" + n + "</color></size>";
            if (floorBox)
            {
                floorBox.color = MathUtil.Hex(border);
                var pref = floorBadge.GetPreferredValues(floorBadge.text);
                floorBox.rectTransform.sizeDelta = new Vector2(pref.x + 32 * Px, pref.y + 10 * Px);
            }
        }

        public void Fade(bool on) { fadeTarget = on ? 1f : 0f; }
        public float FadeValue { get { return fadeValue; } }

        public void DamageFlash() { hurtT = 0.25f; }

        // ---------- Actualización (tiempo de juego para lo que es del mundo) ----------
        public void Update(float dt)
        {
            for (int i = 0; i < floaters.Count; i++)
            {
                var f = floaters[i];
                if (!f.Active) continue;
                f.Age += dt;
                if (f.Age >= f.Life) { f.Active = false; f.T.gameObject.SetActive(false); continue; }
                float k = f.Age / f.Life;
                PlaceAt(f.R, new Vector3(f.X, f.Y + f.Age * 1.4f, f.Z));
                float pop = f.Age < 0.1f ? 0.6f + f.Age * 6f : 1.2f - Mathf.Min(0.2f, (f.Age - 0.1f) * 0.6f);
                f.R.localScale = Vector3.one * pop;
                var c = f.T.color;
                c.a = k > 0.7f ? 1f - (k - 0.7f) / 0.3f : 1f;
                f.T.color = c;
            }
            if (toastT > 0f)
            {
                toastT -= dt;
                if (toastT <= 0f) toastBg.gameObject.SetActive(false);
            }
            if (bannerT > 0f)
            {
                bannerT -= dt;
                float a = bannerT > 2.2f ? (2.6f - bannerT) / 0.4f : bannerT < 0.6f ? bannerT / 0.6f : 1f;
                SetAlpha(bannerTitle, a); SetAlpha(bannerSub, a);
                if (bannerT <= 0f) { bannerTitle.gameObject.SetActive(false); bannerSub.gameObject.SetActive(false); }
            }
            if (hurtT > 0f)
            {
                hurtT -= dt;
                var c = hurtImg.color; c.a = Mathf.Max(0f, hurtT / 0.25f) * 0.55f; hurtImg.color = c;
            }
            UpdateExtra(dt);
        }

        partial void UpdateExtra(float dt);

        // Fundido a negro en tiempo real (0.55 s de transición visual)
        public void RealTimeUpdate(float unscaledDt)
        {
            if (fadeValue != fadeTarget)
            {
                fadeValue = Mathf.MoveTowards(fadeValue, fadeTarget, unscaledDt / 0.55f);
                var c = fadeImg.color; c.a = fadeValue; fadeImg.color = c;
            }
        }

        public void SetFadeInstant(float v) { fadeValue = fadeTarget = v; var c = fadeImg.color; c.a = v; fadeImg.color = c; }

        static void SetAlpha(TMP_Text t, float a)
        {
            var c = t.color;
            if (Mathf.Abs(c.a - a) < 0.004f) return;
            c.a = a; t.color = c;
        }
    }
}
