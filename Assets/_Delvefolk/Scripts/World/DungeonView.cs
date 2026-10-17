using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Geometría, iluminación y decoración de un piso (la parte visual de Dungeon.js).
    // Usa las piezas del kit LowPolyDungeons (escaladas de su rejilla de 4 u a la casilla de 2 u) y, si faltan,
    // los modelos hechos por código del original. Nada de esto afecta a la simulación.
    public sealed class DungeonView
    {
        public sealed class Wall
        {
            public int Tx, Tz;
            public float X, Z, F = 1f, LastF = -1f;
            public Transform Tr;
            public readonly List<GameObject> Attach = new List<GameObject>();
        }

        public sealed class Occluder
        {
            public Transform Obj;
            public float X, Z, F = 1f, Low;
            public bool Disabled;
        }

        public sealed class Torch
        {
            public float Lx, Lz, Seed;
            public Transform Flame, Core;
            public LightSlot Slot;
        }

        public sealed class LightSlot
        {
            public Light Light;
            public Torch Torch;
            public float K;
            public bool Wanted;
        }

        readonly Dungeon d;
        public readonly Transform Group;
        readonly Rng rnd;      // misma secuencia que el original (decoración estable por piso)
        readonly Rng vr;       // elección de variantes de los modelos del kit (no altera la secuencia anterior)
        public readonly List<Wall> Walls = new List<Wall>();
        readonly Dictionary<int, Wall> wallByTile = new Dictionary<int, Wall>();
        public readonly List<Occluder> Occluders = new List<Occluder>();
        public readonly List<Torch> Torches = new List<Torch>();
        public readonly List<LightSlot> LightPool = new List<LightSlot>();
        readonly Dictionary<char, Occluder> gateOcc = new Dictionary<char, Occluder>();
        readonly Dictionary<char, Transform> gateObj = new Dictionary<char, Transform>();
        Transform exitRing;
        MeshRenderer exitRingR;
        Light exitLight;
        float time, lightTimer;
        readonly DelvefolkArt art;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        static Mesh capMesh, floorBox, wallBox, pitBottom, pitSide, flameMesh, coreMesh;

        public DungeonView(Dungeon dungeon, Transform parent, int seed)
        {
            d = dungeon;
            Group = new GameObject("Mazmorra").transform;
            Group.SetParent(parent, false);
            rnd = new Rng((uint)(seed * 977 + 13));
            vr = new Rng((uint)(seed * 977 + 14));
            art = Art.Catalog;
            BuildFloor();
            BuildWalls();
            BuildPits();
            BuildGates();
            BuildArches();
            BuildWallFeatures();
            BuildExit();
            Decorate();
        }

        bool Use(GameObject p) { return Art.Has(p); }
        GameObject Pick(GameObject[] list) { return Art.Pick(list, vr); }

        GameObject Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = Art.Spawn(prefab, parent);
            if (!go) return null;
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            return go;
        }

        static GameObject Obj(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
        {
            return CharacterModel.MeshObject(parent, name, mesh, mat, pos);
        }

        int Key(int tx, int tz) { return tz * 100000 + tx; }

        // ---------- Suelo ----------
        void BuildFloor()
        {
            var fb = new MeshBuilder();
            bool useKit = art != null && Use(Pick(art.Floors));
            for (int tz = 0; tz < d.H; tz++)
                for (int tx = 0; tx < d.W; tx++)
                {
                    var t = d.TypeAt(tx, tz);
                    if (t != TileType.Floor && t != TileType.Gate) continue;
                    if (d.CharAt(tx, tz) == 'X') continue;
                    float v = 0.82f + rnd.Next() * 0.3f;
                    float g = v * (0.96f + rnd.Next() * 0.06f);
                    var pos = Grid.ToWorld(tx, tz);
                    if (useKit)
                    {
                        var p = vr.Next() < 0.04f && art.FloorsRare != null && art.FloorsRare.Length > 0 ? Pick(art.FloorsRare) : Pick(art.Floors);
                        var b = Art.LocalBounds(p);
                        float s = Cfg.Tile / Mathf.Max(0.01f, b.size.x);
                        Spawn(p, Group, pos, Quaternion.Euler(0, 90f * (int)(vr.Next() * 4), 0), new Vector3(s, s, s));
                    }
                    else fb.Add(ProcMesh.Box(Cfg.Tile, 0.3f, Cfg.Tile), pos + new Vector3(0, -0.15f, 0), new Color(v, g, v * 0.95f));
                }
            if (!fb.Empty)
                Obj(Group, "Suelo", fb.ToMesh("suelo"), Mats.Textured(ProcTex.FloorStones, Color.white, true), Vector3.zero);
        }

        // ---------- Muros (solo los del borde) ----------
        void BuildWalls()
        {
            if (!capMesh) capMesh = new MeshBuilder().Add(ProcMesh.Box(Cfg.Tile, 0.06f, Cfg.Tile), new Vector3(0, Cfg.WallH - 0.03f, 0), MathUtil.Hex(0x2b2724)).ToMesh("tapa");
            if (!wallBox)
            {
                var md = ProcMesh.Box(Cfg.Tile, Cfg.WallH, Cfg.Tile, 1, 1, 1, true).Translate(0, Cfg.WallH / 2f, 0);
                wallBox = md.ToMesh("muro");
            }
            bool useKit = art != null && art.Walls != null && art.Walls.Length > 0 && Use(art.Walls[0]);
            for (int tz = 0; tz < d.H; tz++)
                for (int tx = 0; tx < d.W; tx++)
                {
                    if (d.TypeAt(tx, tz) != TileType.Wall) continue;
                    bool border = false;
                    for (int oz = -1; oz <= 1; oz++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            var t = d.TypeAt(tx + ox, tz + oz);
                            if (t != TileType.Wall && t != TileType.Void) border = true;
                        }
                    if (!border) continue;
                    float v = 0.85f + rnd.Next() * 0.25f;
                    var w = new Wall { Tx = tx, Tz = tz, X = Grid.X(tx), Z = Grid.Z(tz) };
                    w.Tr = new GameObject("Muro").transform;
                    w.Tr.SetParent(Group, false);
                    w.Tr.localPosition = new Vector3(w.X, 0, w.Z);
                    if (useKit) BuildKitWall(w, tx, tz);
                    else
                    {
                        var go = Obj(w.Tr, "Bloque", wallBox, Mats.Lit, Vector3.zero);
                        var side = Mats.Textured(ProcTex.Bricks, Color.white);
                        var top = Mats.Solid(0x2b2724);
                        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { side, side, top, top, side, side };
                    }
                    Walls.Add(w);
                    wallByTile[Key(tx, tz)] = w;
                }
        }

        static readonly int[] N4x = { 0, 1, -1, 0 };
        static readonly int[] N4z = { 1, 0, 0, -1 };

        bool IsDoorFace(int tx, int tz, int ox, int oz)
        {
            if (d.CharAt(tx, tz) != 'N') return false;
            int fx, fz;
            return d.FaceDir(tx, tz, out fx, out fz) && fx == ox && fz == oz;
        }

        // ¿La casilla vecina tiene muro construido (de borde)? Las caras contra él no se ven
        bool IsBuiltWall(int tx, int tz)
        {
            if (d.TypeAt(tx, tz) != TileType.Wall) return false;
            for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    var t = d.TypeAt(tx + ox, tz + oz);
                    if (t != TileType.Wall && t != TileType.Void) return true;
                }
            return false;
        }

        void BuildKitWall(Wall w, int tx, int tz)
        {
            Obj(w.Tr, "Tapa", capMesh, Mats.Lit, Vector3.zero);
            for (int k = 0; k < 4; k++)
            {
                int ox = N4x[k], oz = N4z[k];
                bool toFloor = Dungeon.IsFloorLike(d.TypeAt(tx + ox, tz + oz));
                // Caras exteriores: solo las que miran a la cámara (este y sur), que son las que se ven
                bool outer = !toFloor && !IsBuiltWall(tx + ox, tz + oz) && (ox == 1 || oz == 1);
                if (!toFloor && !outer) continue;
                var prefab = IsDoorFace(tx, tz, ox, oz) && art.EntranceDoor ? art.EntranceDoor : Pick(art.Walls);
                var b = Art.LocalBounds(prefab);
                float sx = Cfg.Tile / Mathf.Max(0.01f, b.size.x), sy = Cfg.WallH / Mathf.Max(0.01f, b.size.y), sz = 0.5f;
                float thick = b.size.z * sz;
                var dir = Grid.Dir(ox, oz);
                Spawn(prefab, w.Tr, dir * (Cfg.Tile / 2f - thick / 2f), Quaternion.LookRotation(dir), new Vector3(sx, sy, sz));
            }
        }

        public void ApplyWallScales(bool force)
        {
            for (int i = 0; i < Walls.Count; i++)
            {
                var w = Walls[i];
                if (!force && Mathf.Abs(w.F - w.LastF) < 0.002f) continue;
                w.LastF = w.F;
                w.Tr.localScale = new Vector3(1f, w.F, 1f);
                bool vis = w.F > 0.8f;
                for (int a = 0; a < w.Attach.Count; a++) if (w.Attach[a] && w.Attach[a].activeSelf != vis) w.Attach[a].SetActive(vis);
            }
        }

        // Recorte de muros entre la cámara y los jugadores (para no perderlos de vista)
        public void UpdateOcclusion(float dt, IList<Vector2> points, float yaw)
        {
            // Dirección horizontal hacia la cámara (en Unity el "sur" del web es −Z)
            float cbx = Mathf.Sin(yaw), cbz = -Mathf.Cos(yaw);
            float low = Cfg.WallLow / Cfg.WallH;
            for (int i = 0; i < Walls.Count; i++)
            {
                var w = Walls[i];
                w.F = MathUtil.Damp(w.F, Target(w.X, w.Z, points, cbx, cbz, low), 9f, dt);
            }
            ApplyWallScales(false);
            for (int i = 0; i < Occluders.Count; i++)
            {
                var o = Occluders[i];
                if (o.Disabled || !o.Obj) continue;
                o.F = MathUtil.Damp(o.F, Target(o.X, o.Z, points, cbx, cbz, low) == 1f ? 1f : o.Low, 9f, dt);
                var s = o.Obj.localScale; s.y = o.F; o.Obj.localScale = s;
            }
        }

        static float Target(float x, float z, IList<Vector2> points, float cbx, float cbz, float low)
        {
            for (int i = 0; i < points.Count; i++)
            {
                float rx = x - points[i].x, rz = z - points[i].y;
                float along = rx * cbx + rz * cbz;
                float lat = Mathf.Abs(rx * cbz - rz * cbx);
                if (along > -1.2f && along < 7.5f && lat < 4.6f + along * 0.25f) return low;
            }
            return 1f;
        }

        public Occluder AddOccluder(Transform obj, float x, float z, float low = 0.15f)
        {
            var o = new Occluder { Obj = obj, X = x, Z = z, Low = low };
            Occluders.Add(o);
            return o;
        }

        public void AttachToWall(int tx, int tz, GameObject go)
        {
            Wall w;
            if (go && wallByTile.TryGetValue(Key(tx, tz), out w)) w.Attach.Add(go);
        }

        // ---------- Fosos ----------
        void BuildPits()
        {
            const float D = 4f, T = Cfg.Tile;
            if (!pitBottom) pitBottom = new MeshBuilder().Add(ProcMesh.Plane(T, T).RotateX(-Mathf.PI / 2f), Vector3.zero, MathUtil.Hex(0x140a22)).ToMesh("fondoFoso");
            if (!pitSide) pitSide = new MeshBuilder().Add(ProcMesh.Plane(T, D), Vector3.zero, Color.white).ToMesh("paredFoso");
            var sideMat = Mats.Textured(ProcTex.Bricks, MathUtil.Hex(0x5a5560), false, true);
            var rims = new MeshBuilder();
            for (int tz = 0; tz < d.H; tz++)
                for (int tx = 0; tx < d.W; tx++)
                {
                    if (d.TypeAt(tx, tz) != TileType.Pit) continue;
                    var c = Grid.ToWorld(tx, tz);
                    Obj(Group, "FondoFoso", pitBottom, Mats.Unlit, c + new Vector3(0, -D, 0));
                    for (int k = 0; k < 4; k++)
                    {
                        int ox = N4x[k], oz = N4z[k];
                        if (d.TypeAt(tx + ox, tz + oz) == TileType.Pit) continue;
                        var dir = Grid.Dir(ox, oz);
                        var side = Obj(Group, "ParedFoso", pitSide, sideMat, c + dir * (T / 2f) + new Vector3(0, -D / 2f, 0));
                        // La cara mira hacia el interior del foso
                        side.transform.localRotation = Quaternion.LookRotation(-dir);
                        bool alongX = ox == 0;
                        var rimSize = new Vector3(alongX ? T : 0.25f, 0.12f, alongX ? 0.25f : T);
                        rims.Add(ProcMesh.Box(rimSize.x, rimSize.y, rimSize.z), c + dir * (T / 2f - 0.12f) + new Vector3(0, 0.03f, 0), MathUtil.Hex(0x45403a));
                    }
                }
            if (!rims.Empty) Obj(Group, "Bordes", rims.ToMesh("bordesFoso"), Mats.Lit, Vector3.zero);
        }

        // ---------- Rastrillos ----------
        void BuildGates()
        {
            const float H = Cfg.WallH;
            foreach (var g in d.Gates.Values)
            {
                float span = g.Tiles.Count * Cfg.Tile;
                var root = new GameObject("Rastrillo" + g.Ch).transform;
                root.SetParent(Group, false);
                root.localPosition = new Vector3(g.X, 0, g.Z);
                if (art != null && Use(art.GateBars))
                {
                    var b = Art.LocalBounds(art.GateBars);
                    // Las barras del kit van a lo largo de su X: el rastrillo vertical (misma columna) gira 90°
                    Spawn(art.GateBars, root, Vector3.zero, Quaternion.Euler(0, g.Horizontal ? 0 : 90, 0),
                        new Vector3(span / Mathf.Max(0.01f, b.size.x), H / Mathf.Max(0.01f, b.size.y), 2f));
                }
                else
                {
                    var mb = new MeshBuilder();
                    var iron = MathUtil.Hex(0x3a3c40);
                    int nb = Mathf.RoundToInt(span / 0.45f);
                    var bar = ProcMesh.Box(0.14f, H, 0.14f).Translate(0, H / 2f, 0);
                    var tip = ProcMesh.Cone(0.1f, 0.3f, 4);
                    for (int i = 0; i <= nb; i++)
                    {
                        float o = -span / 2f + (i / (float)nb) * span;
                        var p = g.Horizontal ? new Vector3(o, 0, 0) : new Vector3(0, 0, o);
                        mb.Add(bar, p, iron);
                        mb.Add(tip, Matrix4x4.TRS(p + new Vector3(0, 0.05f, 0), ProcMesh.EulerXYZ(Mathf.PI, 0, 0), Vector3.one), iron);
                    }
                    foreach (var y in new[] { 0.6f, 1.6f, 2.6f })
                        mb.Add(ProcMesh.Box(g.Horizontal ? span : 0.12f, 0.12f, g.Horizontal ? 0.12f : span), new Vector3(0, y, 0), iron);
                    Obj(root, "Barras", mb.ToMesh("rastrillo"), Mats.Lit, Vector3.zero);
                }
                gateObj[g.Ch] = root;
                gateOcc[g.Ch] = AddOccluder(root, g.X, g.Z, 0.2f);
            }
        }

        // Al abrirse, el rastrillo deja de recortarse (sube y desaparece)
        public void OnGateOpened(char ch)
        {
            Occluder o;
            if (gateOcc.TryGetValue(ch, out o)) { o.Disabled = true; var s = o.Obj.localScale; s.y = 1f; o.Obj.localScale = s; }
        }

        // ---------- Arcos de piedra automáticos en cada abertura de 2 casillas ----------
        void BuildArches()
        {
            var made = new HashSet<long>();
            for (int tz = 1; tz < d.H - 1; tz++)
                for (int tx = 1; tx < d.W - 2; tx++)
                {
                    // Abertura de 2 de ancho en una línea de muro horizontal (paso vertical)
                    if (Open(tx, tz) && Open(tx + 1, tz) && IsWall(tx - 1, tz) && IsWall(tx + 2, tz) &&
                        Open(tx, tz - 1) && Open(tx, tz + 1) && (Open(tx - 1, tz - 1) || Open(tx - 1, tz + 1)))
                        MakeArch(made, tx + 0.5f, tz, true);
                }
            for (int tx = 1; tx < d.W - 1; tx++)
                for (int tz = 1; tz < d.H - 2; tz++)
                {
                    if (Open(tx, tz) && Open(tx, tz + 1) && IsWall(tx, tz - 1) && IsWall(tx, tz + 2) &&
                        Open(tx - 1, tz) && Open(tx + 1, tz) && (Open(tx - 1, tz - 1) || Open(tx + 1, tz - 1)))
                        MakeArch(made, tx, tz + 0.5f, false);
                }
        }

        bool Open(int tx, int tz) { var t = d.TypeAt(tx, tz); return t == TileType.Floor || t == TileType.Gate; }
        bool IsWall(int tx, int tz) { return d.TypeAt(tx, tz) == TileType.Wall; }

        void MakeArch(HashSet<long> made, float ftx, float ftz, bool horiz)
        {
            long key = (long)Mathf.RoundToInt(ftx * 10) * 100000 + Mathf.RoundToInt(ftz * 10);
            if (!made.Add(key)) return;
            const float H = Cfg.WallH, width = 2 * Cfg.Tile;
            var c = Grid.ToWorld(ftx, ftz);
            var g = new GameObject("Arco").transform;
            g.SetParent(Group, false);
            g.localPosition = c;
            if (art != null && Use(art.ArchPost) && Use(art.ArchLintel))
            {
                var pb = Art.LocalBounds(art.ArchPost);
                float ps = 0.55f / Mathf.Max(0.01f, pb.size.x), py = (H + 0.2f) / Mathf.Max(0.01f, pb.size.y);
                foreach (var s in new[] { -1f, 1f })
                {
                    var p = horiz ? new Vector3(s * width / 2f, 0, 0) : new Vector3(0, 0, s * width / 2f);
                    Spawn(art.ArchPost, g, p, Quaternion.identity, new Vector3(ps, py, ps));
                }
                var lb = Art.LocalBounds(art.ArchLintel);
                float lx = (width + 0.55f) / Mathf.Max(0.01f, lb.size.x);
                Spawn(art.ArchLintel, g, new Vector3(0, H + 0.2f - lb.max.y * 0.8f, 0), Quaternion.Euler(0, horiz ? 0 : 90, 0), new Vector3(lx, 0.8f, 0.9f));
            }
            else
            {
                var mb = new MeshBuilder();
                var stone = MathUtil.Hex(0x6e665d);
                var post = ProcMesh.Box(0.55f, H + 0.2f, 0.55f).Translate(0, (H + 0.2f) / 2f, 0);
                foreach (var s in new[] { -1f, 1f }) mb.Add(post, horiz ? new Vector3(s * width / 2f, 0, 0) : new Vector3(0, 0, s * width / 2f), stone);
                var arc = ProcMesh.Torus(width / 2f, 0.22f, 5, 10, Mathf.PI);
                mb.Add(arc, Matrix4x4.TRS(new Vector3(0, H - width / 2f + 0.3f, 0), Quaternion.Euler(0, horiz ? 0 : 90, 0), new Vector3(1, 0.55f, 1)), stone);
                mb.Add(ProcMesh.Box(0.4f, 0.5f, 0.6f), Matrix4x4.TRS(new Vector3(0, H + 0.05f, 0), Quaternion.Euler(0, horiz ? 0 : 90, 0), Vector3.one), MathUtil.Hex(0x45403a));
                Obj(g, "Piedra", mb.ToMesh("arco"), Mats.Lit, Vector3.zero);
            }
            AddOccluder(g, c.x, c.z, 0.15f);
        }

        // ---------- Antorchas, puerta de entrada y pool de luces ----------
        void BuildWallFeatures()
        {
            if (!flameMesh) flameMesh = new MeshBuilder().Add(ProcMesh.Cone(0.16f, 0.45f, 5), Vector3.zero, MathUtil.Hex(0xffaa33)).ToMesh("llama");
            if (!coreMesh) coreMesh = new MeshBuilder().Add(ProcMesh.Cone(0.08f, 0.25f, 5), Vector3.zero, MathUtil.Hex(0xfff1a0)).ToMesh("nucleo");
            foreach (var tt in d.TorchTiles)
            {
                int ox, oz;
                if (!d.FaceDir(tt.x, tt.y, out ox, out oz)) continue;
                var dir = Grid.Dir(ox, oz);
                var basePos = Grid.ToWorld(tt.x, tt.y) + dir * (Cfg.Tile / 2f + 0.05f);
                var g = new GameObject("Antorcha").transform;
                g.SetParent(Group, false);
                g.localPosition = basePos + new Vector3(0, 2.0f, 0);
                g.localRotation = Quaternion.LookRotation(dir);
                Transform flame, core;
                if (art != null && Use(art.Torch))
                {
                    var tb = Art.LocalBounds(art.Torch);
                    float s = 1.0f / Mathf.Max(0.01f, tb.size.y);
                    Spawn(art.Torch, g, new Vector3(0, -0.3f, -0.05f), Quaternion.identity, new Vector3(s, s, s) * 1f);
                    float topY = -0.3f + tb.max.y * s, topZ = -0.05f + tb.center.z * s;
                    flame = Obj(g, "Llama", flameMesh, Mats.Unlit, new Vector3(0, topY + 0.18f, topZ)).transform;
                    core = Obj(g, "Nucleo", coreMesh, Mats.Unlit, new Vector3(0, topY + 0.13f, topZ)).transform;
                }
                else
                {
                    var mb = new MeshBuilder();
                    mb.Add(ProcMesh.Box(0.12f, 0.12f, 0.45f), Matrix4x4.TRS(new Vector3(0, 0, 0.18f), ProcMesh.EulerXYZ(-0.5f, 0, 0), Vector3.one), MathUtil.Hex(0x3a3c40));
                    mb.Add(ProcMesh.Cylinder(0.07f, 0.05f, 0.6f, 5), Matrix4x4.TRS(new Vector3(0, 0.2f, 0.35f), ProcMesh.EulerXYZ(0.35f, 0, 0), Vector3.one), MathUtil.Hex(0x4a2f1a));
                    Obj(g, "Soporte", mb.ToMesh("antorcha"), Mats.Lit, Vector3.zero);
                    flame = Obj(g, "Llama", flameMesh, Mats.Unlit, new Vector3(0, 0.62f, 0.45f)).transform;
                    core = Obj(g, "Nucleo", coreMesh, Mats.Unlit, new Vector3(0, 0.57f, 0.45f)).transform;
                }
                AttachToWall(tt.x, tt.y, g.gameObject);
                var lp = basePos + dir * 0.7f;
                Torches.Add(new Torch { Lx = lp.x, Lz = lp.z, Flame = flame, Core = core, Seed = rnd.Next() * 100f });
            }
            // Pool fijo de luces que se reparten entre las antorchas más cercanas al jugador:
            // el coste de iluminación no crece con el tamaño del piso
            for (int i = 0; i < 8; i++)
            {
                var l = Lighting.Point(Group, "LuzAntorcha", 0xff8a3a, 13f);
                l.transform.position = new Vector3(0, -50, 0);
                LightPool.Add(new LightSlot { Light = l });
            }
            lightTimer = 0f;
            // Puerta de entrada decorativa (si no la puso ya el panel del kit)
            if (art == null || !Use(art.EntranceDoor))
            {
                foreach (var dt in d.DoorTiles)
                {
                    int ox, oz;
                    if (!d.FaceDir(dt.x, dt.y, out ox, out oz)) continue;
                    var dir = Grid.Dir(ox, oz);
                    var mb = new MeshBuilder();
                    mb.Add(ProcMesh.Box(1.7f, 2.6f, 0.15f), new Vector3(0, 1.3f, 0), MathUtil.Hex(0x7a4f2a));
                    mb.Add(ProcMesh.Box(2.1f, 0.3f, 0.3f), new Vector3(0, 2.75f, 0), MathUtil.Hex(0x6e665d));
                    foreach (var y in new[] { 0.5f, 1.4f, 2.2f }) mb.Add(ProcMesh.Box(1.75f, 0.12f, 0.2f), new Vector3(0, y, 0), MathUtil.Hex(0x3a3c40));
                    mb.Add(ProcMesh.Torus(0.14f, 0.04f, 4, 8), new Vector3(0.5f, 1.3f, 0.12f), MathUtil.Hex(0x3a3c40));
                    var go = Obj(Group, "Puerta", mb.ToMesh("puerta"), Mats.Lit, Grid.ToWorld(dt.x, dt.y) + dir * (Cfg.Tile / 2f + 0.08f));
                    go.transform.localRotation = Quaternion.LookRotation(dir);
                    AttachToWall(dt.x, dt.y, go);
                }
            }
        }

        // ---------- Salida: escalera de 5 peldaños que baja, anillo azul pulsante y luz ----------
        void BuildExit()
        {
            if (!d.HasExit) return;
            const float T = Cfg.Tile;
            var g = new GameObject("Salida").transform;
            g.SetParent(Group, false);
            g.localPosition = new Vector3(d.ExitX, 0, d.ExitZ);
            var mb = new MeshBuilder();
            const int steps = 5;
            for (int i = 0; i < steps; i++)
            {
                float c = 1f - i * 0.16f;
                var stone = MathUtil.Hex(0x6e665d);
                mb.Add(ProcMesh.Box(T * 0.92f, 0.3f, T / steps), new Vector3(0, -0.15f - i * 0.45f, -T / 2f + (i + 0.5f) * T / steps), new Color(stone.r * c, stone.g * c, stone.b * c));
            }
            mb.Add(ProcMesh.Plane(T, T).RotateX(-Mathf.PI / 2f), new Vector3(0, -2.6f, 0), MathUtil.Hex(0x140a22));
            Obj(g, "Escalera", mb.ToMesh("salida"), Mats.Lit, Vector3.zero);
            var walls = new MeshBuilder();
            foreach (var o in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1) })
                walls.Add(ProcMesh.Plane(T, 2.6f), Matrix4x4.TRS(new Vector3(o.x * T / 2f, -1.3f, o.y * T / 2f), Quaternion.LookRotation(new Vector3(-o.x, 0, -o.y)), Vector3.one), MathUtil.Hex(0x5a5560));
            Obj(g, "ParedesSalida", walls.ToMesh("paredesSalida"), Mats.Textured(ProcTex.Bricks, Color.white, true, true), Vector3.zero);
            var ring = Obj(g, "Anillo", new MeshBuilder().Add(ProcMesh.Ring(1.15f, 1.35f, 24).RotateX(-Mathf.PI / 2f), Vector3.zero, Color.white).ToMesh("anilloSalida"), Mats.Transparent, new Vector3(0, 0.04f, 0));
            exitRing = ring.transform;
            exitRingR = ring.GetComponent<MeshRenderer>();
            exitLight = Lighting.Point(g, "LuzSalida", 0x55ccff, 8f);
            exitLight.transform.localPosition = new Vector3(0, 1.5f, 0);
            exitLight.intensity = 1.6f * Lighting.PointScale;
        }

        // ---------- Decoración (RNG con semilla por piso; misma secuencia de decisiones que el original) ----------
        void Decorate()
        {
            var deco = new MeshBuilder();
            bool kit = art != null;
            for (int tz = 0; tz < d.H; tz++)
                for (int tx = 0; tx < d.W; tx++)
                {
                    var t = d.TypeAt(tx, tz);
                    int i = d.Idx(tx, tz);
                    var c = Grid.ToWorld(tx, tz);
                    if (t == TileType.Floor && d.Special[i] == 0)
                    {
                        bool wl = d.IsWallLike(tx - 1, tz), wr = d.IsWallLike(tx + 1, tz);
                        bool wu = d.IsWallLike(tx, tz - 1), wd = d.IsWallLike(tx, tz + 1);
                        bool nearWall = wl || wr || wu || wd;
                        if (nearWall && rnd.Next() < 0.13f)
                        {
                            // escombros
                            int n = 2 + (int)(rnd.Next() * 3);
                            for (int k = 0; k < n; k++)
                            {
                                bool dark = rnd.Next() < 0.5f;
                                float ox = (wl ? -0.6f : wr ? 0.6f : (rnd.Next() - 0.5f)) + (rnd.Next() - 0.5f) * 0.5f;
                                float oz = (wu ? -0.6f : wd ? 0.6f : (rnd.Next() - 0.5f)) + (rnd.Next() - 0.5f) * 0.5f;
                                float sc = 0.6f + rnd.Next() * 1.1f;
                                var rot = ProcMesh.EulerXYZ(rnd.Next() * 3, rnd.Next() * 3, rnd.Next() * 3);
                                var p = c + new Vector3(ox, 0.1f, -oz);
                                var prefab = kit ? Pick(art.Rubble) : null;
                                if (prefab) Spawn(prefab, Group, p, rot, Vector3.one * sc * 0.6f);
                                else deco.Add(ProcMesh.Dodecahedron(0.25f), Matrix4x4.TRS(p, rot, Vector3.one * sc), MathUtil.Hex(dark ? 0x45403a : 0x6e665d));
                            }
                        }
                        else if (rnd.Next() < 0.035f)
                        {
                            // huesos y calavera
                            var p = c + new Vector3((rnd.Next() - 0.5f) * 1.2f, 0.05f, -(rnd.Next() - 0.5f) * 1.2f);
                            var rot = Quaternion.Euler(0, rnd.Next() * 6f * Mathf.Rad2Deg, 0);
                            if (kit && Use(art.Skull) && art.Bones != null && art.Bones.Length > 0 && Use(art.Bones[0]))
                            {
                                var bones = new GameObject("Huesos").transform;
                                bones.SetParent(Group, false);
                                bones.localPosition = p; bones.localRotation = rot;
                                Spawn(art.Bones[0], bones, Vector3.zero, Quaternion.Euler(0, 35, 0), Vector3.one * 0.8f);
                                Spawn(art.Bones[0], bones, new Vector3(0, 0.03f, 0), Quaternion.Euler(0, -40, 0), Vector3.one * 0.8f);
                                Spawn(art.Skull, bones, new Vector3(0.3f, 0.12f, 0.1f), Quaternion.Euler(0, 20, 0), Vector3.one * 1.4f);
                            }
                            else
                            {
                                var bone = ProcMesh.Cylinder(0.04f, 0.04f, 0.5f, 4);
                                var m = Matrix4x4.TRS(p, rot, Vector3.one);
                                deco.Add(bone, m * Matrix4x4.Rotate(ProcMesh.EulerXYZ(Mathf.PI / 2, 0, 0.6f)), MathUtil.Hex(0xe8e0c8));
                                deco.Add(bone, m * Matrix4x4.Rotate(ProcMesh.EulerXYZ(Mathf.PI / 2, 0, -0.7f)), MathUtil.Hex(0xe8e0c8));
                                deco.Add(ProcMesh.Icosahedron(0.17f), m * Matrix4x4.Translate(new Vector3(0.3f, 0.12f, 0.1f)), MathUtil.Hex(0xe8e0c8));
                                deco.Add(ProcMesh.Box(0.06f, 0.06f, 0.06f), m * Matrix4x4.Translate(new Vector3(0.3f, 0.15f, 0.25f)), MathUtil.Hex(0x111111));
                            }
                        }
                        // Telarañas en esquinas
                        WebCorner(tx, tz, c, wl, wu, -1, -1);
                        WebCorner(tx, tz, c, wr, wu, 1, -1);
                        WebCorner(tx, tz, c, wl, wd, -1, 1);
                        WebCorner(tx, tz, c, wr, wd, 1, 1);
                    }
                    if (t == TileType.Wall && d.CharAt(tx, tz) == '#')
                    {
                        int fox, foz;
                        if (!d.FaceDir(tx, tz, out fox, out foz)) continue;
                        var dir = Grid.Dir(fox, foz);
                        float roll = rnd.Next();
                        if (roll < 0.07f) Banner(tx, tz, c, dir);
                        else if (roll < 0.11f) Chains(tx, tz, c, dir, rnd.Next() - 0.5f);
                    }
                }
            if (!deco.Empty) Obj(Group, "Decoracion", deco.ToMesh("decoracion"), Mats.Lit, Vector3.zero);
        }

        void WebCorner(int tx, int tz, Vector3 c, bool a, bool b, int sx, int sz)
        {
            if (!(a && b && rnd.Next() < 0.55f)) return;
            const float T = Cfg.Tile;
            float s = 0.9f + rnd.Next() * 0.5f;
            // Esquina (sx, sz) en casillas → mundo (sx, −sz)
            var corner = c + new Vector3(sx * (T / 2f - 0.02f), 0, -sz * (T / 2f - 0.02f));
            GameObject go;
            var prefab = art != null ? Pick(art.Webs) : null;
            if (prefab)
            {
                var toRoom = new Vector3(-sx, 0, sz).normalized;
                go = Spawn(prefab, Group, corner + toRoom * 0.35f + new Vector3(0, 3.0f - s * 0.5f, 0), Quaternion.LookRotation(-toRoom) * Quaternion.Euler(-20, 0, 0), Vector3.one * s * 1.6f);
            }
            else
            {
                var md = new MeshData();
                md.Add(new Vector3(-sx * s, 3.1f, 0), Vector3.up, Vector2.zero);
                md.Add(new Vector3(0, 3.1f, sz * s), Vector3.up, Vector2.zero);
                md.Add(new Vector3(0, 3.1f - s * 1.2f, 0), Vector3.up, Vector2.zero);
                md.I.Add(0); md.I.Add(1); md.I.Add(2);
                go = Obj(Group, "Telarana", new MeshBuilder().AddDouble(md, Matrix4x4.identity, new Color(0.87f, 0.87f, 0.87f, 0.18f)).ToMesh("telarana"), Mats.Transparent, new Vector3(corner.x, 0, corner.z));
            }
            AttachToWall(tx + sx, tz, go);
        }

        void Banner(int tx, int tz, Vector3 c, Vector3 dir)
        {
            const float T = Cfg.Tile;
            var pos = c + dir * (T / 2f + 0.03f) + new Vector3(0, 2.8f, 0);
            GameObject go;
            var prefab = art != null ? Pick(art.Banners) : null;
            if (prefab) go = Spawn(prefab, Group, pos + new Vector3(0, 0.1f, 0), Quaternion.LookRotation(dir), Vector3.one);
            else
            {
                var mb = new MeshBuilder();
                mb.AddDouble(ProcMesh.Plane(0.9f, 1.7f, 1, 3), Matrix4x4.Translate(new Vector3(0, -0.85f, 0)), MathUtil.Hex(0x7a1e22));
                mb.AddDouble(ProcMesh.Plane(0.9f, 0.12f), Matrix4x4.Translate(new Vector3(0, -0.3f, 0.01f)), MathUtil.Hex(0xc9a227));
                mb.Add(ProcMesh.Cylinder(0.04f, 0.04f, 1.1f, 5), Matrix4x4.Rotate(ProcMesh.EulerXYZ(0, 0, Mathf.PI / 2)), MathUtil.Hex(0x4a2f1a));
                mb.AddDouble(ProcMesh.Circle(0.2f, 6), Matrix4x4.Translate(new Vector3(0, -0.9f, 0.01f)), MathUtil.Hex(0xc9a227));
                go = Obj(Group, "Estandarte", mb.ToMesh("estandarte"), Mats.Lit, pos);
                go.transform.localRotation = Quaternion.LookRotation(dir);
            }
            AttachToWall(tx, tz, go);
        }

        void Chains(int tx, int tz, Vector3 c, Vector3 dir, float lateral)
        {
            const float T = Cfg.Tile;
            var side = new Vector3(1, 0, 0) * lateral;
            var pos = c + dir * (T / 2f + 0.06f) + side + new Vector3(0, 2.7f, 0);
            GameObject go;
            var prefab = art != null ? Pick(art.Chains) : null;
            if (prefab) go = Spawn(prefab, Group, pos + new Vector3(0, 0.15f, 0), Quaternion.LookRotation(dir), Vector3.one * 1.3f);
            else
            {
                var mb = new MeshBuilder();
                var link = ProcMesh.Torus(0.07f, 0.025f, 3, 6);
                for (int k = 0; k < 7; k++) mb.Add(link, Matrix4x4.TRS(new Vector3(0, -k * 0.12f, 0), Quaternion.Euler(0, (k % 2) * 90, 0), Vector3.one), MathUtil.Hex(0x3a3c40));
                go = Obj(Group, "Cadenas", mb.ToMesh("cadenas"), Mats.Lit, pos);
            }
            AttachToWall(tx, tz, go);
        }

        // ---------- Luces de antorcha ----------
        readonly List<Torch> wantedBuf = new List<Torch>();
        float sortX, sortZ;
        System.Comparison<Torch> byDistance;

        void AssignTorchLights(float px, float pz)
        {
            wantedBuf.Clear();
            wantedBuf.AddRange(Torches);
            sortX = px; sortZ = pz;
            if (byDistance == null) byDistance = (a, b) => Sq(a.Lx - sortX, a.Lz - sortZ).CompareTo(Sq(b.Lx - sortX, b.Lz - sortZ));
            wantedBuf.Sort(byDistance);
            int keep = Mathf.Min(wantedBuf.Count, LightPool.Count - 1);   // deja una libre para poder fundir
            if (wantedBuf.Count > keep) wantedBuf.RemoveRange(keep, wantedBuf.Count - keep);
            foreach (var s in LightPool) s.Wanted = s.Torch != null && wantedBuf.Contains(s.Torch);
            foreach (var t in wantedBuf)
            {
                bool has = false;
                foreach (var s in LightPool) if (s.Torch == t) { has = true; break; }
                if (has) continue;
                LightSlot free = null;
                foreach (var s in LightPool) if (s.Torch == null) { free = s; break; }
                if (free == null) foreach (var s in LightPool) if (!s.Wanted && s.K < 0.05f) { free = s; break; }
                if (free == null) continue;
                if (free.Torch != null) free.Torch.Slot = null;
                free.Torch = t; free.K = 0f; free.Wanted = true; t.Slot = free;
                free.Light.transform.position = new Vector3(t.Lx, 2.6f, t.Lz);
            }
        }

        static float Sq(float x, float z) { return x * x + z * z; }

        public void PrimeLights(float px, float pz)
        {
            AssignTorchLights(px, pz);
            foreach (var s in LightPool) if (s.Wanted) s.K = 1f;
        }

        static float Flicker(float time, float seed)
        {
            return 0.85f + Mathf.Sin(time * 9f + seed) * 0.08f + Mathf.Sin(time * 23f + seed * 2f) * 0.06f;
        }

        public void Update(float dt, float px, float pz, bool hasPoint = true)
        {
            time += dt;
            lightTimer -= dt;
            if (lightTimer <= 0f && hasPoint) { lightTimer = 0.25f; AssignTorchLights(px, pz); }
            foreach (var s in LightPool)
            {
                s.K = MathUtil.Damp(s.K, s.Wanted ? 1f : 0f, 5f, dt);
                if (!s.Wanted && s.K < 0.02f && s.Torch != null) { s.Torch.Slot = null; s.Torch = null; }
                float f = s.Torch != null ? Flicker(time, s.Torch.Seed) : 0f;
                s.Light.intensity = 1.5f * f * s.K * Lighting.PointScale;
                bool on = s.Light.intensity > 0.001f;
                if (s.Light.enabled != on) s.Light.enabled = on;
            }
            foreach (var t in Torches)
            {
                float f = Flicker(time, t.Seed);
                t.Flame.localScale = new Vector3(1f, f * 1.1f, 1f);
                t.Core.localScale = new Vector3(1f, f, 1f);
            }
            foreach (var g in d.Gates.Values)
            {
                Transform tr;
                if (!gateObj.TryGetValue(g.Ch, out tr) || !tr) continue;
                if (g.Open)
                {
                    var p = tr.localPosition;
                    p.y = MathUtil.EaseInOut(g.T) * (Cfg.WallH - 0.2f);
                    tr.localPosition = p;
                    if (g.T >= 1f && tr.gameObject.activeSelf) tr.gameObject.SetActive(false);
                }
            }
            if (exitRing)
            {
                float p = 0.5f + Mathf.Sin(time * 3f) * 0.5f;
                var col = MathUtil.Hex(0x66ddff).linear;
                mpb.SetVector(Mats.BaseColorId, new Vector4(col.r, col.g, col.b, 0.35f + p * 0.45f));
                exitRingR.SetPropertyBlock(mpb);
                exitRing.localScale = Vector3.one * (1f + p * 0.06f);
                exitLight.intensity = (1.2f + p * 0.8f) * Lighting.PointScale;
            }
        }

        public void Dispose()
        {
            if (Group) Object.Destroy(Group.gameObject);
        }
    }
}
