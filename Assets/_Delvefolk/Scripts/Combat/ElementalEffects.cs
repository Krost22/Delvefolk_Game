using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Efectos de las armas elementales sobre los enemigos (estados con duración y límite), zonas en el suelo
    // (suelo mojado, nube de esporas), arrastres del rastrillo y enemigos almacenados/lanzados por la
    // aspiradora. No cambia la IA de los enemigos: solo les aplica ralentización, daño en el tiempo o los
    // aparta un rato del juego. Todo se limpia al cambiar de piso (Clear).
    public sealed class ElementalSystem
    {
        public const float MaxDotTime = 5f;      // ninguna quemadura o veneno dura más que esto
        public const int MaxZones = 8;
        public const float BossMinSlow = 0.8f;   // los jefes casi no se ralentizan (y no se inmovilizan)

        sealed class Status
        {
            public float BurnT, BurnTick, BurnEvery, BurnDmg;
            public float PoisonT, PoisonTick, PoisonEvery, PoisonDmg;
            public float SlowT, SlowMult = 1f, RootT, FxT;
        }

        public enum ZoneKind { Wet, Spores }

        sealed class Zone
        {
            public ZoneKind Kind;
            public float X, Z, R, Life, T, Tick, FxT;
            public float Slow, PoisonTime, PoisonEvery, PoisonDmg;
            public Player By;
            public Transform Visual;
            public MeshRenderer Renderer;
            public int Color;
        }

        sealed class PullOp { public Enemy E; public Player P; public float Left, Speed, Root; }

        sealed class LaunchOp
        {
            public Enemy E; public Player P;
            public float Dx, Dz, Speed, Damage, Delay, Flight;
            public bool Released;
            public readonly HashSet<Enemy> HitSet = new HashSet<Enemy>();
        }

        readonly Game g;
        readonly Dictionary<Enemy, Status> statuses = new Dictionary<Enemy, Status>();
        readonly List<Enemy> keys = new List<Enemy>();
        readonly List<Zone> zones = new List<Zone>();
        readonly List<PullOp> pulls = new List<PullOp>();
        readonly List<LaunchOp> launches = new List<LaunchOp>();
        static Mesh discMesh;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public ElementalSystem(Game game) { g = game; }

        public int ZoneCount { get { return zones.Count; } }
        public int ZoneCountOf(ZoneKind k) { int n = 0; foreach (var z in zones) if (z.Kind == k) n++; return n; }
        public bool IsPulling(Enemy e) { foreach (var p in pulls) if (p.E == e) return true; return false; }

        // ---------- Estados ----------
        Status Get(Enemy e)
        {
            Status s;
            if (!statuses.TryGetValue(e, out s)) { s = new Status(); statuses[e] = s; }
            return s;
        }

        static bool Valid(Enemy e) { return e != null && !e.Dead && !e.Removed && !e.Captured && e.State != EnemyState.Falling; }

        // Quemadura: renueva la duración (no se acumula) y se queda con el daño mayor; nunca pasa de MaxDotTime
        public void Burn(Enemy e, float time, float every, float dmg, Player by)
        {
            if (!Valid(e)) return;
            var s = Get(e);
            if (ElementalRules.IsBoss(e)) dmg *= 0.5f;
            s.BurnT = Mathf.Min(MaxDotTime, Mathf.Max(s.BurnT, time));
            s.BurnEvery = every; s.BurnDmg = Mathf.Max(s.BurnDmg * (s.BurnT > 0f ? 1f : 0f), dmg);
            if (s.BurnTick <= 0f) s.BurnTick = every;
            g.Ui.FloatText(e.X, 2.2f, e.Z, "¡QUEMADO!", TextStyle.Small);
        }

        // Veneno: misma regla (renovar, sin acumular daño ilimitado)
        public void Poison(Enemy e, float time, float every, float dmg, Player by)
        {
            if (!Valid(e)) return;
            var s = Get(e);
            if (ElementalRules.IsBoss(e)) dmg *= 0.5f;
            bool fresh = s.PoisonT <= 0f;
            s.PoisonT = Mathf.Min(MaxDotTime, Mathf.Max(s.PoisonT, time));
            s.PoisonEvery = every; s.PoisonDmg = fresh ? dmg : Mathf.Max(s.PoisonDmg, dmg);
            if (s.PoisonTick <= 0f) s.PoisonTick = every;
            if (fresh) g.Ui.FloatText(e.X, 2.2f, e.Z, "¡ENVENENADO!", TextStyle.Small);
        }

        public void Slow(Enemy e, float mult, float time)
        {
            if (!Valid(e)) return;
            var s = Get(e);
            if (ElementalRules.IsBoss(e)) mult = Mathf.Max(mult, BossMinSlow);
            s.SlowMult = s.SlowT > 0f ? Mathf.Min(s.SlowMult, mult) : mult;
            s.SlowT = Mathf.Min(MaxDotTime, Mathf.Max(s.SlowT, time));
        }

        public void Root(Enemy e, float time)
        {
            if (!Valid(e) || ElementalRules.IsHeavy(e)) return;
            var s = Get(e);
            s.RootT = Mathf.Min(2.5f, Mathf.Max(s.RootT, time));
            g.Ui.FloatText(e.X, 2.2f, e.Z, "¡ENRAIZADO!", TextStyle.Small);
            g.Effects.Burst(e.X, 0.2f, e.Z, BurstOpts.Of(10, 0x5fae3a, 2f, 2f, 0.12f));
        }

        public bool HasBurn(Enemy e) { Status s; return statuses.TryGetValue(e, out s) && s.BurnT > 0f; }
        public bool HasPoison(Enemy e) { Status s; return statuses.TryGetValue(e, out s) && s.PoisonT > 0f; }
        public float BurnLeft(Enemy e) { Status s; return statuses.TryGetValue(e, out s) ? s.BurnT : 0f; }
        public float PoisonLeft(Enemy e) { Status s; return statuses.TryGetValue(e, out s) ? s.PoisonT : 0f; }
        public float SlowOf(Enemy e) { Status s; return statuses.TryGetValue(e, out s) && s.SlowT > 0f ? s.SlowMult : 1f; }
        public bool IsRooted(Enemy e) { Status s; return statuses.TryGetValue(e, out s) && s.RootT > 0f; }

        // ---------- Zonas en el suelo ----------
        public void AddWetFloor(float x, float z, float r, float life, float slow)
        {
            AddZone(new Zone { Kind = ZoneKind.Wet, X = x, Z = z, R = r, Life = life, Slow = slow, Color = 0x3aa8ff });
        }

        public void AddSporeCloud(float x, float z, float r, float life, float poisonTime, float every, float dmg, Player by)
        {
            AddZone(new Zone { Kind = ZoneKind.Spores, X = x, Z = z, R = r, Life = life, PoisonTime = poisonTime, PoisonEvery = every, PoisonDmg = dmg, By = by, Color = 0x9a4ad8 });
        }

        void AddZone(Zone z)
        {
            if (zones.Count >= MaxZones) { DestroyZone(zones[0]); zones.RemoveAt(0); }
            if (!discMesh) discMesh = new MeshBuilder().Add(ProcMesh.Circle(1f, 24).RotateX(-Mathf.PI / 2f), Vector3.zero, Color.white).ToMesh("zonaElemental");
            if (g.FloorRoot)
            {
                var go = CharacterModel.MeshObject(g.FloorRoot, z.Kind == ZoneKind.Wet ? "SueloMojado" : "NubeEsporas", discMesh, Mats.Transparent, new Vector3(z.X, 0.03f, z.Z));
                go.transform.localScale = new Vector3(z.R, 1f, z.R);
                z.Visual = go.transform;
                z.Renderer = go.GetComponent<MeshRenderer>();
                SetZoneAlpha(z, 0.18f);
            }
            zones.Add(z);
        }

        static void SetZoneAlpha(Zone z, float a)
        {
            if (!z.Renderer) return;
            var c = MathUtil.Hex(z.Color).linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(c.r, c.g, c.b, a));
            z.Renderer.SetPropertyBlock(mpb);
        }

        static void DestroyZone(Zone z) { if (z.Visual) Object.Destroy(z.Visual.gameObject); }

        // ---------- Arrastre (rastrillo) ----------
        public void Pull(Enemy e, Player p, float distance, float speed, float rootAfter)
        {
            if (!Valid(e)) return;
            pulls.RemoveAll(o => o.E == e);
            pulls.Add(new PullOp { E = e, P = p, Left = distance, Speed = speed, Root = rootAfter });
        }

        // Mueve un enemigo sin meterlo en un muro, un foso ni atravesar paredes. false si no pudo
        public bool MoveSafely(Enemy e, float dx, float dz)
        {
            float nx = e.X + dx, nz = e.Z + dz;
            var d = g.Dungeon;
            if (d.IsWallLike(Grid.TileX(nx), Grid.TileZ(nz)) || d.IsPitAt(nx, nz) || !d.LineClear(e.X, e.Z, nx, nz)) return false;
            e.Body.X = nx; e.Body.Z = nz;
            return true;
        }

        // ---------- Aspiradora: guardar y lanzar ----------
        // El enemigo sale del juego (sin IA, sin cuerpo, invisible) hasta que lo lancen
        public void Store(Enemy e)
        {
            g.Enemies.Remove(e);
            statuses.Remove(e);
            pulls.RemoveAll(o => o.E == e);
            e.Captured = true;
            e.StatusMoveMult = 1f;
            e.Body.Enabled = false;
            e.Body.Kx = e.Body.Kz = 0f;
            if (e.Root) e.Root.gameObject.SetActive(false);
        }

        public void Launch(Enemy e, Player p, float dx, float dz, float speed, float damage, float delay)
        {
            if (e == null || e.Removed) return;
            launches.Add(new LaunchOp { E = e, P = p, Dx = dx, Dz = dz, Speed = speed, Damage = damage, Delay = delay });
        }

        void Release(LaunchOp op)
        {
            var e = op.E;
            op.Released = true;
            if (e.Removed || !e.Root) return;
            var d = g.Dungeon;
            float x = op.P.X + op.Dx * (op.P.Radius + e.Radius + 0.15f), z = op.P.Z + op.Dz * (op.P.Radius + e.Radius + 0.15f);
            if (d.IsWallLike(Grid.TileX(x), Grid.TileZ(z)) || d.IsPitAt(x, z) || !d.LineClear(op.P.X, op.P.Z, x, z)) { x = op.P.X; z = op.P.Z; }
            e.Captured = false;
            e.Body.Enabled = true;
            e.Body.X = x; e.Body.Z = z;
            e.Body.Kx = op.Dx * op.Speed; e.Body.Kz = op.Dz * op.Speed;
            e.Root.gameObject.SetActive(true);
            e.SyncRoot();
            // Recupera su estado de combate: despierto, persiguiendo, un instante aturdido
            e.Wake(false);
            if (e.State != EnemyState.Dead) e.SetState(EnemyState.Chase);
            e.Stun = Mathf.Max(e.Stun, op.Speed > 8f ? 0.8f : 0.4f);
            if (!g.Enemies.Contains(e)) g.Enemies.Add(e);
            op.Flight = op.Speed > 8f ? 0.6f : 0f;
            if (op.Damage > 0f) e.EnvDamage(op.Damage, "¡PROYECTIL!");
        }

        // Mientras vuela, golpea una vez a cada enemigo que encuentre
        void UpdateFlight(LaunchOp op, float dt)
        {
            op.Flight -= dt;
            var e = op.E;
            if (e.Removed || e.Dead || e.Captured) { op.Flight = 0f; return; }
            g.Effects.Burst(e.X, 0.9f, e.Z, BurstOpts.Of(1, 0x8ff8ff, 0.5f, 0.5f, 0.1f).WithLife(0.3f));
            foreach (var o in g.Enemies.ToArray())
            {
                if (o == e || o.Dead || o.Removed || !o.Body.Enabled || op.HitSet.Contains(o)) continue;
                if (MathUtil.Hypot(o.X - e.X, o.Z - e.Z) > o.Radius + e.Radius + 0.15f) continue;
                op.HitSet.Add(o);
                float k = 14f * (ElementalRules.IsHeavy(o) ? ElementalRules.HeavyKnockMult : 1f);
                o.TakeHit(new Hit { Damage = 14f, Knock = k, Stun = 0.5f, DirX = op.Dx, DirZ = op.Dz, By = op.P });
                g.Ui.FloatText(o.X, 2.4f, o.Z, "¡STRIKE!", TextStyle.Label);
                g.Audio.Play("hit");
                g.CameraCtl.AddShake(0.15f);
            }
        }

        // ---------- Frame ----------
        public void Update(float dt)
        {
            UpdateStatuses(dt);
            UpdateZones(dt);
            UpdatePulls(dt);
            for (int i = launches.Count - 1; i >= 0; i--)
            {
                var op = launches[i];
                if (!op.Released)
                {
                    op.Delay -= dt;
                    if (op.Delay <= 0f) Release(op);
                    continue;
                }
                if (op.Flight > 0f) UpdateFlight(op, dt);
                else launches.RemoveAt(i);
            }
        }

        void UpdateStatuses(float dt)
        {
            keys.Clear();
            keys.AddRange(statuses.Keys);
            foreach (var e in keys)
            {
                var s = statuses[e];
                if (!Valid(e)) { if (!e.Removed) e.StatusMoveMult = 1f; statuses.Remove(e); continue; }
                if (s.BurnT > 0f)
                {
                    s.BurnT -= dt; s.BurnTick -= dt;
                    if (s.BurnTick <= 0f)
                    {
                        s.BurnTick += s.BurnEvery;
                        e.Flash(0.1f, 0xff6a20);
                        g.Effects.Burst(e.X, 1.0f, e.Z, BurstOpts.Of(4, 0xff8a20, 1.5f, 2.5f, 0.09f).WithLife(0.5f));
                        e.EnvDamage(s.BurnDmg, null);
                        if (!Valid(e)) { statuses.Remove(e); continue; }
                    }
                }
                if (s.PoisonT > 0f)
                {
                    s.PoisonT -= dt; s.PoisonTick -= dt;
                    if (s.PoisonTick <= 0f)
                    {
                        s.PoisonTick += s.PoisonEvery;
                        e.Flash(0.1f, 0x9be04a);
                        g.Effects.Burst(e.X, 1.1f, e.Z, BurstOpts.Of(3, 0xa860d8, 1f, 1.5f, 0.1f).WithLife(0.6f));
                        e.EnvDamage(s.PoisonDmg, null);
                        if (!Valid(e)) { statuses.Remove(e); continue; }
                    }
                }
                s.SlowT = Mathf.Max(0f, s.SlowT - dt);
                s.RootT = Mathf.Max(0f, s.RootT - dt);
                if (s.SlowT <= 0f) s.SlowMult = 1f;
                e.StatusMoveMult = s.RootT > 0f ? 0f : s.SlowT > 0f ? s.SlowMult : 1f;
                if (s.SlowT > 0f || s.RootT > 0f)
                {
                    s.FxT -= dt;
                    if (s.FxT <= 0f) { s.FxT = 0.3f; g.Effects.Burst(e.X, 0.15f, e.Z, BurstOpts.Of(2, s.RootT > 0f ? 0x5fae3a : 0x6fd0ff, 1f, 1f, 0.08f).WithLife(0.4f)); }
                }
                if (s.BurnT <= 0f && s.PoisonT <= 0f && s.SlowT <= 0f && s.RootT <= 0f) { e.StatusMoveMult = 1f; statuses.Remove(e); }
            }
        }

        void UpdateZones(float dt)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                var z = zones[i];
                z.T += dt;
                if (z.T >= z.Life) { DestroyZone(z); zones.RemoveAt(i); continue; }
                float k = 1f - Mathf.Max(0f, (z.T - z.Life + 0.6f) / 0.6f);
                SetZoneAlpha(z, (z.Kind == ZoneKind.Wet ? 0.18f : 0.16f) * k);
                z.FxT -= dt;
                if (z.FxT <= 0f)
                {
                    z.FxT = z.Kind == ZoneKind.Wet ? 0.25f : 0.12f;
                    float a = g.FxRand.Next() * Mathf.PI * 2f, r = Mathf.Sqrt(g.FxRand.Next()) * z.R;
                    if (z.Kind == ZoneKind.Wet) g.Effects.Burst(z.X + Mathf.Cos(a) * r, 0.1f, z.Z + Mathf.Sin(a) * r, BurstOpts.Of(2, 0x8fd8ff, 1f, 1.5f, 0.06f).WithLife(0.4f));
                    else g.Effects.Burst(z.X + Mathf.Cos(a) * r, 0.4f, z.Z + Mathf.Sin(a) * r, BurstOpts.Of(2, g.FxRand.Next() < 0.5f ? 0x9be04a : 0xa860d8, 0.4f, 1.2f, 0.2f).WithLife(1.2f).WithGravity(-0.5f));
                }
                bool tick = false;
                if (z.Kind == ZoneKind.Spores) { z.Tick -= dt; if (z.Tick <= 0f) { z.Tick += z.PoisonEvery; tick = true; } }
                foreach (var e in g.Enemies.ToArray())
                {
                    if (!Valid(e) || !e.Body.Enabled || MathUtil.Hypot(e.X - z.X, e.Z - z.Z) > z.R + e.Radius * 0.5f) continue;
                    if (z.Kind == ZoneKind.Wet) Slow(e, z.Slow, 0.3f);
                    else if (tick) Poison(e, z.PoisonTime, z.PoisonEvery, z.PoisonDmg, z.By);
                }
            }
        }

        void UpdatePulls(float dt)
        {
            for (int i = pulls.Count - 1; i >= 0; i--)
            {
                var op = pulls[i];
                var e = op.E;
                // El objetivo murió o desapareció a mitad: se acaba sin más
                if (!Valid(e) || op.P.Removed) { pulls.RemoveAt(i); continue; }
                float dx = op.P.X - e.X, dz = op.P.Z - e.Z, d = MathUtil.Hypot(dx, dz);
                float stop = op.P.Radius + e.Radius + 0.4f;
                float step = Mathf.Min(op.Speed * dt, op.Left, Mathf.Max(0f, d - stop));
                e.Stun = Mathf.Max(e.Stun, 0.25f);
                bool moved = step > 0f && MoveSafely(e, dx / d * step, dz / d * step);
                op.Left -= step;
                if (moved && FxTiming.Every(g, dt, 0.05f)) g.Effects.Burst(e.X, 0.4f, e.Z, BurstOpts.Of(1, 0x7dd65b, 0.5f, 0.5f, 0.08f).WithLife(0.3f));
                if (!moved || op.Left <= 0f || d - step <= stop)
                {
                    if (op.Root > 0f) Root(e, op.Root);
                    pulls.RemoveAt(i);
                }
            }
        }

        // Limpieza total: al cambiar de piso, reiniciar o volver al título
        public void Clear()
        {
            foreach (var kv in statuses) if (kv.Key != null && !kv.Key.Removed) kv.Key.StatusMoveMult = 1f;
            statuses.Clear();
            foreach (var z in zones) DestroyZone(z);
            zones.Clear();
            pulls.Clear();
            launches.Clear();
        }
    }

    static class FxTiming
    {
        // Para efectos esporádicos dentro de un bucle por frame (probabilidad ≈ dt / periodo)
        public static bool Every(Game g, float dt, float period) { return g.FxRand.Next() < Mathf.Clamp01(dt / period); }
    }
}
