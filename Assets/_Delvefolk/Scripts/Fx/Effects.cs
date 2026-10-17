using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Opciones de una ráfaga de partículas (los valores por defecto son los del original)
    public struct BurstOpts
    {
        public int Count;
        public int Color;
        public float Speed, Up, Size, Spread, Gravity, Life, ColorVar, Floor;
        public float? DirX, DirZ;
        public bool HasUp, HasGravity, HasFloor;

        public static BurstOpts Of(int count, int color, float speed = 5f, float up = 4f, float size = 0.16f)
        {
            return new BurstOpts { Count = count, Color = color, Speed = speed, Up = up, HasUp = true, Size = size };
        }

        public BurstOpts Dir(float x, float z, float spread = 0.8f) { DirX = x; DirZ = z; Spread = spread; return this; }
        public BurstOpts WithGravity(float g) { Gravity = g; HasGravity = true; return this; }
        public BurstOpts WithLife(float l) { Life = l; return this; }
        public BurstOpts WithColorVar(float v) { ColorVar = v; return this; }
        public BurstOpts WithFloor(float f) { Floor = f; HasFloor = true; return this; }
    }

    public struct RingOpts
    {
        public int Color;
        public float Radius, Life, Y, R0, Opacity;
        public static RingOpts Of(int color, float radius = 2f, float life = 0.35f, float y = 0.06f)
        {
            return new RingOpts { Color = color, Radius = radius, Life = life, Y = y, R0 = 0.3f, Opacity = 0.8f };
        }
    }

    // Efectos visuales (Effects.js): partículas en una única malla dinámica, anillos, sectores de ataque,
    // trazas y una luz de destello reutilizada. Todo con pools: nada se crea en pleno combate.
    public sealed class Effects
    {
        const int Max = 700;

        struct Part
        {
            public float X, Y, Z, Vx, Vy, Vz, Life, MaxLife, Size, G, Rot, Spin, FloorY;
            public Color32 Col;
        }

        sealed class Fading
        {
            public Transform Tr;
            public MeshRenderer R;
            public MeshFilter F;
            public float T, Life, R0, R1, BaseOpacity;
            public Color Col;
            public bool Ring, Active;
        }

        readonly Part[] parts = new Part[Max];
        int count;
        readonly Mesh mesh;
        readonly Vector3[] verts = new Vector3[Max * 8];
        readonly Color32[] cols = new Color32[Max * 8];
        int lastCount;
        readonly Transform root;
        readonly List<Fading> fadings = new List<Fading>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly Mesh ringMesh, tracerMesh;
        readonly Dictionary<int, Mesh> arcMeshes = new Dictionary<int, Mesh>();
        public readonly Light FlashLight;
        float flashT, flashMax, flashDur;
        readonly Rng rnd;

        static readonly Vector3[] Corners =
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
        };
        static readonly int[] CubeTris = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };

        public Effects(Transform parent, Rng random)
        {
            rnd = random;
            root = new GameObject("Efectos").transform;
            root.SetParent(parent, false);
            mesh = new Mesh { name = "particulas" };
            mesh.MarkDynamic();
            var tris = new int[Max * 36];
            for (int p = 0; p < Max; p++)
                for (int k = 0; k < 36; k++) tris[p * 36 + k] = p * 8 + CubeTris[k];
            for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(0, -1000, 0);
            mesh.vertices = verts;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            var go = new GameObject("Particulas", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Mats.Unlit;
            NoShadows(mr);

            ringMesh = ProcMesh.Ring(0.82f, 1f, 32).ToMesh("anillo");
            tracerMesh = ProcMesh.Box(0.05f, 0.05f, 1f).ToMesh("traza");

            var lg = new GameObject("Destello");
            lg.transform.SetParent(root, false);
            FlashLight = lg.AddComponent<Light>();
            FlashLight.type = LightType.Point;
            FlashLight.range = 12f;
            FlashLight.intensity = 0f;
            FlashLight.shadows = LightShadows.None;
        }

        public static void NoShadows(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        float R() { return rnd.Next(); }

        public void Burst(float x, float y, float z, BurstOpts o)
        {
            int n = o.Count > 0 ? o.Count : 10;
            Color baseCol = MathUtil.Hex(o.Color);
            float speed = o.Speed > 0 ? o.Speed : 5f;
            float up = o.HasUp ? o.Up : 4f;
            float size = o.Size > 0 ? o.Size : 0.16f;
            float spread = o.Spread > 0 ? o.Spread : 0.8f;
            float g = o.HasGravity ? o.Gravity : 16f;
            float life = o.Life > 0 ? o.Life : 0.6f;
            float floorY = o.HasFloor ? o.Floor : 0.05f;
            for (int i = 0; i < n; i++)
            {
                if (count >= Max) { parts[0] = parts[count - 1]; count--; }
                float vx = (R() - 0.5f) * 2f, vz = (R() - 0.5f) * 2f;
                if (o.DirX.HasValue)
                {
                    vx = o.DirX.Value + (R() - 0.5f) * spread;
                    vz = o.DirZ.Value + (R() - 0.5f) * spread;
                }
                float l = MathUtil.Hypot(vx, vz);
                if (l == 0f) l = 1f;
                float sp = speed * (0.4f + R() * 0.8f);
                Color c = baseCol;
                if (o.ColorVar > 0f)
                {
                    float h, s, v;
                    Color.RGBToHSV(c, out h, out s, out v);
                    h = Mathf.Repeat(h + (R() - 0.5f) * o.ColorVar, 1f);
                    v = Mathf.Clamp01(v + (R() - 0.5f) * o.ColorVar);
                    c = Color.HSVToRGB(h, s, v);
                }
                parts[count++] = new Part
                {
                    X = x, Y = y, Z = z,
                    Vx = vx / l * sp, Vz = vz / l * sp,
                    Vy = up * (0.5f + R()),
                    Life = 0f, MaxLife = life * (0.6f + R() * 0.6f),
                    Size = size * (0.6f + R() * 0.8f),
                    G = g, Rot = R() * 6f, Spin = (R() - 0.5f) * 14f,
                    Col = (Color32)c.linear, FloorY = floorY,
                };
            }
        }

        Fading Take(bool ring)
        {
            for (int i = 0; i < fadings.Count; i++)
                if (!fadings[i].Active && fadings[i].Ring == ring) return fadings[i];
            var go = new GameObject(ring ? "Anillo" : "Arco", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            var f = new Fading { Tr = go.transform, R = go.GetComponent<MeshRenderer>(), F = go.GetComponent<MeshFilter>(), Ring = ring };
            NoShadows(f.R);
            fadings.Add(f);
            return f;
        }

        void SetColor(Fading f, float opacity)
        {
            var lin = f.Col.linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(lin.r, lin.g, lin.b, opacity));
            f.R.SetPropertyBlock(mpb);
        }

        public void Ring(float x, float z, RingOpts o)
        {
            var f = Take(true);
            f.F.sharedMesh = ringMesh;
            f.R.sharedMaterial = Mats.Transparent;
            f.Tr.SetPositionAndRotation(new Vector3(x, o.Y != 0 ? o.Y : 0.06f, z), Quaternion.AngleAxis(-90f, Vector3.right));
            f.T = 0f; f.Life = o.Life > 0 ? o.Life : 0.35f;
            f.R0 = o.R0 > 0 ? o.R0 : 0.3f; f.R1 = o.Radius > 0 ? o.Radius : 2f;
            f.BaseOpacity = 0.8f;
            f.Col = MathUtil.Hex(o.Color);
            f.Tr.localScale = Vector3.one * f.R0;
            f.Active = true;
            f.Tr.gameObject.SetActive(true);
            SetColor(f, 0.8f);
        }

        // Sector que muestra el área real del ataque (aditivo)
        public void SwingArc(float x, float z, float facing, float arc, float range, int color, float life = 0.18f)
        {
            int key = Mathf.RoundToInt(arc * 100f) * 10000 + Mathf.RoundToInt(range * 100f);
            Mesh m;
            if (!arcMeshes.TryGetValue(key, out m))
            {
                m = ProcMesh.Ring(0.35f, range, 24, 1, -Mathf.PI / 2f - arc / 2f, arc).ToMesh("sector");
                arcMeshes[key] = m;
            }
            var f = Take(false);
            f.F.sharedMesh = m;
            f.R.sharedMaterial = Mats.Additive;
            f.Tr.SetPositionAndRotation(new Vector3(x, 0.08f, z), ProcMesh.EulerXYZ(-Mathf.PI / 2f, 0f, facing));
            f.Tr.localScale = Vector3.one;
            f.T = 0f; f.Life = life; f.BaseOpacity = 0.5f;
            f.Col = MathUtil.Hex(color);
            f.Active = true;
            f.Tr.gameObject.SetActive(true);
            SetColor(f, 0.45f);
        }

        // Traza recta (perdigones): una barra fina que se desvanece
        public void Tracer(float x1, float z1, float x2, float z2, float y = 1f, int color = 0xffe08a, float life = 0.14f)
        {
            float len = MathUtil.Hypot(x2 - x1, z2 - z1);
            var f = Take(false);
            f.F.sharedMesh = tracerMesh;
            f.R.sharedMaterial = Mats.Additive;
            f.Tr.SetPositionAndRotation(new Vector3((x1 + x2) / 2f, y, (z1 + z2) / 2f), Quaternion.Euler(0f, Mathf.Atan2(x2 - x1, z2 - z1) * Mathf.Rad2Deg, 0f));
            f.Tr.localScale = new Vector3(1f, 1f, len);
            f.T = 0f; f.Life = life; f.BaseOpacity = 0.9f;
            f.Col = MathUtil.Hex(color);
            f.Active = true;
            f.Tr.gameObject.SetActive(true);
            SetColor(f, 0.9f);
        }

        public void Flash(float x, float y, float z, int color = 0xffcc88, float intensity = 3f, float dur = 0.12f)
        {
            FlashLight.color = MathUtil.Hex(color);
            FlashLight.transform.position = new Vector3(x, y, z);
            flashMax = intensity * Lighting.PointScale; flashDur = dur; flashT = dur;
            FlashLight.intensity = flashMax;
        }

        public int ParticleCount { get { return count; } }

        public void Update(float dt)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                var a = parts[i];
                a.Life += dt;
                if (a.Life >= a.MaxLife) { parts[i] = parts[count - 1]; count--; continue; }
                a.Vy -= a.G * dt;
                a.X += a.Vx * dt; a.Y += a.Vy * dt; a.Z += a.Vz * dt;
                if (a.Y < a.FloorY) { a.Y = a.FloorY; a.Vy *= -0.35f; a.Vx *= 0.7f; a.Vz *= 0.7f; }
                a.Rot += a.Spin * dt;
                parts[i] = a;
            }
            for (int i = 0; i < count; i++)
            {
                var a = parts[i];
                float k = 1f - a.Life / a.MaxLife;
                float sz = a.Size * Mathf.Min(1f, k * 2.5f);
                var q = ProcMesh.EulerXYZ(a.Rot, a.Rot * 0.7f, 0f);
                var c = new Vector3(a.X, a.Y, a.Z);
                int b = i * 8;
                for (int v = 0; v < 8; v++) { verts[b + v] = c + q * (Corners[v] * sz); cols[b + v] = a.Col; }
            }
            for (int i = count; i < lastCount; i++)
                for (int v = 0; v < 8; v++) verts[i * 8 + v] = new Vector3(0, -1000, 0);
            if (count > 0 || lastCount > 0)
            {
                mesh.vertices = verts;
                mesh.colors32 = cols;
            }
            lastCount = count;

            for (int i = 0; i < fadings.Count; i++)
            {
                var f = fadings[i];
                if (!f.Active) continue;
                f.T += dt;
                float k = f.T / f.Life;
                if (k >= 1f) { f.Active = false; f.Tr.gameObject.SetActive(false); continue; }
                if (f.Ring)
                {
                    float rad = Mathf.Lerp(f.R0, f.R1, MathUtil.EaseOutCubic(k));
                    f.Tr.localScale = new Vector3(rad, rad, rad);
                    SetColor(f, 0.8f * (1f - k));
                }
                else SetColor(f, f.BaseOpacity * (1f - k));
            }
            if (flashT > 0f)
            {
                flashT -= dt;
                FlashLight.intensity = Mathf.Max(0f, flashMax * (flashT / flashDur));
            }
            else FlashLight.intensity = 0f;
        }

        public void Clear()
        {
            count = 0;
            for (int i = 0; i < fadings.Count; i++) { fadings[i].Active = false; fadings[i].Tr.gameObject.SetActive(false); }
            flashT = 0f;
            FlashLight.intensity = 0f;
        }
    }
}
