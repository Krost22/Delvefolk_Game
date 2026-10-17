using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Barra de vida del jefe arriba en el centro (#boss-bar): la usan el Duende Tendero y los jefes
    public sealed partial class Hud
    {
        RectTransform bossBar;
        TextMeshProUGUI bossName;
        RectTransform bossFill;

        public bool BossShown { get { return bossBar && bossBar.gameObject.activeSelf; } }
        public string BossName { get { return bossName ? bossName.text : null; } }
        public float BossFill { get; private set; }

        void BuildBossBar()
        {
            bossBar = NewRect(screenLayer, "BarraJefe");
            Anchor(bossBar, new Vector2(0.5f, 1f), new Vector2(0, -132 * Px), new Vector2(460 * Px, 48 * Px));
            bossName = MakeText(bossBar, "Nombre", "", 18, Fonts.Serif, MathUtil.Hex(0xff9a6a));
            bossName.fontStyle = FontStyles.Bold;
            Outline(bossName, Color.black, 0.25f);
            Anchor(bossName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -12 * Px), new Vector2(460 * Px, 24 * Px));
            var border = MakeImage(bossBar, "Borde", Color.black);
            Anchor(border.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 9 * Px), new Vector2(460 * Px, 18 * Px));
            var bg = MakeImage(border.rectTransform, "Fondo", MathUtil.Hex(0x2a0f0f));
            Stretch(bg.rectTransform, 2 * Px);
            var fill = MakeImage(bg.rectTransform, "Relleno", MathUtil.Hex(0xd04428));
            bossFill = fill.rectTransform;
            bossFill.anchorMin = Vector2.zero; bossFill.anchorMax = Vector2.one;
            bossFill.pivot = new Vector2(0f, 0.5f);
            bossFill.offsetMin = bossFill.offsetMax = Vector2.zero;
        }

        public void SetBoss(string name, float k)
        {
            if (!bossBar) BuildBossBar();
            if (!bossBar.gameObject.activeSelf) bossBar.gameObject.SetActive(true);
            if (bossName.text != name) bossName.text = name;
            BossFill = Mathf.Max(0f, k);
            bossFill.anchorMax = new Vector2(Mathf.Clamp01(k), 1f);
        }

        public void HideBoss() { if (bossBar) bossBar.gameObject.SetActive(false); }
    }
}
