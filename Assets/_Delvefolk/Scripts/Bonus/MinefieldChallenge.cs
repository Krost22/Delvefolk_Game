using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Delvefolk
{
    // Minijuego A: LA CÁMARA DE LAS MINAS (MinefieldChallenge.js). El suelo de la sala es un tablero de losas.
    // Cada jugador apunta a la losa oculta que tiene delante: INTERACTUAR = sondear, ATACAR = poner/quitar bandera.
    // Pisar una losa oculta también la revela... y si hay mina, quien la pisa queda eliminado.
    // Sondear una mina la hace estallar: daña a todos los que estén cerca y gasta una oportunidad.
    // Toda revelación pasa por MinefieldBoard.Reveal(): una losa se revela una sola vez aunque dos jugadores
    // actúen en el mismo frame (se resuelven en orden de jugador y la segunda acción se rechaza).
    public sealed class MinefieldChallenge : BonusChallenge
    {
        sealed class CellVis { public Transform Plate; public MeshRenderer PlateR; public GameObject Extra; public string State; public Color Base; }

        public int Cols, Rows, Mines;
        public const int BoardZ0 = 2;   // fila del mapa donde empieza el tablero
        public MinefieldBoard Board;
        public int Target, Strikes, Mistakes;
        public float TimeLeft;
        CellVis[] cellVis;
        public readonly MeshRenderer[] Cursors = new MeshRenderer[2];
        public readonly int[] CursorCell = { -1, -1 };   // índice de la losa a la que apunta cada jugador (−1: ninguna)
        public ChestProp Chest;
        static Mesh plateMesh, cursorMesh;
        static readonly string[] NumColors = { null, "#5bb0ff", "#7dd65b", "#ff5a4a", "#c080ff", "#ff9a2a", "#40e0d0", "#ffffff", "#aaaaaa" };
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public MinefieldChallenge(BonusFloorController ctl) : base(ctl) { }

        public override BonusInfo Info
        {
            get
            {
                return new BonusInfo
                {
                    Id = "minefield", Name = "La Cámara de las Minas", Icon = "💣",
                    Tagline = "Un suelo de losas. Algunas esconden pólvora. Los números no mienten.",
                    Rules = new[]
                    {
                        "Cada número dice cuántas minas hay en las 8 losas que lo rodean.",
                        "Mira hacia una losa oculta: <b>{E}</b> la sondea desde donde estás, <b>{A}</b> le pone una bandera.",
                        "Pisar una losa oculta también la destapa... con los pies.",
                        "Losas especiales: 🔍 pista (marca una mina), 🛡️ escudo y 🪙 oro para quien las destape.",
                    },
                    Win = "Destapad todas las losas seguras antes de que acabe el tiempo (" + BonusConfig.Minefield.TimeLimit + " s).",
                    Lose = "Se agotan las " + BonusConfig.Minefield.Strikes + " oportunidades del equipo, el tiempo, o no queda nadie en pie.",
                    Risk = "Sondear una mina: explosión y -1 oportunidad. Pisarla: " +
                        (BonusConfig.Minefield.StepPenalty == "eliminate" ? "quedas ELIMINADO del desafío." : "mucho daño."),
                    Reward = "Cada superviviente: botín aleatorio (+1 extra si no fallasteis nada).",
                    Coop = "Cooperativo: el tablero es el mismo para todos. Cuidado con los empujones...",
                };
            }
        }

        public override LevelDef BuildLevel(int depth)
        {
            Cols = BonusConfig.Minefield.Cols; Rows = BonusConfig.Minefield.Rows;
            Mines = Mathf.Min(BonusConfig.Minefield.MaxMines, (int)MathUtil.RoundJs(BonusConfig.Minefield.Mines + BonusConfig.Minefield.MinesPerFloor * Mathf.Max(0, depth - 1)));
            int W = Cols + 2, H = Rows + 7;
            var rows = Frame(W, H);
            Put(rows, 0, 3, 't'); Put(rows, W - 1, 3, 't'); Put(rows, 0, H - 4, 't'); Put(rows, W - 1, H - 4, 't');
            Put(rows, W / 2, H - 3, 'P');
            Put(rows, W / 2, H - 1, 'N');
            return new LevelDef
            {
                Name = "Piso bonus — La Cámara de las Minas", Fog = 0x0a0608, Map = rows,
                Rooms = new[] { new RoomDef("M", "La Cámara de las Minas", new[] { 1, 1, W - 2, H - 2 }, null) },
            };
        }

        public override void Setup()
        {
            Board = new MinefieldBoard(new MinefieldBoard.Options
            {
                Cols = Cols, Rows = Rows, Mines = Mines, Seed = Ctl.Seed, StartC = Cols / 2, StartR = Rows - 1,
                Hint = BonusConfig.Minefield.Hint, Shield = BonusConfig.Minefield.Shield, Gold = BonusConfig.Minefield.Gold,
                RequireSolvable = BonusConfig.Minefield.RequireSolvable, MaxTries = BonusConfig.Minefield.MaxTries,
            });
            Target = Mathf.CeilToInt(Board.TotalSafe * BonusConfig.Minefield.TargetRatio);
            Strikes = BonusConfig.Minefield.Strikes;
            TimeLeft = BonusConfig.Minefield.TimeLimit;
            Mistakes = 0;
            BuildVisuals();
            SyncAll();
        }

        public override void Start()
        {
            base.Start();
            foreach (var p in G.Players) Participant(p).Strikes = BonusConfig.Minefield.Strikes;
            // Brújula maldita: revela algunas losas seguras al empezar
            Player holder = null;
            foreach (var p in G.Players) if (p.Gear.Has("item_compass")) { holder = p; break; }
            if (holder != null)
            {
                foreach (var r in Board.RevealRandomSafe(3, Rnd)) ApplyReveal(r, holder, false, -1, -1, null);
                G.Ui.Toast("🧭 La brújula maldita de " + holder.Look.Name + " señala tres losas seguras.", 4f);
            }
        }

        // ---------- Geometría del tablero ----------
        public static Vector2Int TileOfCell(int c, int r) { return new Vector2Int(1 + c, BoardZ0 + r); }

        public bool CellAt(float x, float z, out int c, out int r)
        {
            c = Grid.TileX(x) - 1; r = Grid.TileZ(z) - BoardZ0;
            return Board.InBounds(c, r);
        }

        public Vector2 Center(int c, int r) { var t = TileOfCell(c, r); return TileCenter(t.x, t.y); }

        void BuildVisuals()
        {
            if (!plateMesh) plateMesh = new MeshBuilder().Add(ProcMesh.Box(1.84f, 0.16f, 1.84f), Vector3.zero, Color.white).ToMesh("losaMina");
            cellVis = new CellVis[Cols * Rows];
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    var p = Center(c, r);
                    float v = 0.85f + Rnd.Next() * 0.2f;
                    // Losa oculta: pizarra azulada, claramente distinta del suelo normal
                    var go = CharacterModel.MeshObject(Root, "Losa", plateMesh, Mats.Lit, new Vector3(p.x, 0.08f, p.y));
                    var vis = new CellVis { Plate = go.transform, PlateR = go.GetComponent<MeshRenderer>(), Base = MathUtil.Hex(0x6d7888) * v };
                    Tint(vis, vis.Base);
                    cellVis[Board.Idx(c, r)] = vis;
                }
            // Cursor de cada jugador (marco cuadrado de su color)
            if (!cursorMesh) cursorMesh = new MeshBuilder().Add(ProcMesh.Ring(0.98f, 1.2f, 4, 1, Mathf.PI / 4f).RotateX(-Mathf.PI / 2f), Vector3.zero, Color.white).ToMesh("cursorMina");
            for (int i = 0; i < 2; i++)
            {
                var go = CharacterModel.MeshObject(Root, "Cursor" + (i + 1), cursorMesh, Mats.Transparent, Vector3.zero);
                go.SetActive(false);
                Cursors[i] = go.GetComponent<MeshRenderer>();
            }
            // Cofre del tesoro al fondo (decorado: se abre al ganar)
            Chest = BonusProps.Chest(Root, 0xc9a227);
            var cp = TileCenter((Cols + 2) / 2, 1);
            Chest.Root.localPosition = new Vector3(cp.x, 0, cp.y);
        }

        void Tint(CellVis vis, Color c)
        {
            var lin = c.linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(lin.r, lin.g, lin.b, 1f));
            vis.PlateR.SetPropertyBlock(mpb);
        }

        // Ajusta el aspecto de una losa a su estado
        void SyncCell(int i)
        {
            var cell = Board.Cells[i];
            var vis = cellVis[i];
            string key = cell.State + (cell.Hinted ? "h" : "");
            if (vis.State == key) return;
            vis.State = key;
            if (vis.Extra) { Object.Destroy(vis.Extra); vis.Extra = null; }
            int c = i % Cols, r = i / Cols;
            var p = Center(c, r);
            if (cell.State == MinefieldBoard.CellState.Revealed)
            {
                var pos = vis.Plate.localPosition; pos.y = -0.06f; vis.Plate.localPosition = pos;
                Tint(vis, MathUtil.Hex(cell.Mine ? 0x4a2018 : 0x2e2822));
                if (cell.Mine)
                {
                    var m = BonusProps.Mine(Root);
                    m.localPosition = new Vector3(p.x, 0, p.y);
                    vis.Extra = m.gameObject;
                }
                else if (cell.N > 0) vis.Extra = Number(cell.N, new Vector3(p.x, 0.04f, p.y));
            }
            else if (cell.State == MinefieldBoard.CellState.Flagged)
            {
                var f = BonusProps.Flag(Root, cell.Hinted ? 0xffd040 : 0xe2533b);
                f.localPosition = new Vector3(p.x, 0.16f, p.y);
                vis.Extra = f.gameObject;
            }
        }

        // Número pintado sobre la losa (texto tumbado, de su color y con contorno negro)
        GameObject Number(int n, Vector3 pos)
        {
            var go = new GameObject("Numero" + n);
            go.transform.SetParent(Root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            var t = go.AddComponent<TextMeshPro>();
            t.font = Fonts.Body;
            t.text = "<b>" + n + "</b>";
            t.fontSize = 11f;
            t.alignment = TextAlignmentOptions.Center;
            Color col;
            ColorUtility.TryParseHtmlString(NumColors[n], out col);
            t.color = col;
            Delvefolk.Hud.Outline(t, Color.black, 0.3f);
            t.rectTransform.sizeDelta = new Vector2(1.25f, 1.25f);
            return go;
        }

        void SyncAll() { for (int i = 0; i < Board.Cells.Length; i++) SyncCell(i); }

        // ---------- Acciones ----------
        // Losa oculta (o con bandera) a la que apunta el jugador: la vecina mejor alineada con su mirada
        public int AimedCell(Player p)
        {
            int htx = Grid.TileX(p.X), htz = Grid.TileZ(p.Z);
            int best = -1;
            float bestA = 1.05f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int c = htx + dx - 1, r = htz + dz - BoardZ0;
                    var cell = Board.CellAt(c, r);
                    if (cell == null || cell.State == MinefieldBoard.CellState.Revealed) continue;
                    var ctr = Center(c, r);
                    float a = Mathf.Abs(MathUtil.AngleDiff(p.Facing, Mathf.Atan2(ctr.x - p.X, ctr.y - p.Z)));
                    if (a < bestA) { bestA = a; best = Board.Idx(c, r); }
                }
            return best;
        }

        bool CanAct(Player p) { return !p.Dead && !p.Spectator && p.CarriedBy == null && p.Carrying == null && !p.IsThrown && !IsOut(p); }

        public override void PreUpdate(float dt)
        {
            foreach (var p in G.Players)
            {
                int aim = CanAct(p) ? AimedCell(p) : -1;
                CursorCell[p.Index] = aim;
                if (aim < 0) continue;
                if (p.Controls.ConsumeAttack()) RequestFlag(p, aim % Cols, aim / Cols);
            }
        }

        public override bool HandleInteraction(Player p)
        {
            int aim = CursorCell[p.Index];
            if (aim < 0 || !CanAct(p)) return false;
            var cell = Board.Cells[aim];
            G.Ui.SetPrompt(p.Index, cell.State == MinefieldBoard.CellState.Flagged ? "{A} Quitar bandera" : "{E} Sondear · {A} Bandera");
            if (p.Controls.ConsumeInteract()) RequestReveal(p, aim % Cols, aim / Cols, "probe");
            return true;
        }

        public bool RequestFlag(Player p, int c, int r)
        {
            if (Status != "running") return false;
            bool flagged;
            if (!Board.ToggleFlag(c, r, out flagged)) return false;
            var e = Log("flag"); e.Player = p.Index; e.C = c; e.R = r; e.On = flagged;
            G.Audio.Play(flagged ? "mineFlag" : "drop");
            SyncCell(Board.Idx(c, r));
            return true;
        }

        // Único punto de entrada para destapar una losa (sondeo o pisada)
        public MinefieldBoard.RevealResult RequestReveal(Player p, int c, int r, string how)
        {
            if (Status != "running") return new MinefieldBoard.RevealResult { Reason = "desafío terminado" };
            var res = Board.Reveal(c, r, p.Index, how == "step");
            var e = Log(res.Ok ? "reveal" : "reveal-rejected");
            e.Player = p.Index; e.C = c; e.R = r; e.How = how; e.Mine = res.Mine; e.Reason = res.Reason;
            if (!res.Ok)
            {
                if (res.Reason == "bandera") G.Ui.FloatText(p.X, 2.6f, p.Z, "Tiene bandera: quítala con " + p.Controls.S.AttackKey, TextStyle.Small);
                return res;
            }
            ApplyReveal(res, p, true, c, r, how);
            return res;
        }

        void ApplyReveal(MinefieldBoard.RevealResult res, Player p, bool feedback, int c, int r, string how)
        {
            foreach (int i in res.Revealed) SyncCell(i);
            if (res.Mine) { Detonate(p, c, r, how); return; }
            if (feedback)
            {
                G.Audio.Play("mineReveal");
                if (res.Revealed.Count > 3) G.Ui.FloatText(p.X, 2.6f, p.Z, "¡Despejado! +" + res.Revealed.Count, TextStyle.Heal);
            }
            foreach (var s in res.Specials)
            {
                int sc = s.Key % Cols, sr = s.Key / Cols;
                var ctr = Center(sc, sr);
                G.Effects.Burst(ctr.x, 0.5f, ctr.y, BurstOpts.Of(12, 0xffe08a, 3f, 5f, 0.1f));
                if (s.Value == "hint")
                {
                    int m = Board.HintMine(Rnd);
                    if (m >= 0) { SyncCell(m); G.Ui.Toast("🔍 ¡Pista! Una mina queda marcada con bandera dorada.", 3.5f); }
                }
                else if (s.Value == "shield") G.RewardService.ClaimReward(p, "item_shield", Source + ":cell" + s.Key);
                else if (s.Value == "gold") G.RewardService.ClaimReward(p, "gold_small", Source + ":cell" + s.Key);
            }
            if (Board.RevealedSafe >= Target) Win();
        }

        void Detonate(Player p, int c, int r, string how)
        {
            var ctr = Center(c, r);
            float R = BonusConfig.Minefield.BlastRadius;
            Mistakes++;
            G.Audio.Play("explosion");
            G.CameraCtl.AddShake(0.8f);
            G.RequestHitstop(0.08f);
            G.Effects.Flash(ctr.x, 2f, ctr.y, 0xff8a30, 6f, 0.3f);
            G.Effects.Ring(ctr.x, ctr.y, RingOpts.Of(0xffb040, R, 0.4f));
            G.Effects.Burst(ctr.x, 0.6f, ctr.y, BurstOpts.Of(30, 0xff8a20, 9f, 6f, 0.24f).WithColorVar(0.15f));
            G.Ui.FloatText(ctr.x, 2.8f, ctr.y, "¡BUUM!", TextStyle.Big);
            // La onda alcanza a todos los que estén cerca (también al compañero)
            foreach (var q in G.Players)
            {
                if (!q.Targetable) continue;
                if (q == p && how == "step") continue;
                float d = MathUtil.Hypot(q.X - ctr.x, q.Z - ctr.y);
                if (d < R) q.Hurt(BonusConfig.Minefield.ProbeDamage, ctr.x, ctr.y, 13f, new HurtOpts { Kind = "trap" });
            }
            if (how == "step")
            {
                // Si un escudo de emergencia bloquea el impacto, se salva de la eliminación
                bool hit = p.Hurt(BonusConfig.Minefield.StepDamage, ctr.x + (G.Rand.Next() - 0.5f), ctr.y + (G.Rand.Next() - 0.5f), 18f, new HurtOpts { Kind = "trap", Pierce = true });
                if (hit && BonusConfig.Minefield.StepPenalty == "eliminate") Eliminate(p, "pisó una mina");
            }
            if (Status != "running") return;
            if (BonusConfig.Minefield.ErrorScope == "player")
            {
                var st = Participant(p);
                st.Strikes--;
                if (st.Strikes <= 0) Eliminate(p, "sin oportunidades");
            }
            else
            {
                Strikes--;
                G.Ui.Toast(Strikes > 0 ? "¡Mina! Quedan " + Strikes + " oportunidades." : "¡Mina! No quedan oportunidades...", 2.5f);
                if (Strikes <= 0) Finish(false, "Se agotaron las oportunidades");
            }
        }

        void Win()
        {
            // Al ganar se muestran las minas que quedaban (como banderas)
            for (int i = 0; i < Board.Cells.Length; i++)
            {
                var k = Board.Cells[i];
                if (k.Mine && k.State == MinefieldBoard.CellState.Hidden) { k.State = MinefieldBoard.CellState.Flagged; SyncCell(i); }
            }
            Chest.Open = true;
            var cp = Chest.Root.position;
            G.Effects.Burst(cp.x, 1f, cp.z, BurstOpts.Of(30, 0xffd040, 5f, 7f, 0.14f));
            Finish(true, "Todas las losas seguras destapadas");
        }

        public override void Update(float dt)
        {
            base.Update(dt);
            TimeLeft -= dt;
            // Pisar una losa oculta la destapa (solo con el centro bien dentro: rozar el borde no cuenta)
            foreach (var p in G.Players)
            {
                if (p.Dead || p.Spectator || p.CarriedBy != null || p.IsThrown || p.JumpY > 0f || IsOut(p)) continue;   // en el aire no se pisa
                int c, r;
                if (!CellAt(p.X, p.Z, out c, out r)) continue;
                var cell = Board.CellAt(c, r);
                if (cell.State == MinefieldBoard.CellState.Revealed) continue;
                var ctr = Center(c, r);
                if (Mathf.Abs(p.X - ctr.x) < 0.7f && Mathf.Abs(p.Z - ctr.y) < 0.7f) RequestReveal(p, c, r, "step");
                if (Status != "running") return;
            }
            if (TimeLeft <= 0f) { Finish(false, "Se acabó el tiempo"); return; }
            // Cursores
            for (int i = 0; i < 2; i++)
            {
                int aim = CursorCell[i];
                var m = Cursors[i];
                bool vis = aim >= 0 && i < G.Players.Count;
                if (m.gameObject.activeSelf != vis) m.gameObject.SetActive(vis);
                if (vis)
                {
                    var ctr = Center(aim % Cols, aim / Cols);
                    m.transform.localPosition = new Vector3(ctr.x, 0.2f, ctr.y);
                    var col = MathUtil.Hex(Player.Looks[i].Ring).linear;
                    mpb.SetVector(Mats.BaseColorId, new Vector4(col.r, col.g, col.b, 0.65f + Mathf.Sin(Time * 8f) * 0.25f));
                    m.SetPropertyBlock(mpb);
                }
            }
            Chest.UpdateLid(dt);
        }

        public override List<BonusRewardService.PlanItem> RewardPlan()
        {
            var plan = new List<BonusRewardService.PlanItem>();
            if (Status != "success") return plan;
            bool perfect = Mistakes == 0;
            foreach (var p in G.Players)
            {
                if (IsOut(p)) continue;
                int n = BonusConfig.Minefield.RewardRolls + (perfect ? BonusConfig.Minefield.PerfectBonusRolls : 0);
                for (int k = 0; k < n; k++)
                    plan.Add(Plan(p, Rewards.Roll(Rnd, null, p.Gear.Luck()), Source + ":win" + k, k >= BonusConfig.Minefield.RewardRolls ? "sin fallos" : ""));
            }
            return plan;
        }

        public override BonusHudState Hud()
        {
            var h = new BonusHudState { Title = Info.Name, Icon = "💣", Timer = Mathf.Max(0f, TimeLeft), HasProgress = true, ProgressLabel = "Losas seguras", ProgressValue = Board.RevealedSafe, ProgressMax = Target };
            int max = BonusConfig.Minefield.Strikes;
            string strikes = BonusConfig.Minefield.ErrorScope == "team"
                ? "<color=#ff5a4a>" + new string('❤', Mathf.Max(0, Strikes)) + "</color><alpha=#40>" + new string('❤', Mathf.Max(0, max - Strikes)) + "<alpha=#FF>"
                : "por jugador";
            h.Lines.Add("Oportunidades: " + strikes);
            h.Lines.Add("🚩 " + Board.Flags + " / " + Board.MineCount + " minas");
            HudPlayers(h, p => BonusConfig.Minefield.ErrorScope == "player" ? new string('❤', Mathf.Max(0, Participant(p).Strikes)) : "");
            return h;
        }
    }
}
