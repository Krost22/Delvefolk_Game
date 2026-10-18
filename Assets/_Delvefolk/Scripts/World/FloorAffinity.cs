using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Aspecto de una afinidad: tinte de suelo y muros (canal _Recolor del shader: conserva la textura y la
    // luz, solo cambia el color) y detalles decorativos planos que no tocan la geometría ni el recorrido.
    [System.Serializable]
    public sealed class AffinityTheme
    {
        public Element Element;
        public string Name = "", Icon = "", Explain = "";
        public Color FloorTint = Color.white, WallTint = Color.white;
        [Range(0f, 1f)] public float FloorAmount = 0.88f, WallAmount = 0.85f;
        public Color DecalA = Color.white, DecalB = Color.white, Glow = Color.white;
        [Range(0f, 1f)] public float DecalDensity = 0.22f;
        [Tooltip("Arma relacionada que puede salir con esta afinidad")]
        public string WeaponId;
    }

    // Aplica (y deshace) la afinidad a las superficies del piso. Nunca toca materiales compartidos: solo el
    // MaterialPropertyBlock de cada renderizador del entorno (DungeonView.Surfaces + EnvironmentSurface).
    public sealed class FloorThemeApplier
    {
        readonly Game g;
        readonly List<Renderer> tinted = new List<Renderer>();
        Transform decor;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        public Element Applied { get; private set; }
        public int TintedCount { get { return tinted.Count; } }
        public Transform Decor { get { return decor; } }

        public FloorThemeApplier(Game game) { g = game; }

        public void Apply(Element e)
        {
            Clear();
            var t = FloorThemeCatalog.Load().Get(e);
            if (t == null || g.View == null) return;
            Applied = e;
            var floorSet = new HashSet<Renderer>();
            foreach (var r in g.View.Surfaces) if (r) { tinted.Add(r); if (r.name.StartsWith("Suelo") || IsFloorPiece(r)) floorSet.Add(r); }
            if (g.FloorRoot)
                foreach (var s in g.FloorRoot.GetComponentsInChildren<EnvironmentSurface>(true))
                    foreach (var r in s.GetComponentsInChildren<Renderer>(true)) if (!tinted.Contains(r)) tinted.Add(r);
            foreach (var r in tinted)
            {
                bool floor = floorSet.Contains(r);
                var c = (floor ? t.FloorTint : t.WallTint).linear;
                SetRecolor(r, new Vector4(c.r, c.g, c.b, floor ? t.FloorAmount : t.WallAmount));
            }
            BuildDecor(t);
        }

        // Las losas del paquete se colocan a ras de suelo; los muros sobresalen
        static bool IsFloorPiece(Renderer r) { return r.bounds.max.y < 0.35f; }

        static void SetRecolor(Renderer r, Vector4 v)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetVector(Mats.RecolorId, v);
            r.SetPropertyBlock(mpb);
        }

        // Vuelve al aspecto base (los materiales originales nunca se modificaron)
        public void Clear()
        {
            foreach (var r in tinted) if (r) SetRecolor(r, new Vector4(1f, 1f, 1f, 0f));
            tinted.Clear();
            if (decor) Object.Destroy(decor.gameObject);
            decor = null;
            Applied = Element.None;
        }

        // ---------- Detalles decorativos ----------
        // Casillas de suelo libres (sin salida, rastrillos, púas, fosos, objetos colocados ni el inicio)
        List<Vector2Int> FreeTiles()
        {
            var d = g.Dungeon;
            var used = new HashSet<int>();
            foreach (var s in d.Spawns) used.Add(d.Idx(s.Tx, s.Tz));
            int ex = Grid.TileX(d.ExitX), ez = Grid.TileZ(d.ExitZ);
            var list = new List<Vector2Int>();
            for (int tz = 0; tz < d.H; tz++)
                for (int tx = 0; tx < d.W; tx++)
                {
                    if (d.TypeAt(tx, tz) != TileType.Floor || d.CharAt(tx, tz) == 'X') continue;
                    int i = d.Idx(tx, tz);
                    if (used.Contains(i) || d.Spike[i] != 0 || d.Blocked[i] != 0) continue;
                    if (Mathf.Abs(tx - ex) <= 1 && Mathf.Abs(tz - ez) <= 1) continue;
                    list.Add(new Vector2Int(tx, tz));
                }
            return list;
        }

        // ¿Toca un muro por algún lado? Devuelve la dirección hacia él
        bool WallSide(int tx, int tz, out Vector3 dir)
        {
            int[] ox = { 1, -1, 0, 0 }, oz = { 0, 0, 1, -1 };
            for (int k = 0; k < 4; k++)
                if (g.Dungeon.TypeAt(tx + ox[k], tz + oz[k]) == TileType.Wall) { dir = Grid.Dir(ox[k], oz[k]); return true; }
            dir = Vector3.zero;
            return false;
        }

        void BuildDecor(AffinityTheme t)
        {
            decor = new GameObject("TemaAfinidad_" + t.Element).transform;
            decor.SetParent(g.FloorRoot, false);
            var flat = new MeshBuilder();      // manchas y charcos (transparentes)
            var glow = new MeshBuilder();      // grietas, luces y paneles (sin iluminación: brillan)
            var solid = new MeshBuilder();     // hongos, brotes y placas (con luz)
            var r = new Rng((uint)(g.Seed * 131 + g.Floor * 7919 + (int)t.Element));
            const float Y = 0.015f, H = Cfg.Tile / 2f;
            foreach (var tile in FreeTiles())
            {
                if (r.Next() > t.DecalDensity) continue;
                var c = Grid.ToWorld(tile.x, tile.y);
                float ox = (r.Next() - 0.5f) * H, oz = (r.Next() - 0.5f) * H, rot = r.Next() * Mathf.PI * 2f;
                var p = c + new Vector3(ox, Y, oz);
                Vector3 wdir;
                bool nearWall = WallSide(tile.x, tile.y, out wdir);
                switch (t.Element)
                {
                    case Element.Water:
                        Blob(flat, p, 0.3f + r.Next() * 0.35f, 0.6f + r.Next() * 0.4f, rot, A(t.DecalA, 0.22f));
                        if (r.Next() < 0.5f) Blob(flat, p + new Vector3(0.4f, 0.001f, 0.25f), 0.12f, 1f, 0f, A(t.DecalB, 0.3f));
                        if (nearWall) Blob(flat, c + wdir * (H - 0.35f) + new Vector3(0, Y, 0), 0.8f, 0.35f, Mathf.Atan2(wdir.x, wdir.z), A(t.DecalA * 0.6f, 0.2f));
                        break;
                    case Element.Fire:
                        for (int k = 0; k < 3; k++)
                        {
                            float a = rot + k * 2.1f + (r.Next() - 0.5f) * 0.6f, len = 0.4f + r.Next() * 0.5f;
                            var q = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
                            glow.Add(ProcMesh.Box(0.05f, 0.01f, len), Matrix4x4.TRS(p + q * new Vector3(0, 0, len / 2f), q, Vector3.one), t.DecalA);
                        }
                        Blob(flat, p, 0.45f, 1f, 0f, A(t.DecalB, 0.35f));
                        break;
                    case Element.Plant:
                        Blob(flat, p, 0.4f + r.Next() * 0.4f, 0.5f + r.Next() * 0.5f, rot, A(t.DecalA, 0.35f));
                        for (int k = 0; k < 3; k++)
                            solid.Add(ProcMesh.Octahedron(0.07f + r.Next() * 0.05f), p + new Vector3((r.Next() - 0.5f) * 0.6f, 0.05f, (r.Next() - 0.5f) * 0.6f), t.Glow);
                        if (nearWall)
                        {
                            var q = Quaternion.LookRotation(Vector3.Cross(wdir, Vector3.up));
                            solid.Add(ProcMesh.Box(0.06f, 0.05f, Cfg.Tile * 0.9f), Matrix4x4.TRS(c + wdir * (H - 0.08f) + new Vector3(0, 0.03f, 0), q, Vector3.one), t.DecalA);
                        }
                        break;
                    case Element.Poison:
                        Blob(flat, p, 0.4f + r.Next() * 0.45f, 0.6f + r.Next() * 0.4f, rot, A(r.Next() < 0.5f ? t.DecalA : t.DecalB, 0.3f));
                        glow.Add(ProcMesh.Sphere(0.04f, 4, 3), p + new Vector3(0.2f, 0.03f, -0.15f), t.Glow);
                        if (nearWall) Mushroom(solid, c + wdir * (H - 0.3f) + new Vector3((r.Next() - 0.5f) * 0.8f, 0f, (r.Next() - 0.5f) * 0.8f), 0.18f + r.Next() * 0.12f, t);
                        break;
                    case Element.Future:
                        var qq = Quaternion.Euler(0f, 90f * (int)(r.Next() * 4), 0f);
                        solid.Add(ProcMesh.Box(Cfg.Tile * 0.8f, 0.02f, Cfg.Tile * 0.8f), Matrix4x4.TRS(c + new Vector3(0, 0.01f, 0), qq, Vector3.one), MathUtil.Hex(0x5a6270));
                        var col = r.Next() < 0.7f ? t.DecalA : t.DecalB;
                        foreach (var s in new[] { -1f, 1f })
                            glow.Add(ProcMesh.Box(Cfg.Tile * 0.7f, 0.012f, 0.05f), Matrix4x4.TRS(c + qq * new Vector3(0, 0.025f, s * Cfg.Tile * 0.33f), qq, Vector3.one), col);
                        break;
                }
            }
            if (!flat.Empty) CharacterModel.MeshObject(decor, "Manchas", flat.ToMesh("afinidadManchas"), Mats.Transparent, Vector3.zero);
            if (!glow.Empty) CharacterModel.MeshObject(decor, "Brillos", glow.ToMesh("afinidadBrillos"), Mats.Unlit, Vector3.zero);
            if (!solid.Empty) CharacterModel.MeshObject(decor, "Detalles", solid.ToMesh("afinidadDetalles"), Mats.Lit, Vector3.zero);
        }

        static Color A(Color c, float a) { return new Color(c.r, c.g, c.b, a); }

        static void Blob(MeshBuilder b, Vector3 p, float r, float squash, float rot, Color c)
        {
            b.Add(ProcMesh.Circle(1f, 10).RotateX(-Mathf.PI / 2f), Matrix4x4.TRS(p, Quaternion.Euler(0f, rot * Mathf.Rad2Deg, 0f), new Vector3(r, 1f, r * squash)), c);
        }

        static void Mushroom(MeshBuilder b, Vector3 p, float h, AffinityTheme t)
        {
            b.Add(ProcMesh.Cylinder(h * 0.18f, h * 0.22f, h, 5), p + new Vector3(0, h / 2f, 0), MathUtil.Hex(0xf0e2c8));
            b.Add(ProcMesh.Cone(h * 0.55f, h * 0.45f, 6), p + new Vector3(0, h + h * 0.2f, 0), t.DecalB);
        }
    }
}
