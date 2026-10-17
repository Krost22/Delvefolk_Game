using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Delvefolk.Tests
{
    // Fase 1: rejilla, mapas, física de círculos (sin escena)
    public class WorldTests
    {
        static LevelDef Map(params string[] rows) { return new LevelDef { Name = "Prueba", Map = rows }; }

        static readonly LevelDef Box = Map(
            "##########",
            "#........#",
            "#........#",
            "#...#....#",
            "#........#",
            "#..__....#",
            "#..__....#",
            "##########");

        // Ningún punto del círculo puede quedar dentro de una casilla sólida (con un margen numérico)
        static void AssertOutsideSolids(Dungeon d, Body b, string ctx)
        {
            int tx = Grid.TileX(b.X), tz = Grid.TileZ(b.Z);
            Assert.IsFalse(d.IsSolidFor(tx, tz, b), ctx + ": el centro quedó dentro de una casilla sólida (" + tx + "," + tz + ")");
            for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int x = tx + ox, z = tz + oz;
                    if (!d.IsSolidFor(x, z, b)) continue;
                    float cx = Mathf.Clamp(b.X, x * 2f - 1f, x * 2f + 1f);
                    float cz = Mathf.Clamp(b.Z, -z * 2f - 1f, -z * 2f + 1f);
                    float dist = Mathf.Sqrt((b.X - cx) * (b.X - cx) + (b.Z - cz) * (b.Z - cz));
                    Assert.GreaterOrEqual(dist, b.Radius - 0.02f, ctx + ": atraviesa la casilla (" + x + "," + z + ")");
                }
        }

        [Test]
        public void Grid_ConversionCasillaMundoYRedondeoDeJs()
        {
            Assert.AreEqual(new Vector3(6, 0, -8), Grid.ToWorld(3, 4));
            Assert.AreEqual(3, Grid.TileX(6f)); Assert.AreEqual(4, Grid.TileZ(-8f));
            // La fila crece hacia −Z: lo que está más al norte en el ASCII tiene Z mayor
            Assert.Greater(Grid.ToWorld(0, 0).z, Grid.ToWorld(0, 5).z);
            // Frontera entre casillas (x = 1): round de JS manda el .5 a la casilla de la derecha
            Assert.AreEqual(1, Grid.TileX(1f));
            Assert.AreEqual(0, Grid.TileX(0.999f));
            Assert.AreEqual(0, Grid.TileZ(-0.999f));
            Assert.AreEqual(1, Grid.TileZ(-1f));
            Assert.AreEqual(new Vector2(5, -7), Grid.FromWeb(5, 7));
        }

        [Test]
        public void Piso1_SeLeeIgualQueElAscii()
        {
            var d = new Dungeon(LevelData.Levels[0]);
            Assert.AreEqual(42, d.W); Assert.AreEqual(39, d.H);
            Spawn p = default(Spawn);
            foreach (var s in d.Spawns) if (s.Ch == 'P') p = s;
            Assert.AreEqual(34, p.Tx); Assert.AreEqual(36, p.Tz);
            Assert.IsTrue(d.HasExit);
            Assert.AreEqual(Grid.X(1), d.ExitX); Assert.AreEqual(Grid.Z(4), d.ExitZ);
            Assert.AreEqual(TileType.Pit, d.TypeAt(19, 6));
            Assert.AreEqual(TileType.Wall, d.TypeAt(8, 0));     // antorcha
            Assert.AreEqual(1, d.Spike[d.Idx(11, 4)]);
            Assert.AreEqual(1, d.Blocked[d.Idx(9, 3)]);         // columna
            Assert.AreEqual(1, d.Blocked[d.Idx(9, 1)]);         // estantería
            Assert.AreEqual(0, d.Blocked[d.Idx(4, 9)]);         // el jarrón no bloquea
            Assert.AreEqual(2, d.Gates.Count);
            Assert.IsTrue(d.Gates['1'].Horizontal);
            Assert.IsFalse(d.Gates['2'].Horizontal);
            Assert.AreEqual(2, d.Gates['2'].Tiles.Count);
            Assert.AreEqual(4, d.Rooms.Count);
            Assert.AreEqual("La Entrada", d.RoomAt(p.X, p.Z).Name);
        }

        [Test]
        public void PisosEspejo_ReflejanMapaYSalasYEndurecenEnemigos()
        {
            var b = LevelData.Levels[0];
            var m = LevelData.For(3);
            Assert.AreEqual("Piso 3 — Más abajo todavía", m.Name);
            Assert.AreEqual(1.25f, m.HpMult, 1e-6f);
            Assert.AreEqual(42, m.Map[0].Length);
            Assert.AreEqual(b.Map[4][1], m.Map[4][40]);         // la salida pasa al otro lado
            Assert.AreEqual(new[] { 42 - 1 - 38, 31, 42 - 1 - 30, 37 }, new[] { m.Rooms[0].X0, m.Rooms[0].Z0, m.Rooms[0].X1, m.Rooms[0].Z1 });
            Assert.AreEqual(1.5f, LevelData.For(4).HpMult, 1e-6f);
            Assert.AreEqual(LevelData.Levels[1].Map.Length, LevelData.For(4).Map.Length);
        }

        [Test]
        public void Rastrillos_SoloSonTransitablesPasadoEl55PorCiento()
        {
            var d = new Dungeon(LevelData.Levels[0]);
            var g = d.Gates['1'];
            var body = new Body();
            var t = g.Tiles[0];
            Assert.IsTrue(d.IsSolidFor(t.x, t.y, body));
            Assert.IsNotNull(d.OpenGate("1"));
            Assert.IsNull(d.OpenGate("1"));                    // abrir dos veces no hace nada
            d.Update(0.55f);                                   // t = 0.5
            Assert.IsTrue(d.IsSolidFor(t.x, t.y, body));
            d.Update(0.1f);                                    // t ≈ 0.59
            Assert.IsFalse(d.IsSolidFor(t.x, t.y, body));
            Assert.IsTrue(d.NavWalkable(t.x, t.y));
        }

        [Test]
        public void LineaDeVision_LosFososNoTapanLosMurosSi()
        {
            var d = new Dungeon(Box);
            // A través del foso (filas 5-6, columnas 3-4)
            Assert.IsTrue(d.LineClear(Grid.X(2), Grid.Z(5), Grid.X(6), Grid.Z(6)));
            // A través de la columna de muro (4, 3)
            Assert.IsFalse(d.LineClear(Grid.X(2), Grid.Z(3), Grid.X(6), Grid.Z(3)));
            // Caminar en línea recta no puede cruzar el foso
            Assert.IsFalse(d.WalkClear(Grid.X(2), Grid.Z(5), Grid.X(6), Grid.Z(5), 0.5f));
            Assert.IsTrue(d.WalkClear(Grid.X(1), Grid.Z(1), Grid.X(8), Grid.Z(1), 0.4f));
        }

        [Test]
        public void Navegacion_DijkstraOchoDireccionesSinCortarEsquinasYPuasCaras()
        {
            var d = new Dungeon(Map(
                "#######",
                "#.....#",
                "#.#...#",
                "#..^..#",
                "#######"));
            d.UpdateNav(new List<Vector2Int> { new Vector2Int(1, 1) });
            Assert.AreEqual(0f, d.NavDist[d.Idx(1, 1)]);
            Assert.AreEqual(1f, d.NavDist[d.Idx(2, 1)], 1e-4f);
            Assert.AreEqual(1f, d.NavDist[d.Idx(1, 2)], 1e-4f);
            // (2,2) es muro: a (2,3) no se llega en diagonal desde (1,2) cortando la esquina
            Assert.AreEqual(2f, d.NavDist[d.Idx(1, 3)], 1e-4f);
            Assert.AreEqual(3f, d.NavDist[d.Idx(2, 3)], 1e-4f);
            // (3,2) no se alcanza en diagonal desde (2,1): cortaría la esquina del muro (2,2)
            Assert.AreEqual(3f, d.NavDist[d.Idx(3, 2)], 1e-3f);
            // Diagonal libre: (4,2) desde (3,1)
            Assert.AreEqual(2f + 1.414f, d.NavDist[d.Idx(4, 2)], 1e-3f);
            // Las púas cuestan ×6
            Assert.AreEqual(d.NavDist[d.Idx(2, 3)] + 6f, d.NavDist[d.Idx(3, 3)], 1e-3f);
            float dx, dz;
            Assert.IsTrue(d.NavDirection(Grid.X(1), Grid.Z(3), out dx, out dz));
            Assert.AreEqual(0f, dx, 1e-4f); Assert.AreEqual(1f, dz, 1e-4f);   // hacia el norte (+Z)
        }

        [Test]
        public void Fisica_NoAtraviesaMurosA40EnLineaNiEnEsquinas()
        {
            var d = new Dungeon(Box);
            var dirs = new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 1).normalized, new Vector2(-1, -1).normalized, new Vector2(1, -1).normalized, new Vector2(-1, 1).normalized };
            foreach (var dir in dirs)
            {
                foreach (bool asControl in new[] { false, true })
                {
                    var ph = new CirclePhysics(d);
                    var b = ph.Add(new Body { X = Grid.X(6), Z = Grid.Z(2), Radius = 0.42f, KnockDrag = 7f, Restitution = 0.15f });
                    for (int f = 0; f < 240; f++)
                    {
                        if (asControl) { b.Mvx = dir.x * 40f; b.Mvz = dir.y * 40f; }
                        else if (f % 30 == 0) { b.Kx = dir.x * 40f; b.Kz = dir.y * 40f; }
                        ph.Step(1f / 60f);
                        AssertOutsideSolids(d, b, "dir " + dir + (asControl ? " (control)" : " (empuje)") + " frame " + f);
                    }
                }
            }
        }

        [Test]
        public void Fisica_ConDtGrandeTampocoSeAtraviesa()
        {
            var d = new Dungeon(Box);
            var ph = new CirclePhysics(d);
            var b = ph.Add(new Body { X = Grid.X(2), Z = Grid.Z(2), Radius = 0.42f });
            for (int f = 0; f < 50; f++)
            {
                b.Kx = 40f; b.Kz = 25f;
                ph.Step(0.05f);   // dt máximo del bucle
                AssertOutsideSolids(d, b, "frame " + f);
            }
        }

        [Test]
        public void Fisica_ElFosoBloqueaSegunElCuerpo()
        {
            var d = new Dungeon(Box);
            // Jugador: el foso es sólido para él
            var ph = new CirclePhysics(d);
            var player = ph.Add(new Body { X = Grid.X(2), Z = Grid.Z(5), Radius = 0.42f, PitSolid = () => true });
            for (int f = 0; f < 120; f++) { player.Kx = 40f; ph.Step(1f / 60f); Assert.IsFalse(d.IsPitAt(player.X, player.Z)); }
            // Barril: cae (el foso no le bloquea)
            var barrel = ph.Add(new Body { X = Grid.X(2), Z = Grid.Z(6), Radius = 0.5f, PitSolid = () => false });
            barrel.Kx = 12f;
            bool fell = false;
            for (int f = 0; f < 60 && !fell; f++) { ph.Step(1f / 60f); fell = d.IsPitAt(barrel.X, barrel.Z); }
            Assert.IsTrue(fell);
        }

        [Test]
        public void Fisica_ImpactosRapidosIntercambianImpulsoYAvisan()
        {
            var d = new Dungeon(Box);
            var ph = new CirclePhysics(d);
            var a = ph.Add(new Body { X = Grid.X(3), Z = Grid.Z(1), Radius = 0.5f, Mass = 1f });
            var b = ph.Add(new Body { X = Grid.X(3) + 1.2f, Z = Grid.Z(1), Radius = 0.5f, Mass = 1f });
            float hit = 0f;
            b.OnBodyImpact = (o, s, nx, nz) => hit = s;
            a.Kx = 12f;
            for (int f = 0; f < 10; f++) ph.Step(1f / 60f);
            Assert.Greater(hit, 2.5f);
            Assert.Greater(b.Kx, 3f);          // b sale despedido
            // Un empujón lento solo separa (sin impulso)
            var c = ph.Add(new Body { X = Grid.X(6), Z = Grid.Z(4), Radius = 0.5f });
            var e = ph.Add(new Body { X = Grid.X(6) + 0.8f, Z = Grid.Z(4), Radius = 0.5f });
            c.Mvx = 2f;
            ph.Step(1f / 60f);
            Assert.AreEqual(0f, e.Kx, 1e-5f);
            Assert.GreaterOrEqual(Vector2.Distance(new Vector2(c.X, c.Z), new Vector2(e.X, e.Z)), 0.999f);
        }

        [Test]
        public void Fisica_RozamientoDelEmpujeEnDosRegimenes()
        {
            var d = new Dungeon(Box);
            var ph = new CirclePhysics(d);
            var b = ph.Add(new Body { X = Grid.X(5), Z = Grid.Z(2), Radius = 0.3f, KnockDrag = 4.2f });
            b.Kx = 20f;
            ph.Step(0.01f);
            Assert.AreEqual(20f * Mathf.Exp(-4.2f * 0.55f * 0.01f), b.Kx, 1e-3f);   // rápido: "vuela"
            b.Kx = 5f; b.Kz = 0f;
            ph.Step(0.01f);
            Assert.AreEqual(5f * Mathf.Exp(-4.2f * 0.01f), b.Kx, 1e-3f);           // lento: se arrastra
        }

        [Test]
        public void ModeloDelAventurero_SeConstruyeConLasPiezasPorDefecto()
        {
            var root = new GameObject("prueba").transform;
            try
            {
                var parts = CharacterModel.Build(root, Cosmetics.Defaults(1));
                Assert.IsNotNull(parts.ArmPivot);
                Assert.AreEqual(new Vector3(-0.18f, 1.0f, 0.05f), parts.ArmPivot.localPosition);
                Assert.AreEqual(0.45f, parts.Holder.localPosition.z, 1e-6f);
                Assert.IsNull(parts.Cape);
                Assert.GreaterOrEqual(parts.Renderers.Count, 5);
                var cape = CharacterModel.Build(root, Cosmetics.Validate(new Dictionary<string, string> { { "body", "cape" } }, 0, "body"));
                Assert.IsNotNull(cape.Cape);
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }
    }
}
