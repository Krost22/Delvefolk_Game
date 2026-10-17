using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Minijuego F: LA CÁMARA DEL ÚLTIMO ALIENTO (LastBreathChallenge.js).
    // La sala se llena de gas salvo unas pocas zonas seguras que cambian cada pocos segundos (con aviso).
    // Un rodillo de piedra cruza la sala de vez en cuando por una fila marcada. Hay barriles para empujar
    // (y para que te empujen). Quien siga en pie al final gana; el último superviviente se lleva un extra.
    public sealed class LastBreathChallenge : BonusChallenge
    {
        public struct Zone { public int Tx, Tz; }

        sealed class Roller
        {
            public int Row, Dir;
            public float X, Z, Warn;
            public Transform Mesh, Stone, Arrows;
            public readonly HashSet<object> Hit = new HashSet<object>();
        }

        const int W = 13, H = 13;
        readonly List<Vector2Int> tiles = new List<Vector2Int>();
        public HazardFloor Hazard;
        public List<Vector2Int> Tiles { get { return tiles; } }
        Transform zoneGroup;
        readonly List<MeshRenderer> zoneR = new List<MeshRenderer>();
        public float TimeLeft, PhaseT, WarnLeft, RollerT;
        public int Phase;
        public List<Zone> Zones = new List<Zone>(), PrevZones = new List<Zone>();
        Roller roller;
        public int RollersLaunched;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static Mesh zoneMesh, stoneMesh, arrowMesh;

        public LastBreathChallenge(BonusFloorController ctl) : base(ctl) { }

        public override BonusInfo Info
        {
            get
            {
                return new BonusInfo
                {
                    Id = "breath", Name = "La Cámara del Último Aliento", Icon = "🫁",
                    Tagline = "Alguien ha dejado abierto el grifo del gas. Otra vez.",
                    Rules = new[]
                    {
                        "Solo las zonas <b>verdes</b> están libres de gas. Cambian cada " + BonusConfig.Breath.PhaseEvery + " s.",
                        "Cuando el suelo parpadea en naranja tienes " + BonusConfig.Breath.WarnTime + " s para llegar a una zona nueva.",
                        "Un rodillo de piedra cruza la sala por la fila marcada con flechas: apártate, sáltalo (o empuja a alguien).",
                        "Los barriles se pueden empujar para abrirse paso... o para estorbar.",
                    },
                    Win = "Seguir en pie cuando pasen " + BonusConfig.Breath.Duration + " s.",
                    Lose = "Que caigáis todos.",
                    Risk = "El gas quema rápido. Caer te deja fuera de la ronda.",
                    Reward = "Cada superviviente: botín. Si solo queda uno en pie de varios: botín extra para él.",
                    Coop = "Todos contra la sala... pero las zonas seguras son pequeñas.",
                };
            }
        }

        public override LevelDef BuildLevel(int depth)
        {
            var rows = Frame(W, H);
            Put(rows, 3, 3, 'b'); Put(rows, 9, 9, 'b'); Put(rows, 3, 9, 'b'); Put(rows, 9, 3, 'b');
            Put(rows, 6, 3, 'o'); Put(rows, 6, 9, 'o');
            Put(rows, 6, 6, 'P');
            Put(rows, 0, 4, 't'); Put(rows, W - 1, 8, 't'); Put(rows, 4, 0, 't'); Put(rows, 8, H - 1, 't'); Put(rows, 6, H - 1, 'N');
            return new LevelDef
            {
                Name = "Piso bonus — La Cámara del Último Aliento", Fog = 0x060a06, Map = rows,
                Rooms = new[] { new RoomDef("R", "La Cámara del Último Aliento", new[] { 1, 1, W - 2, H - 2 }, null) },
            };
        }

        public override void Setup()
        {
            var d = G.Dungeon;
            tiles.Clear();
            for (int tz = 1; tz < H - 1; tz++)
                for (int tx = 1; tx < W - 1; tx++)
                    if (d.TypeAt(tx, tz) == TileType.Floor) tiles.Add(new Vector2Int(tx, tz));
            Hazard = new HazardFloor(G, Root, tiles, BonusConfig.Breath.DangerDps, 0x60c040, 0xffa030);
            // Las zonas seguras se marcan con un borde verde
            zoneGroup = new GameObject("ZonasSeguras").transform;
            zoneGroup.SetParent(Root, false);
            TimeLeft = BonusConfig.Breath.Duration;
            Phase = -1;
            PhaseT = 0f;
            Zones = new List<Zone>();
            PrevZones = new List<Zone>();
            RollerT = 4f;
            roller = null;
        }

        public override void Start()
        {
            base.Start();
            NextPhase();
        }

        public static bool InZones(int tx, int tz, List<Zone> zones)
        {
            foreach (var z in zones) if (tx >= z.Tx && tx <= z.Tx + 1 && tz >= z.Tz && tz <= z.Tz + 1) return true;
            return false;
        }

        // Elige nuevas zonas seguras de 2x2 (sin solaparse ni coincidir con columnas)
        void NextPhase()
        {
            Phase++;
            PhaseT = 0f;
            var cfgZones = BonusConfig.Breath.Zones;
            int n = cfgZones[Mathf.Min(Phase, cfgZones.Length - 1)];
            var d = G.Dungeon;
            var zones = new List<Zone>();
            for (int tries = 0; zones.Count < n && tries < 200; tries++)
            {
                int tx = 1 + (int)(Rnd.Next() * (W - 3)), tz = 1 + (int)(Rnd.Next() * (H - 3));
                bool okz = true;
                for (int a = 0; a <= 1; a++)
                    for (int b = 0; b <= 1; b++)
                        if (d.Blocked[d.Idx(tx + a, tz + b)] != 0 || d.TypeAt(tx + a, tz + b) != TileType.Floor) okz = false;
                if (!okz) continue;
                bool apart = true;
                foreach (var z in zones) if (!(Mathf.Abs(z.Tx - tx) > 2 || Mathf.Abs(z.Tz - tz) > 2)) { apart = false; break; }
                if (apart) zones.Add(new Zone { Tx = tx, Tz = tz });
            }
            PrevZones = Zones;
            Zones = zones;
            // Nuevas zonas: seguras ya. El resto: aviso (si estaba a salvo) y luego gas
            foreach (var t in tiles)
            {
                if (InZones(t.x, t.y, zones)) Hazard.Set(t.x, t.y, HazardFloor.CellState.Safe);
                else if (Phase == 0 || InZones(t.x, t.y, PrevZones)) Hazard.Set(t.x, t.y, HazardFloor.CellState.Warn);
                else Hazard.Set(t.x, t.y, HazardFloor.CellState.Danger);
            }
            WarnLeft = BonusConfig.Breath.WarnTime;
            DrawZones();
            G.Audio.Play("tick");
        }

        void DrawZones()
        {
            for (int i = zoneGroup.childCount - 1; i >= 0; i--) Object.Destroy(zoneGroup.GetChild(i).gameObject);
            zoneR.Clear();
            if (!zoneMesh) zoneMesh = new MeshBuilder().Add(ProcMesh.Ring(1.9f, 2.2f, 4, 1, Mathf.PI / 4f).RotateX(-Mathf.PI / 2f).Scale(Mathf.Sqrt(2f), 1f, Mathf.Sqrt(2f)), Vector3.zero, Color.white).ToMesh("zonaSegura");
            foreach (var z in Zones)
            {
                var go = CharacterModel.MeshObject(zoneGroup, "Zona", zoneMesh, Mats.Transparent, new Vector3(Grid.X(z.Tx) + 1f, 0.06f, Grid.Z(z.Tz) - 1f));
                zoneR.Add(go.GetComponent<MeshRenderer>());
            }
        }

        // Rodillo: avisa con flechas en su fila y luego la cruza de pared a pared
        void LaunchRoller()
        {
            int row = 1 + (int)(Rnd.Next() * (H - 2));
            int dir = Rnd.Next() < 0.5f ? 1 : -1;
            if (!stoneMesh)
            {
                stoneMesh = new MeshBuilder().Add(ProcMesh.Cylinder(0.85f, 0.85f, 1.7f, 10).RotateX(Mathf.PI / 2f), Vector3.zero, MathUtil.Hex(0x7a7266)).ToMesh("rodillo");
                arrowMesh = new MeshBuilder().Add(ProcMesh.Cone(0.35f, 0.8f, 3), Vector3.zero, new Color(1f, 0.353f, 0.29f, 0.8f)).ToMesh("flechaRodillo");
            }
            var r = new Roller { Row = row, Dir = dir, Z = Grid.Z(row), Warn = 1.6f };
            r.X = dir > 0 ? Grid.X(1) - 0.5f : Grid.X(W - 2) + 0.5f;
            r.Mesh = new GameObject("Rodillo").transform;
            r.Mesh.SetParent(Root, false);
            r.Stone = CharacterModel.MeshObject(r.Mesh, "Piedra", stoneMesh, Mats.Lit, Vector3.zero).transform;
            r.Mesh.gameObject.SetActive(false);
            r.Arrows = new GameObject("Flechas").transform;
            r.Arrows.SetParent(Root, false);
            for (int tx = 1; tx < W - 1; tx += 2)
            {
                var a = CharacterModel.MeshObject(r.Arrows, "Flecha", arrowMesh, Mats.Transparent, new Vector3(Grid.X(tx), 0.1f, r.Z));
                // Tumbada en el suelo, apuntando en el sentido de la marcha
                a.transform.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(dir, 0, 0));
            }
            roller = r;
            RollersLaunched++;
            G.Audio.Play("alert");
        }

        void UpdateRoller(float dt)
        {
            var r = roller;
            if (r.Warn > 0f)
            {
                r.Warn -= dt;
                bool vis = Mathf.FloorToInt(r.Warn * 8f) % 2 == 0;
                if (r.Arrows.gameObject.activeSelf != vis) r.Arrows.gameObject.SetActive(vis);
                if (r.Warn <= 0f) { r.Arrows.gameObject.SetActive(false); r.Mesh.gameObject.SetActive(true); G.Audio.Play("crumble"); }
                return;
            }
            r.X += r.Dir * BonusConfig.Breath.RollerSpeed * dt;
            r.Mesh.localPosition = new Vector3(r.X, 0.85f, r.Z);
            r.Stone.Rotate(0, 0, -r.Dir * dt * 9f * Mathf.Rad2Deg, Space.Self);
            const float hitR = 1.1f;
            foreach (var p in G.Players)
            {
                if (!p.Targetable || r.Hit.Contains(p) || Mathf.Abs(p.Z - r.Z) > hitR || Mathf.Abs(p.X - r.X) > hitR) continue;
                // Desviación del original (§11): como dice la viñeta, se puede saltar por encima
                if (p.JumpY > JumpConfig.DodgeHeight * 0.6f) continue;
                r.Hit.Add(p);
                float side = p.Z >= r.Z ? 1f : -1f;
                if (p.Hurt(BonusConfig.Breath.RollerDamage, r.X - r.Dir, p.Z - side, 0f))
                {
                    p.Knock(r.Dir * 6f, side * 14f);
                    G.Ui.FloatText(p.X, 2.6f, p.Z, "¡Aplastado!", TextStyle.Small);
                }
            }
            foreach (var e in G.Enemies)
            {
                if (e.Dead || r.Hit.Contains(e) || Mathf.Abs(e.Z - r.Z) > hitR || Mathf.Abs(e.X - r.X) > hitR) continue;
                r.Hit.Add(e);
                e.EnvDamage(30f, "¡RODILLO!");
                e.Knock(0f, (e.Z >= r.Z ? 1f : -1f) * 14f);
            }
            foreach (var b in G.Barrels.ToArray())
            {
                if (b.Dead || Mathf.Abs(b.Z - r.Z) > hitR || Mathf.Abs(b.X - r.X) > hitR) continue;
                if (b.Explosive) b.Fuse = 0.05f; else b.BreakApart();
            }
            if (r.X < Grid.X(0) + 1f - 1f || r.X > Grid.X(W - 2) + 1f + 1f)
            {
                Object.Destroy(r.Mesh.gameObject); Object.Destroy(r.Arrows.gameObject);
                roller = null;
            }
        }

        public bool RollerActive { get { return roller != null; } }
        public bool RollerRolling { get { return roller != null && roller.Warn <= 0f; } }
        public Vector2 RollerPos { get { return roller != null ? new Vector2(roller.X, roller.Z) : Vector2.zero; } }
        public bool RolledOver(object o) { return roller != null && roller.Hit.Contains(o); }

        public override void Update(float dt)
        {
            base.Update(dt);
            TimeLeft -= dt;
            PhaseT += dt;
            if (WarnLeft > 0f)
            {
                WarnLeft -= dt;
                if (WarnLeft <= 0f) foreach (var t in tiles) if (!InZones(t.x, t.y, Zones)) Hazard.Set(t.x, t.y, HazardFloor.CellState.Danger);
            }
            if (PhaseT >= BonusConfig.Breath.PhaseEvery) NextPhase();
            Hazard.Update(dt);
            var col = MathUtil.Hex(0x7dff8a).linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(col.r, col.g, col.b, 0.6f + Mathf.Sin(Time * 6f) * 0.25f));
            foreach (var r in zoneR) if (r) r.SetPropertyBlock(mpb);
            RollerT -= dt;
            if (roller == null && RollerT <= 0f) { RollerT = BonusConfig.Breath.RollerEvery; LaunchRoller(); }
            if (roller != null) UpdateRoller(dt);
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                Finish(ActivePlayers().Count > 0, "¡Se cerró el grifo! Fin del gas");
            }
        }

        public override List<BonusRewardService.PlanItem> RewardPlan()
        {
            var plan = new List<BonusRewardService.PlanItem>();
            var standing = new List<Player>();
            foreach (var p in G.Players) if (!IsOut(p) && !p.Dead) standing.Add(p);
            foreach (var p in standing)
                for (int k = 0; k < BonusConfig.Breath.RewardRolls; k++)
                    plan.Add(Plan(p, Rewards.Roll(Rnd, null, p.Gear.Luck()), Source + ":alive" + k, "superviviente"));
            if (standing.Count == 1 && G.Players.Count > 1)
            {
                var p = standing[0];
                for (int k = 0; k < BonusConfig.Breath.LastStandingRolls; k++)
                    plan.Add(Plan(p, Rewards.Roll(Rnd, d => d.Rare || d.Kind != "instant", p.Gear.Luck()), Source + ":last" + k, "último en pie"));
            }
            return plan;
        }

        public override BonusHudState Hud()
        {
            var h = new BonusHudState { Title = Info.Name, Icon = "🫁", Timer = Mathf.Max(0f, TimeLeft) };
            h.Lines.Add(WarnLeft > 0f ? "⚠️ ¡Cambio de zona en " + WarnLeft.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s!" : "Zonas seguras: " + Zones.Count);
            HudPlayers(h);
            return h;
        }
    }
}
