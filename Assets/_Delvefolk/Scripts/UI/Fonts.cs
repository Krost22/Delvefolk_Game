using TMPro;
using UnityEngine;

namespace Delvefolk
{
    // Tipografías del original: títulos en serifa (Georgia) y cuerpo en Trebuchet MS.
    // Se crean en tiempo de ejecución desde las fuentes del sistema; si no existen, la de TextMeshPro.
    public static class Fonts
    {
        static TMP_FontAsset serif, body, fallback;
        static bool tried;

        public static TMP_FontAsset Serif { get { Init(); return serif; } }
        public static TMP_FontAsset Body { get { Init(); return body; } }

        static void Init()
        {
            if (tried && serif && body) return;
            tried = true;
            fallback = TMP_Settings.defaultFontAsset;
            if (!fallback) fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            serif = TryOs("Georgia", "Bold") ?? fallback;
            body = TryOs("Trebuchet MS", "Regular") ?? fallback;
            // Emojis y símbolos: si la fuente principal no los tiene, se buscan en la de Windows
            var emoji = TryOs("Segoe UI Emoji", "Regular");
            var symbol = TryOs("Segoe UI Symbol", "Regular");
            foreach (var f in new[] { serif, body })
            {
                if (!f || f == fallback) continue;
                if (f.fallbackFontAssetTable == null) f.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                if (emoji && !f.fallbackFontAssetTable.Contains(emoji)) f.fallbackFontAssetTable.Add(emoji);
                if (symbol && !f.fallbackFontAssetTable.Contains(symbol)) f.fallbackFontAssetTable.Add(symbol);
                if (fallback && !f.fallbackFontAssetTable.Contains(fallback)) f.fallbackFontAssetTable.Add(fallback);
            }
        }

        static TMP_FontAsset TryOs(string family, string style)
        {
            try
            {
                var f = TMP_FontAsset.CreateFontAsset(family, style, 90);
                if (f) f.name = family + " " + style;
                return f;
            }
            catch (System.Exception) { return null; }
        }
    }
}
