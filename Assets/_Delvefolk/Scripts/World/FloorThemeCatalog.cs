using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Catálogo de afinidades (Resources/FloorThemes). Si falta, se usan los valores por defecto de código.
    [CreateAssetMenu(fileName = "FloorThemes", menuName = "Delvefolk/Afinidades de piso")]
    public sealed class FloorThemeCatalog : ScriptableObject
    {
        public List<AffinityTheme> Themes = new List<AffinityTheme>();

        public AffinityTheme Get(Element e)
        {
            foreach (var t in Themes) if (t != null && t.Element == e) return t;
            return null;
        }

        static FloorThemeCatalog cached;
        static bool loaded;
        public static FloorThemeCatalog Load()
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<FloorThemeCatalog>("FloorThemes");
                if (!cached) { cached = CreateInstance<FloorThemeCatalog>(); cached.Themes = Defaults(); }
            }
            return cached;
        }

        static Color C(int hex) { return MathUtil.Hex(hex); }

        public static List<AffinityTheme> Defaults()
        {
            return new List<AffinityTheme>
            {
                new AffinityTheme { Element = Element.Water, Name = "Agua", Icon = "🌊", Explain = "Piedra húmeda y charcos. Sale el Trapeador.",
                    FloorTint = C(0x3f9fe8), WallTint = C(0x3a80d0), FloorAmount = 0.88f, WallAmount = 0.85f,
                    DecalA = C(0x3aa8ff), DecalB = C(0x8fe0ff), Glow = C(0x6fd0ff), DecalDensity = 0.2f, WeaponId = "mop" },
                new AffinityTheme { Element = Element.Fire, Name = "Fuego", Icon = "🔥", Explain = "Piedra caliente con grietas. Sale la Sartén Infernal.",
                    FloorTint = C(0xf0582a), WallTint = C(0xb83a22), FloorAmount = 0.88f, WallAmount = 0.85f,
                    DecalA = C(0xff6a1a), DecalB = C(0x2a1a14), Glow = C(0xff8a2a), DecalDensity = 0.22f, WeaponId = "hellpan" },
                new AffinityTheme { Element = Element.Plant, Name = "Planta", Icon = "🌿", Explain = "Musgo y enredaderas. Sale el Rastrillo.",
                    FloorTint = C(0x6fc048), WallTint = C(0x4f9a3a), FloorAmount = 0.88f, WallAmount = 0.85f,
                    DecalA = C(0x4f9a32), DecalB = C(0x8a6a3a), Glow = C(0x9fd36a), DecalDensity = 0.28f, WeaponId = "rake" },
                new AffinityTheme { Element = Element.Poison, Name = "Veneno", Icon = "🍄", Explain = "Manchas tóxicas y hongos. Sale el Paraguas de Esporas.",
                    FloorTint = C(0xa0c838), WallTint = C(0x8048c0), FloorAmount = 0.88f, WallAmount = 0.85f,
                    DecalA = C(0x9be04a), DecalB = C(0xa860d8), Glow = C(0xb8ff5a), DecalDensity = 0.22f, WeaponId = "parasol" },
                new AffinityTheme { Element = Element.Future, Name = "Futurista", Icon = "⚡", Explain = "Paneles de metal y luces. Sale la Aspiradora Cuántica.",
                    FloorTint = C(0x9cb0c8), WallTint = C(0x485a74), FloorAmount = 0.88f, WallAmount = 0.85f,
                    DecalA = C(0x4ff0ff), DecalB = C(0xff4ad8), Glow = C(0x4ff0ff), DecalDensity = 0.2f, WeaponId = "vacuum" },
            };
        }
    }
}
