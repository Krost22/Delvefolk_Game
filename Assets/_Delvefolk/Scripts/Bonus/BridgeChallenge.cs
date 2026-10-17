using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Minijuego B: EL PUENTE DEL SACRIFICIO (BridgeChallenge.js).
    // Un foso con losas en rombo entre la salida (abajo) y la meta (arriba).
    //  - Columna central: ruta CORTA, con más losas frágiles, y cada losa aguanta a un solo jugador.
    //  - Columnas exteriores: rutas LARGAS, más seguras, y sus losas aguantan a dos jugadores.
    //  - Una losa frágil cruje al pisarla y cae a los 0.45 s: quien reacciona rápido se salva.
    //  - Las grietas de las frágiles solo se ven de cerca (con alguien en una casilla vecina).
    // Siempre existe al menos una ruta de losas seguras (no depende solo del azar).
    public sealed class BridgeChallenge : BonusChallenge
    {
        public sealed class Platform
        {
            public int C, R, D, Tx, Tz, Uses;
            public bool Fragile;
            public string State = "solid";   // solid | crumbling | gone
            public float T;
            public HashSet<int> Visitors = new HashSet<int>(), Occupied = new HashSet<int>();
            public Transform Slab, Pips, Cracks;
            public List<MeshRenderer> CrackR = new List<MeshRenderer>();
            public float CrackA;
        }

        sealed class Fall { public Player P; public float T; }
        public struct Finisher { public int Index; public float T; }

        int cols, half, gridRows, W, H;
        public const int Z0 = 3;   // fila del mapa donde empieza la rejilla
        public readonly List<Platform> Platforms = new List<Platform>();
        readonly Dictionary<long, Platform> byTile = new Dictionary<long, Platform>();
        public float TimeLeft;
        public readonly List<Finisher> Finished = new List<Finisher>();
        readonly List<Fall> falling = new List<Fall>();
        public int Collapsed;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static Mesh pipMesh, crackMesh;

        public BridgeChallenge(BonusFloorController ctl) : base(ctl) { }

        public override BonusInfo Info
        {
            get
            {
                return new BonusInfo
                {
                    Id = "bridge", Name = "El Puente del Sacrificio", Icon = "🌉",
                    Tagline = "Un foso sin fondo y unas losas que han visto días mejores.",
                    Rules = new[]
                    {
                        "Cruzad de abajo arriba saltando de losa en losa (solo en horizontal o vertical).",
                        "Algunas losas son <b>frágiles</b>: crujen al pisarlas y caen en un suspiro. ¡Atrás, rápido!",
                        "Las grietas de una losa frágil solo se ven si alguien está justo al lado.",
                        "Las losas también se gastan: las del centro aguantan a <b>un</b> aventurero; las de los lados, a dos.",
                        "Ruta central: corta y peligrosa. Rutas laterales: largas y más seguras. ¿Vas delante o esperas?",
                    },
                    Win = "Llegar a la otra orilla antes de " + BonusConfig.Bridge.TimeLimit + " s.",
                    Lose = "Que nadie cruce a tiempo.",
                    Risk = "Caer al foso: -" + BonusConfig.Bridge.FallDamage + " de vida y fuera de la ronda.",
                    Reward = "Quien cruza: botín. El primero en llegar: botín extra. En menos de " + BonusConfig.Bridge.FastBonusTime + " s: oro.",
                    Coop = "Cada uno decide: seguir al que va delante (si la losa aún aguanta) o buscarse su camino.",
                };
            }
        }

        static long Key(int tx, int tz) { return ((long)tx << 32) ^ (uint)tz; }

        public override LevelDef BuildLevel(int depth)
        {
            cols = BonusConfig.Bridge.Cols; half = (cols - 1) / 2;
            gridRows = cols + 2;                    // con 7 columnas: 9 filas
            W = cols + 2; H = 1 + 2 + gridRows + 2 + 1;
            var rows = Frame(W, H);
            Platforms.Clear();
            for (int c = 0; c < cols; c++)
            {
                int d = Mathf.Abs(c - half);
                for (int r = 0; r < gridRows; r++)
                {
                    // Rombo: la columna central tiene menos losas (los muelles de salida y meta entran más)
                    bool isPlat = r >= half - d && r <= gridRows - 1 - (half - d);
                    if (!isPlat) continue;
                    Put(rows, 1 + c, Z0 + r, '_');
                    Platforms.Add(new Platform { C = c, R = r, D = d, Tx = 1 + c, Tz = Z0 + r });
                }
            }
            Put(rows, 0, 2, 't'); Put(rows, W - 1, 2, 't'); Put(rows, 0, H - 3, 't'); Put(rows, W - 1, H - 3, 't');
            Put(rows, 1 + half, H - 3, 'P');
            Put(rows, 1 + half, H - 1, 'N');
            return new LevelDef
            {
                Name = "Piso bonus — El Puente del Sacrificio", Fog = 0x05060a, Map = rows,
                Rooms = new[] { new RoomDef("R", "El Puente del Sacrificio", new[] { 1, 1, W - 2, H - 2 }, null) },
            };
        }

        public override void Setup()
        {
            var d = G.Dungeon;
            AssignFragile();
            byTile.Clear();
            if (!pipMesh) pipMesh = new MeshBuilder().Add(ProcMesh.Circle(0.12f, 6).RotateX(-Mathf.PI / 2f), Vector3.zero, new Color(0.941f, 0.816f, 0.502f, 0.7f)).ToMesh("pip");
            if (!crackMesh) crackMesh = new MeshBuilder().Add(ProcMesh.Box(1f, 1f, 1f), Vector3.zero, Color.white).ToMesh("grieta");
            foreach (var p in Platforms)
            {
                d.Type[d.Idx(p.Tx, p.Tz)] = TileType.Floor;   // la losa hace de suelo hasta que cae
                p.Uses = p.D == 0 ? BonusConfig.Bridge.UsesCenter : BonusConfig.Bridge.UsesOther;
                float tint = 0.85f + Rnd.Next() * 0.2f;
                var c = TileCenter(p.Tx, p.Tz);
                var slab = new MeshBuilder().Add(ProcMesh.Box(Cfg.Tile * 0.9f, 0.5f, Cfg.Tile * 0.9f), Vector3.zero, MathUtil.Hex(0x8a7d6a) * tint).ToMesh("losaPuente");
                p.Slab = CharacterModel.MeshObject(Root, "Losa", slab, Mats.Lit, new Vector3(c.x, -0.25f, c.y)).transform;
                if (p.Fragile)
                {
                    // Grietas: invisibles hasta que alguien está al lado
                    p.Cracks = new GameObject("Grietas").transform;
                    p.Cracks.SetParent(Root, false);
                    p.Cracks.localPosition = new Vector3(c.x, 0f, c.y);
                    for (int k = 0; k < 3; k++)
                    {
                        float len = 0.9f + Rnd.Next() * 0.5f;
                        float rot = Rnd.Next() * Mathf.PI;
                        float x = (Rnd.Next() - 0.5f) * 0.8f, z = (Rnd.Next() - 0.5f) * 0.8f;
                        var l = CharacterModel.MeshObject(p.Cracks, "Grieta", crackMesh, Mats.Transparent, new Vector3(x, 0.01f, -z));
                        l.transform.localRotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0);
                        l.transform.localScale = new Vector3(0.06f, 0.02f, len);
                        var lr = l.GetComponent<MeshRenderer>();
                        p.CrackR.Add(lr);
                        SetAlpha(lr, 0x111111, 0f);
                    }
                }
                // Marca de desgaste: puntos que indican cuántos aventureros aguanta todavía
                p.Pips = new GameObject("Usos").transform;
                p.Pips.SetParent(Root, false);
                p.Pips.localPosition = new Vector3(c.x, 0f, c.y);
                for (int k = 0; k < p.Uses; k++)
                    CharacterModel.MeshObject(p.Pips, "Uso", pipMesh, Mats.Transparent, new Vector3((k - (p.Uses - 1) / 2f) * 0.35f, 0.02f, -0.62f));
                byTile[Key(p.Tx, p.Tz)] = p;
            }
            TimeLeft = BonusConfig.Bridge.TimeLimit;
            Finished.Clear();
            falling.Clear();
            Collapsed = 0;
        }

        void SetAlpha(MeshRenderer r, int hex, float a)
        {
            var col = MathUtil.Hex(hex).linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(col.r, col.g, col.b, a));
            r.SetPropertyBlock(mpb);
        }

        // Reparto de losas frágiles con ruta segura garantizada (sin depender solo del azar)
        void AssignFragile()
        {
            var fr = BonusConfig.Bridge.FragileByDistance;
            for (int tries = 0; tries < 200; tries++)
            {
                foreach (var p in Platforms) p.Fragile = Rnd.Next() < fr[Mathf.Min(p.D, fr.Length - 1)];
                if (SafePathExists()) return;
            }
            foreach (var p in Platforms) if (p.D == half) p.Fragile = false;   // último recurso: los bordes, seguros
        }

        // ¿Hay camino de la salida a la meta solo por losas no frágiles? (BFS sobre la rejilla)
        public bool SafePathExists()
        {
            var ok = new HashSet<long>();
            var plat = new HashSet<long>();
            foreach (var p in Platforms) { plat.Add(Key(p.C, p.R)); if (!p.Fragile) ok.Add(Key(p.C, p.R)); }
            System.Func<int, int, bool> walk = (c, r) => c >= 0 && c < cols && r >= 0 && r < gridRows && (!plat.Contains(Key(c, r)) || ok.Contains(Key(c, r)));
            var q = new Queue<Vector2Int>();
            var seen = new HashSet<long>();
            for (int c = 0; c < cols; c++) if (walk(c, gridRows - 1)) q.Enqueue(new Vector2Int(c, gridRows - 1));
            int[] dcs = { 1, -1, 0, 0 }, drs = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                var cr = q.Dequeue();
                if (!seen.Add(Key(cr.x, cr.y))) continue;
                if (cr.y == 0) return true;
                for (int k = 0; k < 4; k++) if (walk(cr.x + dcs[k], cr.y + drs[k])) q.Enqueue(new Vector2Int(cr.x + dcs[k], cr.y + drs[k]));
            }
            return false;
        }

        public Platform PlatformAt(int tx, int tz) { Platform p; return byTile.TryGetValue(Key(tx, tz), out p) ? p : null; }

        bool IsFalling(Player pl) { foreach (var f in falling) if (f.P == pl) return true; return false; }
        public bool IsFinished(Player p) { foreach (var f in Finished) if (f.Index == p.Index) return true; return false; }

        void Collapse(Platform p)
        {
            var d = G.Dungeon;
            p.State = "gone"; p.T = 0f;
            d.Type[d.Idx(p.Tx, p.Tz)] = TileType.Pit;
            Collapsed++;
            G.Audio.Play("crumble");
            var c = TileCenter(p.Tx, p.Tz);
            G.Effects.Burst(c.x, 0f, c.y, BurstOpts.Of(16, 0x8a7d6a, 3f, 2f, 0.2f).WithFloor(-6f));
            if (p.Pips) p.Pips.gameObject.SetActive(false);
            if (p.Cracks) p.Cracks.gameObject.SetActive(false);
            // Quien estuviera encima cae
            foreach (var pl in G.Players)
            {
                if (pl.Dead || pl.Spectator || pl.CarriedBy != null || IsFinished(pl) || IsFalling(pl)) continue;
                if (Grid.TileX(pl.X) == p.Tx && Grid.TileZ(pl.Z) == p.Tz) StartFall(pl);
            }
        }

        void StartFall(Player pl)
        {
            if (pl.Carrying != null) pl.Release(0f);
            pl.Body.Enabled = false;
            pl.Attack = null; pl.Ability.Finish();
            falling.Add(new Fall { P = pl });
            Eliminate(pl, "cayó al foso");   // fuera desde el primer instante: ni habilidades ni daño en el aire
            G.Audio.Play("fall");
            G.Ui.FloatText(pl.X, 2.4f, pl.Z, "¡AAAAH!", TextStyle.Label);
            Log("fall").Player = pl.Index;
        }

        public override void Update(float dt)
        {
            base.Update(dt);
            TimeLeft -= dt;
            // Ocupación de cada losa
            var occ = new Dictionary<long, HashSet<int>>();
            foreach (var pl in G.Players)
            {
                if (pl.Dead || pl.Spectator || pl.CarriedBy != null || pl.JumpY > 0f || IsOut(pl) || IsFalling(pl)) continue;
                long k = Key(Grid.TileX(pl.X), Grid.TileZ(pl.Z));
                HashSet<int> set;
                if (!occ.TryGetValue(k, out set)) occ[k] = set = new HashSet<int>();
                set.Add(pl.Index);
            }
            foreach (var p in Platforms)
            {
                if (p.State == "gone")
                {
                    p.T += dt;
                    var pos = p.Slab.localPosition; pos.y = -0.25f - p.T * p.T * 12f; p.Slab.localPosition = pos;
                    p.Slab.Rotate(dt * 2f * Mathf.Rad2Deg, 0, 0, Space.Self);
                    if (p.T > 1.2f && p.Slab.gameObject.activeSelf) p.Slab.gameObject.SetActive(false);
                    continue;
                }
                HashSet<int> now;
                if (!occ.TryGetValue(Key(p.Tx, p.Tz), out now)) now = new HashSet<int>();
                // Al pisar una frágil: cruje y cae enseguida
                if (p.State == "solid" && now.Count > 0 && p.Fragile)
                {
                    p.State = "crumbling"; p.T = BonusConfig.Bridge.CollapseDelay;
                    G.Audio.Play("crumble");
                    var c = TileCenter(p.Tx, p.Tz);
                    G.Ui.FloatText(c.x, 1.2f, c.y, "*crac*", TextStyle.Small);
                    var e = Log("crack"); e.C = p.C; e.R = p.R;
                }
                // Desgaste: cuando un aventurero sale de la losa, gasta un uso (cada jugador cuenta una vez)
                foreach (int idx in p.Occupied)
                {
                    if (now.Contains(idx) || p.Visitors.Contains(idx)) continue;
                    p.Visitors.Add(idx);
                    p.Uses--;
                    if (p.Uses >= 0 && p.Uses < p.Pips.childCount) p.Pips.GetChild(p.Uses).gameObject.SetActive(false);
                    if (p.Uses <= 0 && p.State == "solid")
                    {
                        p.State = "crumbling"; p.T = BonusConfig.Bridge.WornDelay;
                        var e = Log("worn"); e.C = p.C; e.R = p.R;
                    }
                }
                p.Occupied = now;
                if (p.State == "crumbling")
                {
                    p.T -= dt;
                    var pos = p.Slab.localPosition; pos.x = Grid.X(p.Tx) + Mathf.Sin(Time * 70f) * 0.05f; p.Slab.localPosition = pos;
                    if (p.T <= 0f) Collapse(p);
                }
            }
            // Grietas visibles solo con alguien en una casilla vecina
            foreach (var p in Platforms)
            {
                if (!p.Cracks || p.State == "gone") continue;
                bool near = false;
                foreach (var pl in G.Players)
                {
                    if (pl.Dead || pl.Spectator) continue;
                    if (Mathf.Abs(Grid.TileX(pl.X) - p.Tx) + Mathf.Abs(Grid.TileZ(pl.Z) - p.Tz) == 1) { near = true; break; }
                }
                float target = near || p.State == "crumbling" ? 0.85f : 0f;
                p.CrackA = MathUtil.Damp(p.CrackA, target, 8f, dt);
                foreach (var lr in p.CrackR) SetAlpha(lr, 0x111111, p.CrackA);
            }
            // Caídas
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var f = falling[i];
                f.T += dt;
                f.P.SetRootY(-f.T * f.T * 10f);
                f.P.Model.localRotation = Quaternion.Euler(f.T * 6f * Mathf.Rad2Deg, 0, 0);
                if (f.T > 0.9f)
                {
                    falling.RemoveAt(i);
                    var pl = f.P;
                    pl.Body.Enabled = true;
                    pl.Body.X = Grid.X(1 + half); pl.Body.Z = Grid.Z(H - 3) - (pl.Index != 0 ? 1.2f : 0f);
                    pl.Body.Kx = pl.Body.Kz = 0f;
                    pl.SetRootY(0f); pl.Model.localRotation = Quaternion.identity;
                    pl.SyncRoot();
                    // Daño de la caída (directo: los espectadores no reciben golpes normales)
                    float dmg = BonusConfig.Bridge.FallDamage;
                    pl.Hp = Mathf.Max(0f, pl.Hp - dmg);
                    G.Ui.SetHealth(pl.Index, pl.Hp, pl.MaxHp);
                    G.Ui.FloatText(pl.X, 2.4f, pl.Z, "-" + dmg, TextStyle.Hurt);
                    if (pl.Hp <= 0f) pl.Die();
                }
            }
            // Llegada a la meta (filas de arriba)
            foreach (var pl in G.Players)
            {
                if (pl.Dead || pl.Spectator || IsOut(pl) || IsFinished(pl)) continue;
                if (Grid.TileZ(pl.Z) < Z0)
                {
                    Finished.Add(new Finisher { Index = pl.Index, T = Time });
                    G.Audio.Play("win");
                    G.Ui.FloatText(pl.X, 2.8f, pl.Z, Finished.Count == 1 ? "¡PRIMERO!" : "¡A salvo!", TextStyle.Big);
                    var e = Log("finish-line"); e.Player = pl.Index;
                }
            }
            if (Status != "running") return;
            bool pending = false;
            foreach (var pl in G.Players) if (!IsOut(pl) && !pl.Dead && !IsFinished(pl)) { pending = true; break; }
            if (!pending && Finished.Count > 0) Finish(true, "Cruzasteis el puente");
            else if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                Finish(Finished.Count > 0, Finished.Count > 0 ? "Se acabó el tiempo (alguien llegó)" : "Nadie cruzó a tiempo");
            }
        }

        public override List<BonusRewardService.PlanItem> RewardPlan()
        {
            var plan = new List<BonusRewardService.PlanItem>();
            for (int k = 0; k < Finished.Count; k++)
            {
                var f = Finished[k];
                Player p = null;
                foreach (var q in G.Players) if (q.Index == f.Index) p = q;
                if (p == null) continue;
                plan.Add(Plan(p, Rewards.Roll(Rnd, null, p.Gear.Luck()), Source + ":cross", "cruzó en " + MathUtil.RoundJs(f.T) + " s"));
                if (k == 0) for (int i = 0; i < BonusConfig.Bridge.FirstBonusRolls; i++) plan.Add(Plan(p, Rewards.Roll(Rnd, null, p.Gear.Luck()), Source + ":first" + i, "el primero"));
                if (f.T <= BonusConfig.Bridge.FastBonusTime) plan.Add(Plan(p, "gold_small", Source + ":fast", "rapidísimo"));
            }
            return plan;
        }

        public override BonusHudState Hud()
        {
            var h = new BonusHudState { Title = Info.Name, Icon = "🌉", Timer = Mathf.Max(0f, TimeLeft), HasProgress = true, ProgressLabel = "Han cruzado", ProgressValue = Finished.Count, ProgressMax = G.Players.Count };
            h.Lines.Add("Losas caídas: " + Collapsed);
            HudPlayers(h, p => IsFinished(p) ? "🏁" : "");
            return h;
        }
    }
}
