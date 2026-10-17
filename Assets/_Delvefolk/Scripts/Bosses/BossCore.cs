using System;
using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // SISTEMA DE JEFES (BossCore.js) — piezas comunes que usan todos los jefes:
    //  - Telegraphs  (G.Telegraphs): avisos en el suelo que se rellenan y luego golpean (círculo, aro, línea, cono)
    //                y ondas expansivas que avanzan por el suelo (se esquivan SALTANDO).
    //  - Projectiles (G.Projectiles): proyectiles cinemáticos (sin cuerpo físico). Si son "reflectables",
    //                un golpe de jugador los devuelve y entonces dañan a enemigos y jefes.
    //  - Boss: clase base de los jefes (extiende Enemy): vida escalada por jugadores y encuentros, fases,
    //          patrones de ataque con pesos y enfriamientos, armadura/invulnerabilidad.
    //  - Bosses: registro de jefes.
    // Todo usa tiempo de juego (dt). Coordenadas y ángulos ya en Unity (z invertida respecto al web).
    public static class BossConfig
    {
        public const int EveryFloors = 2;                                           // jefe al terminar los pisos 2, 4, 6...
        public static readonly string[] Order = { "golem", "twins", "chef", "bell" };   // orden en que aparecen
        public const float HpPerExtraPlayer = 0.6f;                                 // +60 % de vida por cada jugador extra
        public const float HpPerEncounter = 0.25f;                                  // +25 % por cada jefe ya visto
        public const float IntroTime = 2.5f;                                        // presentación antes de despertar
        public const int RewardRolls = 1;                                           // botines raros por jugador al vencer
    }

    // ---------------------------------------------------------------- Formas (para avisos y golpes)
    public enum ShapeType { Circle, Ring, Line, Cone }

    public sealed class Shape
    {
        public ShapeType Type;
        public float X, Z, R, R0, R1, X1, Z1, X2, Z2, W, Angle, Arc, Range;

        public static Shape Circle(float x, float z, float r) { return new Shape { Type = ShapeType.Circle, X = x, Z = z, R = r }; }
        public static Shape Ring(float x, float z, float r0, float r1) { return new Shape { Type = ShapeType.Ring, X = x, Z = z, R0 = r0, R1 = r1 }; }
        public static Shape Line(float x1, float z1, float x2, float z2, float w) { return new Shape { Type = ShapeType.Line, X1 = x1, Z1 = z1, X2 = x2, Z2 = z2, W = w }; }
        public static Shape Cone(float x, float z, float angle, float arc, float range) { return new Shape { Type = ShapeType.Cone, X = x, Z = z, Angle = angle, Arc = arc, Range = range }; }

        public bool Contains(float x, float z, float pad = 0f)
        {
            switch (Type)
            {
                case ShapeType.Circle: return MathUtil.Hypot(x - X, z - Z) <= R + pad;
                case ShapeType.Ring: { float d = MathUtil.Hypot(x - X, z - Z); return d >= R0 - pad && d <= R1 + pad; }
                case ShapeType.Line: return SegDist(x, z, X1, Z1, X2, Z2) <= W / 2f + pad;
                default:
                {
                    float d = MathUtil.Hypot(x - X, z - Z);
                    if (d > Range + pad) return false;
                    return Mathf.Abs(MathUtil.AngleDiff(Angle, Mathf.Atan2(x - X, z - Z))) <= Arc / 2f + Mathf.Atan2(pad, Mathf.Max(d, 0.3f));
                }
            }
        }

        public static float SegDist(float px, float pz, float x1, float z1, float x2, float z2)
        {
            float dx = x2 - x1, dz = z2 - z1, l2 = dx * dx + dz * dz;
            if (l2 == 0f) l2 = 1f;
            float t = Mathf.Clamp01(((px - x1) * dx + (pz - z1) * dz) / l2);
            return MathUtil.Hypot(px - (x1 + dx * t), pz - (z1 + dz * t));
        }

        // Malla plana en el suelo; el objeto queda en el ancla (centro, vértice del cono o mitad de la línea)
        public Transform BuildMesh(Transform parent, int color, float opacity, float y = 0.05f)
        {
            MeshData md;
            Vector3 pos; float yaw = 0f;
            if (Type == ShapeType.Circle) { md = ProcMesh.Circle(R, 32).RotateX(-Mathf.PI / 2f); pos = new Vector3(X, y, Z); }
            else if (Type == ShapeType.Ring) { md = ProcMesh.Ring(R0, R1, 40).RotateX(-Mathf.PI / 2f); pos = new Vector3(X, y, Z); }
            else if (Type == ShapeType.Cone)
            {
                md = new MeshData();
                const int seg = 24;
                md.Add(Vector3.zero, Vector3.up, Vector2.zero);
                for (int i = 0; i <= seg; i++)
                {
                    float a = -Arc / 2f + Arc * i / seg;
                    md.Add(new Vector3(Mathf.Sin(a) * Range, 0, Mathf.Cos(a) * Range), Vector3.up, Vector2.zero);
                    if (i > 0) { md.I.Add(0); md.I.Add(i); md.I.Add(i + 1); }
                }
                pos = new Vector3(X, y, Z); yaw = Angle;
            }
            else
            {
                float len = MathUtil.Hypot(X2 - X1, Z2 - Z1);
                md = ProcMesh.Plane(W, len).RotateX(-Mathf.PI / 2f);
                pos = new Vector3((X1 + X2) / 2f, y, (Z1 + Z2) / 2f);
                yaw = Mathf.Atan2(X2 - X1, Z2 - Z1);
            }
            var mesh = new MeshBuilder().Add(md, Vector3.zero, MathUtil.Hex(color)).ToMesh("aviso");
            var go = CharacterModel.MeshObject(parent, "Aviso", mesh, Mats.Transparent, Vector3.zero);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            SetOpacity(go.GetComponent<Renderer>(), opacity);
            return go.transform;
        }

        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        public static void SetOpacity(Renderer r, float a)
        {
            if (!r) return;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Mats.BaseColorId, new Color(1f, 1f, 1f, a));
            r.SetPropertyBlock(mpb);
        }

        public static void DestroyMeshObject(Transform t)
        {
            if (!t) return;
            var mf = t.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh) UnityEngine.Object.Destroy(mf.sharedMesh);
            UnityEngine.Object.Destroy(t.gameObject);
        }
    }

    public static class BossHits
    {
        // Daña a los jugadores dentro de una forma. jumpable: saltando por encima no le da
        public static List<Player> HitPlayersIn(Game g, Shape shape, float damage, float knock, float srcX, float srcZ,
            bool jumpable = false, HashSet<Player> hitSet = null, string kind = null)
        {
            var hit = new List<Player>();
            foreach (var p in g.Players.ToArray())
            {
                if (!p.Targetable || p.IsThrown || (hitSet != null && hitSet.Contains(p))) continue;
                if (jumpable && p.JumpY > JumpConfig.DodgeHeight * 0.6f) continue;
                if (!shape.Contains(p.X, p.Z, p.Radius)) continue;
                if (hitSet != null) hitSet.Add(p);
                if (p.Hurt(damage, srcX, srcZ, knock, new HurtOpts { Kind = kind })) hit.Add(p);
            }
            return hit;
        }
    }

    // ---------------------------------------------------------------- Avisos y ondas
    public sealed class Telegraph
    {
        public Shape Shape;
        public float Time, T;
        public Action<Telegraph> OnFire;
        public Transform Base, Fill;
        public bool Done, Removed;
        public Telegraphs Owner;
        public void Cancel() { Owner.Remove(this); }
    }

    public sealed class Wave
    {
        public float X, Z, R = 0.5f, Speed = 7f, MaxR = 14f, Width = 0.9f, Damage = 12f, Knock = 10f;
        public int Color = 0xffb040;
        public readonly HashSet<Player> HitSet = new HashSet<Player>();
        public Transform Mesh;
    }

    public sealed class Telegraphs
    {
        readonly Game G;
        public readonly List<Telegraph> List = new List<Telegraph>();
        public readonly List<Wave> Waves = new List<Wave>();

        public Telegraphs(Game g) { G = g; }

        // Aviso que se rellena durante "time" segundos y al terminar llama a onFire(handle)
        public Telegraph Add(Shape shape, float time, Action<Telegraph> onFire, int color = 0xff3020)
        {
            var h = new Telegraph { Shape = shape, Time = time, OnFire = onFire, Owner = this };
            h.Base = shape.BuildMesh(G.FloorRoot, color, 0.18f);
            h.Fill = shape.BuildMesh(G.FloorRoot, color, 0.4f, 0.06f);
            h.Fill.localScale = shape.Type == ShapeType.Line ? new Vector3(1, 1, 0.001f) : new Vector3(0.001f, 1, 0.001f);
            List.Add(h);
            return h;
        }

        public void Remove(Telegraph h)
        {
            if (h.Removed) return;
            h.Removed = true; h.Done = true;
            Shape.DestroyMeshObject(h.Base);
            Shape.DestroyMeshObject(h.Fill);
        }

        // Onda que se expande desde (x,z): daña a quien toque el frente de la onda y NO esté saltando
        public Wave AddWave(float x, float z, Wave w = null)
        {
            w = w ?? new Wave();
            w.X = x; w.Z = z;
            var mesh = new MeshBuilder().Add(ProcMesh.Ring(1f - w.Width / 4f, 1f, 48).RotateX(-Mathf.PI / 2f), Vector3.zero, MathUtil.Hex(w.Color)).ToMesh("onda");
            var go = CharacterModel.MeshObject(G.FloorRoot, "Onda", mesh, Mats.Transparent, Vector3.zero);
            go.transform.position = new Vector3(x, 0.12f, z);
            go.transform.localScale = Vector3.one * w.R;
            Shape.SetOpacity(go.GetComponent<Renderer>(), 0.75f);
            w.Mesh = go.transform;
            Waves.Add(w);
            return w;
        }

        public void Update(float dt)
        {
            for (int i = List.Count - 1; i >= 0; i--)
            {
                if (i >= List.Count) continue;
                var h = List[i];
                if (h.Removed) { List.RemoveAt(i); continue; }
                h.T += dt;
                float k = Mathf.Min(1f, h.T / h.Time);
                // El relleno crece desde el centro y parpadea al final
                float kk = Mathf.Max(0.001f, k);
                h.Fill.localScale = h.Shape.Type == ShapeType.Line ? new Vector3(1, 1, kk) : new Vector3(kk, 1, kk);
                Shape.SetOpacity(h.Fill.GetComponent<Renderer>(), k > 0.8f ? 0.35f + Mathf.Sin(h.T * 40f) * 0.2f : 0.4f);
                if (h.T >= h.Time)
                {
                    if (h.OnFire != null) h.OnFire(h);
                    Remove(h);
                    List.Remove(h);
                }
            }
            for (int i = Waves.Count - 1; i >= 0; i--)
            {
                var w = Waves[i];
                w.R += w.Speed * dt;
                w.Mesh.localScale = Vector3.one * w.R;
                Shape.SetOpacity(w.Mesh.GetComponent<Renderer>(), 0.75f * (1f - w.R / w.MaxR * 0.6f));
                BossHits.HitPlayersIn(G, Shape.Ring(w.X, w.Z, w.R - w.Width / 2f, w.R + w.Width / 2f), w.Damage, w.Knock, w.X, w.Z, true, w.HitSet);
                if (w.R >= w.MaxR)
                {
                    Shape.DestroyMeshObject(w.Mesh);
                    Waves.RemoveAt(i);
                }
            }
        }

        public void Clear()
        {
            foreach (var h in List) Remove(h);
            foreach (var w in Waves) Shape.DestroyMeshObject(w.Mesh);
            List.Clear(); Waves.Clear();
        }
    }

    // ---------------------------------------------------------------- Proyectiles
    public sealed class Projectile : IHittable
    {
        public float PX, PZ, Y = 1f, Vx, Vz, ProjRadius = 0.35f, Damage = 10f, Knock = 8f, Life = 4f, T;
        public int Color = 0xff8040;
        public bool Hostile = true, Reflectable, Low, Pierce, Dead;
        public float ReflectDamage;
        public bool HasArc; public float ArcVy, ArcG;
        public Transform Model;
        public Player Reflector;   // quien lo devolvió
        public object Tag;         // datos propios del jefe (tipo de plato, etc.)
        public Action<Projectile> OnLand, OnWall;
        public Action<Projectile, Player> OnHitPlayer;
        public Action<Projectile, Enemy> OnHitEnemy;
        public readonly HashSet<object> HitSet = new HashSet<object>();
        internal Projectiles Owner;
        float rx, ry;

        public float X { get { return PX; } }
        public float Z { get { return PZ; } }
        public float Radius { get { return ProjRadius; } }
        public void TakeHit(Hit hit) { Reflector = hit.By; Owner.Reflect(this, hit); }

        internal void Spin(float dt)
        {
            rx += dt * 6f; ry += dt * 4f;
            Model.position = new Vector3(PX, Y, PZ);
            Model.rotation = ProcMesh.EulerXYZ(rx, ry, 0f);
        }
    }

    public sealed class Projectiles
    {
        readonly Game G;
        public readonly List<Projectile> List = new List<Projectile>();

        public Projectiles(Game g) { G = g; }

        // model: si es null, una roca (icosaedro) del color indicado
        public Projectile Spawn(Projectile p, Transform model = null)
        {
            p.Owner = this;
            if (!model)
            {
                var mesh = new MeshBuilder().Add(ProcMesh.Icosahedron(p.ProjRadius, 0), Vector3.zero, MathUtil.Hex(p.Color)).ToMesh("proyectil");
                model = CharacterModel.MeshObject(G.FloorRoot, "Proyectil", mesh, Mats.Lit, Vector3.zero).transform;
            }
            else model.SetParent(G.FloorRoot, true);
            p.Model = model;
            model.position = new Vector3(p.PX, p.Y, p.PZ);
            List.Add(p);
            return p;
        }

        public void Reflect(Projectile p, Hit hit)
        {
            if (!p.Reflectable || !p.Hostile || p.Dead) return;
            float sp = MathUtil.Hypot(p.Vx, p.Vz) * 1.3f + 2f;
            p.Vx = hit.DirX * sp; p.Vz = hit.DirZ * sp;
            p.Hostile = false;
            p.T = 0f; p.Life = Mathf.Max(p.Life, 3f);
            if (p.HasArc) { p.HasArc = false; p.Y = 1f; }
            p.HitSet.Clear();
            G.Audio.Play("clang");
            G.Ui.FloatText(p.PX, 2.2f, p.PZ, "¡DEVUELTO!", TextStyle.Label);
            G.Effects.Burst(p.PX, p.Y, p.PZ, BurstOpts.Of(10, 0xfff4c0, 5f, 2f, 0.1f));
        }

        public void Remove(Projectile p, bool burst)
        {
            if (p.Dead) return;
            p.Dead = true;
            if (burst) G.Effects.Burst(p.PX, Mathf.Max(0.3f, p.Y), p.PZ, BurstOpts.Of(8, p.Color, 3f, 3f, 0.12f));
            if (p.Model) UnityEngine.Object.Destroy(p.Model.gameObject);
        }

        public void Update(float dt)
        {
            var d = G.Dungeon;
            for (int i = 0; i < List.Count; i++)
            {
                var p = List[i];
                if (p.Dead) continue;
                p.T += dt;
                float nx = p.PX + p.Vx * dt, nz = p.PZ + p.Vz * dt;
                if (d.IsWallLike(Grid.TileX(nx), Grid.TileZ(nz))) { if (p.OnWall != null) p.OnWall(p); Remove(p, true); continue; }
                p.PX = nx; p.PZ = nz;
                if (p.HasArc)
                {
                    p.ArcVy -= p.ArcG * dt;
                    p.Y += p.ArcVy * dt;
                    if (p.Y <= 0.15f) { p.Y = 0.15f; if (p.OnLand != null) p.OnLand(p); Remove(p, true); continue; }
                }
                p.Spin(dt);
                if (p.T >= p.Life) { Remove(p, true); continue; }
                bool flying = p.HasArc && p.Y > 1.4f;   // en lo alto de la parábola no golpea a nadie
                if (p.Hostile && !flying)
                {
                    foreach (var pl in G.Players.ToArray())
                    {
                        if (!pl.Targetable || pl.IsThrown || p.HitSet.Contains(pl)) continue;
                        if (p.Low && pl.JumpY > JumpConfig.DodgeHeight * 0.6f) continue;
                        if (MathUtil.Hypot(pl.X - p.PX, pl.Z - p.PZ) > p.ProjRadius + pl.Radius) continue;
                        p.HitSet.Add(pl);
                        pl.Hurt(p.Damage, p.PX - p.Vx * 0.05f, p.PZ - p.Vz * 0.05f, p.Knock);
                        if (p.OnHitPlayer != null) p.OnHitPlayer(p, pl);
                        if (!p.Pierce) { Remove(p, true); break; }
                    }
                }
                else if (!p.Hostile)
                {
                    // Devuelto: ahora daña a enemigos y jefes
                    foreach (var e in G.Enemies.ToArray())
                    {
                        if (e.Dead || e.Removed || !e.Body.Enabled || p.HitSet.Contains(e)) continue;
                        if (MathUtil.Hypot(e.X - p.PX, e.Z - p.PZ) > p.ProjRadius + e.Radius) continue;
                        p.HitSet.Add(e);
                        float l = MathUtil.Hypot(p.Vx, p.Vz); if (l == 0f) l = 1f;
                        var h = Hit.Of(p.ReflectDamage > 0f ? p.ReflectDamage : MathUtil.RoundJs(p.Damage * 1.8f), 12f, 0.8f, p.Vx / l, p.Vz / l);
                        h.Reflected = true;
                        e.TakeHit(h);
                        if (p.OnHitEnemy != null) p.OnHitEnemy(p, e);
                        Remove(p, true);
                        break;
                    }
                }
            }
            List.RemoveAll(p => p.Dead);
        }

        public void Clear()
        {
            foreach (var p in List) Remove(p, false);
            List.Clear();
        }
    }

    // ---------------------------------------------------------------- Clase base de jefe
    // Patrón: { Id, Weight, Cooldown, Phases, Can(boss, player, dist), Start(boss), Update(boss, dt, player, dist) → true al acabar, End(boss) }
    // Dentro de Update, la velocidad deseada se escribe en boss.Mv (por defecto quieto).
    public sealed class BossPattern
    {
        public string Id;
        public float Weight = 1f, Cooldown, InitialCd, CdLeft;
        public Func<float> WeightFn;      // peso que depende del estado (si existe, manda sobre Weight)
        public int[] Phases;
        public Func<Boss, Player, float, bool> Can;
        public Action<Boss> Start;
        public Func<Boss, float, Player, float, bool> Update;
        public Action<Boss> End;
    }

    public abstract class Boss : Enemy
    {
        public bool IsAwake;              // dormido durante la presentación (no se mueve ni recibe daño)
        public int Phase = 1;
        public readonly List<BossPattern> Patterns = new List<BossPattern>();
        public BossPattern Current;
        public float PatternT, PatternCd = 1.2f;
        public Vector2 Mv;
        public Player LastHitter;         // último jugador que le golpeó
        public Player ForcedTarget;       // si se fija, persigue a este jugador mientras pueda
        public bool Invulnerable;
        public bool HoldVictory;          // impide la victoria aunque esté muerto (p. ej. va a revivir)
        public float[] PhaseThresholds = { 0.5f };
        public float PatternGap = 0.9f;
        public bool HoldStill;            // sin patrón en curso no se mueve (pruebas)
        float clangT = -10f;

        // Valores por defecto de un jefe; cada uno ajusta lo suyo
        protected static EnemyCfg BossCfg(Action<EnemyCfg> set)
        {
            var c = new EnemyCfg
            {
                Name = "Jefe", Hp = 300, Speed = 2.5f, Radius = 0.9f, Mass = 6, KnockDrag = 8, StunMult = 0.3f,
                Damage = 0, AttackRange = 1.2f, Windup = 0.5f, AttackTime = 0.3f, Recover = 0.5f, AttackCooldown = 1,
                BarY = 2.6f, HatY = 2.2f, HatScale = 1.6f, Coins = new[] { 30, 40 }, HurtSound = "ogreHurt", DieSound = "ogreDie",
            };
            set(c);
            return c;
        }

        protected Boss(Game g, float x, float z, EnemyCfg cfg, bool pitImmune = true) : base(g, x, z, cfg)
        {
            var bf = g.BossFloor;
            int np = Mathf.Max(1, g.Players.Count);
            MaxHp = Hp = MathUtil.RoundJs(cfg.Hp * (1f + BossConfig.HpPerExtraPlayer * (np - 1)) * (1f + BossConfig.HpPerEncounter * (bf != null ? bf.Encounter : 0)));
            NoSwap = true;
            SetState(EnemyState.Custom);
            if (pitImmune) Body.PitSolid = () => true;
        }

        // ---------- API para las subclases ----------
        public BossPattern AddPattern(BossPattern def) { def.CdLeft = def.InitialCd; Patterns.Add(def); return def; }
        public virtual void OnAwake() { }
        protected virtual void OnPhase(int n) { }
        // Daño entrante: devolver el golpe modificado. Blocked = true lo anula (armadura)
        protected virtual Hit ModifyIncoming(Hit hit) { return hit; }

        // Movimiento cuando no hay patrón en curso: acercarse al objetivo sin pegarse demasiado
        protected virtual Vector2 Idle(float dt, Player player, float dist, float dx, float dz)
        {
            if (player == null || dist < 2.6f) return Vector2.zero;
            var dir = AvoidHazards(ChaseDirection(dist, dx, dz, dt));
            Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(dir.x, dir.y), 6f, dt);
            return new Vector2(dir.x * Cfg.Speed, dir.y * Cfg.Speed);
        }

        public void Say(string text) { G.Ui.FloatText(X, Cfg.BarY + 0.4f, Z, text, TextStyle.Label); }
        public void FaceTo(float x, float z, float rate = 0f, float dt = 0f)
        {
            float a = Mathf.Atan2(x - X, z - Z);
            Facing = rate > 0f ? MathUtil.DampAngle(Facing, a, rate, dt) : a;
        }
        public List<Player> AlivePlayers() { return G.Players.FindAll(p => p.Targetable); }
        public Enemy SpawnMinion(char ch, float x, float z, EnemyOpts opts = null)
        {
            var e = G.SpawnEnemy(ch, x, z, opts);
            if (e != null) { e.Wake(true); e.SetState(EnemyState.Chase); }
            return e;
        }
        public Telegraph Telegraph(Shape shape, float time, Action<Telegraph> onFire, int color = 0xff3020) { return G.Telegraphs.Add(shape, time, onFire, color); }
        public Wave Wave(Wave w) { return G.Telegraphs.AddWave(X, Z, w); }
        public Projectile Shoot(Projectile p, Transform model = null) { return G.Projectiles.Spawn(p, model); }

        // ---------- Objetivo ----------
        public override Player PickTarget(float dt)
        {
            var f = ForcedTarget;
            if (f != null && f.Targetable) { Target = f; return f; }
            return base.PickTarget(dt);
        }

        // ---------- Máquina de patrones ----------
        protected override bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv)
        {
            mv = Vector2.zero;
            if (!IsAwake) return true;
            if (State != EnemyState.Custom) return false;
            foreach (var p in Patterns) p.CdLeft = Mathf.Max(0f, p.CdLeft - dt);
            if (!Controllable) return false;           // aturdido o volando: la base lo gestiona
            if (Current != null)
            {
                PatternT += dt;
                Mv = Vector2.zero;
                var cur = Current;
                bool done = cur.Update(this, dt, player, dist);
                if (done && Current == cur) EndPattern();
                mv = Mv;
                return true;
            }
            PatternCd -= dt;
            if (PatternCd <= 0f)
            {
                var p = ChoosePattern(player, dist);
                if (p != null) { StartPattern(p); return true; }
            }
            mv = HoldStill ? Vector2.zero : Idle(dt, player, dist, dx, dz);
            return true;
        }

        BossPattern ChoosePattern(Player player, float dist)
        {
            var ok = Patterns.FindAll(p => p.CdLeft <= 0f && (p.Phases == null || Array.IndexOf(p.Phases, Phase) >= 0) && (p.Can == null || p.Can(this, player, dist)));
            if (ok.Count == 0) return null;
            return G.Rand.Weighted(ok, p => p.WeightFn != null ? p.WeightFn() : p.Weight);
        }

        public void StartPattern(BossPattern p)
        {
            Current = p;
            PatternT = 0f;
            p.CdLeft = p.Cooldown;
            if (p.Start != null) p.Start(this);
        }

        public void EndPattern()
        {
            var p = Current;
            if (p == null) return;
            Current = null;
            if (p.End != null) p.End(this);
            PatternCd = PatternGap;
        }

        public void CancelPattern() { EndPattern(); }

        public bool Is(string patternId) { return Current != null && Current.Id == patternId; }

        // ---------- Daño ----------
        public override void TakeHit(Hit hit)
        {
            if (!IsAwake || Dead) return;
            if (hit.By != null) LastHitter = hit.By;
            var h = ModifyIncoming(hit.Clone()) ?? hit;
            if (h.Blocked || Invulnerable)
            {
                Flash(0.08f, 0x8888ff);
                G.Audio.Play("clang");
                if (G.Time - clangT > 0.4f) { clangT = G.Time; G.Ui.FloatText(X, 2.4f, Z, h.BlockText ?? "¡CLANG!", TextStyle.Small); }
                return;
            }
            base.TakeHit(h);
        }

        public override void Damage(float amount, bool env, string label)
        {
            if (!IsAwake || Dead) return;
            if (Invulnerable) return;
            base.Damage(amount, env, label);
            // Fases: al bajar de cada umbral de vida
            var th = PhaseThresholds;
            while (!Dead && Phase - 1 < th.Length && Hp / MaxHp <= th[Phase - 1])
            {
                Phase++;
                CancelPattern();
                PatternCd = 1.0f;
                G.Audio.Play("bossRoar");
                G.CameraCtl.AddShake(0.5f);
                G.Ui.FloatText(X, Cfg.BarY + 0.8f, Z, "¡FASE " + Phase + "!", TextStyle.Big);
                OnPhase(Phase);
            }
        }

        public override void Die(string cause)
        {
            if (Dead) return;
            CancelPattern();
            base.Die(cause);
            if (G.BossFloor != null) G.BossFloor.OnBossDied(this);
        }
    }

    // ---------------------------------------------------------------- Registro
    public sealed class BossEntry
    {
        public string Id, Name, Title, Icon = "👑", Hint;
        public int ArenaW = 17, ArenaH = 15, Fog = 0x0a0606;
        public Action<Action<int, int, char>, int, int> Decorate;
        public Func<Game, float, float, List<Boss>> Spawn;
    }

    public static class Bosses
    {
        public static readonly Dictionary<string, BossEntry> List = new Dictionary<string, BossEntry>();
        public static void Register(string id, BossEntry e) { e.Id = id; List[id] = e; }
        public static BossEntry Get(string id) { BossEntry e; return id != null && List.TryGetValue(id, out e) ? e : null; }

        static Bosses() { RegisterAll(); }
        static void RegisterAll() { BossRegistrations.All(); }
    }

    // Cada jefe añade aquí su entrada (BossFloor la necesita antes de crear ninguno)
    public static partial class BossRegistrations
    {
        public static void All()
        {
            Golem(); Twins(); Chef(); Bell();
        }

        static partial void Golem();
        static partial void Twins();
        static partial void Chef();
        static partial void Bell();
    }
}
