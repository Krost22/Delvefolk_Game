using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Panel de cada jugador (J1 a la izquierda, J2 a la derecha): VIDA, MANO, HAB., EQUIPO, estado y
    // "DERRIBADO"; más el aviso para que se una el J2 y la barra de controles. Solo se escribe lo que cambia.
    public sealed partial class Hud
    {
        sealed class PanelUi
        {
            public RectTransform Root; public Image Border, Bg;
            public TextMeshProUGUI Name, Keys;
            public RectTransform HpRow; public Image HpBg, HpGhost, HpFill; public TextMeshProUGUI HpNum;
            public RectTransform WRow, WBox; public TextMeshProUGUI WIcon, WName, WStyle; public Image DurBg, DurFill; public float WPop;
            public RectTransform ARow; public Image AbRing, AbBg, AbCd; public TextMeshProUGUI AbGlyph, AbName, AbKey;
            public Color AbColor = Gold;
            public RectTransform GRow; public TextMeshProUGUI ArmorIcon, Chips, GoldText; public Image ArmorBg, ArmorFill; public float GoldPop;
            public TextMeshProUGUI Status, Downed;
            public bool Shown = true, IsDowned, Low, AbReady, AbActive, GearEmpty = true, HasArmor;
            public string HpText, WNameText, StatusText = "", GoldShown, ChipsShown;
            public int AbDeg = -1;
            public float LayoutH, ChipT;
        }

        readonly PanelUi[] panels = new PanelUi[ControlSchemes.MaxPlayers];
        RectTransform panelsLayer, hudGroup;
        Image joinHint; TextMeshProUGUI joinText;
        Image controlsBg;
        Image floorBox;
        float pulseT;

        const float PanelW = 290f * Px;
        static readonly Color LabelColor = MathUtil.Hex(0xb39a72);
        static readonly Color KeyGrey = MathUtil.Hex(0x8a7a62);

        partial void InitPanels()
        {
            panelsLayer = MakeLayer("Paneles");
            panelsLayer.SetSiblingIndex(topLayer.GetSiblingIndex());
            for (int i = 0; i < panels.Length; i++) panels[i] = MakePanel(i);
            // Unirse: ENTER o A en un mando (abajo, sobre la barra de controles)
            joinHint = MakeImage(panelsLayer, "UnirseJ2", new Color(10 / 255f, 20 / 255f, 35 / 255f, 0.8f));
            var jr = joinHint.rectTransform;
            jr.anchorMin = jr.anchorMax = jr.pivot = new Vector2(0.5f, 0);
            jr.anchoredPosition = new Vector2(0, 52 * Px);
            var jb = MakeImage(jr, "Borde", MathUtil.Hex(0x5bb0ff));
            Stretch(jb.rectTransform); AsFrame(jb, 2 * Px);
            joinText = MakeText(jr, "Texto", "<b>¿Alguien más?</b> pulsa " + Key("ENTER") + " o " + Key("A") + " en un mando para unirte", 14, Fonts.Body, MathUtil.Hex(0xcfe3ff));
            joinText.richText = true;
            Stretch(joinText.rectTransform, 10);
            var pref = joinText.GetPreferredValues(joinText.text);
            jr.sizeDelta = new Vector2(pref.x + 28, pref.y + 20);
            joinHint.gameObject.SetActive(false);
            // Barra de controles (abajo a la derecha)
            controlsBg = MakeImage(panelsLayer, "Controles", new Color(0, 0, 0, 0.45f));
            var cr = controlsBg.rectTransform;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1, 0);
            cr.anchoredPosition = new Vector2(-16 * Px, 14 * Px);
            const string b0 = "<color=#f3e6c8><b>", b1 = "</b></color>";
            var ct = MakeText(cr, "Texto",
                b0 + "X" + b1 + " / " + b0 + "ENTER" + b1 + " / " + b0 + "A" + b1 + ": atacar · lanzar — " + b0 + "E" + b1 + " / " + b0 + "SHIFT der." + b1 + " / " + b0 + "X" + b1 + "🎮: recoger · agarrar · descender — " +
                b0 + "Z" + b1 + " / " + b0 + "CTRL der." + b1 + " / " + b0 + "Y" + b1 + ": habilidad — " + b0 + "ESPACIO" + b1 + " / " + b0 + "Num3" + b1 + " / " + b0 + "B" + b1 + ": saltar · " +
                b0 + "M" + b1 + " sonido · " + b0 + "Esc" + b1 + " pausa", 12, Fonts.Body, MathUtil.Hex(0xa8957a));
            ct.richText = true;
            Stretch(ct.rectTransform, 8);
            var cp = ct.GetPreferredValues(ct.text);
            cr.sizeDelta = new Vector2(cp.x + 20, cp.y + 12);
            // Distintivo de piso con borde (sustituye al texto simple)
            floorBox = MakeImage(screenLayer, "PisoCaja", MathUtil.Hex(0x6b5132));
            AsFrame(floorBox, 2 * Px);
            var fr = floorBox.rectTransform;
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 1f); fr.pivot = new Vector2(0.5f, 1f);
            fr.anchoredPosition = new Vector2(0, -14 * Px);
            var fbg = MakeImage(fr, "Fondo", new Color(20 / 255f, 14 / 255f, 10 / 255f, 0.92f));
            Stretch(fbg.rectTransform, 2);
            floorBadge.rectTransform.SetParent(fr, false);
            Stretch(floorBadge.rectTransform, 2);
            floorBadge.richText = true;
            floorBadge.fontStyle = FontStyles.Bold;
            floorBox.transform.SetSiblingIndex(0);
        }

        PanelUi MakePanel(int i)
        {
            var u = new PanelUi();
            var look = Player.Looks[i];
            var ring = MathUtil.Hex(look.Ring);
            u.Border = MakeImage(panelsLayer, "Panel" + (i + 1), ring);
            AsFrame(u.Border, 2 * Px);
            u.Root = u.Border.rectTransform;
            // J1 arriba a la izquierda, J2 arriba a la derecha, J3 abajo a la izquierda, J4 abajo a la derecha (sobre los controles)
            bool right = i % 2 == 1, bottom = i >= 2;
            var anchor = new Vector2(right ? 1 : 0, bottom ? 0 : 1);
            u.Root.anchorMin = u.Root.anchorMax = u.Root.pivot = anchor;
            u.Root.anchoredPosition = new Vector2((right ? -16 : 16) * Px, bottom ? (right ? 52 : 16) * Px : -16 * Px);
            u.Bg = MakeImage(u.Root, "Fondo", new Color(24 / 255f, 17 / 255f, 13 / 255f, 0.9f));
            Stretch(u.Bg.rectTransform, 2 * Px);
            var c = u.Bg.rectTransform;
            float pad = 12 * Px, w = PanelW - pad * 2 - 4 * Px;

            // Fila del título: nombre y teclas
            u.Name = MakeText(c, "Nombre", look.Name, 16, Fonts.Body, ring);
            u.Name.fontStyle = FontStyles.Bold; u.Name.characterSpacing = 8f;
            Outline(u.Name, Color.black, 0.2f);
            u.Name.alignment = TextAlignmentOptions.Left;
            u.Keys = MakeText(c, "Teclas", ControlSchemes.Help(ControlSchemes.For(ControlSchemes.DefaultFor(i))), 10, Fonts.Body, KeyGrey);
            u.Keys.alignment = TextAlignmentOptions.Right;

            // VIDA
            u.HpRow = Row(c, "Vida", "VIDA");
            u.HpBg = MakeImage(u.HpRow, "Barra", MathUtil.Hex(0x120808));
            PlaceIn(u.HpBg.rectTransform, 50 * Px, 0, w - 50 * Px, 18 * Px);
            var inner = MakeImage(u.HpBg.rectTransform, "Fondo", MathUtil.Hex(0x2a0f0f));
            Stretch(inner.rectTransform, 2 * Px);
            u.HpGhost = Bar(inner.rectTransform, "Fantasma", MathUtil.Hex(0xf0d080)); Stretch(u.HpGhost.rectTransform);
            u.HpFill = Bar(inner.rectTransform, "Relleno", MathUtil.Hex(0xd83a2c)); Stretch(u.HpFill.rectTransform);
            u.HpNum = MakeText(u.HpBg.rectTransform, "Numero", "", 11, Fonts.Body, Ink);
            u.HpNum.fontStyle = FontStyles.Bold; Outline(u.HpNum, Color.black, 0.25f);
            Stretch(u.HpNum.rectTransform);

            // MANO
            u.WRow = Row(c, "Mano", "MANO");
            u.WBox = NewRect(u.WRow, "Caja");
            PlaceIn(u.WBox, 50 * Px, 0, w - 50 * Px, 44 * Px);
            CenterPivot(u.WBox);   // el "pop" al cambiar de arma escala desde el centro
            u.WIcon = MakeText(u.WBox, "Icono", "", 26, Fonts.Body, Color.white);
            PlaceIn(u.WIcon.rectTransform, 0, 0, 34 * Px, 44 * Px);
            u.WName = MakeText(u.WBox, "Nombre", "", 16, Fonts.Body, MathUtil.Hex(0xffe2a0));
            u.WName.fontStyle = FontStyles.Bold; u.WName.alignment = TextAlignmentOptions.Left; u.WName.richText = true;
            Outline(u.WName, Color.black, 0.2f);
            PlaceIn(u.WName.rectTransform, 44 * Px, 0, w - 94 * Px, 20 * Px);
            u.WStyle = MakeText(u.WBox, "Estilo", "", 11, Fonts.Body, MathUtil.Hex(0xc9b48d));
            u.WStyle.alignment = TextAlignmentOptions.Left;
            PlaceIn(u.WStyle.rectTransform, 44 * Px, 20 * Px, w - 94 * Px, 15 * Px);
            u.DurBg = MakeImage(u.WBox, "Durabilidad", MathUtil.Hex(0x1a120b));
            PlaceIn(u.DurBg.rectTransform, 44 * Px, 37 * Px, w - 94 * Px, 5 * Px);
            u.DurFill = Bar(u.DurBg.rectTransform, "Relleno", MathUtil.Hex(0x9fd36a)); Stretch(u.DurFill.rectTransform);

            // HAB.
            u.ARow = Row(c, "Habilidad", "HAB.");
            u.AbRing = MakeImage(u.ARow, "Aro", Gold);
            u.AbRing.sprite = CircleSprite();
            PlaceIn(u.AbRing.rectTransform, 50 * Px, 0, 34 * Px, 34 * Px);
            u.AbBg = MakeImage(u.AbRing.rectTransform, "Fondo", new Color(0, 0, 0, 0.75f));
            u.AbBg.sprite = CircleSprite();
            Stretch(u.AbBg.rectTransform, 2 * Px);
            u.AbGlyph = MakeText(u.AbRing.rectTransform, "Glifo", "", 19, Fonts.Body, Color.white);
            Stretch(u.AbGlyph.rectTransform);
            u.AbCd = MakeImage(u.AbRing.rectTransform, "Enfriamiento", new Color(0, 0, 0, 0.72f));
            u.AbCd.sprite = CircleSprite();
            u.AbCd.type = Image.Type.Filled; u.AbCd.fillMethod = Image.FillMethod.Radial360;
            u.AbCd.fillOrigin = (int)Image.Origin360.Top; u.AbCd.fillClockwise = true; u.AbCd.fillAmount = 0f;
            Stretch(u.AbCd.rectTransform, 2 * Px);
            u.AbName = MakeText(u.ARow, "Nombre", "", 13, Fonts.Body, MathUtil.Hex(0xffe2a0));
            u.AbName.fontStyle = FontStyles.Bold; u.AbName.alignment = TextAlignmentOptions.Left;
            Outline(u.AbName, Color.black, 0.2f);
            PlaceIn(u.AbName.rectTransform, 94 * Px, 1 * Px, w - 94 * Px, 17 * Px);
            u.AbKey = MakeText(u.ARow, "Tecla", "", 11, Fonts.Body, MathUtil.Hex(0xc9b48d));
            u.AbKey.alignment = TextAlignmentOptions.Left; u.AbKey.richText = true;
            PlaceIn(u.AbKey.rectTransform, 94 * Px, 18 * Px, w - 94 * Px, 16 * Px);

            // EQUIPO (armadura, objetos, mejoras y oro)
            u.GRow = Row(c, "Equipo", "EQUIPO");
            u.ArmorIcon = MakeText(u.GRow, "Armadura", "", 18, Fonts.Body, Color.white);
            PlaceIn(u.ArmorIcon.rectTransform, 50 * Px, 0, 22 * Px, 22 * Px);
            u.ArmorBg = MakeImage(u.GRow, "BarraArmadura", MathUtil.Hex(0x1a120b));
            PlaceIn(u.ArmorBg.rectTransform, 74 * Px, 8 * Px, 44 * Px, 6 * Px);
            u.ArmorFill = Bar(u.ArmorBg.rectTransform, "Relleno", MathUtil.Hex(0x7dd6ff)); Stretch(u.ArmorFill.rectTransform);
            u.Chips = MakeText(u.GRow, "Fichas", "", 15, Fonts.Body, Ink);
            u.Chips.alignment = TextAlignmentOptions.Left; u.Chips.richText = true;
            u.GoldText = MakeText(u.GRow, "Oro", "", 12, Fonts.Body, MathUtil.Hex(0xffd36a));
            u.GoldText.fontStyle = FontStyles.Bold; u.GoldText.alignment = TextAlignmentOptions.Right;
            PlaceIn(u.GoldText.rectTransform, w - 70 * Px, 0, 70 * Px, 22 * Px);
            CenterPivot(u.GoldText.rectTransform);

            // Estado y derribado
            u.Status = MakeText(c, "Estado", "", 12, Fonts.Body, MathUtil.Hex(0xffd36a));
            u.Status.fontStyle = FontStyles.Bold;
            u.Downed = MakeText(c, "Derribado", "DERRIBADO · que te reanime tu compañero", 12, Fonts.Body, MathUtil.Hex(0xff6a5a));
            u.Downed.fontStyle = FontStyles.Bold;
            u.Status.gameObject.SetActive(false);
            u.Downed.gameObject.SetActive(false);
            u.GRow.gameObject.SetActive(false);
            if (i == 1) { u.Shown = false; u.Root.gameObject.SetActive(false); }
            Relayout(u);
            return u;
        }

        public static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // Coloca un hijo con su esquina superior izquierda en (x, y) (y hacia abajo) dentro del padre
        public static void PlaceIn(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        RectTransform Row(RectTransform parent, string name, string label)
        {
            var r = NewRect(parent, name);
            var l = MakeText(r, "Etiqueta", label, 11, Fonts.Body, LabelColor);
            l.fontStyle = FontStyles.Bold; l.characterSpacing = 8f; l.alignment = TextAlignmentOptions.Left;
            PlaceIn(l.rectTransform, 0, 0, 48 * Px, 18 * Px);
            return r;
        }

        // Apila las filas visibles y ajusta el alto del panel
        void Relayout(PanelUi u)
        {
            float pad = 12 * Px, top = 8 * Px, w = PanelW - pad * 2 - 4 * Px, y = top;
            PlaceIn(u.Name.rectTransform, pad, y, w * 0.4f, 22 * Px);
            PlaceIn(u.Keys.rectTransform, pad + w * 0.4f, y + 4 * Px, w * 0.6f, 16 * Px);
            y += 26 * Px;
            PlaceIn(u.HpRow, pad, y, w, 18 * Px); Center(u.HpRow, 18 * Px); y += 26 * Px;
            PlaceIn(u.WRow, pad, y, w, 44 * Px); Center(u.WRow, 44 * Px); y += 50 * Px;
            PlaceIn(u.ARow, pad, y, w, 34 * Px); Center(u.ARow, 34 * Px); y += 40 * Px;
            if (u.GRow.gameObject.activeSelf) { PlaceIn(u.GRow, pad, y, w, 22 * Px); Center(u.GRow, 22 * Px); y += 28 * Px; }
            if (u.Status.gameObject.activeSelf) { PlaceIn(u.Status.rectTransform, pad, y, w, 16 * Px); y += 18 * Px; }
            if (u.Downed.gameObject.activeSelf) { PlaceIn(u.Downed.rectTransform, pad, y, w, 16 * Px); y += 18 * Px; }
            y += 6 * Px;
            if (Mathf.Abs(u.LayoutH - y) > 0.5f) { u.LayoutH = y; u.Root.sizeDelta = new Vector2(PanelW, y); }
        }

        // La etiqueta de cada fila, centrada en vertical respecto a la fila
        static void Center(RectTransform row, float h)
        {
            var l = (RectTransform)row.GetChild(0);
            l.anchoredPosition = new Vector2(0, -(h - 18 * Px) / 2f);
        }

        static Sprite circle;
        static Sprite CircleSprite()
        {
            if (circle) return circle;
            const int s = 64;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { name = "circulo", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x + 0.5f - s / 2f, dy = y + 0.5f - s / 2f;
                    float a = Mathf.Clamp01(s / 2f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            circle = Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return circle;
        }

        // ---------- API ----------
        partial void OnHealthPanel(int i, float hp, float max, bool instant)
        {
            var u = panels[i];
            if (u == null) return;
            float k = Mathf.Max(0f, hp / max);
            u.HpFill.fillAmount = k;
            string t = Mathf.CeilToInt(hp) + " / " + Mathf.RoundToInt(max);
            if (t != u.HpText) { u.HpText = t; u.HpNum.text = t; }
            u.Low = k < 0.3f;
            if (!u.Low) u.HpFill.color = MathUtil.Hex(0xd83a2c);
            if (instant) u.HpGhost.fillAmount = k;
        }

        partial void OnWeaponPanel(int i, Weapon w)
        {
            var u = panels[i];
            if (u == null) return;
            string name = w.Name.ToUpperInvariant();
            bool changed = u.WNameText != name;
            u.WNameText = name;
            u.WIcon.text = w.Icon;
            u.WName.text = w.Broken ? "<s>" + name + "</s>" : name;
            u.WName.color = w.Broken ? MathUtil.Hex(0xa0968a) : MathUtil.Hex(0xffe2a0);
            u.WIcon.color = w.Broken ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;
            u.WStyle.text = w.Style;
            bool hasDur = w.HasDurability;
            u.DurBg.gameObject.SetActive(hasDur);
            if (hasDur)
            {
                float k = w.Broken ? 0f : w.Durability / w.MaxDurability;
                u.DurFill.fillAmount = k;
                u.DurFill.color = MathUtil.Hex(k > 0.5f ? 0x9fd36a : k > 0.25f ? 0xe8c547 : 0xe2533b);
            }
            if (changed) u.WPop = 0.35f;
        }

        partial void OnAbilityPanel(int i, AbilityController a)
        {
            var u = panels[i];
            if (u == null || a == null || a.Def == null) return;
            var d = a.Def;
            u.AbColor = MathUtil.Hex(d.Color);
            u.AbRing.color = u.AbColor;
            u.AbGlyph.text = d.Icon;
            u.AbName.text = d.Name.ToUpperInvariant();
            u.AbKey.text = KeySmall(g.SchemeOf(i).AbilityKey) + " · " + d.Cooldown + " s";
            u.AbDeg = -1;
        }

        // Tecla pequeña (las del panel van a 11 px)
        static string KeySmall(string label)
        {
            return "<mark=#2a1f16ee padding=\"4,4,1,1\"><color=#ffe2a0><b>" + label + "</b></color></mark>";
        }

        partial void OnGearPanel(int i, PlayerGear gear)
        {
            var u = panels[i];
            if (u == null || gear == null) return;
            string armorIcon; float armorK; string chips;
            gear.HudInfo(out armorIcon, out armorK, out chips);
            u.HasArmor = armorIcon != null;
            u.ArmorIcon.gameObject.SetActive(u.HasArmor);
            u.ArmorBg.gameObject.SetActive(u.HasArmor);
            if (u.HasArmor) { u.ArmorIcon.text = armorIcon; u.ArmorFill.fillAmount = armorK; }
            if (chips != u.ChipsShown) { u.ChipsShown = chips; u.Chips.text = chips ?? ""; }
            float cx = (u.HasArmor ? 124 : 50) * Px;
            PlaceIn(u.Chips.rectTransform, cx, 0, PanelW - 24 * Px - cx - 74 * Px, 22 * Px);
            SetGoldText(u, gear.Gold);
            bool empty = !u.HasArmor && string.IsNullOrEmpty(chips) && gear.Gold == 0;
            if (empty != u.GearEmpty) { u.GearEmpty = empty; u.GRow.gameObject.SetActive(!empty); Relayout(u); }
        }

        void SetGoldText(PanelUi u, int gold)
        {
            string t = gold != 0 ? "🪙 " + gold : "";
            if (t != u.GoldShown) { u.GoldShown = t; u.GoldText.text = t; }
        }

        // Solo el contador de monedas (recoger monedas no rehace todo el panel de equipo)
        partial void OnGoldPanel(int i, int gold)
        {
            var u = panels[i];
            if (u == null) return;
            SetGoldText(u, gold);
            if (gold != 0 && u.GearEmpty) { u.GearEmpty = false; u.GRow.gameObject.SetActive(true); Relayout(u); }
        }

        // El contador de monedas del panel da un saltito al recoger una
        public void CoinPop(int i) { if (panels[i] != null) panels[i].GoldPop = 0.3f; }

        partial void OnDownedPanel(int i, bool downed)
        {
            var u = panels[i];
            if (u == null || u.IsDowned == downed) return;
            u.IsDowned = downed;
            u.Downed.gameObject.SetActive(downed);
            u.Bg.color = downed ? new Color(0.08f, 0.07f, 0.07f, 0.9f) : new Color(24 / 255f, 17 / 255f, 13 / 255f, 0.9f);
            Relayout(u);
        }

        partial void OnShowPlayerPanel(int i, bool on)
        {
            var u = panels[i];
            if (u == null) return;
            u.Shown = on;
            u.Root.gameObject.SetActive(on);
        }

        partial void OnJoinHint(bool on) { if (joinHint) joinHint.gameObject.SetActive(on); }

        // Teclas del panel de un jugador (cambian si elige otro dispositivo)
        public void SetPanelKeys(int i, string text) { var u = panels[i]; if (u != null && u.Keys && u.Keys.text != text) u.Keys.text = text; }
        public string PanelKeys(int i) { return panels[i].Keys.text; }

        public bool PanelShown(int i) { return panels[i] != null && panels[i].Shown; }
        public string PanelHpText(int i) { return panels[i].HpText; }
        public string PanelWeaponName(int i) { return panels[i].WNameText; }
        public string PanelAbilityName(int i) { return panels[i].AbName.text; }
        public string PanelStatus(int i) { return panels[i].StatusText; }
        public string PanelGold(int i) { return panels[i].GoldShown; }
        public bool PanelDowned(int i) { return panels[i].IsDowned; }
        public bool JoinHintShown { get { return joinHint && joinHint.gameObject.activeSelf; } }

        // El HUD de partida entero (paneles, piso, avisos, viñeta...) se oculta en el título y la sala
        public bool HudShown { get; private set; } = true;
        public void ShowHud(bool on)
        {
            HudShown = on;
            vignette.gameObject.SetActive(on);
            worldLayer.gameObject.SetActive(on);
            screenLayer.gameObject.SetActive(on);
            panelsLayer.gameObject.SetActive(on);
            topLayer.gameObject.SetActive(on);
        }

        partial void UpdatePanels(float dt)
        {
            pulseT += dt;
            float pulse = 0.5f + 0.5f * Mathf.Sin(pulseT * Mathf.PI / 0.6f);
            var players = g.Players;
            for (int i = 0; i < panels.Length; i++)
            {
                var u = panels[i];
                if (u == null || !u.Shown) continue;
                var head = heads[i];
                u.HpGhost.fillAmount = head.Ghost;
                // Vida baja: la barra late
                if (u.Low) u.HpFill.color = Color.Lerp(MathUtil.Hex(0xd83a2c), MathUtil.Hex(0xff8a70), pulse);
                if (u.WPop > 0f)
                {
                    u.WPop = Mathf.Max(0f, u.WPop - dt);
                    float k = 1f - u.WPop / 0.35f;
                    float s = k < 0.4f ? 1f + 0.12f * (k / 0.4f) : 1.12f - 0.12f * ((k - 0.4f) / 0.6f);
                    u.WBox.localScale = Vector3.one * s;
                }
                if (u.GoldPop > 0f)
                {
                    u.GoldPop = Mathf.Max(0f, u.GoldPop - dt);
                    float k = 1f - u.GoldPop / 0.3f;
                    u.GoldText.rectTransform.localScale = Vector3.one * (k < 0.4f ? 1f + 0.12f * (k / 0.4f) : 1.12f - 0.12f * ((k - 0.4f) / 0.6f));
                }
                if (u.IsDowned) u.Downed.alpha = 0.6f + 0.4f * pulse;
                Player pl = i < players.Count ? players[i] : null;
                if (pl == null) continue;
                // Enfriamiento de la habilidad (relleno circular en pasos de 3°) y estado lista / activa
                var a = pl.Ability;
                float ck = a.CooldownK;
                int deg = Mathf.RoundToInt(ck * 120f) * 3;
                if (deg != u.AbDeg || a.Active != u.AbActive)
                {
                    u.AbDeg = deg;
                    u.AbActive = a.Active;
                    u.AbCd.fillAmount = deg / 360f;
                    u.AbReady = ck == 0f && !a.Active;
                    u.AbBg.color = u.AbActive ? u.AbColor : new Color(0, 0, 0, 0.75f);
                }
                if (u.AbReady)
                {
                    float gk = 0.5f + 0.5f * Mathf.Sin(pulseT * Mathf.PI / 0.9f);
                    u.AbRing.color = Color.Lerp(u.AbColor, Color.white, gk * 0.45f);
                }
                else if (u.AbRing.color != u.AbColor) u.AbRing.color = u.AbColor;
                // Estado del personaje (aturdido, desorientado, prisa, lentitud, protección...)
                string st = "";
                if (pl.Spectator) st = Join(st, "👻 fuera del desafío");
                if (pl.StunT > 0f) st = Join(st, "💫 aturdido");
                if (pl.ConfusedT > 0f) st = Join(st, "🌀 desorientado");
                if (a.Active && a.Id == "guard") st = Join(st, "🐢 protegido");
                if (pl.HasteT > 0f) st = Join(st, "💨 veloz");
                if (pl.SlowT > 0f) st = Join(st, "🐌 lento");
                if (pl.IsEating) st = Join(st, "🍗 comiendo");
                if (st != u.StatusText)
                {
                    bool vis = st.Length > 0;
                    bool relayout = vis != u.Status.gameObject.activeSelf;
                    u.StatusText = st;
                    u.Status.text = st;
                    u.Status.gameObject.SetActive(vis);
                    if (relayout) Relayout(u);
                }
                // Mejoras con segundos restantes: el panel de equipo se rehace una vez por segundo
                if (pl.Gear.HasTimedBuffs)
                {
                    u.ChipT -= dt;
                    if (u.ChipT <= 0f) { u.ChipT = 1f; OnGearPanel(i, pl.Gear); }
                }
            }
            if (joinHint && joinHint.gameObject.activeSelf)
            {
                var c = joinText.color; c.a = 0.65f + 0.35f * pulse; joinText.color = c;
            }
        }

        static string Join(string a, string b) { return a.Length == 0 ? b : a + " · " + b; }
    }
}
