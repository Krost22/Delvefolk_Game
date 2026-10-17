using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Rótulo flotante en el mundo que ven todos los jugadores (pistas descubiertas, etiquetas de platos...)
    public sealed class WorldLabel
    {
        public RectTransform R;
        public TextMeshProUGUI T;
        public Image Border, Bg;
        public Vector3 Pos;
        public bool Alive = true;
        public string Text { get { return T.text; } }
        public void Remove() { Alive = false; if (R) Object.Destroy(R.gameObject); }
    }

    // HUD del minijuego, pantallas de introducción y resultados de los pisos bonus (UIManager.js)
    public sealed partial class Hud
    {
        RectTransform bonusHud, bonusScreen, bonusCard;
        TextMeshProUGUI bonusHudText, bonusReady;
        Image bonusProgBg, bonusProgFill;
        int bonusHudT;
        string readyShown;
        readonly List<WorldLabel> labels = new List<WorldLabel>();
        public string BonusTagText { get; private set; }
        public bool BonusScreenShown { get { return bonusScreen && bonusScreen.gameObject.activeSelf; } }
        public bool BonusHudShown { get { return bonusHud && bonusHud.gameObject.activeSelf; } }
        public string BonusHudText { get { return bonusHudText ? bonusHudText.text : null; } }
        public string BonusCardText { get; private set; }

        // Teclas de todos los jugadores para una acción: {A} atacar, {E} interactuar, {H} habilidad, {J} saltar
        public static string KeyFor(List<Player> players, char which)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < players.Count; i++)
            {
                var s = players[i].Controls.S;
                string k = which == 'A' ? s.AttackKey : which == 'H' ? s.AbilityKey : which == 'J' ? s.JumpKey : s.InteractKey;
                if (i > 0) sb.Append('/');
                sb.Append(KeySmall(k));
            }
            return sb.ToString();
        }

        public static string FillKeys(string t, List<Player> players)
        {
            if (string.IsNullOrEmpty(t)) return t;
            return t.Replace("{E}", KeyFor(players, 'E')).Replace("{A}", KeyFor(players, 'A')).Replace("{H}", KeyFor(players, 'H')).Replace("{J}", KeyFor(players, 'J'));
        }

        // ---------- HUD del minijuego (arriba, centrado) ----------
        void BuildBonusHud()
        {
            var border = MakeImage(screenLayer, "HudBonus", Gold);
            AsFrame(border, 2 * Px);
            bonusHud = border.rectTransform;
            bonusHud.anchorMin = bonusHud.anchorMax = new Vector2(0.5f, 1f);
            bonusHud.pivot = new Vector2(0.5f, 1f);
            bonusHud.anchoredPosition = new Vector2(0, -56 * Px);
            var bg = MakeImage(bonusHud, "Fondo", new Color(20 / 255f, 14 / 255f, 10 / 255f, 0.88f));
            Stretch(bg.rectTransform, 2 * Px);
            bonusHudText = MakeText(bonusHud, "Texto", "", 13, Fonts.Body, Ink);
            bonusHudText.richText = true;
            bonusHudText.alignment = TextAlignmentOptions.Top;
            bonusHudText.textWrappingMode = TextWrappingModes.Normal;
            Stretch(bonusHudText.rectTransform, 10 * Px);
            bonusProgBg = MakeImage(bonusHud, "Progreso", MathUtil.Hex(0x1a120b));
            bonusProgFill = Bar(bonusProgBg.rectTransform, "Relleno", MathUtil.Hex(0x9fd36a));
            Stretch(bonusProgFill.rectTransform);
            bonusHud.gameObject.SetActive(false);
        }

        public void ShowBonusHud(bool on)
        {
            if (!bonusHud) BuildBonusHud();
            bonusHud.gameObject.SetActive(on);
            bonusHudT = 0;
            // Con el HUD del minijuego, el banner baja y el aviso se va abajo
            Anchor(bannerTitle.rectTransform, new Vector2(0.5f, on ? 0.62f : 0.72f), Vector2.zero, new Vector2(1400, 90));
            Anchor(bannerSub.rectTransform, new Vector2(0.5f, on ? 0.62f : 0.72f), new Vector2(0, -62), new Vector2(1400, 50));
            var tr = toastBg.rectTransform;
            tr.anchorMin = tr.anchorMax = on ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
            tr.anchoredPosition = on ? new Vector2(0, 60 * Px + 30) : new Vector2(0, -78 * Px);
        }

        public void UpdateBonusHud(BonusHudState h)
        {
            if (h == null || !bonusHud || !bonusHud.gameObject.activeSelf) return;
            bonusHudT -= 1;
            if (bonusHudT > 0) return;
            bonusHudT = 6;   // ~10 veces por segundo basta
            var sb = new System.Text.StringBuilder();
            sb.Append("<b><color=#ffcf6a>").Append(h.Icon).Append(' ').Append(h.Title).Append("</color></b>\n");
            if (h.Timer.HasValue)
            {
                float t = h.Timer.Value;
                sb.Append("<size=170%><b>").Append(t < 10f ? "<color=#ff5a4a>" : "<color=#ffffff>").Append("⏳ ").Append(Mathf.CeilToInt(t)).Append(" s</color></b></size>\n");
            }
            float progY = -1f;
            if (h.HasProgress)
            {
                sb.Append("<size=92%><color=#c9b48d>").Append(h.ProgressLabel).Append(' ').Append(h.ProgressValue).Append('/').Append(h.ProgressMax).Append("</color></size>\n\n");
                progY = 0;
            }
            foreach (var l in h.Lines) sb.Append(l).Append('\n');
            if (h.Players.Count > 0)
            {
                for (int i = 0; i < h.Players.Count; i++)
                {
                    var p = h.Players[i];
                    if (i > 0) sb.Append("   ");
                    string col = "#" + p.Color.ToString("x6");
                    if (p.Out) sb.Append("<alpha=#70><s>");
                    sb.Append("<b><color=").Append(col).Append('>').Append(p.Name).Append("</color></b>").Append(p.Out ? " ✖" : p.Down ? " 💤" : " ✔");
                    if (!string.IsNullOrEmpty(p.Extra)) sb.Append(' ').Append(p.Extra);
                    if (p.Out) sb.Append("</s><alpha=#FF>");
                }
            }
            string txt = sb.ToString().TrimEnd('\n');
            bonusHudText.text = txt;
            var pref = bonusHudText.GetPreferredValues(txt, 420 * Px - 20 * Px, 0f);
            float w = Mathf.Clamp(pref.x + 26 * Px, 260 * Px, 420 * Px);
            bonusHud.sizeDelta = new Vector2(w, pref.y + 22 * Px);
            bonusProgBg.gameObject.SetActive(progY >= 0f);
            if (progY >= 0f)
            {
                // La barra de progreso va justo debajo de su etiqueta
                float lineH = bonusHudText.fontSize * 1.25f;
                float y = 10 * Px + lineH + (h.Timer.HasValue ? lineH * 1.7f : 0f) + lineH * 0.95f;
                PlaceIn(bonusProgBg.rectTransform, 30 * Px, y, w - 60 * Px, 7 * Px);
                bonusProgFill.fillAmount = Mathf.Min(1f, h.ProgressValue / (float)Mathf.Max(1, h.ProgressMax));
            }
        }

        // ---------- Pantallas de introducción y resultados ----------
        RectTransform BonusCard()
        {
            if (!bonusScreen)
            {
                bonusScreen = Overlay("PantallaBonus", 0.72f);
                bonusCard = NewRect(bonusScreen, "Tarjeta");
                bonusScreen.gameObject.SetActive(false);
            }
            // Se rehace el contenido cada vez
            for (int i = bonusCard.childCount - 1; i >= 0; i--) Object.Destroy(bonusCard.GetChild(i).gameObject);
            return bonusCard;
        }

        RectTransform CardPanel(Vector2 size)
        {
            var parent = BonusCard();
            var p = Panel(parent, "Panel", size);
            return p;
        }

        TextMeshProUGUI Tag(RectTransform p, string text, int bg, int fg, float y)
        {
            var box = MakeImage(p, "Etiqueta", MathUtil.Hex(bg));
            var t = MakeText(box.rectTransform, "Texto", "<cspace=0.25em>" + text + "</cspace>", 13, Fonts.Body, MathUtil.Hex(fg));
            t.fontStyle = FontStyles.Bold; t.richText = true;
            Stretch(t.rectTransform);
            var pref = t.GetPreferredValues(t.text);
            var r = box.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f); r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0, -y);
            r.sizeDelta = new Vector2(pref.x + 28 * Px, pref.y + 6 * Px);
            BonusTagText = text;
            return t;
        }

        public void ShowBonusIntro(BonusInfo info, List<Player> players)
        {
            ShowBonusIntroImpl(info, players);
        }

        // Texto de las reglas (siempre disponible; las viñetas ilustradas lo complementan)
        void ShowBonusIntroImpl(BonusInfo info, List<Player> players)
        {
            bool illustrated = false;
            IllustratedIntro(info, players, ref illustrated);
            if (illustrated) return;
            var p = CardPanel(new Vector2(720 * Px, 640 * Px));
            Tag(p, "PISO BONUS", 0xc9a227, 0x1a120b, 22 * Px);
            Title(p, info.Icon + " " + info.Name, 30, 52 * Px);
            var tag = Body(p, info.Tagline, 16, 100 * Px, 30 * Px);
            tag.alignment = TextAlignmentOptions.Center;
            var sb = new System.Text.StringBuilder();
            foreach (var r in info.Rules) sb.Append("• ").Append(FillKeys(r, players)).Append('\n');
            var rules = Body(p, sb.ToString().TrimEnd(), 15, 140 * Px, 170 * Px);
            rules.alignment = TextAlignmentOptions.TopLeft;
            rules.lineSpacing = 8f;
            string grid =
                "<color=#7dd65b><b>🏆 Victoria</b></color>  " + FillKeys(info.Win, players) + "\n" +
                "<color=#e2533b><b>☠️ Derrota</b></color>  " + FillKeys(info.Lose, players) + "\n" +
                "<color=#ff9a2a><b>⚠️ Riesgo</b></color>  " + FillKeys(info.Risk, players) + "\n" +
                "<color=#ffd36a><b>🎁 Recompensa</b></color>  " + FillKeys(info.Reward, players);
            var g2 = Body(p, grid, 13, 320 * Px, 150 * Px);
            g2.alignment = TextAlignmentOptions.TopLeft;
            g2.lineSpacing = 10f;
            if (!string.IsNullOrEmpty(info.Coop))
            {
                var coop = Body(p, info.Coop, 14, 470 * Px, 40 * Px, TipColor);
                coop.alignment = TextAlignmentOptions.Center;
            }
            var small = Body(p, "Perder no acaba la partida: después seguiréis bajando.", 12, 520 * Px, 24 * Px, TipColor);
            small.alignment = TextAlignmentOptions.Center;
            bonusReady = Body(p, "", 15, 560 * Px, 40 * Px);
            bonusReady.alignment = TextAlignmentOptions.Center;
            readyShown = null;
            BonusCardText = info.Name + "\n" + sb + grid;
            bonusScreen.gameObject.SetActive(true);
        }

        partial void IllustratedIntro(BonusInfo info, List<Player> players, ref bool shown);

        public void ShowBonusResult(BonusChallenge ch, BonusFloorController.Result res, List<Player> players)
        {
            bool ok = res.Success;
            var p = CardPanel(new Vector2(720 * Px, 520 * Px));
            Tag(p, ok ? "¡DESAFÍO SUPERADO!" : "DESAFÍO FALLIDO", ok ? 0x7dd65b : 0xe2533b, ok ? 0x1a120b : 0xffffff, 22 * Px);
            Title(p, ch.Info.Icon + " " + ch.Info.Name, 30, 52 * Px);
            var reason = Body(p, res.Reason ?? "", 14, 100 * Px, 24 * Px, TipColor);
            reason.alignment = TextAlignmentOptions.Center;
            float colW = (720 * Px - 80 * Px) / Mathf.Max(1, players.Count);
            var all = new System.Text.StringBuilder();
            for (int i = 0; i < players.Count; i++)
            {
                var pl = players[i];
                BonusChallenge.Part st;
                res.Part.TryGetValue(pl.Index, out st);
                string fate = st != null && st.Out ? "<color=#ff8a7a>Eliminado: " + st.Why + "</color>" : pl.Dead ? "<color=#ff8a7a>Derribado</color>" : "<color=#9fd36a>Superviviente</color>";
                var sb = new System.Text.StringBuilder();
                sb.Append("<b><color=#").Append(pl.Look.Ring.ToString("x6")).Append(">").Append(pl.Look.Name).Append("</color></b>  <size=85%>").Append(fate).Append("</size>\n");
                List<BonusRewardService.Granted> list;
                if (res.Granted != null && res.Granted.TryGetValue(pl.Index, out list) && list.Count > 0)
                {
                    foreach (var r in list)
                        sb.Append(r.Icon).Append(' ').Append(r.Text).Append(" <size=80%><color=#8a7a62>").Append(r.Category).Append(string.IsNullOrEmpty(r.Note) ? "" : " · " + r.Note).Append("</color></size>\n");
                }
                else sb.Append("<i><color=#8a7a62>Sin recompensa esta vez</color></i>");
                var box = MakeImage(p, "Jugador" + (i + 1), MathUtil.Hex(pl.Look.Ring));
                AsFrame(box, 2 * Px);
                PlaceIn(box.rectTransform, 40 * Px + i * colW, 140 * Px, colW - 10 * Px, 230 * Px);
                var t = MakeText(box.rectTransform, "Texto", sb.ToString(), 14, Fonts.Body, Ink);
                t.richText = true; t.textWrappingMode = TextWrappingModes.Normal; t.alignment = TextAlignmentOptions.TopLeft;
                Stretch(t.rectTransform, 10 * Px);
                all.Append(sb).Append('\n');
            }
            var small = Body(p, "Quien cayó se levanta con " + BonusConfig.ReviveHp + " de vida. Seguís bajando.", 12, 390 * Px, 24 * Px, TipColor);
            small.alignment = TextAlignmentOptions.Center;
            bonusReady = Body(p, "", 15, 430 * Px, 40 * Px);
            bonusReady.alignment = TextAlignmentOptions.Center;
            readyShown = null;
            BonusCardText = all.ToString();
            bonusScreen.gameObject.SetActive(true);
        }

        public void UpdateBonusOverlay(HashSet<int> confirmed, List<Player> players, float timeLeft)
        {
            if (!bonusReady) return;
            var sb = new System.Text.StringBuilder();
            foreach (var p in players)
            {
                string col = "#" + p.Look.Ring.ToString("x6");
                sb.Append("<color=").Append(col).Append("><b>").Append(p.Look.Name).Append("</b></color> ");
                sb.Append(confirmed.Contains(p.Index) ? "<color=#7dff8a>✔ listo</color>" : KeySmall(p.Controls.S.AttackKey));
                sb.Append("     ");
            }
            sb.Append("<size=80%><color=#8a7a62>sigue solo en ").Append(Mathf.CeilToInt(timeLeft)).Append(" s</color></size>");
            string html = sb.ToString();
            if (html != readyShown) { readyShown = html; bonusReady.text = html; }
        }

        public string BonusReadyText { get { return readyShown; } }

        public void HideBonusOverlay()
        {
            if (bonusScreen) bonusScreen.gameObject.SetActive(false);
            readyShown = null;
        }

        partial void HideMoreScreens() { HideBonusOverlay(); }

        // ---------- Rótulos en el mundo ----------
        public WorldLabel Label(string text, Vector3 pos, int color = 0xffe2a0, float bgAlpha = 0.85f)
        {
            var l = new WorldLabel { Pos = pos };
            l.Border = MakeImage(worldLayer, "Rotulo", MathUtil.Hex(color));
            AsFrame(l.Border, 2 * Px);
            l.R = l.Border.rectTransform;
            l.Bg = MakeImage(l.R, "Fondo", new Color(20 / 255f, 14 / 255f, 10 / 255f, bgAlpha));
            Stretch(l.Bg.rectTransform, 2 * Px);
            l.T = MakeText(l.R, "Texto", text, 15, Fonts.Body, MathUtil.Hex(color));
            l.T.fontStyle = FontStyles.Bold;
            Stretch(l.T.rectTransform);
            var pref = l.T.GetPreferredValues(text);
            l.R.sizeDelta = new Vector2(pref.x + 18 * Px, pref.y + 8 * Px);
            labels.Add(l);
            PlaceAt(l.R, pos);
            return l;
        }

        public bool HasLabel(string fragment)
        {
            foreach (var l in labels) if (l.Alive && l.T.text.Contains(fragment)) return true;
            return false;
        }

        public void ClearLabels()
        {
            foreach (var l in labels) if (l.Alive) l.Remove();
            labels.Clear();
        }

        void UpdateLabels()
        {
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                var l = labels[i];
                if (!l.Alive || !l.R) { labels.RemoveAt(i); continue; }
                PlaceAt(l.R, l.Pos);
            }
        }
    }
}
