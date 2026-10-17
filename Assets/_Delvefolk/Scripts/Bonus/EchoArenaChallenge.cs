using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Minijuego D: LA ARENA DE LOS ECOS (EchoArenaChallenge.js).
    // Sobrevivir un tiempo limitado en una arena pequeña: oleadas de enemigos débiles, trampas, barriles... y el
    // borde de la arena se vuelve peligroso anillo a anillo (con aviso). Las armas improvisadas, los empujones y
    // las habilidades funcionan igual que en la mazmorra. Recompensa según el tiempo aguantado y el objetivo de equipo.
    public sealed class EchoArenaChallenge : BonusChallenge
    {
        const int N = 15, Cc = 7;
        readonly List<Vector2Int> floorTiles = new List<Vector2Int>();
        public HazardFloor Hazard;
        public int SafeRing;      // anillo máximo seguro
        public float TimeLeft, WaveT, ShrinkT;
        public int Wave, Kills;
        public readonly List<Enemy> Spawned = new List<Enemy>();
        public readonly Dictionary<int, float> Survived = new Dictionary<int, float>();
        int pendingRing = -1;
        float pendingT;

        public EchoArenaChallenge(BonusFloorController ctl) : base(ctl) { }

        public override BonusInfo Info
        {
            get
            {
                var tiers = BonusConfig.Arena.RewardTiers;
                return new BonusInfo
                {
                    Id = "arena", Name = "La Arena de los Ecos", Icon = "🏟️",
                    Tagline = "Una arena redonda, un público invisible que aplaude... y el suelo que se derrite por los bordes.",
                    Rules = new[]
                    {
                        "Cada " + BonusConfig.Arena.WaveEvery + " s entra una oleada de monstruos debiluchos (cada vez más).",
                        "Cada " + BonusConfig.Arena.ShrinkEvery + " s el anillo exterior se vuelve <b>lava</b>: primero parpadea en naranja, luego quema.",
                        "Las armas del suelo, los barriles, las púas y vuestras habilidades funcionan como siempre.",
                        "Empujar a los monstruos a la lava también cuenta (y empujar a tu compañero... también pasa).",
                    },
                    Win = "Aguantar " + BonusConfig.Arena.Duration + " s en pie.",
                    Lose = "Que caigáis todos antes de tiempo.",
                    Risk = "Caer te deja fuera de la ronda (sin recompensa de tiempo).",
                    Reward = "Según el tiempo aguantado: ≥" + tiers[0][0] + " s → " + tiers[0][1] + " botines, ≥" + tiers[1][0] + " s → " + tiers[1][1] +
                        ". Objetivo de equipo: " + BonusConfig.Arena.KillGoal + " monstruos → +1 botín a los supervivientes.",
                    Coop = "Cooperativo con caos: la lava no distingue entre amigos y enemigos.",
                };
            }
        }

        public override LevelDef BuildLevel(int depth)
        {
            var rows = Frame(N, N);
            // Esquinas recortadas: arena octogonal
            for (int z = 1; z < N - 1; z++)
                for (int x = 1; x < N - 1; x++)
                    if (Mathf.Abs(x - Cc) + Mathf.Abs(z - Cc) > 10) Put(rows, x, z, '#');
            Put(rows, Cc - 3, Cc - 3, 'o'); Put(rows, Cc + 3, Cc + 3, 'o');
            Put(rows, Cc + 3, Cc - 3, 'b'); Put(rows, Cc - 3, Cc + 3, 'b'); Put(rows, Cc, Cc - 5, 'b');
            Put(rows, Cc + 5, Cc, 'e');
            Put(rows, Cc - 2, Cc + 1, '^'); Put(rows, Cc + 2, Cc - 1, '^');
            Put(rows, Cc - 1, Cc - 2, 'K'); Put(rows, Cc + 1, Cc + 2, 'F');
            Put(rows, Cc, Cc, 'P');
            Put(rows, 0, Cc, 't'); Put(rows, N - 1, Cc, 't'); Put(rows, Cc, 0, 't'); Put(rows, Cc, N - 1, 'N');
            return new LevelDef
            {
                Name = "Piso bonus — La Arena de los Ecos", Fog = 0x0a0606, Map = rows,
                Rooms = new[] { new RoomDef("R", "La Arena de los Ecos", new[] { 1, 1, N - 2, N - 2 }, null) },
            };
        }

        static int Ring(int tx, int tz) { return Mathf.Max(Mathf.Abs(tx - Cc), Mathf.Abs(tz - Cc)); }

        public override void Setup()
        {
            var d = G.Dungeon;
            floorTiles.Clear();
            for (int tz = 0; tz < d.H; tz++)
                for (int tx = 0; tx < d.W; tx++)
                    if (d.TypeAt(tx, tz) == TileType.Floor) floorTiles.Add(new Vector2Int(tx, tz));
            Hazard = new HazardFloor(G, Root, floorTiles, BonusConfig.Arena.DangerDps);
            SafeRing = Cc - 1;      // anillo máximo seguro (6)
            TimeLeft = BonusConfig.Arena.Duration;
            WaveT = 2.5f; Wave = 0;
            ShrinkT = BonusConfig.Arena.ShrinkEvery;
            Spawned.Clear();
            Kills = 0;
            Survived.Clear();
        }

        public override void Start()
        {
            base.Start();
            foreach (var p in G.Players) Survived[p.Index] = 0f;
        }

        void SpawnWave()
        {
            var d = G.Dungeon;
            Wave++;
            int n = BonusConfig.Arena.WaveSize + BonusConfig.Arena.WaveGrowth * (Wave - 1);
            int r = Mathf.Max(2, SafeRing);
            var spots = new List<Vector2Int>();
            foreach (var t in floorTiles)
                if (Ring(t.x, t.y) == r && d.Blocked[d.Idx(t.x, t.y)] == 0 && d.Spike[d.Idx(t.x, t.y)] == 0) spots.Add(t);
            Rnd.Shuffle(spots);
            for (int i = 0; i < n && i < spots.Count; i++)
            {
                var t = spots[i];
                bool goblin = Rnd.Next() < 0.35f;
                var e = G.SpawnEnemy(goblin ? 'G' : 'S', Grid.X(t.x), Grid.Z(t.y),
                    goblin ? new EnemyOpts { Cfg = c => { c.Hp = 18; c.Damage = 6; c.Speed = 4f; } } : new EnemyOpts { Scale = 0.8f, Cfg = c => { c.Hp = 20; c.Damage = 7; } });
                if (e == null) continue;
                e.Wake(true);
                e.SetState(EnemyState.Chase);
                Spawned.Add(e);
                G.Effects.Burst(e.X, 0.5f, e.Z, BurstOpts.Of(12, 0x9a60ff, 3f, 4f, 0.12f));
            }
            G.Ui.Toast("¡Oleada " + Wave + "!", 1.5f);
            G.Audio.Play("alert");
        }

        // El anillo exterior seguro pasa a aviso y, tras 2.5 s, a peligro
        void Shrink()
        {
            if (SafeRing <= 2) return;
            int r = SafeRing;
            foreach (var t in floorTiles) if (Ring(t.x, t.y) == r) Hazard.Set(t.x, t.y, HazardFloor.CellState.Warn);
            pendingRing = r; pendingT = 2.5f;
            SafeRing--;
            G.Audio.Play("tick");
        }

        public override void Update(float dt)
        {
            base.Update(dt);
            TimeLeft -= dt;
            foreach (var p in G.Players) if (!IsOut(p) && !p.Dead) Survived[p.Index] = Time;
            WaveT -= dt;
            if (WaveT <= 0f) { WaveT = BonusConfig.Arena.WaveEvery; SpawnWave(); }
            ShrinkT -= dt;
            if (ShrinkT <= 0f) { ShrinkT = BonusConfig.Arena.ShrinkEvery; Shrink(); }
            if (pendingRing >= 0)
            {
                pendingT -= dt;
                if (pendingT <= 0f)
                {
                    foreach (var t in floorTiles) if (Ring(t.x, t.y) >= pendingRing) Hazard.Set(t.x, t.y, HazardFloor.CellState.Danger);
                    pendingRing = -1;
                    G.CameraCtl.AddShake(0.2f);
                }
            }
            Hazard.Update(dt);
            // Bajas de esta arena (objetivo de equipo)
            Kills = 0;
            foreach (var e in Spawned) if (e.Dead) Kills++;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                foreach (var p in G.Players) if (!IsOut(p) && !p.Dead) Survived[p.Index] = BonusConfig.Arena.Duration;
                // Los monstruos que quedan se desvanecen
                foreach (var e in Spawned)
                {
                    if (e.Dead) continue;
                    G.Effects.Burst(e.X, 0.6f, e.Z, BurstOpts.Of(10, 0x9a60ff, 3f, 4f, 0.12f));
                    e.Destroy();
                }
                Finish(ActivePlayers().Count > 0, "¡Tiempo! Aguantasteis en la arena");
            }
        }

        public override List<BonusRewardService.PlanItem> RewardPlan()
        {
            var plan = new List<BonusRewardService.PlanItem>();
            bool teamGoal = Kills >= BonusConfig.Arena.KillGoal;
            foreach (var p in G.Players)
            {
                float t;
                Survived.TryGetValue(p.Index, out t);
                int n = 0;
                foreach (var tier in BonusConfig.Arena.RewardTiers) if (t >= tier[0] - 0.01f) { n = (int)tier[1]; break; }
                for (int k = 0; k < n; k++) plan.Add(Plan(p, Rewards.Roll(Rnd, null, p.Gear.Luck()), Source + ":time" + k, Mathf.FloorToInt(t) + " s"));
                if (n == 0 && t >= 10f) plan.Add(Plan(p, "gold_small", Source + ":consolation", "consolación"));
                if (teamGoal && !IsOut(p)) plan.Add(Plan(p, Rewards.Roll(Rnd, null, p.Gear.Luck()), Source + ":team", "objetivo de equipo"));
            }
            return plan;
        }

        public override BonusHudState Hud()
        {
            var h = new BonusHudState
            {
                Title = Info.Name, Icon = "🏟️", Timer = Mathf.Max(0f, TimeLeft), HasProgress = true,
                ProgressLabel = "Monstruos (objetivo)", ProgressValue = Mathf.Min(Kills, BonusConfig.Arena.KillGoal), ProgressMax = BonusConfig.Arena.KillGoal,
            };
            h.Lines.Add("Oleada " + Wave + " · zona segura " + (SafeRing * 2 + 1) + "×" + (SafeRing * 2 + 1));
            HudPlayers(h, p => { float t; Survived.TryGetValue(p.Index, out t); return Mathf.FloorToInt(t) + " s"; });
            return h;
        }
    }
}
