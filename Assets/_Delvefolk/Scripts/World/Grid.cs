using UnityEngine;

namespace Delvefolk
{
    // Conversión entre casillas del mapa ASCII y el mundo de Unity (¡crítico!).
    // three.js es dextrógiro y Unity levógiro: la fila 0 del ASCII queda al "norte" (+Z) y las filas crecen hacia −Z.
    //   casilla (tx, tz) → mundo (tx·2, 0, −tz·2)
    //   mundo (x, z) → casilla (round(x/2), round(−z/2)) con el redondeo de JS
    // Cualquier coordenada de mundo del web (x, z) escrita a mano se convierte a Unity como (x, −z).
    public static class Grid
    {
        public const float T = Cfg.Tile;

        public static float X(int tx) { return tx * T; }
        public static float Z(int tz) { return -tz * T; }
        public static Vector3 ToWorld(int tx, int tz) { return new Vector3(tx * T, 0f, -tz * T); }
        // Para coordenadas fraccionarias de casilla (centros de grupos, arcos entre dos casillas)
        public static Vector3 ToWorld(float tx, float tz) { return new Vector3(tx * T, 0f, -tz * T); }

        public static int TileX(float x) { return MathUtil.RoundJs(x / T); }
        public static int TileZ(float z) { return MathUtil.RoundJs(-z / T); }

        // Coordenada de mundo del web → Unity
        public static Vector2 FromWeb(float x, float z) { return new Vector2(x, -z); }

        // Dirección de casilla (ox, oz) del ASCII (oz = +1 es la fila siguiente, al sur) → dirección de mundo
        public static Vector3 Dir(int ox, int oz) { return new Vector3(ox, 0f, -oz); }
    }
}
