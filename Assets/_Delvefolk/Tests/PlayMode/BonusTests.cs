using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 6: equipo, recompensas y pisos bonus (port de tests/e2e.test.js)
    public class BonusTests : GameTestBase
    {
        // T.start(2) + g.bonus.forceNext + g.descend() (+ esperar el fundido)
        void EnterBonus(string id, int players = 2)
        {
            StartRun(players);
            G.Bonus.ForceNext = id;
            G.Descend();
            Step(1);
        }

        // enterBonus del original: además, todos confirman la introducción
        T Begin<T>(string id) where T : BonusChallenge
        {
            EnterBonus(id);
            TapAll("Space", "Enter");
            Assert.AreEqual("running", G.Bonus.Phase);
            return (T)G.Bonus.Challenge;
        }

        // leaveBonus del original
        void Leave()
        {
            if (G.Bonus.Phase == "running") G.Bonus.Challenge.Finish(false, "fin");
            Step(110);
            TapAll("Space", "Enter");
            Step(1);
        }

        int RewardsOf(int player, string keyPart)
        {
            return G.RewardService.Log.Count(l => l.Player == player && l.Key.Contains(keyPart));
        }

        // ---------- Marco de los bonus ----------
        [UnityTest]
        public IEnumerator Bonus_ApareceAlBajarIntroConReglasYEmpiezaConConfirmacionesOSoloPorTiempo()
        {
            EnterBonus("minefield");
            Assert.AreEqual(GameState.Bonus, G.State);
            Assert.AreEqual("intro", G.Bonus.Phase);
            Assert.IsTrue(G.InBonus);
            Assert.IsTrue(G.Ui.BonusScreenShown, "introducción visible");
            Assert.AreEqual("PISO BONUS", G.Ui.BonusTagText);
            Assert.AreEqual("BONUS", G.Ui.FloorShown);
            Tap("Space");
            Assert.AreEqual("intro", G.Bonus.Phase, "falta la confirmación de J2");
            StringAssert.Contains("✔ listo", G.Ui.BonusReadyText);
            Step(Mathf.CeilToInt(BonusConfig.IntroTimeout * 60f) + 5);   // J2 no confirma: arranca solo
            Assert.AreEqual("running", G.Bonus.Phase);
            Assert.AreEqual(GameState.Play, G.State);
            Assert.IsTrue(G.Ui.BonusHudShown);
            yield break;
        }

        [UnityTest]
        public IEnumerator Bonus_TrasElPiso1SiempreYNuncaElMismoDosVecesSeguidas()
        {
            StartRun(1);
            Assert.IsTrue(G.Bonus.ShouldTrigger(1), "en modo normal, siempre tras el piso 1");
            G.Settings.BonusMode = "never";
            Assert.IsFalse(G.Bonus.ShouldTrigger(1));
            G.Settings.BonusMode = "always";
            Assert.IsTrue(G.Bonus.ShouldTrigger(5));
            G.Settings.BonusMode = "normal";
            // Sin repeticiones seguidas
            for (int i = 0; i < 30; i++)
            {
                G.Bonus.History.Add(G.Bonus.History.Count % 2 == 0 ? "loot" : "arena");
                string last = G.Bonus.History[G.Bonus.History.Count - 1];
                var pick = typeof(BonusFloorController).GetMethod("PickChallenge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.AreNotEqual(last, (string)pick.Invoke(G.Bonus, null));
            }
            yield break;
        }

        // ---------- La Cámara de las Minas ----------
        [UnityTest]
        public IEnumerator Minas_AccionesSimultaneasSobreLaMismaLosaSeResuelvenUnaSolaVez()
        {
            var ch = Begin<MinefieldChallenge>("minefield");
            var B = ch.Board;
            Player p1 = G.Players[0], p2 = G.Players[1];
            // Una losa oculta y segura junto a una ya destapada: los dos la miran desde esa losa vecina
            int c = -1, r = -1, nc = -1, nr = -1;
            int[] dcs = { 0, 0, 1, -1 }, drs = { 1, -1, 0, 0 };
            for (int i = 0; i < B.Cells.Length && c < 0; i++)
            {
                if (B.Cells[i].Mine || B.Cells[i].State != MinefieldBoard.CellState.Hidden) continue;
                for (int k = 0; k < 4; k++)
                {
                    var n = B.CellAt(i % B.Cols + dcs[k], i / B.Cols + drs[k]);
                    if (n == null || n.State != MinefieldBoard.CellState.Revealed) continue;
                    c = i % B.Cols; r = i / B.Cols; nc = c + dcs[k]; nr = r + drs[k];
                    break;
                }
            }
            Assert.GreaterOrEqual(c, 0, "hay una losa oculta junto a la zona destapada");
            var ctr = ch.Center(c, r);
            var from = ch.Center(nc, nr);
            // Uno a cada lado del centro de la losa vecina, en perpendicular a la dirección de la mirada
            float ox = nr != r ? 0.5f : 0f, oz = nr != r ? 0f : 0.5f;
            PutU(p1, from.x - ox, from.y - oz); PutU(p2, from.x + ox, from.y + oz);
            Face(p1, ctr.x, ctr.y); Face(p2, ctr.x, ctr.y);
            Step(1);
            TapAll("KeyE", "ShiftRight");
            var evs = ch.Events.Where(e => e.C == c && e.R == r).ToList();
            Assert.AreEqual(1, evs.Count(e => e.Type == "reveal"), "una sola revelación");
            // Llamada directa doble en el mismo instante
            int c2 = System.Array.FindIndex(B.Cells, k => k.State == MinefieldBoard.CellState.Hidden && !k.Mine);
            var a = ch.RequestReveal(p1, c2 % B.Cols, c2 / B.Cols, "probe");
            var b = ch.RequestReveal(p2, c2 % B.Cols, c2 / B.Cols, "probe");
            Assert.IsTrue(a.Ok);
            Assert.IsFalse(b.Ok, "la segunda se rechaza");
            Assert.AreEqual("ya revelada", b.Reason);
            yield break;
        }

        [UnityTest]
        public IEnumerator Minas_SondearGastaOportunidadPisarEliminaYGanarRepartesSinDuplicados()
        {
            var ch = Begin<MinefieldChallenge>("minefield");
            var B = ch.Board;
            Player p1 = G.Players[0], p2 = G.Players[1];
            var mines = new List<int>();
            for (int i = 0; i < B.Cells.Length; i++) if (B.Cells[i].Mine) mines.Add(i);
            // 1) Sondeo de una mina junto a una losa ya revelada
            int first = mines.First(i => B.Neighbors(i % B.Cols, i / B.Cols).Any(nb => B.CellAt(nb.Key, nb.Value).State == MinefieldBoard.CellState.Revealed));
            int mc = first % B.Cols, mr = first / B.Cols;
            var safe = B.Neighbors(mc, mr).First(nb => B.CellAt(nb.Key, nb.Value).State == MinefieldBoard.CellState.Revealed);
            var sp = ch.Center(safe.Key, safe.Value);
            PutU(p1, sp.x, sp.y); p1.Iframes = 0f;
            ch.RequestReveal(p1, mc, mr, "probe");
            Assert.AreEqual(2, ch.Strikes, "una oportunidad menos");
            Assert.Less(p1.Hp, 100f, "la explosión daña al que sondea");
            Assert.IsFalse(ch.IsOut(p1), "sondear no elimina");
            var off = ch.Center(0, B.Rows - 1);
            PutU(p1, 2f, off.y - 4f);   // fuera del tablero: el empujón no le hace pisar nada
            Step(60);
            // 2) J2 pisa otra mina
            int second = mines.First(i => i != first);
            var mctr = ch.Center(second % B.Cols, second / B.Cols);
            PutU(p1, 2f, off.y - 4f);
            p2.Iframes = 0f; PutU(p2, mctr.x, mctr.y); Step(2);
            Assert.IsTrue(ch.IsOut(p2), "pisar una mina elimina");
            Assert.IsTrue(p2.Spectator);
            Assert.IsFalse(p2.Targetable);
            Assert.AreEqual(1, ch.Strikes);
            Assert.AreEqual("eliminado del desafío", p2.Ability.BlockReason(), "el eliminado no puede usar habilidades");
            Tap("Enter"); Step(3);
            Assert.IsNull(p2.Attack, "el eliminado no puede atacar");
            // 3) J1 destapa todas las seguras → victoria
            for (int i = 0; i < B.Cells.Length; i++)
                if (!B.Cells[i].Mine && B.Cells[i].State != MinefieldBoard.CellState.Revealed) ch.RequestReveal(p1, i % B.Cols, i / B.Cols, "probe");
            Assert.AreEqual("success", ch.Status);
            int logBefore = G.RewardService.Log.Count;
            Assert.GreaterOrEqual(RewardsOf(0, ":win"), 1, "el superviviente recibe botín");
            Assert.AreEqual(0, RewardsOf(1, ":win"), "el eliminado no recibe botín");
            G.RewardService.GrantPlan(ch.RewardPlan());
            Assert.AreEqual(logBefore, G.RewardService.Log.Count, "sin recompensas duplicadas");
            Step(110);
            Assert.AreEqual("result", G.Bonus.Phase);
            Assert.IsTrue(G.Ui.BonusScreenShown);
            Assert.AreEqual("¡DESAFÍO SUPERADO!", G.Ui.BonusTagText);
            string gear = JsonUtility.ToJson(p1.Gear.GetState());
            TapAll("Space", "Enter"); Step(1);
            Assert.AreEqual(GameState.Play, G.State);
            Assert.IsTrue(G.InShop, "tras el bonus, la tienda");
            Assert.AreEqual(2, G.Shop.Next, "que lleva al piso 2");
            Assert.IsFalse(G.InBonus);
            Player q1 = G.Players[0], q2 = G.Players[1];
            Assert.IsFalse(q2.Spectator, "el eliminado vuelve a la normalidad");
            Assert.IsFalse(q2.Dead);
            Assert.AreEqual(gear, JsonUtility.ToJson(q1.Gear.GetState()), "el equipo ganado viaja al piso siguiente");
            yield break;
        }

        [UnityTest]
        public IEnumerator Minas_AgotarOportunidadesEsDerrotaPeroLaPartidaSigue()
        {
            var ch = Begin<MinefieldChallenge>("minefield");
            var B = ch.Board;
            var p1 = G.Players[0];
            var mines = new List<int>();
            for (int i = 0; i < B.Cells.Length; i++) if (B.Cells[i].Mine) mines.Add(i);
            var off = ch.Center(0, B.Rows - 1);
            PutU(p1, 2f, off.y - 4f);   // lejos de las explosiones
            for (int k = 0; k < 3; k++) ch.RequestReveal(p1, mines[k] % B.Cols, mines[k] / B.Cols, "probe");
            Assert.AreEqual("failed", ch.Status);
            Assert.AreEqual(0, G.RewardService.Log.Count(l => l.Key.Contains(":win")), "sin botín al fallar");
            Step(110);
            Assert.AreEqual("result", G.Bonus.Phase);
            Assert.AreEqual("DESAFÍO FALLIDO", G.Ui.BonusTagText);
            TapAll("Space", "Enter"); Step(1);
            Assert.AreEqual(GameState.Play, G.State);
            Assert.IsTrue(G.InShop);
            Assert.AreEqual(2, G.Shop.Next);
            Assert.IsTrue(G.Players.All(p => !p.Dead));
            yield break;
        }

        [UnityTest]
        public IEnumerator Minas_SiCaenTodosElDesafioFallaPeroSeLevantanConVida()
        {
            var ch = Begin<MinefieldChallenge>("minefield");
            foreach (var p in G.Players) p.Hurt(999, p.X, p.Z, 0, new HurtOpts { Raw = true, Pierce = true });
            Step(200);
            Assert.AreEqual("failed", ch.Status);
            Assert.AreEqual("result", G.Bonus.Phase);
            Assert.AreEqual(GameState.Bonus, G.State, "no hay pantalla de derrota");
            TapAll("Space", "Enter"); Step(1);
            Assert.AreEqual(GameState.Play, G.State);
            foreach (var p in G.Players) { Assert.IsFalse(p.Dead); Assert.AreEqual(BonusConfig.ReviveHp, p.Hp); }
            yield break;
        }

        [UnityTest]
        public IEnumerator Minas_LasPantallasDeIntroYResultadoAvanzanSolas()
        {
            EnterBonus("minefield");
            Step(Mathf.CeilToInt(BonusConfig.IntroTimeout * 60f) + 5);
            G.Bonus.Challenge.Finish(false, "prueba");
            Step(110 + Mathf.CeilToInt(BonusConfig.ResultTimeout * 60f) + 5);
            Assert.AreEqual(GameState.Play, G.State, "sin que nadie confirme, se sigue bajando");
            Assert.IsFalse(G.InBonus);
            yield break;
        }

        [UnityTest]
        public IEnumerator Reintentar_RestauraElEquipoDelInicioDelPisoSinDuplicar()
        {
            G.Settings.BonusMode = "never";
            StartRun(1);
            var p = G.Players[0];
            G.RewardService.ClaimReward(p, "item_gloves", "test:a");
            var second = G.RewardService.ClaimReward(p, "item_gloves", "test:a");
            Assert.IsFalse(second.Ok);
            Assert.IsTrue(second.Dup, "reclamar dos veces se rechaza");
            G.FloorStart = G.Players.Select(q => new CarryState { Item = q.Weapon.GetState(), Hp = 100, Gear = q.Gear.GetState() }).ToArray();
            G.RewardService.ClaimReward(p, "armor_chain", "test:b");
            p.Hurt(999, p.X, p.Z, 0, new HurtOpts { Raw = true, Pierce = true });
            Step(120);
            Assert.AreEqual(GameState.Dead, G.State);
            G.Retry(); Step(2);
            var q1 = G.Players[0];
            Assert.IsTrue(q1.Gear.Has("item_gloves"), "conserva lo que tenía al empezar el piso");
            Assert.IsNull(q1.Gear.ArmorId, "pierde lo ganado después");
            yield break;
        }

        [UnityTest]
        public IEnumerator Equipo_ArmaduraEnLaVidaSobreLaCabezaYBrujulaHaciaLaSalida()
        {
            G.Settings.BonusMode = "never";
            StartRun(2);
            var p2 = G.Players[1];
            p2.Hp = 100f; p2.Iframes = 0f; p2.Hurt(40, p2.X + 1f, p2.Z, 0);
            G.RewardService.ClaimReward(p2, "armor_chain", "test:hp");
            Step(2);
            Assert.IsTrue(G.Ui.HeadShown(1));
            Assert.Greater(p2.Gear.ArmorFraction(), 0f, "línea azul de armadura");
            Assert.IsTrue(p2.Parts.Armor.childCount > 0, "piezas de armadura en el muñeco");
            // Brújula maldita: flecha hacia la salida
            var p1 = G.Players[0];
            G.RewardService.ClaimReward(p1, "item_compass", "test:compass");
            Step(2);
            Assert.IsTrue(p1.CompassVisible);
            float want = Mathf.Atan2(G.Dungeon.ExitX - p1.X, G.Dungeon.ExitZ - p1.Z);
            Assert.Less(Mathf.Abs(MathUtil.AngleDiff(p1.CompassYaw, want)), 0.05f);
            yield break;
        }

        // ---------- Arena ----------
        [UnityTest]
        public IEnumerator Arena_ElAnilloAvisaLuegoQuemaYAguantarDa2Botines()
        {
            var ch = Begin<EchoArenaChallenge>("arena");
            Player p1 = G.Players[0], p2 = G.Players[1];
            float ctr = Grid.X(EchoArenaChallenge.Center), ctz = Grid.Z(EchoArenaChallenge.Center);
            System.Action keep = () => { foreach (var e in G.Enemies) e.Stun = 5f; PutU(p1, ctr, ctz); PutU(p2, ctr + 1f, ctz); };
            for (int i = 0; i < BonusConfig.Arena.ShrinkEvery * 60 + 10; i++) { keep(); Step(1); }
            Assert.Greater(ch.Hazard.Count(HazardFloor.CellState.Warn), 0, "primero aviso");
            Assert.AreEqual(0, ch.Hazard.Count(HazardFloor.CellState.Danger), "sin quemar todavía");
            for (int i = 0; i < 160; i++) { keep(); Step(1); }
            Assert.Greater(ch.Hazard.Count(HazardFloor.CellState.Danger), 0, "luego quema");
            // Pisar la lava hace daño
            var edge = ch.FloorTiles.First(t => EchoArenaChallenge.Ring(t.x, t.y) == 6);
            p1.Iframes = 0f;
            float hp0 = p1.Hp;
            for (int i = 0; i < 40; i++) { PutU(p1, Grid.X(edge.x), Grid.Z(edge.y)); PutU(p2, ctr, ctz); foreach (var e in G.Enemies) e.Stun = 5f; Step(1); }
            Assert.Greater(hp0 - p1.Hp, 0f, "la lava hace daño");
            p1.Hp = 100f; p2.Hp = 100f;
            int guard = 0;
            while (ch.Status == "running" && guard++ < 4000) { keep(); p1.Hp = p2.Hp = 100f; Step(1); }
            Assert.AreEqual("success", ch.Status);
            Assert.AreEqual(2, RewardsOf(0, ":time"));
            Assert.AreEqual(2, RewardsOf(1, ":time"), "dos botines por aguantar todo");
            Leave();
            yield break;
        }

        // ---------- Último Aliento ----------
        [UnityTest]
        public IEnumerator UltimoAliento_ZonasCambianGasQuemaFueraYElUltimoRecibeExtra()
        {
            var ch = Begin<LastBreathChallenge>("breath");
            Player p1 = G.Players[0], p2 = G.Players[1];
            System.Action inZone = () => { var z = ch.Zones[0]; PutU(p1, Grid.X(z.Tx) + 1f, Grid.Z(z.Tz) - 1f); };
            string zones0 = string.Join(";", ch.Zones.Select(z => z.Tx + "," + z.Tz));
            float hp0 = p2.Hp;
            System.Action outside = () =>
            {
                var d = G.Dungeon;
                var t = ch.Tiles.First(k => !LastBreathChallenge.InZones(k.x, k.y, ch.Zones) && d.Blocked[d.Idx(k.x, k.y)] == 0);
                PutU(p2, Grid.X(t.x), Grid.Z(t.y));
            };
            for (int i = 0; i < 4 * 60; i++) { inZone(); outside(); Step(1); }
            Assert.Greater(hp0 - p2.Hp, 0f, "el gas quema fuera de las zonas");
            Assert.AreEqual(0f, 100f - p1.Hp, "dentro de la zona no hay daño");
            for (int i = 0; i < 4 * 60; i++) { inZone(); Step(1); }
            Assert.AreNotEqual(zones0, string.Join(";", ch.Zones.Select(z => z.Tx + "," + z.Tz)), "las zonas cambian");
            p2.Hurt(999, p2.X, p2.Z, 0, new HurtOpts { Raw = true, Pierce = true });
            int guard = 0;
            while (ch.Status == "running" && guard++ < 4000) { inZone(); p1.Hp = 100f; p1.Iframes = 1f; Step(1); }
            Assert.AreEqual("success", ch.Status);
            string keys = string.Join(",", G.RewardService.Log.Where(l => l.Player == 0).Select(l => l.Key.Split(':').Last()));
            StringAssert.Contains("alive0", keys, "superviviente");
            StringAssert.Contains("last0", keys, "último en pie");
            Assert.AreEqual(0, G.RewardService.Log.Count(l => l.Player == 1), "el caído no recibe nada");
            Leave();
            yield break;
        }

        [UnityTest]
        public IEnumerator UltimoAliento_ElRodilloSeEsquivaSaltando()
        {
            var ch = Begin<LastBreathChallenge>("breath");
            Player p1 = G.Players[0], p2 = G.Players[1];
            System.Action safe = () => { var z = ch.Zones[0]; PutU(p2, Grid.X(z.Tx) + 1f, Grid.Z(z.Tz) - 1f); p2.Hp = 100f; };
            // Pasa un rodillo con p1 en el aire justo encima, y otro con p1 en el suelo
            for (int pass = 0; pass < 2; pass++)
            {
                bool jump = pass == 0, hit = false;
                ch.RollerT = 0f;
                int guard = 0;
                while (!ch.RollerRolling && guard++ < 600) { safe(); Step(1); }
                Assert.IsTrue(ch.RollerRolling, "el rodillo arranca");
                guard = 0;
                while (ch.RollerActive && guard++ < 600)
                {
                    safe();
                    var r = ch.RollerPos;
                    PutU(p1, r.x, r.y);
                    p1.Hp = 100f; p1.Iframes = 0f;
                    if (jump) { p1.JumpY = 1f; p1.JumpV = 0f; } else p1.EndJump();
                    Step(1);
                    hit |= ch.RolledOver(p1);
                }
                if (jump) Assert.IsFalse(hit, "saltando por encima no aplasta");
                else Assert.IsTrue(hit, "en el suelo sí aplasta");
            }
            Leave();
            yield break;
        }

        // ---------- Puente ----------
        [UnityTest]
        public IEnumerator Puente_SiempreHayRutaSeguraLaFragilCaeYCruzarDaBotin()
        {
            StartRun(2);
            int bad = 0;
            for (uint s = 1; s <= 200; s++)
            {
                var ctl = new BonusFloorController(G) { Seed = s };
                var t = new BridgeChallenge(ctl);
                t.BuildLevel(1); t.AssignFragile();
                if (!t.SafePathExists()) bad++;
            }
            Assert.AreEqual(0, bad, "tableros sin ruta segura");
            G.Bonus.ForceNext = "bridge"; G.Descend(); Step(1);
            TapAll("Space", "Enter");
            var ch = (BridgeChallenge)G.Bonus.Challenge;
            Player p1 = G.Players[0], p2 = G.Players[1];
            // J2 pisa una losa frágil y se queda quieto: cae
            var f = ch.Platforms.First(p => p.Fragile);
            for (int i = 0; i < 70; i++) { PutU(p2, Grid.X(f.Tx), Grid.Z(f.Tz)); Step(1); if (ch.FallingCount > 0 || ch.IsOut(p2)) break; }
            Assert.AreEqual("gone", f.State, "la losa frágil cae");
            Assert.IsTrue(ch.IsOut(p2), "quien estaba encima queda fuera");
            Step(70);
            Assert.Less(p2.Hp, 100f, "la caída duele");
            Assert.IsTrue(p2.Spectator);
            // J1 cruza por una ruta segura (BFS) casilla a casilla
            System.Func<int, int, BridgeChallenge.Platform> plat = (c, r) => ch.Platforms.FirstOrDefault(p => p.C == c && p.R == r);
            System.Func<int, int, bool> walk = (c, r) => c >= 0 && c < ch.Cols && r >= 0 && r < ch.GridRows && (plat(c, r) == null || (!plat(c, r).Fragile && plat(c, r).State != "gone"));
            var prev = new Dictionary<Vector2Int, Vector2Int?>();
            var q = new Queue<Vector2Int>();
            for (int c = 0; c < ch.Cols; c++) if (walk(c, ch.GridRows - 1)) { var s0 = new Vector2Int(c, ch.GridRows - 1); q.Enqueue(s0); prev[s0] = null; }
            Vector2Int? end = null;
            int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                var n = q.Dequeue();
                if (n.y == 0) { end = n; break; }
                for (int k = 0; k < 4; k++)
                {
                    var m = new Vector2Int(n.x + dc[k], n.y + dr[k]);
                    if (walk(m.x, m.y) && !prev.ContainsKey(m)) { prev[m] = n; q.Enqueue(m); }
                }
            }
            Assert.IsTrue(end.HasValue, "hay ruta");
            var path = new List<Vector2Int>();
            for (Vector2Int? n = end; n.HasValue; n = prev[n.Value]) path.Insert(0, n.Value);
            PutU(p1, Grid.X(1 + path[0].x), Grid.Z(BridgeChallenge.Z0 + ch.GridRows)); Step(2);
            foreach (var n in path) for (int i = 0; i < 8; i++) { PutU(p1, Grid.X(1 + n.x), Grid.Z(BridgeChallenge.Z0 + n.y)); Step(1); }
            for (int i = 0; i < 8; i++) { PutU(p1, Grid.X(1 + path[path.Count - 1].x), Grid.Z(BridgeChallenge.Z0 - 1)); Step(1); }
            Assert.AreEqual("success", ch.Status);
            Assert.AreEqual(1, ch.Finished.Count);
            string keys = string.Join(",", G.RewardService.Log.Where(l => l.Player == 0).Select(l => l.Key.Split(':').Last()));
            StringAssert.Contains("cross", keys, "botín por cruzar");
            StringAssert.Contains("first0", keys, "y por ser el primero");
            Leave();
            yield break;
        }

        // ---------- Botín Maldito ----------
        [UnityTest]
        public IEnumerator Botin_UnCofreLoAbreUnoExaminarSeComparteAperturasLimitadas()
        {
            var ch = Begin<CursedLootChallenge>("loot");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var it = ch.Items.First(x => x.Kind == "normal");
            it.Content = "shared";
            PutU(p1, it.X - 1.2f, it.Z - 0.3f); PutU(p2, it.X + 1.2f, it.Z - 0.3f);
            Face(p1, it.X, it.Z); Face(p2, it.X, it.Z);
            // Los dos mantienen INTERACTUAR a la vez
            HoldAll(Mathf.CeilToInt(ch.OpenSeconds * 60f) + 4, "KeyE", "ShiftRight");
            Assert.AreEqual(1, ch.Events.Count(e => e.Type == "open" && e.Chest == it.Id), "un solo jugador abre el cofre");
            Assert.AreEqual(1, RewardsOf(0, ":item" + it.Id));
            Assert.AreEqual(1, RewardsOf(1, ":item" + it.Id), "el cofre compartido da algo a cada uno, una vez");
            Assert.IsFalse(ch.RequestOpen(p2, it), "no se abre dos veces");
            // Examinar: la pista queda a la vista de todos
            var it2 = ch.Items.First(x => x.Kind == "normal" && x.State == "closed");
            PutU(p1, it2.X - 1.2f, it2.Z); Face(p1, it2.X, it2.Z);
            HoldAll(Mathf.CeilToInt(ch.ExamineSeconds * 60f) + 4, "Space");
            Assert.IsTrue(it2.Examined);
            Assert.IsNotNull(it2.Label, "la pista se muestra a todos");
            Assert.IsTrue(G.Ui.HasLabel(it2.Hint));
            Assert.IsNull(p1.Attack, "examinar no lanza golpes");
            // Gastar aperturas: el desafío acaba
            foreach (var x in ch.Items)
            {
                if (x.State != "closed") continue;
                x.Content = "reward";
                var who = ch.OpensLeft(p1) > 0 ? p1 : ch.OpensLeft(p2) > 0 ? p2 : null;
                if (who == null) break;
                ch.RequestOpen(who, x);
            }
            Step(2);
            Assert.AreEqual("success", ch.Status, "acaba al gastar las aperturas");
            Leave();
            yield break;
        }

        // ---------- Banquete ----------
        [UnityTest]
        public IEnumerator Banquete_ElFestinPremiaAQuienComeMaldiceAlRestoYLaNotaRevela()
        {
            var ch = Begin<FeastChallenge>("feast");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var clue = ch.Items.First(x => x.Kind == "normal");
            clue.Content = "clue";
            ch.RequestOpen(p2, clue);
            Assert.GreaterOrEqual(ch.Items.Count(x => x != clue && x.Label != null && x.Label.Alive && x.State == "closed" && x.Kind == "normal"), 1, "la nota revela platos");
            var sp = ch.Items.First(x => x.Kind == "special");
            float hp2 = p2.Hp;
            ch.RequestOpen(p1, sp);
            Assert.AreEqual(2, G.RewardService.Log.Count(l => l.Player == 0), "doble premio para quien come el festín");
            Assert.IsTrue(G.RewardService.Log.Any(l => l.Player == 1 && l.Reward == "curse_frail"), "maldición para el resto");
            Assert.AreEqual(20f, hp2 - p2.Hp);
            Assert.AreEqual(2, ch.LivingMonsters(), "y aparecen monstruos");
            Leave();
            yield break;
        }
    }
}
