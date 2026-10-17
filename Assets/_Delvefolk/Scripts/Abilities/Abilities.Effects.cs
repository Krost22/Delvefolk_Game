using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Comportamiento de las 7 habilidades (Abilities.js). Todo pasa por los sistemas existentes:
    // empuje (Knock), aturdimiento, daño (Damage / Hurt), física y efectos.
    public static partial class Abilities
    {
        // ---------- Ayudas comunes ----------
        public static List<Enemy> EnemiesIn(Game g, float x, float z, float r)
        {
            var l = new List<Enemy>();
            foreach (var e in g.Enemies)
                if (!e.Dead && !e.Removed && e.State != EnemyState.Falling && e.Body.Enabled && MathUtil.Hypot(e.X - x, e.Z - z) < r + e.Radius) l.Add(e);
            return l;
        }

        public static List<Player> PlayersIn(Game g, float x, float z, float r, Player except)
        {
            var l = new List<Player>();
            foreach (var q in g.Players)
                if (q != except && q.Targetable && !q.Removed && MathUtil.Hypot(q.X - x, q.Z - z) < r + q.Radius) l.Add(q);
            return l;
        }

        public static List<Barrel> BarrelsIn(Game g, float x, float z, float r)
        {
            var l = new List<Barrel>();
            foreach (var b in g.Barrels)
                if (!b.Dead && !b.Falling && MathUtil.Hypot(b.X - x, b.Z - z) < r + b.Radius) l.Add(b);
            return l;
        }

        public static List<Breakable> BreakablesIn(Game g, float x, float z, float r)
        {
            var l = new List<Breakable>();
            foreach (var b in g.Breakables)
                if (!b.Dead && MathUtil.Hypot(b.X - x, b.Z - z) < r + b.Radius) l.Add(b);
            return l;
        }

        // Ninguna habilidad deja a un enemigo aturdido más del tope
        public static void StunEnemy(Enemy e, float t)
        {
            float m = e.Cfg.StunMult;
            if (m == 0f) m = 1f;
            e.Stun = Mathf.Min(AbilityRules.EnemyStunCap, Mathf.Max(e.Stun, t * m));
        }

        // Interrumpe la preparación o el ataque de un enemigo
        public static void Interrupt(Enemy e)
        {
            if (e.State == EnemyState.Sleep) e.Wake(false);
            if (e.State == EnemyState.Windup || e.State == EnemyState.Attack)
            {
                e.SetState(EnemyState.Chase);
                e.AttackCd = Mathf.Max(e.AttackCd, 0.6f);
                e.G.Ui.FloatText(e.X, 2.4f, e.Z, "¡INTERRUMPIDO!", TextStyle.Small);
            }
        }

        struct Dir { public float X, Z, D; }

        // Dirección desde "from" hacia "to" (si coinciden, una al azar)
        static Dir Outward(Game g, float fx, float fz, float tx, float tz)
        {
            float dx = tx - fx, dz = tz - fz, l = MathUtil.Hypot(dx, dz);
            if (l < 1e-3f)
            {
                float a = g.Rand.Next() * Mathf.PI * 2f;
                return new Dir { X = Mathf.Cos(a), Z = Mathf.Sin(a), D = 0f };
            }
            return new Dir { X = dx / l, Z = dz / l, D = l };
        }

        // ======================= A: TERREMOTO BRUTAL =======================
        static bool QuakeActivate(AbilityCtx c)
        {
            var g = c.G; var pl = c.P; var act = c.Act;
            float r = c["radius"];
            pl.Squash = 1f;
            g.Audio.Play("quake");
            g.CameraCtl.AddShake(0.6f);
            g.RequestHitstop(0.06f);
            g.Effects.Ring(pl.X, pl.Z, RingOpts.Of(0xd09a50, r, 0.45f));
            g.Effects.Ring(pl.X, pl.Z, RingOpts.Of(0xffe0a0, r * 0.6f, 0.3f));
            g.Effects.Burst(pl.X, 0.2f, pl.Z, BurstOpts.Of(30, 0x8a7b68, 7f, 6f, 0.2f).WithColorVar(0.1f));
            g.Ui.FloatText(pl.X, 2.8f, pl.Z, "¡TEMBLOR!", TextStyle.Big);
            foreach (var e in EnemiesIn(g, pl.X, pl.Z, r))
            {
                if (!act.HitSet.Add(e)) continue;
                Interrupt(e);
                var o = Outward(g, pl.X, pl.Z, e.X, e.Z);
                e.Knock(o.X * c["knock"] / e.Body.Mass, o.Z * c["knock"] / e.Body.Mass);
                StunEnemy(e, c["stun"]);
                e.PopT = 0.55f;
                e.Damage(c["damage"], false, null);
            }
            foreach (var b in BarrelsIn(g, pl.X, pl.Z, r))
            {
                if (!act.HitSet.Add(b)) continue;
                var o = Outward(g, pl.X, pl.Z, b.X, b.Z);
                b.Knock(o.X * 7f, o.Z * 7f);
                b.Spin = 1f;
            }
            foreach (var b in BreakablesIn(g, pl.X, pl.Z, r)) if (act.HitSet.Add(b)) b.EnvHit(10f);
            foreach (var q in PlayersIn(g, pl.X, pl.Z, r, pl))
            {
                if (!act.HitSet.Add(q)) continue;
                var o = Outward(g, pl.X, pl.Z, q.X, q.Z);
                q.Knock(o.X * 4f, o.Z * 4f);
                q.PopT = 0.55f;
                if (q.ApplyStun(c["allyStun"])) g.Ui.FloatText(q.X, 2.6f, q.Z, "¡Eh, que me mareo!", TextStyle.Small);
            }
            return true;
        }

        // ======================= B: EXPLOSIÓN DESESPERADA =======================
        static bool BlastActivate(AbilityCtx c)
        {
            var g = c.G; var pl = c.P; var act = c.Act;
            float x = pl.X, z = pl.Z, r = c["radius"];
            g.Audio.Play("explosion");
            g.CameraCtl.AddShake(0.9f);
            g.RequestHitstop(0.1f);
            g.Effects.Flash(x, 2f, z, 0xff8a30, 7f, 0.35f);
            g.Effects.Ring(x, z, RingOpts.Of(0xffb040, r, 0.4f));
            g.Effects.Burst(x, 0.8f, z, BurstOpts.Of(40, 0xff8a20, 10f, 6f, 0.26f).WithColorVar(0.15f).WithLife(0.6f));
            g.Effects.Burst(x, 0.8f, z, BurstOpts.Of(16, 0x444444, 5f, 7f, 0.4f).WithLife(1.0f).WithGravity(4f));
            g.Ui.FloatText(x, 3.0f, z, "¡KABUM DESESPERADO!", TextStyle.Big);
            foreach (var e in EnemiesIn(g, x, z, r))
            {
                if (!act.HitSet.Add(e)) continue;
                var o = Outward(g, pl.X, pl.Z, e.X, e.Z);
                float f = 1f - Mathf.Min(1f, o.D / (r + e.Radius)) * 0.5f;
                Interrupt(e);
                e.Knock(o.X * c["knock"] * f / e.Body.Mass, o.Z * c["knock"] * f / e.Body.Mass);
                StunEnemy(e, c["stun"]);
                e.Damage(c["damage"] * f * pl.Gear.DamageMult(), false, null);
            }
            foreach (var b in BarrelsIn(g, x, z, r + 0.4f))
            {
                if (!act.HitSet.Add(b)) continue;
                var o = Outward(g, pl.X, pl.Z, b.X, b.Z);
                b.Knock(o.X * 10f, o.Z * 10f);
                if (b.Explosive) { if (b.Fuse < 0f || b.Fuse > 0.2f) b.Fuse = 0.2f; }
                else b.EnvHit(40f);
            }
            foreach (var b in BreakablesIn(g, x, z, r)) if (act.HitSet.Add(b)) b.EnvHit(99f);
            foreach (var q in PlayersIn(g, x, z, r, pl))
            {
                if (!act.HitSet.Add(q)) continue;
                var o = Outward(g, pl.X, pl.Z, q.X, q.Z);
                float f = 1f - Mathf.Min(1f, o.D / r) * 0.5f;
                if (q.Hurt(MathUtil.RoundJs(c["allyDamage"] * f), x, z, 14f)) g.Ui.FloatText(q.X, 2.6f, q.Z, "¡Que soy de tu equipo!", TextStyle.Small);
            }
            // El coste: el propio usuario se quema (ignora la invulnerabilidad breve y la armadura)
            pl.Hurt(c["selfDamage"], x, z, 0f, new HurtOpts { Raw = true, Pierce = true });
            return true;
        }

        // ======================= C: CARGA DEL BUEY =======================
        static bool ChargeActivate(AbilityCtx c)
        {
            c.P.Squash = 1f;
            c.G.Audio.Play("charge");
            c.G.Ui.FloatText(c.P.X, 2.7f, c.P.Z, "¡MUUU!", TextStyle.Label);
            return true;
        }

        // Empuje hacia delante y un poco hacia el lado en el que está el objetivo
        static Vector2 HitDir(Player pl, float ox, float oz, float fx, float fz)
        {
            float lx = ox - pl.X, lz = oz - pl.Z;
            float side = Mathf.Sign(lx * fz - lz * fx);
            return new Vector2(fx + -fz * side * 0.35f, fz + fx * side * 0.35f);
        }

        static void ChargeTick(AbilityCtx c, float dt)
        {
            var g = c.G; var pl = c.P; var act = c.Act;
            // Control parcial: el giro hacia la dirección pulsada está muy limitado
            var ax = pl.Controls.Axis();
            if (ax.x != 0f || ax.y != 0f)
            {
                Vector2 f, r;
                g.CameraCtl.GetBasis(out f, out r);
                float want = Mathf.Atan2(f.x * ax.y + r.x * ax.x, f.y * ax.y + r.y * ax.x);
                float d = MathUtil.AngleDiff(pl.Facing, want);
                pl.Facing += Mathf.Clamp(d, -c["turnRate"] * dt, c["turnRate"] * dt);
            }
            float fx = Mathf.Sin(pl.Facing), fz = Mathf.Cos(pl.Facing);
            float sp = c["speed"] * (act.T < 0.08f ? act.T / 0.08f : 1f);
            pl.Vx = fx * sp; pl.Vz = fz * sp;
            pl.Body.Mvx = pl.Vx; pl.Body.Mvz = pl.Vz;
            if (g.FxRand.Next() < 0.6f) g.Effects.Burst(pl.X - fx * 0.4f, 0.1f, pl.Z - fz * 0.4f, BurstOpts.Of(1, 0x8a7b68, 1f, 1.5f, 0.14f));
            // Arrollar: cada cuerpo una sola vez por carga
            float reach = pl.Radius + 0.35f;
            float cx = pl.X + fx * 0.3f, cz = pl.Z + fz * 0.3f;
            foreach (var e in EnemiesIn(g, cx, cz, reach))
            {
                if (!act.HitSet.Add(e)) continue;
                var d = HitDir(pl, e.X, e.Z, fx, fz);
                float k = c["knock"] * pl.Gear.KnockDealtMult() / e.Body.Mass;
                Interrupt(e);
                e.Knock(d.x * k, d.y * k);
                StunEnemy(e, c["stun"]);
                e.Damage(c["damage"] * pl.Gear.DamageMult(), false, act.HitSet.Count == 1 ? "¡EMBESTIDA!" : null);
                g.Audio.Play("hit"); g.RequestHitstop(0.05f); g.CameraCtl.AddShake(0.25f);
            }
            foreach (var b in BarrelsIn(g, cx, cz, reach))
            {
                if (!act.HitSet.Add(b)) continue;
                var d = HitDir(pl, b.X, b.Z, fx, fz);
                b.TakeHit(new Hit { Damage = 10, Knock = 14f * pl.Gear.KnockDealtMult(), Stun = 0f, DirX = d.x, DirZ = d.y });
            }
            foreach (var q in PlayersIn(g, cx, cz, reach, pl))
            {
                if (!act.HitSet.Add(q)) continue;
                var d = HitDir(pl, q.X, q.Z, fx, fz);
                q.Knock(d.x * c["allyKnock"], d.y * c["allyKnock"]);
                q.ApplyStun(c["allyStun"]);
                g.Ui.FloatText(q.X, 2.6f, q.Z, "¡Atropellado!", TextStyle.Small);
            }
            // Los muebles se llevan un buen golpe (y si aguantan, la carga se estrella contra ellos)
            foreach (var b in BreakablesIn(g, cx, cz, reach)) if (act.HitSet.Add(b)) b.EnvHit(20f);
            // Choque contra pared, columna o cajas: la carga se acaba de golpe
            float probe = pl.Radius + 0.12f;
            float tx = pl.X + fx * probe, tz = pl.Z + fz * probe;
            bool wall = g.Dungeon.IsSolidFor(Grid.TileX(tx), Grid.TileZ(tz), pl.Body);
            if (!wall) foreach (var s in g.Statics) if (MathUtil.Hypot(s.Body.X - tx, s.Body.Z - tz) < s.Body.Radius) { wall = true; break; }
            if (!wall) foreach (var s in g.Breakables) if (!s.Dead && MathUtil.Hypot(s.Body.X - tx, s.Body.Z - tz) < s.Body.Radius) { wall = true; break; }
            if (act.T > 0.06f && wall)
            {
                act.Cancelled = true;
                act.Crashed = true;
                g.Audio.Play("crash");
                g.CameraCtl.AddShake(0.4f);
                g.Effects.Burst(tx, 1.0f, tz, BurstOpts.Of(14, 0x9a8c7a, 4f, 4f, 0.18f));
                g.Ui.FloatText(pl.X, 2.8f, pl.Z, "¡PLOF!", TextStyle.Label);
                pl.Hurt(c["wallDamage"], tx, tz, 5f, new HurtOpts { Pierce = true });
                pl.ApplyStun(c["wallStun"], true);
            }
        }

        static void ChargeEnd(AbilityCtx c)
        {
            var pl = c.P;
            pl.Vx *= 0.3f; pl.Vz *= 0.3f;
            pl.Body.Mvx = pl.Vx; pl.Body.Mvz = pl.Vz;
        }

        // ======================= D: GRITO DE GUERRA =======================
        static bool ShoutActivate(AbilityCtx c)
        {
            var g = c.G; var pl = c.P; var act = c.Act;
            float r = c["radius"];
            g.Audio.Play("shout");
            g.CameraCtl.AddShake(0.35f);
            for (int i = 0; i < 3; i++) g.Effects.Ring(pl.X, pl.Z, RingOpts.Of(i == 1 ? 0xffd040 : 0xff6040, r * (0.55f + i * 0.25f), 0.35f + i * 0.12f, 1.0f));
            g.Ui.FloatText(pl.X, 3.0f, pl.Z, "¡¡AAAAARGH!!", TextStyle.Big);
            foreach (var e in EnemiesIn(g, pl.X, pl.Z, r))
            {
                if (!act.HitSet.Add(e)) continue;
                Interrupt(e);
                StunEnemy(e, c["stun"]);
                var o = Outward(g, pl.X, pl.Z, e.X, e.Z);
                e.Knock(o.X * 3f / e.Body.Mass, o.Z * 3f / e.Body.Mass);
                e.Damage(c["damage"], false, null);
            }
            foreach (var b in BarrelsIn(g, pl.X, pl.Z, r))
            {
                if (!act.HitSet.Add(b)) continue;
                var o = Outward(g, pl.X, pl.Z, b.X, b.Z);
                b.Knock(o.X * c["push"], o.Z * c["push"]);
                b.Spin = 1f;
            }
            // Objetos ligeros del suelo salen despedidos un par de metros
            foreach (var pk in g.Pickups.ToArray())
            {
                if (pk.Tossing || pk.Magnet != null) continue;
                var o = Outward(g, pl.X, pl.Z, pk.X, pk.Z);
                if (o.D > r) continue;
                pk.BlowTo(pk.X + o.X * 2.5f, pk.Z + o.Z * 2.5f);
            }
            var team = PlayersIn(g, pl.X, pl.Z, c["allyRadius"], pl);
            team.Insert(0, pl);
            foreach (var q in team)
            {
                q.AddHaste(c["haste"], c["hasteTime"]);
                g.Effects.Burst(q.X, 1.2f, q.Z, BurstOpts.Of(8, 0xffd040, 2f, 4f, 0.08f));
            }
            return true;
        }

        // ======================= E: INTERCAMBIO CAÓTICO =======================
        // Objetivos visibles (sin muros en medio) dentro del alcance; se prefiere el que está dentro del cono de
        // visión con menor (distancia + ángulo·3). Si nadie está en el cono, el más cercano.
        static Entity PickSwapTarget(Game g, Player pl, AbilityCtx c)
        {
            var cands = new List<Entity>();
            foreach (var e in EnemiesIn(g, pl.X, pl.Z, c["range"])) if (!e.NoSwap) cands.Add(e);
            if (g.Settings.SwapAllies)
                foreach (var q in PlayersIn(g, pl.X, pl.Z, c["range"], pl)) if (!q.IsThrown && q.CarriedBy == null) cands.Add(q);
            Entity best = null, near = null;
            float bestS = float.PositiveInfinity, nearD = float.PositiveInfinity;
            foreach (var t in cands)
            {
                if (!g.Dungeon.LineClear(pl.X, pl.Z, t.X, t.Z)) continue;
                float d = MathUtil.Hypot(t.X - pl.X, t.Z - pl.Z);
                float ang = Mathf.Abs(MathUtil.AngleDiff(pl.Facing, Mathf.Atan2(t.X - pl.X, t.Z - pl.Z)));
                if (ang < c["cone"] && d + ang * 3f < bestS) { bestS = d + ang * 3f; best = t; }
                if (d < nearD) { nearD = d; near = t; }
            }
            return best ?? near;
        }

        // Cada cuerpo debe caber en su destino (nunca dentro de un muro ni sobre un foso)
        static bool Fits(Dungeon d, float x, float z)
        {
            int tx = Grid.TileX(x), tz = Grid.TileZ(z);
            return d.InBounds(tx, tz) && !d.IsWallLike(tx, tz) && !d.IsPitAt(x, z);
        }

        static bool SwapActivate(AbilityCtx c)
        {
            var g = c.G; var pl = c.P;
            if (pl.Carrying != null || pl.CarriedBy != null) return false;
            var t = PickSwapTarget(g, pl, c);
            if (t == null) { g.Ui.FloatText(pl.X, 2.6f, pl.Z, "Nadie a quien cambiar", TextStyle.Small); return false; }
            var d = g.Dungeon;
            float ax = pl.Body.X, az = pl.Body.Z, bx = t.Body.X, bz = t.Body.Z;
            if (!Fits(d, bx, bz) || !Fits(d, ax, az)) { g.Ui.FloatText(pl.X, 2.6f, pl.Z, "No hay hueco", TextStyle.Small); return false; }
            c.Act.HitSet.Add(t);
            g.Effects.Burst(ax, 1f, az, BurstOpts.Of(16, 0xb060ff, 3f, 3f, 0.16f));
            g.Effects.Burst(bx, 1f, bz, BurstOpts.Of(16, 0xb060ff, 3f, 3f, 0.16f));
            pl.Body.X = bx; pl.Body.Z = bz; t.Body.X = ax; t.Body.Z = az;
            foreach (var b in new[] { pl.Body, t.Body }) { b.Kx = 0f; b.Kz = 0f; g.Physics.SolveWalls(b); }
            pl.Body.Ignore = t.Body; pl.Body.IgnoreT = 0.3f;
            pl.SyncRoot(); t.SyncRoot();
            g.Audio.Play("swap");
            g.Effects.Ring(pl.X, pl.Z, RingOpts.Of(0xb060ff, 1.4f, 0.3f));
            g.Effects.Ring(t.X, t.Z, RingOpts.Of(0xb060ff, 1.4f, 0.3f));
            var en = t as Enemy;
            if (en != null)
            {
                Interrupt(en);
                StunEnemy(en, c["enemyStun"]);
                g.Ui.FloatText(en.X, 2.4f, en.Z, "¿¡Eh!?", TextStyle.Label);
            }
            else
            {
                var q = (Player)t;
                q.ConfusedT = Mathf.Max(q.ConfusedT, c["confuse"]);
                g.Ui.FloatText(q.X, 2.6f, q.Z, "¿Dónde estoy?", TextStyle.Small);
            }
            g.Ui.FloatText(pl.X, 2.8f, pl.Z, "¡CHAS!", TextStyle.Label);
            return true;
        }

        // ======================= F: IMÁN DE CHATARRA =======================
        // Solo se atraen objetos marcados como imantables: objetos del suelo y barriles
        static bool MagnetActivate(AbilityCtx c)
        {
            var g = c.G; var pl = c.P; var act = c.Act;
            float r = c["radius"];
            g.Audio.Play("magnet");
            var ro = RingOpts.Of(0x60b0ff, r, 0.4f); ro.R0 = r;
            g.Effects.Ring(pl.X, pl.Z, ro);
            int n = 0;
            foreach (var pk in g.Pickups)
            {
                if (pk.Tossing || pk.Magnet != null || MathUtil.Hypot(pk.X - pl.X, pk.Z - pl.Z) > r) continue;
                pk.Magnet = new ItemPickup.MagnetState { By = pl, Speed = c["itemSpeed"], Damage = c["hitDamage"], Knock = c["hitKnock"] };
                n++;
            }
            act.Barrels = BarrelsIn(g, pl.X, pl.Z, r);
            foreach (var b in act.Barrels) { b.MagnetBy = pl; b.MagnetT = 1.4f; n++; }
            g.Ui.FloatText(pl.X, 2.8f, pl.Z, n > 0 ? "¡VENID A MÍ!" : "¿Nada?", n > 0 ? TextStyle.Label : TextStyle.Small);
            return true;
        }

        static void MagnetTick(AbilityCtx c, float dt)
        {
            var pl = c.P;
            // Tirón continuo: cuanto más pesado, más despacio responde (aceleración / masa)
            foreach (var b in c.Act.Barrels)
            {
                if (b.Dead || b.Falling) continue;
                float dx = pl.X - b.X, dz = pl.Z - b.Z, d = MathUtil.Hypot(dx, dz);
                if (d < 2.2f) continue;
                float a = c["barrelPull"] / b.Body.Mass * dt;
                b.Knock(dx / d * a, dz / d * a);
            }
            if (c.G.FxRand.Next() < 0.5f) c.G.Effects.Burst(pl.X, 1.3f, pl.Z, BurstOpts.Of(1, 0x60b0ff, 2f, 0f, 0.07f).WithGravity(0f));
        }

        // ======================= G: ÚLTIMO RECURSO =======================
        static readonly MaterialPropertyBlock shellMpb = new MaterialPropertyBlock();

        static bool GuardActivate(AbilityCtx c)
        {
            var g = c.G; var pl = c.P;
            g.Audio.Play("guard");
            if (pl.Parts.Shell) pl.Parts.Shell.gameObject.SetActive(true);
            g.Effects.Ring(pl.X, pl.Z, RingOpts.Of(0xffd040, 1.5f, 0.3f, 1f));
            g.Ui.FloatText(pl.X, 2.8f, pl.Z, "¡MODO TORTUGA!", TextStyle.Label);
            return true;
        }

        static void GuardTick(AbilityCtx c, float dt)
        {
            var r = c.P.Parts.ShellRenderer;
            if (!r) return;
            float t = c.Act.T;
            float op = 0.22f + Mathf.Sin(t * 12f) * 0.06f + (t > 2.3f ? Mathf.Sin(t * 40f) * 0.1f : 0f);
            // El color por vértice del caparazón ya lleva alfa 0.25
            shellMpb.SetVector(Mats.BaseColorId, new Vector4(1f, 1f, 1f, Mathf.Clamp01(op / 0.25f)));
            r.SetPropertyBlock(shellMpb);
        }

        static void GuardEnd(AbilityCtx c)
        {
            var pl = c.P;
            if (pl.Parts.Shell) pl.Parts.Shell.gameObject.SetActive(false);
            pl.AddSlow(c["slow"], c["slowTime"]);
            c.G.Ui.FloatText(pl.X, 2.6f, pl.Z, "Uf... qué cansancio", TextStyle.Small);
        }
    }
}
