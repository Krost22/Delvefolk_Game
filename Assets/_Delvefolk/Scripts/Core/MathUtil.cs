using System;
using System.Collections.Generic;

namespace Delvefolk
{
    // Constantes globales del juego (DF.CONFIG del original)
    public static class Cfg
    {
        public const float Tile = 2f;          // tamaño de una casilla del mapa en unidades de mundo
        public const float WallH = 3.2f;       // altura de los muros
        public const float WallLow = 0.45f;    // altura de los muros "recortados" que tapan al jugador
        public const int PlayerMaxHp = 100;
    }

    // Utilidades matemáticas (DF.Utils). Todo en float salvo donde importa la exactitud.
    public static class MathUtil
    {
        public const float PI = (float)Math.PI;
        public const float TAU = (float)(Math.PI * 2);

        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        // Suavizado exponencial independiente del framerate
        public static float Damp(float a, float b, float k, float dt) { return a + (b - a) * (1f - (float)Math.Exp(-k * dt)); }

        // Diferencia angular en (-π, π], con el mismo resto que JS (% conserva el signo del dividendo)
        public static float AngleDiff(float a, float b)
        {
            float d = (b - a) % TAU;
            if (d > PI) d -= TAU;
            if (d < -PI) d += TAU;
            return d;
        }

        public static float DampAngle(float a, float b, float k, float dt) { return a + AngleDiff(a, b) * (1f - (float)Math.Exp(-k * dt)); }

        public static float Len(float x, float z) { return (float)Math.Sqrt(x * x + z * z); }
        public static float Hypot(float x, float z) { return (float)Math.Sqrt(x * x + z * z); }

        // Math.round de JS: redondea .5 hacia +∞ (Mathf.Round redondea al par: no sirve para las casillas)
        public static int RoundJs(float v) { return (int)Math.Floor(v + 0.5f); }

        public static float EaseOutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }
        public static float EaseInCubic(float t) { return t * t * t; }
        public static float EaseInOut(float t) { return t < 0.5f ? 2f * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 2) / 2f; }
        public static float EaseOutBack(float t) { const float c = 1.9f; float u = t - 1f; return 1f + (c + 1f) * u * u * u + c * u * u; }

        public static float Sin(float a) { return (float)Math.Sin(a); }
        public static float Cos(float a) { return (float)Math.Cos(a); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Abs(float v) { return v < 0 ? -v : v; }
        public static float Sign(float v) { return v > 0 ? 1f : v < 0 ? -1f : 0f; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Exp(float v) { return (float)Math.Exp(v); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }

        // Color desde 0xRRGGBB (los colores del original vienen así)
        public static UnityEngine.Color Hex(int rgb, float a = 1f)
        {
            return new UnityEngine.Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        }
    }

    // Generador pseudoaleatorio con semilla: mulberry32, idéntico al de Utils.js
    public sealed class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed; }
        public Rng(int seed) { s = unchecked((uint)seed); }

        public uint State { get { return s; } set { s = value; } }

        // Valor exacto del original en [0, 1)
        public double NextDouble()
        {
            unchecked
            {
                s += 0x6D2B79F5u;
                uint t = s;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        // En float: nunca devuelve 1 (el redondeo de 0.9999999998 a float daría 1)
        public float Next()
        {
            float f = (float)NextDouble();
            return f >= 1f ? 0.99999994f : f;
        }

        public float Range(float a, float b) { return a + Next() * (b - a); }
        // Entero en [a, b] (ambos incluidos), como a + floor(r * (b - a + 1))
        public int IntIncl(int a, int b) { return a + (int)Math.Floor(Next() * (b - a + 1)); }
        public bool Chance(float p) { return Next() < p; }

        public T Pick<T>(IList<T> list) { return list[(int)Math.Floor(Next() * list.Count)]; }

        public void Shuffle<T>(IList<T> arr)
        {
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(NextDouble() * (i + 1));   // en doble precisión, como el original
                T tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp;
            }
        }

        // Elección ponderada (DF.Utils.weighted): weightOf devuelve el peso de cada elemento
        public T Weighted<T>(IList<T> items, Func<T, float> weightOf)
        {
            float total = 0f;
            for (int i = 0; i < items.Count; i++) total += Math.Max(0f, weightOf(items[i]));
            float r = Next() * total;
            for (int i = 0; i < items.Count; i++)
            {
                r -= Math.Max(0f, weightOf(items[i]));
                if (r < 0) return items[i];
            }
            return items.Count > 0 ? items[items.Count - 1] : default(T);
        }
    }
}
