using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Delvefolk
{
    // Materiales compartidos (DF.Materials): casi todo usa colores por vértice, así que bastan unos pocos.
    // Nunca se destruyen al cambiar de piso.
    public static class Mats
    {
        static Shader shader;
        static Material lit, litDouble, unlit, unlitDouble, transparent, transparentLit, additive;
        static readonly Dictionary<Material, Material> fromAsset = new Dictionary<Material, Material>();
        static readonly Dictionary<(Texture, Color, bool, bool), Material> textured = new Dictionary<(Texture, Color, bool, bool), Material>();

        public static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int RecolorId = Shader.PropertyToID("_Recolor");
        public static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public static Shader Shader
        {
            get
            {
                if (!shader)
                {
                    var cat = Art.Catalog;
                    shader = cat != null && cat.FlatShader ? cat.FlatShader : Shader.Find("Delvefolk/Flat");
                }
                return shader;
            }
        }

        // Opaco con iluminación y sombreado plano (MeshLambertMaterial flatShading)
        public static Material Lit { get { return lit ? lit : (lit = Make("df_lit", false, false, false, false)); } }
        public static Material LitDouble { get { return litDouble ? litDouble : (litDouble = Make("df_litDouble", false, false, false, true)); } }
        // Sin iluminación (MeshBasicMaterial): llamas, ojos, fondo de los fosos
        public static Material Unlit { get { return unlit ? unlit : (unlit = Make("df_unlit", true, false, false, false)); } }
        public static Material UnlitDouble { get { return unlitDouble ? unlitDouble : (unlitDouble = Make("df_unlitDouble", true, false, false, true)); } }
        // Transparentes (la opacidad va en el alfa del color de vértice)
        public static Material Transparent { get { return transparent ? transparent : (transparent = Make("df_transparent", true, true, false, true)); } }
        public static Material TransparentLit { get { return transparentLit ? transparentLit : (transparentLit = Make("df_transparentLit", false, true, false, false)); } }
        public static Material Additive { get { return additive ? additive : (additive = Make("df_additive", true, true, true, true)); } }

        public static Material Make(string name, bool isUnlit, bool isTransparent, bool isAdditive, bool doubleSided, bool vertexColor = true)
        {
            var m = new Material(Shader) { name = name };
            m.SetFloat("_Unlit", isUnlit ? 1f : 0f);
            m.SetFloat("_UseVertexColor", vertexColor ? 1f : 0f);
            m.SetFloat("_FlatShading", 1f);
            m.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (isTransparent)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", isAdditive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            m.enableInstancing = true;
            return m;
        }

        // Material con textura procedural (ladrillos, losas, madera); el tinte multiplica
        public static Material Textured(Texture tex, Color tint, bool vertexColor = false, bool doubleSided = false)
        {
            var key = (tex, tint, vertexColor, doubleSided);
            Material m;
            if (textured.TryGetValue(key, out m) && m) return m;
            m = Make("df_tex_" + tex.name, false, false, false, doubleSided, vertexColor);
            m.SetTexture("_BaseMap", tex);
            m.SetColor(BaseColorId, tint);
            textured[key] = m;
            return m;
        }

        static readonly Dictionary<int, Material> solids = new Dictionary<int, Material>();

        // Material opaco de un color fijo (para mallas sin color de vértice)
        public static Material Solid(int hex)
        {
            Material m;
            if (solids.TryGetValue(hex, out m) && m) return m;
            m = Make("df_solid_" + hex.ToString("x6"), false, false, false, false, false);
            m.SetColor(BaseColorId, MathUtil.Hex(hex));
            solids[hex] = m;
            return m;
        }

        // Equivalente con nuestro shader de un material de los paquetes de assets (textura, color, emisión)
        public static Material FromAsset(Material src)
        {
            if (!src) return Lit;
            Material m;
            if (fromAsset.TryGetValue(src, out m) && m) return m;
            Texture tex = null;
            if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
            if (!tex && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
            Color col = Color.white;
            if (src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");
            else if (src.HasProperty("_Color")) col = src.GetColor("_Color");
            bool isTransparent = src.renderQueue >= 2500 || src.name.ToLowerInvariant().Contains("light");
            m = Make("df_" + src.name, false, false, false, false, false);
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor(BaseColorId, new Color(col.r, col.g, col.b, 1f));
            m.SetFloat("_FlatShading", 0f);
            if (src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
            {
                m.SetColor(EmissionId, src.GetColor("_EmissionColor"));
                if (src.HasProperty("_EmissionMap") && src.GetTexture("_EmissionMap")) m.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
            }
            // Materiales de "luz" (llamas, cristales): sin iluminación para que brillen en la oscuridad
            if (isTransparent) m.SetFloat("_Unlit", 1f);
            fromAsset[src] = m;
            return m;
        }

        // Ambiente del juego: luz ambiente + hemisférica (cielo/suelo), en espacio lineal
        public static void SetAmbient(Color ambient, float ambientIntensity, Color sky, Color ground, float hemiIntensity)
        {
            Shader.SetGlobalVector("_DfAmbient", (Vector4)(ambient.linear * ambientIntensity));
            Shader.SetGlobalVector("_DfHemiSky", (Vector4)(sky.linear * hemiIntensity));
            Shader.SetGlobalVector("_DfHemiGround", (Vector4)(ground.linear * hemiIntensity));
        }
    }

    // Texturas procedurales (canvas del original): ladrillo, losas y madera
    public static class ProcTex
    {
        static Texture2D bricks, floorStones, wood;

        public static Texture2D Bricks { get { return bricks ? bricks : (bricks = MakeBricks()); } }
        public static Texture2D FloorStones { get { return floorStones ? floorStones : (floorStones = MakeFloor()); } }
        public static Texture2D Wood { get { return wood ? wood : (wood = MakeWood()); } }

        static Texture2D New(string name, int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
        }

        static void Rect(Color32[] px, int s, int x, int y, int w, int h, Color c, float a = 1f)
        {
            for (int j = Mathf.Max(0, y); j < Mathf.Min(s, y + h); j++)
                for (int i = Mathf.Max(0, x); i < Mathf.Min(s, x + w); i++)
                {
                    // y de canvas hacia abajo: fila invertida
                    int k = (s - 1 - j) * s + i;
                    Color b = px[k];
                    px[k] = Color.Lerp(b, c, a);
                }
        }

        static void Noise(Color32[] px, float amt, Rng r)
        {
            for (int k = 0; k < px.Length; k++)
            {
                int n = (int)((r.Next() - 0.5f) * amt);
                var c = px[k];
                px[k] = new Color32((byte)Mathf.Clamp(c.r + n, 0, 255), (byte)Mathf.Clamp(c.g + n, 0, 255), (byte)Mathf.Clamp(c.b + n, 0, 255), 255);
            }
        }

        static Texture2D MakeBricks()
        {
            const int s = 256;
            var r = new Rng(7);
            var px = new Color32[s * s];
            Rect(px, s, 0, 0, s, s, MathUtil.Hex(0x2a2622));
            int rows = 4, bh = s / rows;
            for (int row = 0; row < rows; row++)
            {
                int bw = s / 2, off = (row % 2) * bw / 2;
                for (int i = -1; i < 3; i++)
                {
                    int x = i * bw + off;
                    float v = 80 + r.Next() * 40;
                    Rect(px, s, x + 3, row * bh + 3, bw - 6, bh - 6, new Color((v + 8) / 255f, v / 255f, (v - 10) / 255f));
                    Rect(px, s, x + 3, row * bh + 3, bw - 6, 4, Color.white, 0.06f);
                    Rect(px, s, x + 3, row * bh + bh - 9, bw - 6, 6, Color.black, 0.18f);
                }
            }
            Noise(px, 22, r);
            var t = New("df_bricks", s); t.SetPixels32(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeFloor()
        {
            const int s = 256;
            var r = new Rng(3);
            var px = new Color32[s * s];
            Rect(px, s, 0, 0, s, s, MathUtil.Hex(0x1c1a18));
            int n = 2, b = s / n;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float v = 70 + r.Next() * 30;
                    Rect(px, s, i * b + 3, j * b + 3, b - 6, b - 6, new Color((v + 6) / 255f, v / 255f, (v - 8) / 255f));
                }
            Noise(px, 26, r);
            var t = New("df_floor", s); t.SetPixels32(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeWood()
        {
            const int s = 128;
            var r = new Rng(11);
            var px = new Color32[s * s];
            Rect(px, s, 0, 0, s, s, MathUtil.Hex(0x7a4f2a));
            for (int i = 0; i < 40; i++)
            {
                int y = (int)(r.Next() * s);
                Rect(px, s, 0, y, s, 1 + (int)(r.Next() * 2), new Color((40 + r.Next() * 30) / 255f, (20 + r.Next() * 15) / 255f, 5 / 255f), 0.35f);
            }
            for (int i = 0; i < 4; i++) Rect(px, s, i * s / 4, 0, 2, s, Color.black, 0.4f);
            Noise(px, 14, r);
            var t = New("df_wood", s); t.SetPixels32(px); t.Apply(true);
            return t;
        }
    }
}
