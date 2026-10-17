using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Pantallas a toda ventana (título, pausa, derrota, victoria...) con el estilo de taberna del original:
    // tarjeta marrón muy oscura con borde #6b5132, acento oro y títulos en serifa
    public sealed partial class Hud
    {
        public static readonly Color PanelBg = new Color(0.10f, 0.07f, 0.05f, 0.95f);
        static readonly Color TipColor = MathUtil.Hex(0xc9b48d);
        RectTransform screensLayer;
        RectTransform titleScreen, pauseScreen, deathScreen, winScreen;
        TextMeshProUGUI deathText, winStats;
        public string ShownScreen { get; private set; }

        RectTransform ScreensLayer()
        {
            if (screensLayer) return screensLayer;
            var go = new GameObject("Pantallas", typeof(RectTransform));
            screensLayer = (RectTransform)go.transform;
            screensLayer.SetParent(Root, false);
            Stretch(screensLayer);
            screensLayer.SetAsLastSibling();
            return screensLayer;
        }

        // Tarjeta centrada con borde; devuelve el contenedor de contenido
        public RectTransform Panel(Transform parent, string name, Vector2 size)
        {
            var border = MakeImage(parent, name, PanelBorder);
            AsFrame(border, 3f * Px);
            Anchor(border.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var bg = MakeImage(border.rectTransform, "Fondo", PanelBg);
            Stretch(bg.rectTransform, 3f * Px);
            return bg.rectTransform;
        }

        // Fondo oscuro a toda pantalla (más negro en los bordes, como el degradado radial del CSS)
        public RectTransform Overlay(string name, float dim)
        {
            var o = MakeImage(ScreensLayer(), name, new Color(0.04f, 0.024f, 0.016f, dim * 0.62f));
            Stretch(o.rectTransform);
            o.raycastTarget = true;
            var edge = MakeImage(o.rectTransform, "Bordes", new Color(0, 0, 0, 0.9f));
            edge.sprite = VignetteSprite();
            Stretch(edge.rectTransform);
            // Se construye activa (TextMeshPro necesita despertar sus textos) y quien la usa la oculta después
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

        public TextMeshProUGUI Body(Transform parent, string text, float px, float y, float h, Color? color = null)
        {
            var t = MakeText(parent, "Texto", text, px, Fonts.Body, color ?? Ink);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            var r = t.rectTransform;
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0, -y);
            r.sizeDelta = new Vector2(-60, h);
            return t;
        }

        // Botón dorado del original (#c9a227, texto oscuro, sombra inferior); x es el centro respecto al padre
        public Button MakeButton(Transform parent, string label, float y, System.Action onClick, float x = 0f, float px = 18f)
        {
            var shadow = MakeImage(parent, "Boton", MathUtil.Hex(0x6b5112));
            var r = shadow.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            var face = MakeImage(r, "Cara", Gold);
            face.raycastTarget = true;
            var fr = face.rectTransform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(0, 4 * Px); fr.offsetMax = Vector2.zero;
            var b = face.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = MakeText(fr, "Texto", label, px, Fonts.Body, MathUtil.Hex(0x1a120b));
            t.fontStyle = FontStyles.Bold;
            t.richText = true;
            Stretch(t.rectTransform);
            var pref = t.GetPreferredValues(label);
            r.sizeDelta = new Vector2(pref.x + 52 * Px, pref.y + 24 * Px + 4 * Px);
            r.anchoredPosition = new Vector2(x, -y);
            return b;
        }

        public static TextMeshProUGUI ButtonText(Button b) { return b.GetComponentInChildren<TextMeshProUGUI>(); }

        public void HideScreens()
        {
            if (deathScreen) deathScreen.gameObject.SetActive(false);
            if (winScreen) winScreen.gameObject.SetActive(false);
            if (pauseScreen) pauseScreen.gameObject.SetActive(false);
            HideMoreScreens();
            ShownScreen = null;
        }

        partial void HideMoreScreens();

        // ---------- Título ----------
        public bool TitleShown { get { return titleScreen && titleScreen.gameObject.activeSelf; } }

        public void ShowTitle(bool on)
        {
            if (on && !titleScreen) BuildTitle();
            if (titleScreen) titleScreen.gameObject.SetActive(on);
        }

        void BuildTitle()
        {
            titleScreen = Overlay("Titulo", 0.55f);
            var p = Panel(titleScreen, "Tarjeta", new Vector2(640 * Px, 640 * Px));
            var h1 = Title(p, "DELVEFOLK", 64, 26 * Px);
            h1.characterSpacing = 18f;
            Outline(h1, MathUtil.Hex(0x3a1a08), 0.28f);
            var tag = Body(p, "No tienes las armas adecuadas.\nAsí que usarás <color=#ffcf6a><i>cualquier cosa</i></color> que encuentres.", 18, 118 * Px, 60 * Px);
            tag.alignment = TextAlignmentOptions.Center;
            // Controles de cada jugador
            string J1 =
                "<color=#7dd65b><b><cspace=0.15em>Jugador 1</cspace></b></color>\n" +
                Key("W A S D") + " mover\n" + Key("ESPACIO") + " atacar / comer\n" + Key("E") + " recoger / agarrar\n" +
                Key("Q") + " habilidad especial\n" + Key("SHIFT izq.") + " saltar";
            string J2 =
                "<color=#5bb0ff><b><cspace=0.15em>Jugador 2</cspace></b></color>\n" +
                Key("FLECHAS") + " mover\n" + Key("ENTER") + " atacar / comer\n" + Key("SHIFT der.") + " recoger / agarrar\n" +
                Key("CTRL der.") + " habilidad especial\n" + Key("Num3") + " / " + Key("-") + " saltar";
            var c1 = MakeText(p, "J1", J1, 15, Fonts.Body, Ink);
            var c2 = MakeText(p, "J2", J2, 15, Fonts.Body, Ink);
            foreach (var c in new[] { c1, c2 })
            {
                c.richText = true; c.alignment = TextAlignmentOptions.TopLeft; c.lineSpacing = 22f;
            }
            PlaceIn(c1.rectTransform, 70 * Px, 190 * Px, 250 * Px, 190 * Px);
            PlaceIn(c2.rectTransform, 340 * Px, 190 * Px, 250 * Px, 190 * Px);
            var tip = Body(p,
                "Solo puedes llevar <b>una cosa</b> en la mano: un arma, comida... o a tu compañero, que se puede lanzar contra los monstruos.\n" +
                "Las armas se gastan y se rompen. Para curarte, coge comida y cómetela (si te pegan, se te cae el bocado).\n" +
                "Antes de bajar, cada uno elige una <b>habilidad especial</b> y su aspecto en la sala de preparación.\n" +
                "Si te agarran (un compañero o un Abrazón), <b>machaca botones</b> para soltarte.\n" +
                "Mandos compatibles: el mando 1 controla al J1 y el mando 2 al J2 (A atacar, X interactuar, B saltar, Y habilidad).",
                14, 340 * Px, 200 * Px, TipColor);
            tip.alignment = TextAlignmentOptions.Center;
            tip.lineSpacing = 12f;
            MakeButton(p, "1 jugador <size=70%>(ESPACIO)</size>", 560 * Px, () => g.OpenLobby(1), -130 * Px);
            MakeButton(p, "2 jugadores <size=70%>(ENTER)</size>", 560 * Px, () => g.OpenLobby(2), 130 * Px);
            titleScreen.gameObject.SetActive(false);
        }

        // ---------- Pausa ----------
        public bool PauseShown { get { return pauseScreen && pauseScreen.gameObject.activeSelf; } }

        public void ShowPause(bool on)
        {
            if (on && !pauseScreen)
            {
                pauseScreen = Overlay("Pausa", 0.72f);
                var p = Panel(pauseScreen, "Tarjeta", new Vector2(560 * Px, 250 * Px));
                Title(p, "Pausa", 34, 30 * Px);
                var t = Body(p, "Tómate un respiro. Los slimes no se van a ningún sitio.", 14, 90 * Px, 40 * Px, TipColor);
                t.alignment = TextAlignmentOptions.Center;
                MakeButton(p, "Continuar", 150 * Px, () => g.TogglePause());
            }
            if (pauseScreen) pauseScreen.gameObject.SetActive(on);
        }

        // ---------- Derrota: "Has caído" y reintentar el piso ----------
        public void ShowDeath(string text)
        {
            if (!deathScreen)
            {
                deathScreen = Overlay("Derrota", 0.72f);
                var p = Panel(deathScreen, "Tarjeta", new Vector2(600 * Px, 260 * Px));
                Title(p, "Has caído", 34, 30 * Px);
                deathText = Body(p, "", 14, 90 * Px, 50 * Px, TipColor);
                deathText.alignment = TextAlignmentOptions.Center;
                MakeButton(p, "Reintentar el piso (R)", 160 * Px, () => g.Retry());
            }
            deathText.text = text;
            deathScreen.gameObject.SetActive(true);
            ShownScreen = "death";
        }

        // ---------- Victoria al llegar por primera vez al piso 3 ----------
        public void ShowWin(RunStats s, int gold, int bonusPlayed)
        {
            if (!winScreen)
            {
                winScreen = Overlay("Victoria", 0.72f);
                var p = Panel(winScreen, "Tarjeta", new Vector2(640 * Px, 470 * Px));
                Title(p, "¡Sobreviviste a los dos pisos!", 34, 30 * Px);
                var sub = Body(p, "Con una escoba, una sartén, algún compañero lanzado y muy poca dignidad.", 14, 86 * Px, 40 * Px, TipColor);
                sub.alignment = TextAlignmentOptions.Center;
                winStats = Body(p, "", 16, 140 * Px, 200 * Px);
                winStats.alignment = TextAlignmentOptions.Center;
                winStats.lineSpacing = 30f;
                MakeButton(p, "Seguir descendiendo", 370 * Px, () => g.ContinueDeeper());
            }
            int m = Mathf.FloorToInt(s.Time / 60f), sec = Mathf.FloorToInt(s.Time % 60f);
            const string b0 = "<color=#ffcf6a><b>", b1 = "</b></color>";
            winStats.text =
                "Enemigos derrotados: " + b0 + s.Kills + b1 + "\n" +
                "KOs ambientales: " + b0 + s.EnvKills + b1 + "\n" +
                "Daño hecho por la mazmorra: " + b0 + Mathf.RoundToInt(s.EnvDamage) + b1 + "\n" +
                "Habilidades usadas: " + b0 + s.Abilities + b1 + " · Oro: " + b0 + gold + b1 + "\n" +
                "Desafíos bonus jugados: " + b0 + bonusPlayed + b1 + "\n" +
                "Tiempo: " + b0 + m + ":" + sec.ToString("00") + b1;
            winScreen.gameObject.SetActive(true);
            ShownScreen = "win";
        }

        public void SetGold(int i, int gold) { OnGoldPanel(i, gold); }
        partial void OnGoldPanel(int i, int gold);
    }
}
