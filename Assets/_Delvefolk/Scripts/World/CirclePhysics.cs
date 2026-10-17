using System;
using System.Collections.Generic;

namespace Delvefolk
{
    public enum BodyKind { Misc, Player, Enemy, Prop, Static, Boss }

    // Cuerpo circular en el plano XZ. Velocidad total = control (mv) + empuje (k).
    public sealed class Body
    {
        public float X, Z, Radius = 0.4f, Mass = 1f, InvMass;
        public float Mvx, Mvz;       // velocidad de control: la reescribe su dueño cada frame
        public float Kx, Kz;         // velocidad de empuje: se frena con rozamiento y rebota en paredes
        public bool IsStatic, Ghost, Enabled = true, Airborne;
        public float KnockDrag = 4.2f, Restitution = 0.45f;
        public object Owner;
        public BodyKind Kind = BodyKind.Misc;
        public Body Ignore;          // p. ej. el compañero recién lanzado
        public float IgnoreT;
        // ¿Los fosos le bloquean? (jugador: siempre; enemigo: salvo empujado o muerto; barril: nunca)
        public Func<bool> PitSolid = AlwaysTrue;
        public Action<float, float, float> OnWallImpact;          // (velocidad, nx, nz)
        public Action<Body, float, float, float> OnBodyImpact;    // (otro, velocidad, nx hacia el otro, nz)

        static bool AlwaysTrue() { return true; }

        public float KnockSpeed { get { return (float)Math.Sqrt(Kx * Kx + Kz * Kz); } }
    }

    // Consulta de colisión con la rejilla (la implementa Dungeon)
    public interface ISolidGrid
    {
        bool IsSolidFor(int tx, int tz, Body b);
    }

    // Sistema de colisiones único para todo el juego (Physics.js, exacto).
    // Todo cuerpo es un círculo; los muros son casillas (AABB) de la rejilla.
    public sealed class CirclePhysics
    {
        public readonly List<Body> Bodies = new List<Body>();
        public ISolidGrid Grid;

        public CirclePhysics(ISolidGrid grid) { Grid = grid; }

        public Body Add(Body b)
        {
            b.InvMass = b.IsStatic ? 0f : 1f / b.Mass;
            Bodies.Add(b);
            return b;
        }

        public void Remove(Body b) { Bodies.Remove(b); }

