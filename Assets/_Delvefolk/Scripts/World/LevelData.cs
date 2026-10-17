using System;
using System.Collections.Generic;
using System.Linq;

namespace Delvefolk
{
    // Sala: rectángulo de casillas (inclusivo). Al entrar se despiertan sus enemigos;
    // "Opens" indica el rastrillo que se abre cuando la sala queda limpia.
    public sealed class RoomDef
    {
        public string Id, Name, Hint, Opens;
        public int X0, Z0, X1, Z1;
        public RoomDef(string id, string name, int[] rect, string hint, string opens = null)
        {
            Id = id; Name = name; Hint = hint; Opens = opens;
            X0 = rect[0]; Z0 = rect[1]; X1 = rect[2]; Z1 = rect[3];
        }
    }

    public sealed class LevelDef
    {
        public string Name;
        public int Fog = 0x050407;
        public string[] Map;
        public RoomDef[] Rooms = new RoomDef[0];
        public float HpMult = 1f;
    }

    // Mapas de los pisos (Levels.js / Anexo A). Cada carácter es una casilla de 2×2 unidades.
    //  #  muro            .  suelo           _  foso (abismo)      ^  púas
    //  P  jugador          X  salida          1/2/3 rastrillo (se abre al limpiar la sala que lo indica)
    //  S  slime            G  goblin          L  slime gordo     A  abrazón (te agarra)
    //  b  barril           e  barril de pólvora   c  cajas     o  columna
    //  t  antorcha (en muro)   N  puerta de entrada (en muro)  h  muslo de pollo (cura)
    //  B  escoba   F  sartén   U  cubo   K  palo   T  tomo pesado   J  pata de silla   q  queso
    //  Muebles rompibles (sueltan objetos): l estantería (pegada a la pared)   r armero (pegado a la pared)
    //                                       m mesa   v jarrón
    public static class LevelData
    {
        public static readonly LevelDef[] Levels =
        {
            new LevelDef
            {
                Name = "Piso 1 — Los Sótanos",
                Fog = 0x07060a,
                Map = new[]
                {
                    "   #####t########t#####                   ",
                    "   #bb...l...l........#                   ",
                    "   #................e.#                   ",
                    "####..G..o......o.G...#                   ",
                    "#X.2.......^^^^.......#                   ",
                    "#..2..................t                   ",
                    "####.....S......S.___.#                   ",
                    "   t..............___.#                   ",
                    "   #.e..^........^___.#                   ",
                    "   #v...........m....r#                   ",
                    "   #########..#########                   ",
                    "           #..#                           ",
                    "           #..#                           ",
                    "           #..#                           ",
                    "   #########11##t####                     ",
                    "   #..l..l..........#                     ",
                    "   #.e...........bb.#     #########t######",
                    "   #......^^........t     #...^^^^..l.l..#",
                    "   #...G..^^...o....#     t...^^^^.....F.#",
                    "   #..........A.....#     #.............r#",
                    "   t....m...........###t###..............#",
                    "   #....o....................o..S.....o..t",
                    "   #v........^...........................#",
                    "   #.........^......#######.............b#",
                    "   #.^^............r#     #.K.....m.....b#",
                    "   #.^^..G....___...#     #............v.#",
                    "   #..........___...#     #######..#######",
                    "   #.......e..___U..#           #..#      ",
                    "   #h..............v#           #..t      ",
                    "   #######t##########           #..#      ",
                    "                             ####..#####  ",
                    "                             #.b.....b.#  ",
                    "                             #.v.....v.#  ",
                    "                             #....B....#  ",
                    "                             t.........t  ",
                    "                             #r......m.#  ",
                    "                             #....P....#  ",
                    "                             #c.......c#  ",
                    "                             #####N#####  ",
                },
                Rooms = new[]
                {
                    new RoomDef("A", "La Entrada", new[] { 30, 31, 38, 37 }, "WASD para moverte. Esa cosa del suelo... ¿es una escoba?"),
                    new RoomDef("B", "Sala de los Trastos", new[] { 27, 17, 40, 25 }, "¡Un slime! Barrerlo hacia las púas también cuenta como pelear."),
                    new RoomDef("C", "Sala de Combate", new[] { 4, 15, 19, 28 }, "Paredes, púas, barriles, un foso... la mazmorra es tu arma.", "1"),
                    new RoomDef("D", "Cámara del Fondo", new[] { 4, 1, 21, 9 }, "Limpia la sala para que se abra la salida.", "2"),
                },
            },
            new LevelDef
            {
                Name = "Piso 2 — Las Cloacas del Rey Slime",
                Fog = 0x050808,
                Map = new[]
                {
                    "   ########t########              ",
                    "   #c.l..l...^.__..####           ",
                    "   #...^....G....^.2.X#           ",
                    "   t..S...L...S....2..#           ",
                    "   #...^.^......G..t###           ",
                    "   #h.......e^....v#              ",
                    "   ####..###########              ",
                    "      #..#                        ",
                    "      #..#                        ",
                    "      #..#                        ",
                    "  #####11######                   ",
                    "  #.l.....l..b#                   ",
                    "  #b....e.....#                   ",
                    "  #..G..^..G..t    #####t#########",
                    "  #...^...^...#    #b...l...l...e#",
                    "  #..o.___.o..##t###..^.^.^G^.^..#",
                    "  t....___...........o..S..A.....#",
                    "  #.eo.___.o...........^.^.^.^S^.t",
                    "  #...^...^...######.K.........o.#",
                    "  #v....^.....#    #v.^...^...^..#",
                    "  #..G.....S.r#    #########..####",
                    "  #....m....U.#            #..#   ",
                    "  #h..........#            #..#   ",
                    "  ######t######            #..#   ",
                    "                         ###..t## ",
                    "                         #b....b# ",
                    "                         #v.....# ",
                    "                         t.....rt ",
                    "                         #..P...# ",
                    "                         #c....h# ",
                    "                         ###N#### ",
                },
                Rooms = new[]
                {
                    new RoomDef("A", "El Rellano", new[] { 26, 25, 31, 29 }, "Piso 2. Aquí abajo hay más bichos y menos trastos."),
                    new RoomDef("B", "Galería de Púas", new[] { 20, 14, 32, 19 }, "Un suelo lleno de púas... para ellos, no para ti."),
                    new RoomDef("C", "El Foso", new[] { 3, 11, 13, 22 }, "Lo que cae al foso no vuelve a subir.", "1"),
                    new RoomDef("D", "Trono del Slime", new[] { 4, 1, 18, 5 }, "¡El Rey Slime! Si se parte en trozos, sigue empujando.", "2"),
                },
            },
        };

