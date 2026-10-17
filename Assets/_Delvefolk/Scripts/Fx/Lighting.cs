using UnityEngine;

namespace Delvefolk
{
    // Luces de la escena (Game.js): ambiente + hemisférica por shader, "luna" direccional, lámpara por jugador,
    // pool fijo de luces de antorcha y la luz de la salida. Las intensidades del original (three.js, sin
    // corrección física) se escalan a URP, cuya caída es de inversa del cuadrado.
    public static class Lighting
    {
        // Factor de conversión de intensidad de luces puntuales (three.js legacy → URP)
        public const float PointScale = 7f;
        public const float DirScale = 1f;

        public static Light Point(Transform parent, string name, int color, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = MathUtil.Hex(color);
            l.range = range;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        // Ambiente del original: AmbientLight(#6a5f80, 0.5) + HemisphereLight(#8090b0, #2a1d14, 0.35)
        public static void ApplyAmbient()
        {
            Mats.SetAmbient(MathUtil.Hex(0x6a5f80), 0.5f, MathUtil.Hex(0x8090b0), MathUtil.Hex(0x2a1d14), 0.35f);
        }

        public static void SetFog(int color, float near, float far)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = MathUtil.Hex(color);
            RenderSettings.fogStartDistance = near;
            RenderSettings.fogEndDistance = far;
        }
    }
}
