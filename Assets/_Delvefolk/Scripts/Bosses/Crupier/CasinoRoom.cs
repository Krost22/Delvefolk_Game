using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Sala del trono del Crupier (referencia "Escenario - Piso 1, zona del trono"): trono con dos leones al fondo,
    // alfombra octogonal con corona en el centro, pasillos de alfombra de tréboles en cruz, columnas con estandarte,
    // mesas de casino en las esquinas con candelabros encima y tragaperras en las paredes laterales.
    // Las casillas cubiertas por alfombra roja son las que quema el resultado ROJO de la ruleta.
    public sealed class CasinoRoom
    {
        readonly Game G;
        readonly CasinoArt art;
        readonly Transform root;
        public readonly int W, H, Cx, Cz;
        public readonly List<Vector2Int> RedTiles = new List<Vector2Int>();
        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<Light> Lights = new List<Light>();
        public bool UsedModels { get; private set; }

        // Tamaño de la alfombra central (de punta a punta de los brazos) y sus partes, en unidades del mundo
        // (el modelo trae los brazos en diagonal: se gira 45° para que enlacen con los pasillos)
        public const float EmblemSize = 13f, EmblemYaw = 45f, EmblemRadius = 5.0f, ArmHalf = 1.3f, ArmReach = 7f;

        public CasinoRoom(Game g, int w, int h)
        {
            G = g; W = w; H = h;
            Cx = w / 2; Cz = (3 + (h - 2)) / 2;
            art = CasinoArt.Load();
            root = new GameObject("SalaDelTrono").transform;
            root.SetParent(g.FloorRoot, false);
            ComputeRedTiles();
            UsedModels = art != null && Art.Has(art.Emblem);
            if (UsedModels) BuildWithModels();
            else BuildCarpetFallback();
            G.Dungeon.InvalidateNav();
        }

        // ---------- Alfombras: qué casillas son "rojas" ----------
        float EX { get { return Grid.X(Cx); } }
        public Vector3 Center { get { return new Vector3(EX, 0f, EZ); } }
        float EZ { get { return Grid.Z(Cz); } }

        // Pasillos de alfombra (casillas inclusivas): sur hasta la entrada, norte hasta el trono y a los lados
        IEnumerable<RectInt> Runners()
        {
            yield return new RectInt(Cx, Cz + 4, 1, H - 2 - (Cz + 4) + 1);   // sur
            yield return new RectInt(Cx, 5, 1, Cz - 4 - 5 + 1);             // norte (entre el trono y la corona)
            yield return new RectInt(1, Cz, Cx - 4 - 1 + 1, 1);             // oeste
            yield return new RectInt(Cx + 4, Cz, W - 2 - (Cx + 4) + 1, 1);  // este
        }

        bool InEmblem(int tx, int tz)
        {
            float dx = Grid.X(tx) - EX, dz = Grid.Z(tz) - EZ;
            const float reach = ArmReach;
            return MathUtil.Hypot(dx, dz) <= EmblemRadius || (Mathf.Abs(dx) <= ArmHalf && Mathf.Abs(dz) <= reach) || (Mathf.Abs(dz) <= ArmHalf && Mathf.Abs(dx) <= reach);
        }

        void ComputeRedTiles()
        {
            var d = G.Dungeon;
            var set = new HashSet<Vector2Int>();
            for (int z = 3; z <= H - 2; z++)
                for (int x = 1; x <= W - 2; x++)
                    if (InEmblem(x, z)) set.Add(new Vector2Int(x, z));
            foreach (var r in Runners())
                for (int z = r.yMin; z < r.yMax; z++)
                    for (int x = r.xMin; x < r.xMax; x++) set.Add(new Vector2Int(x, z));
            foreach (var t in set)
                if (d.TypeAt(t.x, t.y) == TileType.Floor && d.Blocked[d.Idx(t.x, t.y)] == 0) RedTiles.Add(t);
            RedTiles.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        }

        // ---------- Con los modelos de Meshy ----------
        void BuildWithModels()
        {
            // Alfombras (planas, justo sobre el suelo)
            var emb = Place(art.Emblem, "AlfombraCorona", EX, EZ, 0.02f, EmblemYaw);
            Stretch(emb, EmblemSize, EmblemSize);
            foreach (var r in Runners())
            {
                bool alongZ = r.width == 1;
                float x0 = Grid.X(r.xMin) - 1f, x1 = Grid.X(r.xMax - 1) + 1f;
                float z0 = Grid.Z(r.yMin) + 1f, z1 = Grid.Z(r.yMax - 1) - 1f;
                var t = Place(art.Runner, "Pasillo", (x0 + x1) / 2f, (z0 + z1) / 2f, 0.04f, alongZ ? 0f : 90f);
                Stretch(t, 2.5f, alongZ ? Mathf.Abs(z1 - z0) : Mathf.Abs(x1 - x0));
            }

            // Trono y leones al fondo, mirando a la entrada
            Prop(art.Throne, "Trono", Cx, 4, 3.0f, art.ThroneYaw, 1.0f, true);
            Prop(art.Lion, "Leon", Cx - 2, 4, 2.3f, art.LionYaw, 0.9f, false);
            Prop(art.Lion, "Leon", Cx + 2, 4, 2.3f, art.LionYaw, 0.9f, false);
            Curtains();
            AddLight(Grid.X(Cx), 3.2f, Grid.Z(4) + 1.5f, 0xff9a40, 11f, 1.3f);

            // Columnas de picas flanqueando la sala
            foreach (var c in new[] { new Vector2Int(Cx - 4, 6), new Vector2Int(Cx + 4, 6), new Vector2Int(Cx - 4, H - 5), new Vector2Int(Cx + 4, H - 5) })
                Prop(art.Column, "Columna", c.x, c.y, 4.2f, art.ColumnYaw, 0.75f, true);

            // Mesas de casino en las esquinas, con un candelabro encima de cada una
            foreach (var c in new[] { new Vector2Int(3, 6), new Vector2Int(W - 4, 6), new Vector2Int(3, H - 5), new Vector2Int(W - 4, H - 5) })
            {
                float x = Grid.X(c.x), z = Grid.Z(c.y);
                var t = Place(art.CasinoTable, "MesaCasino", x, z, 0f, art.TableYaw + 90f);
                FitLongest(t, 3.6f);
                Block(c.x, c.y);
                Bodies.Add(G.Physics.Add(new Body { X = x - 0.8f, Z = z, Radius = 0.95f, IsStatic = true, Kind = BodyKind.Static }));
                Bodies.Add(G.Physics.Add(new Body { X = x + 0.8f, Z = z, Radius = 0.95f, IsStatic = true, Kind = BodyKind.Static }));
                if (Art.Has(art.Chandelier))
                {
                    var ch = Place(art.Chandelier, "Candelabro", x, z, 4.3f, 0f);
                    FitHeight(ch, 1.8f);
                }
                AddLight(x, 4.6f, z, 0xffa850, 12f, 1.2f);
            }

            // Tragaperras contra las paredes laterales, mirando al centro
            foreach (var c in new[] { new Vector2Int(1, Cz - 2), new Vector2Int(1, Cz + 2), new Vector2Int(W - 2, Cz - 2), new Vector2Int(W - 2, Cz + 2) })
                Prop(art.SlotMachine, "Tragaperras", c.x, c.y, 2.6f, art.SlotYaw + (c.x < Cx ? -90f : 90f), 0.8f, false, true);
        }

        // Pieza con colisión que bloquea su casilla. byWidth: el tamaño es el ancho (no la altura)
        Transform Prop(GameObject prefab, string name, int tx, int tz, float size, float yaw, float radius, bool occluder, bool byWidth = false)
        {
            if (!Art.Has(prefab)) return null;
            float x = Grid.X(tx), z = Grid.Z(tz);
            var t = Place(prefab, name, x, z, 0f, yaw);
            if (byWidth) FitLongest(t, size); else FitHeight(t, size);
            Block(tx, tz);
            Bodies.Add(G.Physics.Add(new Body { X = x, Z = z, Radius = radius, IsStatic = true, Kind = BodyKind.Static }));
            if (occluder) G.View.AddOccluder(t, x, z, 0.12f);
            return t;
        }

        void Block(int tx, int tz)
        {
            var d = G.Dungeon;
            d.Blocked[d.Idx(tx, tz)] = 1;
        }

        // Instancia el modelo dentro de un "soporte" con el pivote abajo y en el centro, ya girado (yaw en grados)
        Transform Place(GameObject prefab, string name, float x, float z, float y, float yaw)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            var inner = new GameObject("Centrado").transform;
            inner.SetParent(holder, false);
            var go = Art.Spawn(prefab, inner);
            var b = WorldBounds(go);
            var off = inner.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            go.transform.localPosition -= off;
            inner.localScale = Vector3.one;
            holder.localPosition = new Vector3(x, y, z);
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            sizes[holder] = b.size;
            return holder;
        }

        readonly Dictionary<Transform, Vector3> sizes = new Dictionary<Transform, Vector3>();

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = new Bounds(go.transform.position, Vector3.zero);
            for (int i = 0; i < rs.Length; i++) { if (i == 0) b = rs[i].bounds; else b.Encapsulate(rs[i].bounds); }
            return b;
        }

        void FitHeight(Transform t, float h) { t.GetChild(0).localScale = Vector3.one * (h / Mathf.Max(0.001f, sizes[t].y)); }
        void FitLongest(Transform t, float l) { var s = sizes[t]; t.GetChild(0).localScale = Vector3.one * (l / Mathf.Max(0.001f, Mathf.Max(s.x, s.z))); }

        // Alfombras: ancho (x) y largo (z) exactos y grosor fijo (por debajo de la capa de aviso del suelo)
        public const float CarpetThickness = 0.05f;
        void Stretch(Transform t, float w, float l)
        {
            var s = sizes[t];
            t.GetChild(0).localScale = new Vector3(w / Mathf.Max(0.001f, s.x), CarpetThickness / Mathf.Max(0.0001f, s.y), l / Mathf.Max(0.001f, s.z));
        }

        void AddLight(float x, float y, float z, int color, float range, float intensity)
        {
            var l = Lighting.Point(root, "LuzCasino", color, range);
            l.transform.position = new Vector3(x, y, z);
            l.intensity = intensity * Lighting.PointScale;
            Lights.Add(l);
        }

        // Cortinas rojas con barra dorada en la pared del fondo, a los lados del rastrillo
        void Curtains()
        {
            float wallFace = Grid.Z(2) - Cfg.Tile / 2f - 0.12f;
            var mb = new MeshBuilder();
            Color red = MathUtil.Hex(0x9a1420), dark = MathUtil.Hex(0x6e0e18), gold = MathUtil.Hex(0xe8c050);
            foreach (float s in new[] { -1f, 1f })
            {
                float cx = Grid.X(Cx) + s * 3.1f;
                for (int k = 0; k < 4; k++)   // pliegues
                    mb.Add(ProcMesh.Box(0.55f, 3.4f, 0.16f), new Vector3(cx - 0.75f + k * 0.5f, 1.75f, wallFace + (k % 2 == 0 ? 0f : -0.08f)), k % 2 == 0 ? red : dark);
                mb.Add(ProcMesh.Cylinder(0.06f, 0.06f, 2.4f, 6), Matrix4x4.TRS(new Vector3(cx, 3.5f, wallFace - 0.05f), Quaternion.Euler(0, 0, 90f), Vector3.one), gold);
            }
            CharacterModel.MeshObject(root, "Cortinas", mb.ToMesh("cortinas"), Mats.Lit, Vector3.zero);
        }

        // ---------- Sin modelos (clon sin la carpeta _Custom): alfombra roja por código ----------
        void BuildCarpetFallback()
        {
            var mb = new MeshBuilder();
            var quad = ProcMesh.Plane(Cfg.Tile, Cfg.Tile).RotateX(-Mathf.PI / 2f);
            foreach (var t in RedTiles) mb.Add(quad, new Vector3(Grid.X(t.x), 0.02f, Grid.Z(t.y)), MathUtil.Hex(0xa8161e));
            var go = CharacterModel.MeshObject(root, "Alfombra", mb.ToMesh("alfombraCasino"), Mats.Transparent, Vector3.zero);
            Shape.SetOpacity(go.GetComponent<Renderer>(), 0.8f);
        }

        public void Destroy()
        {
            foreach (var b in Bodies) G.Physics.Remove(b);
            Bodies.Clear();
        }
    }
}