        // Pisos extra: el mapa de ((n−1) mod 2)+1 espejado horizontalmente, con enemigos más duros
        public static LevelDef For(int n)
        {
            var L = Levels;
            if (n <= L.Length) return L[n - 1];
            var b = L[(n - 1) % L.Length];
            int w = b.Map.Max(r => r.Length);
            return new LevelDef
            {
                Name = "Piso " + n + " — Más abajo todavía",
                Fog = b.Fog,
                Map = b.Map.Select(r => new string(r.PadRight(w, ' ').Reverse().ToArray())).ToArray(),
                Rooms = b.Rooms.Select(r => new RoomDef(r.Id, r.Name, new[] { w - 1 - r.X1, r.Z0, w - 1 - r.X0, r.Z1 }, r.Hint, r.Opens)).ToArray(),
                HpMult = 1f + 0.25f * (n - L.Length),
            };
        }

        // Construye un mapa rectangular: marco de muros con suelo dentro y letras colocadas en casillas concretas
        public static string[] Frame(int w, int h, IEnumerable<KeyValuePair<(int x, int z), char>> marks = null)
        {
            var rows = new char[h][];
            for (int z = 0; z < h; z++)
            {
                rows[z] = new char[w];
                for (int x = 0; x < w; x++) rows[z][x] = (x == 0 || z == 0 || x == w - 1 || z == h - 1) ? '#' : '.';
            }
            if (marks != null) foreach (var m in marks) rows[m.Key.z][m.Key.x] = m.Value;
            return rows.Select(r => new string(r)).ToArray();
        }
    }
}
