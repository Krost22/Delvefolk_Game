using System;
using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Datos de malla en construcción (posiciones, normales, uv, colores e índices por submalla).
    // Las primitivas copian los generadores de three.js con los mismos parámetros y ejes (Y arriba):
    // al leer los números tal cual en Unity el resultado sale espejado izquierda↔derecha (aceptado).
    public sealed class MeshData
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<List<int>> Sub = new List<List<int>> { new List<int>() };

        public List<int> I { get { return Sub[0]; } }
        public int Count { get { return V.Count; } }

        public void Add(Vector3 v, Vector3 n, Vector2 uv) { V.Add(v); N.Add(n); UV.Add(uv); C.Add(Color.white); }

        public MeshData Translate(float x, float y, float z)
        {
            var t = new Vector3(x, y, z);
            for (int i = 0; i < V.Count; i++) V[i] += t;
            return this;
        }

        public MeshData Scale(float x, float y, float z)
        {
            for (int i = 0; i < V.Count; i++) { var v = V[i]; V[i] = new Vector3(v.x * x, v.y * y, v.z * z); }
            // Normales con la inversa-traspuesta de la escala
            for (int i = 0; i < N.Count; i++) { var n = N[i]; N[i] = new Vector3(n.x / x, n.y / y, n.z / z).normalized; }
            return this;
        }

        public MeshData Rotate(Quaternion q)
        {
            for (int i = 0; i < V.Count; i++) { V[i] = q * V[i]; N[i] = q * N[i]; }
            return this;
        }

        public MeshData RotateX(float a) { return Rotate(Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.right)); }
        public MeshData RotateY(float a) { return Rotate(Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up)); }
        public MeshData RotateZ(float a) { return Rotate(Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.forward)); }

        public MeshData Transform(Matrix4x4 m)
        {
            var nm = m.inverse.transpose;
            for (int i = 0; i < V.Count; i++) { V[i] = m.MultiplyPoint3x4(V[i]); N[i] = nm.MultiplyVector(N[i]).normalized; }
            return this;
        }

        public MeshData Tint(Color c)
        {
            for (int i = 0; i < C.Count; i++) C[i] = c;
            return this;
        }

        // Une todas las submallas en una (para mallas de un solo material)
        public MeshData Flatten()
        {
            for (int s = 1; s < Sub.Count; s++) Sub[0].AddRange(Sub[s]);
            if (Sub.Count > 1) Sub.RemoveRange(1, Sub.Count - 1);
            return this;
        }

        public Mesh ToMesh(string name = "proc")
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, UV);
            // Los colores se escriben en sRGB (como en el original); el shader trabaja en espacio lineal
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                var lin = new List<Color>(C.Count);
                for (int i = 0; i < C.Count; i++) { var c = C[i].linear; c.a = C[i].a; lin.Add(c); }
                m.SetColors(lin);
            }
            else m.SetColors(C);
            m.subMeshCount = Sub.Count;
            for (int s = 0; s < Sub.Count; s++) m.SetTriangles(Sub[s], s, true);
            m.RecalculateBounds();
            return m;
        }
    }

    // Generadores de primitivas (BoxGeometry, CylinderGeometry, ... de three.js r149)
    public static class ProcMesh
    {
        // ---------- Caja (6 grupos: +x, -x, +y, -y, +z, -z) ----------
        public static MeshData Box(float w, float h, float d, int ws = 1, int hs = 1, int ds = 1, bool groups = false)
        {
            var md = new MeshData();
            if (groups) for (int g = 1; g < 6; g++) md.Sub.Add(new List<int>());
            BuildPlane(md, 2, 1, 0, -1, -1, d, h, w, ds, hs, groups ? 0 : 0);
            BuildPlane(md, 2, 1, 0, 1, -1, d, h, -w, ds, hs, groups ? 1 : 0);
            BuildPlane(md, 0, 2, 1, 1, 1, w, d, h, ws, ds, groups ? 2 : 0);
            BuildPlane(md, 0, 2, 1, 1, -1, w, d, -h, ws, ds, groups ? 3 : 0);
            BuildPlane(md, 0, 1, 2, 1, -1, w, h, d, ws, hs, groups ? 4 : 0);
            BuildPlane(md, 0, 1, 2, -1, -1, w, h, -d, ws, hs, groups ? 5 : 0);
            return md;
        }

        static void BuildPlane(MeshData md, int u, int v, int w, float udir, float vdir, float width, float height, float depth, int gridX, int gridY, int group)
        {
            float segW = width / gridX, segH = height / gridY;
            float wh = width / 2f, hh = height / 2f, dh = depth / 2f;
            int gx1 = gridX + 1, gy1 = gridY + 1;
            int start = md.Count;
            for (int iy = 0; iy < gy1; iy++)
            {
                float y = iy * segH - hh;
                for (int ix = 0; ix < gx1; ix++)
                {
                    float x = ix * segW - wh;
                    var p = new Vector3(); p[u] = x * udir; p[v] = y * vdir; p[w] = dh;
                    var n = new Vector3(); n[w] = depth > 0 ? 1 : -1;
                    md.Add(p, n, new Vector2(ix / (float)gridX, 1f - iy / (float)gridY));
                }
            }
            var idx = md.Sub[group];
            for (int iy = 0; iy < gridY; iy++)
                for (int ix = 0; ix < gridX; ix++)
                {
                    int a = start + ix + gx1 * iy, b = start + ix + gx1 * (iy + 1);
                    int c = start + (ix + 1) + gx1 * (iy + 1), d = start + (ix + 1) + gx1 * iy;
                    idx.Add(a); idx.Add(b); idx.Add(d);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }
        }

        // ---------- Cilindro / cono ----------
        public static MeshData Cylinder(float rTop, float rBottom, float height, int radial = 8, int hseg = 1, bool openEnded = false, float thetaStart = 0f, float thetaLength = MathUtil.TAU)
        {
            var md = new MeshData();
            float half = height / 2f;
            float slope = (rBottom - rTop) / height;
            var rows = new List<int[]>();
            for (int y = 0; y <= hseg; y++)
            {
                var row = new int[radial + 1];
                float v = y / (float)hseg;
                float radius = v * (rBottom - rTop) + rTop;
                for (int x = 0; x <= radial; x++)
                {
                    float u = x / (float)radial;
                    float th = u * thetaLength + thetaStart;
                    float s = Mathf.Sin(th), c = Mathf.Cos(th);
                    row[x] = md.Count;
                    md.Add(new Vector3(radius * s, -v * height + half, radius * c), new Vector3(s, slope, c).normalized, new Vector2(u, 1f - v));
                }
                rows.Add(row);
            }
            for (int x = 0; x < radial; x++)
                for (int y = 0; y < hseg; y++)
                {
                    int a = rows[y][x], b = rows[y + 1][x], c = rows[y + 1][x + 1], d = rows[y][x + 1];
                    md.I.Add(a); md.I.Add(b); md.I.Add(d);
                    md.I.Add(b); md.I.Add(c); md.I.Add(d);
                }
            if (!openEnded)
            {
                if (rTop > 0) Cap(md, true, rTop, half, radial, thetaStart, thetaLength);
                if (rBottom > 0) Cap(md, false, rBottom, half, radial, thetaStart, thetaLength);
            }
            return md;
        }

        static void Cap(MeshData md, bool top, float radius, float half, int radial, float thetaStart, float thetaLength)
        {
            float sign = top ? 1f : -1f;
            int centerStart = md.Count;
            for (int x = 1; x <= radial; x++) md.Add(new Vector3(0, half * sign, 0), new Vector3(0, sign, 0), new Vector2(0.5f, 0.5f));
            int centerEnd = md.Count;
            for (int x = 0; x <= radial; x++)
            {
                float u = x / (float)radial;
                float th = u * thetaLength + thetaStart;
                float s = Mathf.Sin(th), c = Mathf.Cos(th);
                md.Add(new Vector3(radius * s, half * sign, radius * c), new Vector3(0, sign, 0), new Vector2(c * 0.5f + 0.5f, s * 0.5f * sign + 0.5f));
            }
            for (int x = 0; x < radial; x++)
            {
                int c = centerStart + x, i = centerEnd + x;
                if (top) { md.I.Add(i); md.I.Add(i + 1); md.I.Add(c); }
                else { md.I.Add(i + 1); md.I.Add(i); md.I.Add(c); }
            }
        }

        public static MeshData Cone(float radius, float height, int radial = 8, int hseg = 1, bool openEnded = false, float thetaStart = 0f, float thetaLength = MathUtil.TAU)
        {
            return Cylinder(0f, radius, height, radial, hseg, openEnded, thetaStart, thetaLength);
        }

        // ---------- Esfera ----------
        public static MeshData Sphere(float radius, int wseg = 8, int hseg = 6, float phiStart = 0f, float phiLength = MathUtil.TAU, float thetaStart = 0f, float thetaLength = MathUtil.PI)
        {
            var md = new MeshData();
            wseg = Math.Max(3, wseg); hseg = Math.Max(2, hseg);
            float thetaEnd = Mathf.Min(thetaStart + thetaLength, MathUtil.PI);
            var grid = new List<int[]>();
            for (int iy = 0; iy <= hseg; iy++)
            {
                var row = new int[wseg + 1];
                float v = iy / (float)hseg;
                float uOff = 0f;
                if (iy == 0 && thetaStart == 0f) uOff = 0.5f / wseg;
                else if (iy == hseg && thetaEnd >= MathUtil.PI - 1e-6f) uOff = -0.5f / wseg;
                for (int ix = 0; ix <= wseg; ix++)
                {
                    float u = ix / (float)wseg;
                    var p = new Vector3(
                        -radius * Mathf.Cos(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength),
                        radius * Mathf.Cos(thetaStart + v * thetaLength),
                        radius * Mathf.Sin(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength));
                    row[ix] = md.Count;
                    md.Add(p, p.sqrMagnitude > 0 ? p.normalized : Vector3.up, new Vector2(u + uOff, 1f - v));
                }
                grid.Add(row);
            }
            for (int iy = 0; iy < hseg; iy++)
                for (int ix = 0; ix < wseg; ix++)
                {
                    int a = grid[iy][ix + 1], b = grid[iy][ix], c = grid[iy + 1][ix], d = grid[iy + 1][ix + 1];
                    if (iy != 0 || thetaStart > 0) { md.I.Add(a); md.I.Add(b); md.I.Add(d); }
                    if (iy != hseg - 1 || thetaEnd < MathUtil.PI - 1e-6f) { md.I.Add(b); md.I.Add(c); md.I.Add(d); }
                }
            return md;
        }

        // ---------- Poliedros (no indexados, normales planas con detalle 0) ----------
        static readonly float Phi = (1f + Mathf.Sqrt(5f)) / 2f;

        public static MeshData Icosahedron(float radius, int detail = 0)
        {
            float t = Phi;
            float[] v = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
            int[] i = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            return Polyhedron(v, i, radius, detail);
        }

        public static MeshData Octahedron(float radius, int detail = 0)
        {
            float[] v = { 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1 };
            int[] i = { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 2, 5, 1, 5, 3, 1, 3, 4, 1, 4, 2 };
            return Polyhedron(v, i, radius, detail);
        }

        public static MeshData Tetrahedron(float radius, int detail = 0)
        {
            float[] v = { 1, 1, 1, -1, -1, 1, -1, 1, -1, 1, -1, -1 };
            int[] i = { 2, 1, 0, 0, 3, 2, 1, 3, 0, 2, 3, 1 };
            return Polyhedron(v, i, radius, detail);
        }

        public static MeshData Dodecahedron(float radius, int detail = 0)
        {
            float t = Phi, r = 1f / Phi;
            float[] v =
            {
                -1, -1, -1, -1, -1, 1, -1, 1, -1, -1, 1, 1, 1, -1, -1, 1, -1, 1, 1, 1, -1, 1, 1, 1,
                0, -r, -t, 0, -r, t, 0, r, -t, 0, r, t,
                -r, -t, 0, -r, t, 0, r, -t, 0, r, t, 0,
                -t, 0, -r, t, 0, -r, -t, 0, r, t, 0, r,
            };
            int[] i =
            {
                3, 11, 7, 3, 7, 15, 3, 15, 13, 7, 19, 17, 7, 17, 6, 7, 6, 15, 17, 4, 8, 17, 8, 10, 17, 10, 6,
                8, 0, 16, 8, 16, 2, 8, 2, 10, 0, 12, 1, 0, 1, 18, 0, 18, 16, 6, 10, 2, 6, 2, 13, 6, 13, 15,
                2, 16, 18, 2, 18, 3, 2, 3, 13, 18, 1, 9, 18, 9, 11, 18, 11, 3, 4, 14, 12, 4, 12, 0, 4, 0, 8,
                11, 9, 5, 11, 5, 19, 11, 19, 7, 19, 5, 14, 19, 14, 4, 19, 4, 17, 1, 12, 14, 1, 14, 5, 1, 5, 9,
            };
            return Polyhedron(v, i, radius, detail);
        }

        static MeshData Polyhedron(float[] verts, int[] idx, float radius, int detail)
        {
            var buf = new List<Vector3>();
            for (int k = 0; k < idx.Length; k += 3)
            {
                Vector3 a = V3(verts, idx[k]), b = V3(verts, idx[k + 1]), c = V3(verts, idx[k + 2]);
                int cols = detail + 1;
                var grid = new Vector3[cols + 1][];
                for (int i = 0; i <= cols; i++)
                {
                    Vector3 aj = Vector3.Lerp(a, c, i / (float)cols), bj = Vector3.Lerp(b, c, i / (float)cols);
                    int rows = cols - i;
                    grid[i] = new Vector3[rows + 1];
                    for (int j = 0; j <= rows; j++) grid[i][j] = (j == 0 && i == cols) ? aj : Vector3.Lerp(aj, bj, rows == 0 ? 0 : j / (float)rows);
                }
                for (int i = 0; i < cols; i++)
                    for (int j = 0; j < 2 * (cols - i) - 1; j++)
                    {
                        int q = j / 2;
                        if (j % 2 == 0) { buf.Add(grid[i][q + 1]); buf.Add(grid[i + 1][q]); buf.Add(grid[i][q]); }
                        else { buf.Add(grid[i][q + 1]); buf.Add(grid[i + 1][q + 1]); buf.Add(grid[i + 1][q]); }
                    }
            }
            var md = new MeshData();
            for (int k = 0; k < buf.Count; k += 3)
            {
                Vector3 a = buf[k].normalized * radius, b = buf[k + 1].normalized * radius, c = buf[k + 2].normalized * radius;
                // Normal de la cara (en three.js: computeVertexNormals sobre geometría no indexada)
                Vector3 fn = Vector3.Cross(c - b, a - b).normalized;
                Vector3 na = detail == 0 ? fn : a.normalized, nb = detail == 0 ? fn : b.normalized, nc = detail == 0 ? fn : c.normalized;
                int s = md.Count;
                md.Add(a, na, SphereUV(a)); md.Add(b, nb, SphereUV(b)); md.Add(c, nc, SphereUV(c));
                md.I.Add(s); md.I.Add(s + 1); md.I.Add(s + 2);
            }
            return md;
        }

        static Vector3 V3(float[] v, int i) { return new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]); }

        static Vector2 SphereUV(Vector3 p)
        {
            var n = p.normalized;
            return new Vector2(Mathf.Atan2(n.z, -n.x) / MathUtil.TAU + 0.5f, Mathf.Atan2(-n.y, Mathf.Sqrt(n.x * n.x + n.z * n.z)) / MathUtil.PI + 0.5f);
        }

        // ---------- Toro (con arco) ----------
        public static MeshData Torus(float radius, float tube, int radialSeg = 6, int tubularSeg = 12, float arc = MathUtil.TAU)
        {
            var md = new MeshData();
            for (int j = 0; j <= radialSeg; j++)
                for (int i = 0; i <= tubularSeg; i++)
                {
                    float u = i / (float)tubularSeg * arc;
                    float v = j / (float)radialSeg * MathUtil.TAU;
                    var p = new Vector3((radius + tube * Mathf.Cos(v)) * Mathf.Cos(u), (radius + tube * Mathf.Cos(v)) * Mathf.Sin(u), tube * Mathf.Sin(v));
                    var center = new Vector3(radius * Mathf.Cos(u), radius * Mathf.Sin(u), 0);
                    md.Add(p, (p - center).normalized, new Vector2(i / (float)tubularSeg, j / (float)radialSeg));
                }
            for (int j = 1; j <= radialSeg; j++)
                for (int i = 1; i <= tubularSeg; i++)
                {
                    int a = (tubularSeg + 1) * j + i - 1, b = (tubularSeg + 1) * (j - 1) + i - 1;
                    int c = (tubularSeg + 1) * (j - 1) + i, d = (tubularSeg + 1) * j + i;
                    md.I.Add(a); md.I.Add(b); md.I.Add(d);
                    md.I.Add(b); md.I.Add(c); md.I.Add(d);
                }
            return md;
        }

        // ---------- Anillo y círculo (en el plano XY, normal +Z) ----------
        public static MeshData Ring(float inner, float outer, int thetaSeg = 24, int phiSeg = 1, float thetaStart = 0f, float thetaLength = MathUtil.TAU)
        {
            var md = new MeshData();
            float radius = inner, step = (outer - inner) / phiSeg;
            for (int j = 0; j <= phiSeg; j++)
            {
                for (int i = 0; i <= thetaSeg; i++)
                {
                    float seg = thetaStart + i / (float)thetaSeg * thetaLength;
                    var p = new Vector3(radius * Mathf.Cos(seg), radius * Mathf.Sin(seg), 0);
                    md.Add(p, Vector3.forward, new Vector2((p.x / outer + 1) / 2, (p.y / outer + 1) / 2));
                }
                radius += step;
            }
            for (int j = 0; j < phiSeg; j++)
            {
                int level = j * (thetaSeg + 1);
                for (int i = 0; i < thetaSeg; i++)
                {
                    int s = i + level;
                    int a = s, b = s + thetaSeg + 1, c = s + thetaSeg + 2, d = s + 1;
                    md.I.Add(a); md.I.Add(b); md.I.Add(d);
                    md.I.Add(b); md.I.Add(c); md.I.Add(d);
                }
            }
            return md;
        }

        public static MeshData Circle(float radius, int segments = 16, float thetaStart = 0f, float thetaLength = MathUtil.TAU)
        {
            var md = new MeshData();
            segments = Math.Max(3, segments);
            md.Add(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                float seg = thetaStart + s / (float)segments * thetaLength;
                var p = new Vector3(radius * Mathf.Cos(seg), radius * Mathf.Sin(seg), 0);
                md.Add(p, Vector3.forward, new Vector2((p.x / radius + 1) / 2, (p.y / radius + 1) / 2));
            }
            for (int i = 1; i <= segments; i++) { md.I.Add(i); md.I.Add(i + 1); md.I.Add(0); }
            return md;
        }

        // ---------- Plano (XY, normal +Z) ----------
        public static MeshData Plane(float w, float h, int ws = 1, int hs = 1)
        {
            var md = new MeshData();
            float wh = w / 2f, hh = h / 2f, sw = w / ws, sh = h / hs;
            int gx1 = ws + 1;
            for (int iy = 0; iy <= hs; iy++)
            {
                float y = iy * sh - hh;
                for (int ix = 0; ix <= ws; ix++)
                {
                    float x = ix * sw - wh;
                    md.Add(new Vector3(x, -y, 0), Vector3.forward, new Vector2(ix / (float)ws, 1f - iy / (float)hs));
                }
            }
            for (int iy = 0; iy < hs; iy++)
                for (int ix = 0; ix < ws; ix++)
                {
                    int a = ix + gx1 * iy, b = ix + gx1 * (iy + 1), c = (ix + 1) + gx1 * (iy + 1), d = (ix + 1) + gx1 * iy;
                    md.I.Add(a); md.I.Add(b); md.I.Add(d);
                    md.I.Add(b); md.I.Add(c); md.I.Add(d);
                }
            return md;
        }

        // ---------- Torno (perfil girado sobre Y) ----------
        public static MeshData Lathe(IList<Vector2> points, int segments = 12, float phiStart = 0f, float phiLength = MathUtil.TAU)
        {
            var md = new MeshData();
            phiLength = MathUtil.Clamp(phiLength, 0, MathUtil.TAU);
            float inv = 1f / segments;
            int np = points.Count;
            for (int i = 0; i <= segments; i++)
            {
                float phi = phiStart + i * inv * phiLength;
                float s = Mathf.Sin(phi), c = Mathf.Cos(phi);
                for (int j = 0; j < np; j++)
                {
                    // Normal del perfil: perpendicular al tramo, girada con el ángulo
                    Vector2 a = points[Math.Max(0, j - 1)], b = points[Math.Min(np - 1, j + 1)];
                    Vector2 t = (b - a).normalized;
                    var n2 = new Vector2(t.y, -t.x);
                    md.Add(new Vector3(points[j].x * s, points[j].y, points[j].x * c), new Vector3(n2.x * s, n2.y, n2.x * c).normalized,
                        new Vector2(i / (float)segments, j / (float)(np - 1)));
                }
            }
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < np - 1; j++)
                {
                    int bse = j + i * np;
                    int a = bse, b = bse + np, c = bse + np + 1, d = bse + 1;
                    md.I.Add(a); md.I.Add(b); md.I.Add(d);
                    md.I.Add(c); md.I.Add(d); md.I.Add(b);
                }
            return md;
        }

        // Rotación con el orden de Euler por defecto de three.js ('XYZ' intrínseco: Rx·Ry·Rz)
        public static Quaternion EulerXYZ(float x, float y, float z)
        {
            return Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);
        }

        // Orden 'YXZ' (pivote del brazo): coincide con Quaternion.Euler de Unity
        public static Quaternion EulerYXZ(float x, float y, float z)
        {
            return Quaternion.Euler(x * Mathf.Rad2Deg, y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);
        }
    }

    // Fusiona piezas (con su transformación y color) en una sola malla con colores por vértice (mergeMeshes)
    public sealed class MeshBuilder
    {
        readonly MeshData data = new MeshData();

        public MeshBuilder Add(MeshData part, Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            return Add(part, Matrix4x4.TRS(pos, rot, scale), color);
        }

        public MeshBuilder Add(MeshData part, Vector3 pos, Color color)
        {
            return Add(part, Matrix4x4.Translate(pos), color);
        }

        public MeshBuilder Add(MeshData part, Matrix4x4 m, Color color)
        {
            var nm = m.inverse.transpose;
            int baseIndex = data.Count;
            for (int i = 0; i < part.Count; i++)
            {
                data.V.Add(m.MultiplyPoint3x4(part.V[i]));
                data.N.Add(nm.MultiplyVector(part.N[i]).normalized);
                data.UV.Add(part.UV[i]);
                var pc = part.C[i];
                data.C.Add(new Color(pc.r * color.r, pc.g * color.g, pc.b * color.b, pc.a * color.a));
            }
            // Una escala negativa invierte el sentido de los triángulos
            bool flip = m.determinant < 0;
            for (int s = 0; s < part.Sub.Count; s++)
            {
                var src = part.Sub[s];
                var dst = data.I;
                for (int k = 0; k < src.Count; k += 3)
                {
                    if (flip) { dst.Add(baseIndex + src[k]); dst.Add(baseIndex + src[k + 2]); dst.Add(baseIndex + src[k + 1]); }
                    else { dst.Add(baseIndex + src[k]); dst.Add(baseIndex + src[k + 1]); dst.Add(baseIndex + src[k + 2]); }
                }
            }
            return this;
        }

        public bool Empty { get { return data.Count == 0; } }
        public MeshData Data { get { return data; } }
        public Mesh ToMesh(string name = "merged") { return data.ToMesh(name); }
    }
}