        public void Step(float dt)
        {
            var bodies = Bodies;
            float maxDisp = 0f;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b.IsStatic || !b.Enabled) continue;
                float vx = b.Mvx + b.Kx, vz = b.Mvz + b.Kz;
                float d = (float)Math.Sqrt(vx * vx + vz * vz) * dt;
                if (d > maxDisp) maxDisp = d;
            }
            // Subpasos: nunca avanzar más de 0.2 unidades por subpaso → no se atraviesan muros
            int n = Math.Min(10, Math.Max(1, (int)Math.Ceiling(maxDisp / 0.2f)));
            float h = dt / n;
            for (int s = 0; s < n; s++)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    var b = bodies[i];
                    if (b.IsStatic || !b.Enabled) continue;
                    b.X += (b.Mvx + b.Kx) * h;
                    b.Z += (b.Mvz + b.Kz) * h;
                }
                SolveBodies();
                for (int i = 0; i < bodies.Count; i++)
                {
                    var b = bodies[i];
                    if (b.IsStatic || !b.Enabled) continue;
                    SolveWalls(b);
                }
            }
            // Rozamiento del empuje: a gran velocidad "vuela" (poco rozamiento), luego frena al arrastrarse
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b.IsStatic) continue;
                float sp = (float)Math.Sqrt(b.Kx * b.Kx + b.Kz * b.Kz);
                float f = (float)Math.Exp(-b.KnockDrag * (sp > 9f ? 0.55f : 1f) * dt);
                b.Kx *= f; b.Kz *= f;
                if (Math.Abs(b.Kx) < 0.01f) b.Kx = 0f;
                if (Math.Abs(b.Kz) < 0.01f) b.Kz = 0f;
            }
        }

        void SolveBodies()
        {
            var bodies = Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var a = bodies[i];
                if (!a.Enabled || a.Ghost) continue;
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    var b = bodies[j];
                    if (!b.Enabled || b.Ghost) continue;
                    if (a.IsStatic && b.IsStatic) continue;
                    if (a.Ignore == b || b.Ignore == a) continue;
                    if ((a.Airborne && !b.IsStatic) || (b.Airborne && !a.IsStatic)) continue;   // saltando por encima
                    float dx = b.X - a.X, dz = b.Z - a.Z;
                    float r = a.Radius + b.Radius;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= r * r) continue;
                    float d = (float)Math.Sqrt(d2);
                    float nx, nz;
                    if (d < 1e-5f) { nx = 1f; nz = 0f; d = 0f; } else { nx = dx / d; nz = dz / d; }
                    float overlap = r - d;
                    float wsum = a.InvMass + b.InvMass;
                    if (wsum == 0f) continue;
                    a.X -= nx * overlap * (a.InvMass / wsum);
                    a.Z -= nz * overlap * (a.InvMass / wsum);
                    b.X += nx * overlap * (b.InvMass / wsum);
                    b.Z += nz * overlap * (b.InvMass / wsum);
                    // Solo los impactos rápidos (empujones) intercambian impulso
                    float rv = (b.Kx + b.Mvx - a.Kx - a.Mvx) * nx + (b.Kz + b.Mvz - a.Kz - a.Mvz) * nz;
                    float ka = (float)Math.Sqrt(a.Kx * a.Kx + a.Kz * a.Kz), kb = (float)Math.Sqrt(b.Kx * b.Kx + b.Kz * b.Kz);
                    if (rv < -2.5f && (ka > 2f || kb > 2f))
                    {
                        const float e = 0.35f;
                        float jimp = -(1f + e) * rv / wsum;
                        a.Kx -= jimp * a.InvMass * nx; a.Kz -= jimp * a.InvMass * nz;
                        b.Kx += jimp * b.InvMass * nx; b.Kz += jimp * b.InvMass * nz;
                        float speed = -rv;
                        if (a.OnBodyImpact != null) a.OnBodyImpact(b, speed, -nx, -nz);
                        if (b.OnBodyImpact != null) b.OnBodyImpact(a, speed, nx, nz);
                    }
                }
            }
        }

        // Empuja el cuerpo fuera de las casillas sólidas (2 iteraciones). Coordenadas de Unity:
        // la casilla (tx, tz) ocupa x ∈ [2tx−1, 2tx+1] y z ∈ [−2tz−1, −2tz+1].
        public void SolveWalls(Body b)
        {
            const float T = Cfg.Tile, half = T / 2f;
            for (int iter = 0; iter < 2; iter++)
            {
                int tx0 = MathUtil.RoundJs((b.X - b.Radius) / T), tx1 = MathUtil.RoundJs((b.X + b.Radius) / T);
                int tz0 = MathUtil.RoundJs(-(b.Z + b.Radius) / T), tz1 = MathUtil.RoundJs(-(b.Z - b.Radius) / T);
                bool hit = false;
                for (int tz = tz0; tz <= tz1; tz++)
                {
                    for (int tx = tx0; tx <= tx1; tx++)
                    {
                        if (!Grid.IsSolidFor(tx, tz, b)) continue;
                        float minX = tx * T - half, maxX = tx * T + half;
                        float minZ = -tz * T - half, maxZ = -tz * T + half;
                        float cx = MathUtil.Clamp(b.X, minX, maxX);
                        float cz = MathUtil.Clamp(b.Z, minZ, maxZ);
                        float dx = b.X - cx, dz = b.Z - cz;
                        float d2 = dx * dx + dz * dz;
                        if (d2 >= b.Radius * b.Radius) continue;
                        float nx, nz, pen;
                        if (d2 > 1e-8f)
                        {
                            float d = (float)Math.Sqrt(d2);
                            nx = dx / d; nz = dz / d; pen = b.Radius - d;
                        }
                        else
                        {
                            // Centro dentro de la casilla: salir por el lado más cercano
                            float l = b.X - minX, r = maxX - b.X, u = b.Z - minZ, w = maxZ - b.Z;
                            float m = Math.Min(Math.Min(l, r), Math.Min(u, w));
                            if (m == l) { nx = -1f; nz = 0f; }
                            else if (m == r) { nx = 1f; nz = 0f; }
                            else if (m == u) { nx = 0f; nz = -1f; }
                            else { nx = 0f; nz = 1f; }
                            pen = m + b.Radius;
                        }
                        b.X += nx * pen; b.Z += nz * pen;
                        hit = true;
                        float vn = b.Kx * nx + b.Kz * nz;
                        if (vn < 0f)
                        {
                            b.Kx -= (1f + b.Restitution) * vn * nx;
                            b.Kz -= (1f + b.Restitution) * vn * nz;
                            if (b.OnWallImpact != null && -vn > 3f) b.OnWallImpact(-vn, nx, nz);
                        }
                    }
                }
                if (!hit) break;
            }
        }
    }
}
