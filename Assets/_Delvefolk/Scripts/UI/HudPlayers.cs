using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    public enum BarKind { None, Eat, Revive, Struggle }

    // Lo que va sobre la cabeza de cada jugador: nombre, vida (con "fantasma" y línea de armadura),
    // energía, barra de progreso (comer, reanimar, forcejeo) y avisos de interacción con las teclas.
    public sealed partial class Hud
    {
        sealed class HeadUi
        {
            public RectTransform Head;
            public Image HpBg, HpGhost, HpFill, Armor, StaBg, StaFill;
            public TextMeshProUGUI Name;
            public RectTransform Bar; public Image BarFill; public float BarK = -1f;
            public RectTransform Prompt; public TextMeshProUGUI PromptText; public Image PromptBg; public string PromptRaw;
            public float HpK = 1f, Ghost = 1f, ArmorK;
            public string State;
            public bool Visible = true;
        }

        readonly HeadUi[] heads = new HeadUi[2];
        bool headsHidden;
        const float BarW = 64f, BarH = 8f;

        partial void InitExtra()
        {
            for (int i = 0; i < 2; i++) heads[i] = MakeHead(i);
            InitPanels();
        }

        partial void InitPanels();

        HeadUi MakeHead(int i)
        {
            var u = new HeadUi();
            var look = Player.Looks[i];
            var go = new GameObject("Cabeza" + (i + 1), typeof(RectTransform));
            u.Head = (RectTransform)go.transform;
            u.Head.SetParent(worldLayer, false);
            u.Head.sizeDelta = new Vector2(BarW * Px, 30 * Px);
            u.Name = MakeText(u.Head, "Nombre", look.Name, 13, Fonts.Body, MathUtil.Hex(look.Ring));
            u.Name.fontStyle = FontStyles.Bold;
            Outline(u.Name, new Color(0, 0, 0, 0.9f), 0.25f);
            Anchor(u.Name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 14 * Px), new Vector2(80 * Px, 18 * Px));
            u.HpBg = MakeImage(u.Head, "Fondo", new Color(0.05f, 0.02f, 0.02f, 0.85f));
            Anchor(u.HpBg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BarW * Px + 4, BarH * Px + 4));
            var ring = MakeImage(u.HpBg.rectTransform, "Borde", MathUtil.Hex(look.Ring, 0.9f));
            Stretch(ring.rectTransform, -1f);
            ring.transform.SetAsFirstSibling();
            u.HpGhost = Bar(u.HpBg.rectTransform, "Fantasma", MathUtil.Hex(0xf0d080));
            u.HpFill = Bar(u.HpBg.rectTransform, "Vida", MathUtil.Hex(0xe23b3b));
            u.Armor = MakeImage(u.HpBg.rectTransform, "Armadura", MathUtil.Hex(0x7dd6ff));
            var ar = u.Armor.rectTransform;
            ar.anchorMin = new Vector2(0, 0); ar.anchorMax = new Vector2(0, 0); ar.pivot = new Vector2(0, 0);
            ar.anchoredPosition = new Vector2(2, 2); ar.sizeDelta = new Vector2(0, 2);
            u.StaBg = MakeImage(u.Head, "Energia", new Color(0, 0, 0, 0.7f));
            Anchor(u.StaBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -(BarH * Px) / 2f - 5), new Vector2(BarW * Px, 4));
            u.StaFill = Bar(u.StaBg.rectTransform, "Relleno", MathUtil.Hex(0xffd040));

            var bgo = new GameObject("Progreso" + (i + 1), typeof(RectTransform));
            u.Bar = (RectTransform)bgo.transform;
            u.Bar.SetParent(worldLayer, false);
            u.Bar.sizeDelta = new Vector2(BarW * Px, 7);
            var bbg = MakeImage(u.Bar, "Fondo", new Color(0, 0, 0, 0.8f));
            Stretch(bbg.rectTransform);
            u.BarFill = Bar(u.Bar, "Relleno", MathUtil.Hex(0x7dff8a));
            u.Bar.gameObject.SetActive(false);

            u.PromptBg = MakeImage(worldLayer, "Aviso" + (i + 1), new Color(0.08f, 0.06f, 0.04f, 0.85f));
            u.Prompt = u.PromptBg.rectTransform;
            u.PromptText = MakeText(u.Prompt, "Texto", "", 15, Fonts.Body, Ink);
            Stretch(u.PromptText.rectTransform, 6);
            u.PromptText.richText = true;
            u.Prompt.gameObject.SetActive(false);
            u.Head.gameObject.SetActive(false);   // hasta que haya partida (como la clase "on" del original)
            return u;
        }

        static Image Bar(RectTransform parent, string name, Color c)
        {
            var img = MakeImage(parent, name, c);
            var r = img.rectTransform;
            r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0, 0.5f);
            r.offsetMin = new Vector2(2, 2); r.offsetMax = new Vector2(-2, -2);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.sprite = WhiteSprite();
            img.fillAmount = 1f;
            return img;
        }

        static Sprite white;
        public static Sprite WhiteSprite()
        {
            if (white) return white;
            var t = new Texture2D(4, 4) { name = "blanco" };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px); t.Apply();
            white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return white;
        }

        // ---------- API de jugador (los paneles laterales llegan en la fase de presentación) ----------
        public void SetHealth(int i, float hp, float max, bool instant = false)
        {
            var u = heads[i];
            u.HpK = Mathf.Clamp01(hp / Mathf.Max(1f, max));
            if (instant) u.Ghost = u.HpK;
            OnHealthPanel(i, hp, max, instant);
        }

        partial void OnHealthPanel(int i, float hp, float max, bool instant);

        public void SetWeapon(int i, Weapon w) { OnWeaponPanel(i, w); }
        partial void OnWeaponPanel(int i, Weapon w);
        public void SetGear(int i, PlayerGear gear) { OnGearPanel(i, gear); }
        partial void OnGearPanel(int i, PlayerGear gear);
        public void SetAbility(int i, AbilityController a) { OnAbilityPanel(i, a); }
        partial void OnAbilityPanel(int i, AbilityController a);
        public void SetDowned(int i, bool downed) { OnDownedPanel(i, downed); }
        partial void OnDownedPanel(int i, bool downed);
        public void ShowPlayer(int i, bool on) { OnShowPlayerPanel(i, on); }
        partial void OnShowPlayerPanel(int i, bool on);
        public void SetJoinHint(bool on) { OnJoinHint(on); }
        partial void OnJoinHint(bool on);

        public string PromptOf(int i) { return heads[i].PromptRaw; }

        // Aviso de interacción sobre la cabeza del jugador. text puede incluir {A} (acción), {E} (interactuar) y {J} (saltar)
        public void SetPrompt(int i, string text)
        {
            var u = heads[i];
            if (text == u.PromptRaw) return;
            u.PromptRaw = text;
            if (string.IsNullOrEmpty(text)) { u.Prompt.gameObject.SetActive(false); return; }
            var s = ControlSchemes.All[i];
            string html = text.Replace("{A}", Key(s.AttackKey)).Replace("{E}", Key(s.InteractKey)).Replace("{J}", Key(s.JumpKey));
            if (html.IndexOf("<mark", System.StringComparison.Ordinal) < 0) html = Key(s.InteractKey) + " " + html;
            u.PromptText.text = html;
            var pref = u.PromptText.GetPreferredValues(html);
            u.Prompt.sizeDelta = new Vector2(pref.x + 16, pref.y + 10);
            u.Prompt.gameObject.SetActive(true);
        }

        // Tecla dibujada como "tecla física"
        public static string Key(string label)
        {
            return "<mark=#2a1f16ee padding=\"6,6,2,2\"><color=#ffe2a0><b>" + label + "</b></color></mark>";
        }

        // Barra de progreso sobre la cabeza (comer, reanimar, forcejeo). k < 0 la oculta
        public void SetPlayerBar(int i, float k, BarKind kind)
        {
            var u = heads[i];
            u.BarK = k;
            if (k < 0f || kind == BarKind.None) { u.BarK = -1f; u.Bar.gameObject.SetActive(false); return; }
            u.Bar.gameObject.SetActive(true);
            u.BarFill.color = kind == BarKind.Eat ? MathUtil.Hex(0x7dff8a) : kind == BarKind.Revive ? MathUtil.Hex(0x5bb0ff) : MathUtil.Hex(0xffd36a);
            u.BarFill.fillAmount = Mathf.Clamp01(k);
        }

        public float PlayerBarOf(int i) { return heads[i].BarK; }
        // Estado de la vida sobre la cabeza (pruebas)
        public bool HeadShown(int i) { return heads[i].Head.gameObject.activeInHierarchy; }
        public float HeadFill(int i) { return heads[i].HpFill.fillAmount; }
        public string HeadName(int i) { return heads[i].Name.text; }

        public void HideHeads()
        {
            headsHidden = true;
            for (int i = 0; i < 2; i++)
            {
                heads[i].Head.gameObject.SetActive(false);
                heads[i].Bar.gameObject.SetActive(false);
                heads[i].Prompt.gameObject.SetActive(false);
            }
        }

        public void ShowHeads() { headsHidden = false; }

        partial void UpdateExtra(float dt)
        {
            var players = g.Players;
            bool inMenu = g.State == GameState.Title || g.State == GameState.Lobby;
            Vector2 p0 = Vector2.zero, p1 = Vector2.zero;
            bool has0 = false, has1 = false;
            for (int i = 0; i < 2; i++)
            {
                var u = heads[i];
                Player pl = i < players.Count ? players[i] : null;
                if (pl == null || inMenu || headsHidden)
                {
                    if (u.Head.gameObject.activeSelf) u.Head.gameObject.SetActive(false);
                    if (u.Prompt.gameObject.activeSelf && (pl == null || inMenu)) u.Prompt.gameObject.SetActive(false);
                    continue;
                }
                bool carried = pl.CarriedBy != null || pl.GrabbedBy != null;
                float top = carried ? 3.9f : 2.9f + pl.JumpY;
                // Vida sobre la cabeza (y la barra de progreso justo encima de ella)
                bool showHead = !pl.Dead && !pl.Removed;
                if (u.Head.gameObject.activeSelf != showHead) u.Head.gameObject.SetActive(showHead);
                if (showHead)
                {
                    PlaceAt(u.Head, new Vector3(pl.X, top - 0.75f, pl.Z));
                    float sk = Mathf.Max(0f, pl.Stamina) / JumpConfig.Stamina;
                    string st = Mathf.RoundToInt(u.HpK * 200) + "|" + Mathf.RoundToInt(u.Ghost * 200) + "|" + Mathf.RoundToInt(u.ArmorK * 100) + "|" + Mathf.RoundToInt(sk * 100) + "|" + (pl.Spectator ? 1 : 0);
                    if (st != u.State)
                    {
                        u.State = st;
                        u.HpFill.fillAmount = u.HpK;
                        u.HpGhost.fillAmount = u.Ghost;
                        u.StaBg.gameObject.SetActive(sk < 1f);
                        u.StaFill.fillAmount = sk;
                        u.StaFill.color = sk < 0.35f ? MathUtil.Hex(0xff5040) : MathUtil.Hex(0xffd040);
                        var ar = u.Armor.rectTransform; ar.sizeDelta = new Vector2((BarW * Px) * u.ArmorK, 2);
                        u.Armor.gameObject.SetActive(u.ArmorK > 0f);
                    }
                }
                if (u.BarK >= 0f)
                {
                    var hp = PlaceAt(u.Bar, new Vector3(pl.X, top - 0.75f, pl.Z));
                    if (hp) u.Bar.anchoredPosition += new Vector2(0, 15f * Px);
                }
                if (u.PromptRaw != null)
                {
                    PlaceAt(u.Prompt, new Vector3(pl.X, carried ? 3.9f : 2.9f, pl.Z));
                    if (i == 0) { p0 = u.Prompt.anchoredPosition; has0 = true; } else { p1 = u.Prompt.anchoredPosition; has1 = true; }
                    u.Prompt.anchoredPosition += new Vector2(0, u.Prompt.sizeDelta.y / 2f);
                }
                // Fantasma de la vida: baja a 0.6/s tras el daño
                u.Ghost = u.Ghost > u.HpK ? Mathf.Max(u.HpK, u.Ghost - dt * 0.6f) : u.HpK;
            }
            // Si los dos avisos quedarían uno encima del otro, se separan a izquierda y derecha
            if (has0 && has1 && Mathf.Abs(p0.x - p1.x) < 260 && Mathf.Abs(p0.y - p1.y) < 40)
            {
                bool leftFirst = p0.x <= p1.x;
                var a = heads[0].Prompt; var b = heads[1].Prompt;
                a.anchoredPosition += new Vector2((leftFirst ? -1 : 1) * (a.sizeDelta.x / 2f + 4), 0);
                b.anchoredPosition += new Vector2((leftFirst ? 1 : -1) * (b.sizeDelta.x / 2f + 4), 0);
            }
            UpdatePanels(dt);
        }

        partial void UpdatePanels(float dt);
    }
}
