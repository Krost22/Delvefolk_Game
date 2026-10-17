using System;
using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public enum TileType : byte { Void = 0, Floor = 1, Wall = 2, Pit = 3, Gate = 4 }

    public struct Spawn
    {
        public char Ch;
        public int Tx, Tz;
        public float X, Z;
    }

    public sealed class Room
    {
        public RoomDef Def;
        public string Id { get { return Def.Id; } }
        public string Name { get { return Def.Name; } }
        public string Hint { get { return Def.Hint; } }
        public string Opens { get { return Def.Opens; } }
        public int X0, Z0, X1, Z1;
        public bool Entered, Cleared;
        public readonly List<object> Enemies = new List<object>();
    }

    // Rastrillo: casillas contiguas con el mismo número
    public sealed class Gate
    {
        public char Ch;
        public readonly List<Vector2Int> Tiles = new List<Vector2Int>();
        public bool Open;
        public float T;           // animación de subida 0..1 (transitable a partir de 0.55)
        public float X, Z;        // centro en el mundo
        public bool Horizontal;   // casillas en la misma fila
    }

    // Lógica de un piso construido desde su mapa ASCII (Dungeon.js sin la geometría):
    // tipos de casilla, colisión con la rejilla, línea de visión, campo de navegación, salas y rastrillos.
    public sealed class Dungeon : ISolidGrid
    {
        public readonly LevelDef Def;
        public readonly int W, H;
        public readonly TileType[] Type;
        public readonly byte[] Spike, Blocked, Special;
        public readonly char[] GateChar;
        public readonly List<Spawn> Spawns = new List<Spawn>();
        public readonly List<Vector2Int> TorchTiles = new List<Vector2Int>();
        public readonly List<Vector2Int> DoorTiles = new List<Vector2Int>();
        public readonly List<Room> Rooms = new List<Room>();
        public readonly Dictionary<char, Gate> Gates = new Dictionary<char, Gate>();
        public readonly float[] NavDist;
        public bool HasExit;
        public float ExitX, ExitZ;

        // Dijkstra: montículo binario preasignado (sin asignaciones por frame)
        readonly float[] heapD;
        readonly int[] heapI;
        int heapN;
        int[] navSources = new int[0];
        int navSourceCount = -1;
        bool navDirty = true;

        public const string SpawnChars = "PXSGLAbeocBFUKhq^TJlrmv";

        public Dungeon(LevelDef def)
        {
            Def = def;
            var rows = def.Map;
            H = rows.Length;
            int w = 0;
            foreach (var r in rows) w = Math.Max(w, r.Length);
            W = w;
            int n = W * H;
            Type = new TileType[n];
            Spike = new byte[n];
            Blocked = new byte[n];
            Special = new byte[n];
            GateChar = new char[n];
            NavDist = new float[n];
            heapD = new float[n * 8 + 16];
            heapI = new int[n * 8 + 16];
            Parse();
            BuildGates();
        }

        void Parse()
        {
            var rows = Def.Map;
            for (int tz = 0; tz < H; tz++)
            {
                for (int tx = 0; tx < W; tx++)
                {
                    char ch = tx < rows[tz].Length ? rows[tz][tx] : ' ';
                    int i = Idx(tx, tz);
                    TileType t = TileType.Floor;
                    if (ch == ' ') t = TileType.Void;
                    else if (ch == '#') t = TileType.Wall;
                    else if (ch == 't') { t = TileType.Wall; TorchTiles.Add(new Vector2Int(tx, tz)); }
                    else if (ch == 'N') { t = TileType.Wall; DoorTiles.Add(new Vector2Int(tx, tz)); }
                    else if (ch == '_') t = TileType.Pit;
                    else if (ch == '1' || ch == '2' || ch == '3') { t = TileType.Gate; GateChar[i] = ch; }
                    Type[i] = t;
                    if (ch == '^') Spike[i] = 1;
                    // Columnas, cajas y muebles grandes: obstáculos para la navegación (hasta que se rompen)
                    if (ch == 'o' || ch == 'c' || ch == 'l' || ch == 'r' || ch == 'm') Blocked[i] = 1;
                    if (".#t_ ".IndexOf(ch) < 0) Special[i] = 1;
                    if (SpawnChars.IndexOf(ch) >= 0)
                        Spawns.Add(new Spawn { Ch = ch, Tx = tx, Tz = tz, X = Grid.X(tx), Z = Grid.Z(tz) });
                    if (ch == 'X') { HasExit = true; ExitX = Grid.X(tx); ExitZ = Grid.Z(tz); }
                }
            }
            foreach (var r in Def.Rooms)
                Rooms.Add(new Room { Def = r, X0 = r.X0, Z0 = r.Z0, X1 = r.X1, Z1 = r.Z1 });
        }

        void BuildGates()
        {
            for (int tz = 0; tz < H; tz++)
                for (int tx = 0; tx < W; tx++)
                {
                    char ch = GateChar[Idx(tx, tz)];
                    if (ch == '\0') continue;
                    Gate g;
                    if (!Gates.TryGetValue(ch, out g)) { g = new Gate { Ch = ch }; Gates[ch] = g; }
                    g.Tiles.Add(new Vector2Int(tx, tz));
                }
            foreach (var g in Gates.Values)
            {
                float sx = 0, sz = 0;
                bool horiz = true;
                foreach (var t in g.Tiles) { sx += t.x; sz += t.y; if (t.y != g.Tiles[0].y) horiz = false; }
                g.Horizontal = horiz;
                var c = Grid.ToWorld(sx / g.Tiles.Count, sz / g.Tiles.Count);
                g.X = c.x; g.Z = c.z;
            }
        }

        public int Idx(int tx, int tz) { return tz * W + tx; }
        public bool InBounds(int tx, int tz) { return tx >= 0 && tz >= 0 && tx < W && tz < H; }
        public TileType TypeAt(int tx, int tz) { return InBounds(tx, tz) ? Type[Idx(tx, tz)] : TileType.Void; }
        public char CharAt(int tx, int tz) { return InBounds(tx, tz) && tx < Def.Map[tz].Length ? Def.Map[tz][tx] : ' '; }
        public static bool IsFloorLike(TileType t) { return t == TileType.Floor || t == TileType.Gate || t == TileType.Pit; }

        public bool IsWallLike(int tx, int tz)
        {
            var t = TypeAt(tx, tz);
            if (t == TileType.Wall || t == TileType.Void) return true;
            if (t == TileType.Gate) return !GateOpen(tx, tz);
            return false;
        }

        // Consulta única de colisión con la rejilla usada por la física
        public bool IsSolidFor(int tx, int tz, Body body)
        {
            var t = TypeAt(tx, tz);
            if (t == TileType.Wall || t == TileType.Void) return true;
            if (t == TileType.Gate) return !GateOpen(tx, tz);
            if (t == TileType.Pit) return body.PitSolid();
            return false;
        }

        public bool IsPitAt(float x, float z) { return TypeAt(Grid.TileX(x), Grid.TileZ(z)) == TileType.Pit; }
        public bool IsSpikeAt(float x, float z)
        {
            int tx = Grid.TileX(x), tz = Grid.TileZ(z);
            return InBounds(tx, tz) && Spike[Idx(tx, tz)] != 0;
        }

        public Room RoomAt(float x, float z)
        {
            int tx = Grid.TileX(x), tz = Grid.TileZ(z);
            for (int i = 0; i < Rooms.Count; i++)
            {
                var r = Rooms[i];
                if (tx >= r.X0 && tx <= r.X1 && tz >= r.Z0 && tz <= r.Z1) return r;
            }
            return null;
        }

        // ¿Hay línea libre de muros entre dos puntos? (los fosos no bloquean la vista)
        public bool LineClear(float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0;
            float d = MathUtil.Hypot(dx, dz);
            int steps = (int)Math.Ceiling(d / 0.25f);
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                if (IsWallLike(Grid.TileX(x0 + dx * t), Grid.TileZ(z0 + dz * t))) return false;
            }
            return true;
        }

        // ¿Se puede caminar en línea recta? (sin muros, fosos ni púas en el camino; tres líneas paralelas)
        public bool WalkClear(float x0, float z0, float x1, float z1, float radius)
        {
            float dx = x1 - x0, dz = z1 - z0;
            float d = MathUtil.Hypot(dx, dz);
            if (d == 0f) d = 1f;
            float px = -dz / d * radius, pz = dx / d * radius;
            int steps = (int)Math.Ceiling(d / 0.4f);
            int ttx = Grid.TileX(x1), ttz = Grid.TileZ(z1);
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                for (int s = -1; s <= 1; s++)
                {
                    int tx = Grid.TileX(x0 + dx * t + px * s), tz = Grid.TileZ(z0 + dz * t + pz * s);
                    if (IsWallLike(tx, tz)) return false;
                    int k = Idx(tx, tz);
                    if (Type[k] == TileType.Pit || Spike[k] != 0 || Blocked[k] != 0)
                    {
                        // permitido si el objetivo está en esa misma casilla
                        if (!(ttx == tx && ttz == tz)) return false;
                    }
                }
            }
            return true;
        }

        // ---------- Navegación (Dijkstra sobre la rejilla, 8 direcciones, multifuente) ----------
        static readonly int[] DirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DirZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] DirC = { 1, 1, 1, 1, 1.414f, 1.414f, 1.414f, 1.414f };

        // Hay que recalcular (se abrió un rastrillo o se rompió un mueble)
        public void InvalidateNav() { navDirty = true; }

        // sources: casillas (tx, tz) de los jugadores atacables. Solo recalcula si cambian.
        public void UpdateNav(List<Vector2Int> sources)
        {
            if (!navDirty && sources.Count == navSourceCount)
            {
                bool same = true;
                for (int i = 0; i < sources.Count; i++)
                    if (navSources[i] != sources[i].y * 100000 + sources[i].x) { same = false; break; }
                if (same) return;
            }
            navDirty = false;
            if (navSources.Length < sources.Count) navSources = new int[sources.Count + 2];
            navSourceCount = sources.Count;
            for (int i = 0; i < sources.Count; i++) navSources[i] = sources[i].y * 100000 + sources[i].x;

            var dist = NavDist;
            for (int i = 0; i < dist.Length; i++) dist[i] = 1e9f;
            heapN = 0;
            foreach (var s in sources)
            {
                if (!InBounds(s.x, s.y)) continue;
                int start = Idx(s.x, s.y);
                dist[start] = 0f;
                Push(start, 0f);
            }
            while (heapN > 0)
            {
                float d; int i;
                Pop(out d, out i);
                if (d > dist[i]) continue;
                int cx = i % W, cz = i / W;
                for (int k = 0; k < 8; k++)
                {
                    int ox = DirX[k], oz = DirZ[k];
                    int nx = cx + ox, nz = cz + oz;
                    if (!NavWalkable(nx, nz)) continue;
                    // No se cortan esquinas: las dos ortogonales deben ser transitables
                    if (ox != 0 && oz != 0 && (!NavWalkable(cx + ox, cz) || !NavWalkable(cx, cz + oz))) continue;
                    int ni = Idx(nx, nz);
                    float cost = DirC[k] * (Spike[ni] != 0 ? 6f : 1f);
                    float nd = d + cost;
                    if (nd < dist[ni]) { dist[ni] = nd; Push(ni, nd); }
                }
            }
        }

        void Push(int i, float d)
        {
            if (heapN >= heapD.Length) return;
            int c = heapN++;
            heapD[c] = d; heapI[c] = i;
            while (c > 0)
            {
                int p = (c - 1) >> 1;
                if (heapD[p] <= heapD[c]) break;
                Swap(p, c); c = p;
            }
        }

        void Pop(out float d, out int i)
        {
            d = heapD[0]; i = heapI[0];
            heapN--;
            if (heapN > 0)
            {
                heapD[0] = heapD[heapN]; heapI[0] = heapI[heapN];
                int c = 0;
                for (;;)
                {
                    int l = c * 2 + 1, r = l + 1, m = c;
                    if (l < heapN && heapD[l] < heapD[m]) m = l;
                    if (r < heapN && heapD[r] < heapD[m]) m = r;
                    if (m == c) break;
                    Swap(m, c); c = m;
                }
            }
        }

        void Swap(int a, int b)
        {
            float td = heapD[a]; heapD[a] = heapD[b]; heapD[b] = td;
            int ti = heapI[a]; heapI[a] = heapI[b]; heapI[b] = ti;
        }

        public bool NavWalkable(int tx, int tz)
        {
            if (!InBounds(tx, tz)) return false;
            int i = Idx(tx, tz);
            var t = Type[i];
            if (t == TileType.Floor) return Blocked[i] == 0;
            if (t == TileType.Gate) return GateOpen(tx, tz);
            return false;
        }

        public float NavDistAt(float x, float z)
        {
            int tx = Grid.TileX(x), tz = Grid.TileZ(z);
            return InBounds(tx, tz) ? NavDist[Idx(tx, tz)] : 1e9f;
        }

        // Dirección recomendada desde (x, z) siguiendo el campo de distancias (false si no hay)
        public bool NavDirection(float x, float z, out float dirX, out float dirZ)
        {
            dirX = 0; dirZ = 0;
            int tx = Grid.TileX(x), tz = Grid.TileZ(z);
            if (!InBounds(tx, tz)) return false;
            float best = NavDist[Idx(tx, tz)];
            int bx = tx, bz = tz;
            for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oz == 0) continue;
                    int nx = tx + ox, nz = tz + oz;
                    if (!NavWalkable(nx, nz)) continue;
                    if (ox != 0 && oz != 0 && (!NavWalkable(tx + ox, tz) || !NavWalkable(tx, tz + oz))) continue;
                    float d = NavDist[Idx(nx, nz)];
                    if (d < best) { best = d; bx = nx; bz = nz; }
                }
            if (bx == tx && bz == tz) return false;
            float dx = Grid.X(bx) - x, dz = Grid.Z(bz) - z;
            float l = MathUtil.Hypot(dx, dz);
            if (l == 0f) l = 1f;
            dirX = dx / l; dirZ = dz / l;
            return true;
        }

        // ---------- Rastrillos ----------
        public bool GateOpen(int tx, int tz)
        {
            char ch = GateChar[Idx(tx, tz)];
            Gate g;
            return ch != '\0' && Gates.TryGetValue(ch, out g) && g.Open && g.T > 0.55f;
        }

        public Gate OpenGate(string ch)
        {
            if (string.IsNullOrEmpty(ch)) return null;
            Gate g;
            if (!Gates.TryGetValue(ch[0], out g) || g.Open) return null;
            g.Open = true;
            navDirty = true;   // recalcular navegación
            return g;
        }

        // Un mueble roto libera su casilla para la navegación
        public void Unblock(int tx, int tz)
        {
            if (!InBounds(tx, tz)) return;
            Blocked[Idx(tx, tz)] = 0;
            navDirty = true;
        }

        public void Update(float dt)
        {
            foreach (var g in Gates.Values)
            {
                if (g.Open && g.T < 1f)
                {
                    bool wasPassable = g.T > 0.55f;
                    g.T = Mathf.Min(1f, g.T + dt / 1.1f);
                    if (!wasPassable && g.T > 0.55f) navDirty = true;
                }
            }
        }

        // Dirección (ox, oz) de casilla hacia el suelo desde una casilla de muro (orden del original)
        static readonly int[] FaceX = { 0, 1, -1, 0 };
        static readonly int[] FaceZ = { 1, 0, 0, -1 };
        public bool FaceDir(int tx, int tz, out int ox, out int oz)
        {
            for (int k = 0; k < 4; k++)
            {
                var t = TypeAt(tx + FaceX[k], tz + FaceZ[k]);
                if (t == TileType.Floor || t == TileType.Gate) { ox = FaceX[k]; oz = FaceZ[k]; return true; }
            }
            ox = 0; oz = 0;
            return false;
        }
    }
}
