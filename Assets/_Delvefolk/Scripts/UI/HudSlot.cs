using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Panel de la tragaperras en pantalla: tres rodillos que giran y frenan (los mueve SlotOverlay)
    public sealed partial class Hud
    {
        public const int SlotCells = 5;   // casillas visibles por rodillo (la del centro es el resultado)
        const float ReelW = 170f, ReelH = 200f, CellH = 64f;

        RectTransform slotPanel;
        TextMeshProUGUI slotTitle, slotFooter;
        readonly TextMeshProUGUI[,] slotCells = new TextMeshProUGUI[3, SlotCells];
        readonly TextMeshProUGUI[] slotHeads = new TextMeshProUGUI[3];
        readonly TextMeshProUGUI[] slotResults = new TextMeshProUGUI[3];
        readonly Image[] slotFrames = new Image[3];

        public bool SlotShown { get { return slotPanel && slotPanel.gameObject.activeSelf; } }
        public string SlotFooterText { get { return slotFooter ? slotFooter.text : null; } }

        void BuildSlotPanel()
        {
            slotPanel = NewRect(screenLayer, "Tragaperras");
            Anchor(slotPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 20 * Px), new Vector2(620 * Px, 400 * Px));
            var bg = MakeImage(slotPanel, "Fondo", new Color(0.09f, 0.05f, 0.04f, 0.94f));
            Stretch(bg.rectTransform);
            var border = MakeImage(slotPanel, "Marco", MathUtil.Hex(0xc9a227));
            Stretch(border.rectTransform);
            var inner = MakeImage(border.rectTransform, "Dentro", MathUtil.Hex(0x2a120e));
            Stretch(inner.rectTransform, 4 * Px);
            slotTitle = MakeText(slotPanel, "Titulo", "🎰 TRAGAPERRAS", 26, Fonts.Serif, MathUtil.Hex(0xffd36a));
            slotTitle.fontStyle = FontStyles.Bold;
            Outline(slotTitle, Color.black, 0.25f);
            Anchor(slotTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -26 * Px), new Vector2(600 * Px, 36 * Px));
            string[] heads = { "ENEMIGOS", "HABILIDAD", "AFINIDAD" };
            for (int r = 0; r < 3; r++)
            {
                float x = (r - 1) * (ReelW + 18f) * Px;
                slotHeads[r] = MakeText(slotPanel, "Cabecera" + r, heads[r], 13, Fonts.Body, MathUtil.Hex(0xc9b48d));
                Anchor(slotHeads[r].rectTransform, new Vector2(0.5f, 1f), new Vector2(x, -62 * Px), new Vector2(ReelW * Px, 18 * Px));
                slotFrames[r] = MakeImage(slotPanel, "Rodillo" + r, MathUtil.Hex(0x8a6a2a));
                var fr = slotFrames[r].rectTransform;
                Anchor(fr, new Vector2(0.5f, 1f), new Vector2(x, -(74 + ReelH / 2f) * Px), new Vector2(ReelW * Px, ReelH * Px));
                var face = MakeImage(fr, "Cara", MathUtil.Hex(0xf3e6c4));
                Stretch(face.rectTransform, 3 * Px);
                face.gameObject.AddComponent<RectMask2D>();   // las casillas que salen por arriba y abajo no se ven
                for (int c = 0; c < SlotCells; c++)
                {
                    var t = MakeText(face.rectTransform, "Casilla" + c, "", 34, Fonts.Body, MathUtil.Hex(0x2a1a10));
                    t.richText = true;
                    t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    t.rectTransform.sizeDelta = new Vector2(ReelW * Px, CellH * Px);
                    slotCells[r, c] = t;
                }
                // Línea de premio: la casilla del centro
                var line = MakeImage(fr, "Linea", new Color(1f, 0.25f, 0.2f, 0.35f));
                Anchor(line.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ReelW * Px, 3 * Px));
                slotResults[r] = MakeText(slotPanel, "Resultado" + r, "", 13, Fonts.Body, Ink);
                slotResults[r].textWrappingMode = TextWrappingModes.Normal;
                Anchor(slotResults[r].rectTransform, new Vector2(0.5f, 1f), new Vector2(x, -(82 + ReelH + 22) * Px), new Vector2(ReelW * Px, 40 * Px));
            }
            slotFooter = MakeText(slotPanel, "Pie", "", 14, Fonts.Body, MathUtil.Hex(0xc9b48d));
            slotFooter.richText = true;
            Anchor(slotFooter.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 22 * Px), new Vector2(600 * Px, 22 * Px));
            slotPanel.gameObject.SetActive(false);
        }

        public void ShowSlot(bool on)
        {
            if (!slotPanel) BuildSlotPanel();
            slotPanel.gameObject.SetActive(on);
            if (on) for (int r = 0; r < 3; r++) { slotResults[r].text = ""; slotFrames[r].color = MathUtil.Hex(0x8a6a2a); }
        }

        // Rodillo r en la posición pos (índice de opción, con decimales: la parte fraccionaria es el desplazamiento)
        public void SetSlotReel(int r, string[] icons, float pos, bool stopped)
        {
            if (!slotPanel) BuildSlotPanel();
            int n = icons.Length;
            int baseI = Mathf.FloorToInt(pos);
            float frac = pos - baseI;
            int mid = SlotCells / 2;
            for (int c = 0; c < SlotCells; c++)
            {
                int k = baseI + (c - mid);
                var t = slotCells[r, c];
                t.text = icons[((k % n) + n) % n];
                // La opción siguiente entra por arriba
                t.rectTransform.anchoredPosition = new Vector2(0f, (-(c - mid) + frac) * CellH * Px);
                t.alpha = stopped && c != mid ? 0.35f : 1f;
            }
            if (stopped) slotFrames[r].color = MathUtil.Hex(0xffd36a);
        }

        public void SetSlotResult(int r, string text) { if (slotPanel) slotResults[r].text = text; }
        public void SetSlotFooter(string text) { if (slotPanel) slotFooter.text = text; }
    }
}
