using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Pantallas a toda ventana (derrota, victoria...) con el estilo de taberna del original:
    // panel marrón muy oscuro translúcido, borde #6b5132, acento oro y títulos en serifa
    public sealed partial class Hud
    {
        public static readonly Color PanelBg = new Color(0.10f, 0.07f, 0.05f, 0.93f);
        RectTransform screensLayer;
        RectTransform deathScreen, winScreen;
        TextMeshProUGUI deathText, winStats;
        public string ShownScreen { get; private set; }

        RectTransform ScreensLayer()
        {
            if (screensLayer) return screensLayer;
            var go = new GameObject("Pantallas", typeof(RectTransform));
            screensLayer = (RectTransform)go.transform;
            screensLayer.SetParent(Root, false);
            Stretch(screensLayer);
            screensLayer.SetSiblingIndex(topLayer.GetSiblingIndex());
            return screensLayer;
        }

        // Panel centrado con borde dorado; devuelve el contenedor de contenido
        public RectTransform Panel(Transform parent, string name, Vector2 size)
        {
            var border = MakeImage(parent, name, PanelBorder);
            Anchor(border.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var bg = MakeImage(border.rectTransform, "Fondo", PanelBg);
            Stretch(bg.rectTransform, 3f);
            return bg.rectTransform;
        }

        RectTransform Overlay(string name, float dim)
        {
            var o = MakeImage(ScreensLayer(), name, new Color(0.02f, 0.016f, 0.03f, dim));
            Stretch(o.rectTransform);
            o.raycastTarget = true;
            o.gameObject.SetActive(false);
            return o.rectTransform;
        }

        public TextMeshProUGUI Title(Transform parent, string text, float px, float y)
        {
            var t = MakeText(parent, "Titulo", text, px, Fonts.Serif, TitleColor);
            t.fontStyle = FontStyles.Bold;
            Outline(t, MathUtil.Hex(0x3a1a08), 0.2f);
            var r = t.rectTransform;
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0, -y);
            r.sizeDelta = new Vector2(0, px * Px * 1.4f);
            return t;
        }

        public TextMeshProUGUI Body(Transform parent, string text, float px, float y, float h)
        {
            var t = MakeText(parent, "Texto", text, px, Fonts.Body, Ink);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            var r = t.rectTransform;
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0, -y);
            r.sizeDelta = new Vector2(-60, h);
            return t;
        }

        // Botón con aspecto de tecla grande; el clic llama a la acción
        public Button MakeButton(Transform parent, string label, float y, System.Action onClick)
        {
            var img = MakeImage(parent, "Boton", MathUtil.Hex(0x2a1f16));
            img.raycastTarget = true;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0, -y);
            r.sizeDelta = new Vector2(440, 64);
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = MathUtil.Hex(0x8a6b42);
            outline.effectDistance = new Vector2(2, -2);
            var b = img.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick());
            var t = MakeText(r, "Texto", label, 20, Fonts.Serif, MathUtil.Hex(0xffe2a0));
            t.fontStyle = FontStyles.Bold;
            Stretch(t.rectTransform);
            return b;
        }

        public void HideScreens()
        {
            if (deathScreen) deathScreen.gameObject.SetActive(false);
            if (winScreen) winScreen.gameObject.SetActive(false);
            HideMoreScreens();
            ShownScreen = null;
        }

        partial void HideMoreScreens();

        // Derrota: "Has caído" y reintentar el piso
        public void ShowDeath(string text)
        {
            if (!deathScreen)
            {
                deathScreen = Overlay("Derrota", 0.72f);
                var p = Panel(deathScreen, "Panel", new Vector2(760, 330));
                Title(p, "Has caído", 46, 30);
                deathText = Body(p, "", 20, 110, 80);
                deathText.alignment = TextAlignmentOptions.Center;
                MakeButton(p, "Reintentar el piso (R)", 220, () => g.Retry());
            }
            deathText.text = text;
            deathScreen.gameObject.SetActive(true);
            ShownScreen = "death";
        }

        // Victoria al llegar por primera vez al piso 3
        public void ShowWin(RunStats s, int gold, int bonusPlayed)
        {
            if (!winScreen)
            {
                winScreen = Overlay("Victoria", 0.72f);
                var p = Panel(winScreen, "Panel", new Vector2(820, 560));
                Title(p, "¡Sobreviviste a los dos pisos!", 40, 30);
                var sub = Body(p, "Con una escoba, una sartén, algún compañero lanzado y muy poca dignidad.", 19, 96, 60);
                sub.alignment = TextAlignmentOptions.Center;
                sub.fontStyle = FontStyles.Italic;
                winStats = Body(p, "", 20, 160, 280);
                winStats.alignment = TextAlignmentOptions.Center;
                MakeButton(p, "Seguir descendiendo", 470, () => g.ContinueDeeper());
            }
            int m = Mathf.FloorToInt(s.Time / 60f), sec = Mathf.FloorToInt(s.Time % 60f);
            winStats.text =
                "Enemigos derrotados: <b>" + s.Kills + "</b>\n" +
                "KOs ambientales: <b>" + s.EnvKills + "</b>\n" +
                "Daño hecho por la mazmorra: <b>" + Mathf.RoundToInt(s.EnvDamage) + "</b>\n" +
                "Habilidades usadas: <b>" + s.Abilities + "</b> · Oro: <b>" + gold + "</b>\n" +
                "Desafíos bonus jugados: <b>" + bonusPlayed + "</b>\n" +
                "Tiempo: <b>" + m + ":" + sec.ToString("00") + "</b>";
            winScreen.gameObject.SetActive(true);
            ShownScreen = "win";
        }

        public void SetGold(int i, int gold) { OnGoldPanel(i, gold); }
        partial void OnGoldPanel(int i, int gold);
    }
}
