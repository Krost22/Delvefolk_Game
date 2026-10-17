using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Demo con un solo jefe final (El Crupier) y su ruleta con probabilidades configurables
    public class CrupierTests : BossTestBase
    {
        const float Dt = 1f / 60f;

        // Una habilidad de prueba que cuenta cuántas veces se activa
        sealed class CountingAbility : IRouletteAbility
        {
            public int Begins, Cancels; float t;
            public string Title { get { return "prueba"; } }
            public bool HoldsDealer { get { return t < 0.1f; } }
            public void Begin() { Begins++; t = 0f; }
            public bool Tick(float dt) { t += dt; return t >= 0.3f; }
            public void Cancel() { Cancels++; }
        }

        static RouletteConfig QuickCfg(params float[] w)
        {
            var c = ScriptableObject.CreateInstance<RouletteConfig>();
            for (int i = 0; i < w.Length; i++) c.Segments[i].Weight = w[i];
            c.PrepareTime = 0.2f; c.SpinDuration = 0.6f; c.RevealPause = 0.1f; c.Cooldown = 0.3f; c.LogResults = false;
            return c;
        }

        static void Run(RouletteController ctl, RouletteWheel wheel, RouletteState until, int max = 2000)
        {
            for (int i = 0; i < max && ctl.State != until; i++) { ctl.Tick(Dt); wheel.Update(Dt, 0f, 0f); }
            Assert.AreEqual(until, ctl.State, "la ruleta llega a " + until);
        }

        CrupierBoss Crupier(bool keepRoulettePattern = false)
        {
            BossConfig.DemoFinalBoss = BossConfig.DemoBossDefault;
            var bf = StartBoss("crupier");
            var b = (CrupierBoss)bf.BossList[0];
            b.Patterns.RemoveAll(p => !keepRoulettePattern || p.Id != "ruleta");
            b.HoldStill = true;
            return b;
        }

        void RunBoss(CrupierBoss b, RouletteState until, int max = 1500)
        {
            for (int i = 0; i < max && b.Roulette.State != until; i++) Step(1);
            Assert.AreEqual(until, b.Roulette.State, "la ruleta llega a " + until);
        }

        void Spin(CrupierBoss b, RouletteOutcome o)
        {
            b.Roulette.TestForce = o;
            Assert.IsTrue(b.Roulette.TrySpin());
            RunBoss(b, RouletteState.ApplyingEffect);
        }

        // ---------- Demo ----------
        [UnityTest]
        public IEnumerator Demo_SoloElCrupierYSoloTrasElUltimoPiso()
        {
            BossConfig.DemoFinalBoss = BossConfig.DemoBossDefault;
            G.Settings.bonusMode = "never";
            StartRun(2);
            int last = LevelData.Levels.Length;
            for (int f = 1; f <= last + 4; f++) Assert.AreEqual(f == last, G.BossDue(f), "jefe al bajar del piso " + f);
            G.LoadFloor(last, G.CollectCarry()); Step(2);
            var p = G.Players[0];
            PutU(p, G.Dungeon.ExitX, G.Dungeon.ExitZ - 1f); Step(2);
            Tap("KeyE"); Step(2);
            Assert.IsTrue(G.InBoss);
            Assert.AreEqual("crupier", G.BossFloor.Entry.Id);
            Assert.AreEqual(1, G.BossFloor.BossList.Count);
            Step(Mathf.CeilToInt(BossConfig.IntroTime * 60f) + 2);
            var b = (CrupierBoss)G.BossFloor.BossList[0];
            Assert.IsTrue(b.IsAwake);
            Assert.Greater(b.Casino.RedTiles.Count, 20, "suelo a cuadros rojos");
            b.Die("hit");
            Step(2);
            Assert.AreEqual("won", G.BossFloor.Phase);
            G.Descend(); Step(3);
            Assert.AreEqual(GameState.Win, G.State, "tras el jefe final llega la victoria");
            yield break;
        }

        [UnityTest]
        public IEnumerator Demo_SinModoDemoVuelvenLosJefesNormales()
        {
            BossConfig.DemoFinalBoss = null;
            G.Settings.bonusMode = "never";
            StartRun(1);
            Assert.IsTrue(G.BossDue(2));
            Assert.IsTrue(G.BossDue(4));
            G.EnterBoss(2, 3, G.CollectCarry(), null);
            Assert.AreEqual("golem", G.BossFloor.Entry.Id);
            yield break;
        }

        // ---------- Ruleta ----------
        [UnityTest]
        public IEnumerator Ruleta_SeDetieneEnElSegmentoElegidoYFrenaPocoAPoco()
        {
            var b = Crupier();
            b.Roulette.TestNoAbility = true;
            foreach (RouletteOutcome o in System.Enum.GetValues(typeof(RouletteOutcome)))
            {
                b.Roulette.TestForce = o;
                Assert.IsTrue(b.Roulette.TrySpin(), "tirada " + o);
                Assert.IsTrue(b.Wheel.Visible, "la rueda aparece");
                RunBoss(b, RouletteState.Spinning);
                var steps = new List<float>();
                float prev = b.Wheel.Forward;
                while (b.Roulette.State == RouletteState.Spinning) { Step(1); steps.Add(b.Wheel.Forward - prev); prev = b.Wheel.Forward; }
                Assert.AreEqual(RouletteState.Revealing, b.Roulette.State);
                Assert.AreEqual(o, b.Roulette.Result);
                Assert.AreEqual(b.Roulette.ResultIndex, b.Wheel.IndexUnderPointer(), "el indicador señala " + o);
                Assert.Greater(steps.Count, 120, "el giro dura");
                Assert.Greater(steps.Take(10).Average(), 20f * steps.Skip(steps.Count - 10).Average(), "frena al final");
                Assert.IsTrue(steps.All(s => s >= -1e-3f), "no gira hacia atrás");
                Assert.GreaterOrEqual(steps.Sum(), 360f * b.RCfg.MinTurns, "da vueltas completas");
                RunBoss(b, RouletteState.Idle);
            }
            Assert.AreEqual(0, b.Roulette.Mismatches);
            Assert.AreEqual(0, b.Roulette.AbilitiesFired, "probar sin habilidad no activa ninguna");
            yield break;
        }

        [UnityTest]
        public IEnumerator Ruleta_AngulosEjeSentidoEIndicadorConfigurables()
        {
            StartRun(1);
            var cfg = QuickCfg(10, 0, 30, 5, 20, 35);
            cfg.UniformAngles = false;
            float[] ang = { 10f, 75f, 150f, 200f, 260f, 320f };
            for (int i = 0; i < ang.Length; i++) cfg.Segments[i].AngleDeg = ang[i];
            cfg.SpinAxis = Vector3.up; cfg.ReferenceDir = Vector3.forward; cfg.Clockwise = false; cfg.PointerAngle = 135f;
            var wheel = new RouletteWheel(G, cfg, G.FloorRoot);
            var rng = new Rng(42u);
            var ctl = new RouletteController(cfg, wheel, rng.Next);
            ctl.TestNoAbility = true;
            var seen = new HashSet<RouletteOutcome>();
            for (int k = 0; k < 40; k++)
            {
                Assert.IsTrue(ctl.TrySpin());
                Run(ctl, wheel, RouletteState.Revealing);
                Assert.AreEqual(ctl.ResultIndex, wheel.IndexUnderPointer(), "tirada " + k);
                seen.Add(ctl.Result);
                Run(ctl, wheel, RouletteState.Idle);
            }
            Assert.IsFalse(seen.Contains(RouletteOutcome.Verde), "peso 0: nunca sale");
            Assert.GreaterOrEqual(seen.Count, 4);
            Assert.AreEqual(0, ctl.Mismatches);
            wheel.Destroy();
            Object.Destroy(cfg);
            yield break;
        }

        [UnityTest]
        public IEnumerator Ruleta_SinTiradasSimultaneasYUnaHabilidadPorTirada()
        {
            StartRun(1);
            var cfg = QuickCfg();
            var wheel = new RouletteWheel(G, cfg, G.FloorRoot);
            var ctl = new RouletteController(cfg, wheel, new Rng(3u).Next);
            var ab = new CountingAbility();
            foreach (RouletteOutcome o in System.Enum.GetValues(typeof(RouletteOutcome))) ctl.Abilities.Connect(o, ab);
            for (int k = 0; k < 5; k++)
            {
                Assert.IsTrue(ctl.TrySpin());
                foreach (var s in new[] { RouletteState.Spinning, RouletteState.Revealing, RouletteState.ApplyingEffect, RouletteState.Cooldown })
                {
                    Assert.IsFalse(ctl.TrySpin(), "no se puede girar en " + ctl.State);
                    Run(ctl, wheel, s);
                }
                Assert.IsFalse(ctl.TrySpin(), "ni durante el descanso");
                Run(ctl, wheel, RouletteState.Idle);
            }
            Assert.AreEqual(5, ab.Begins, "una habilidad por tirada");
            Assert.AreEqual(5, ctl.AbilitiesFired);
            wheel.Destroy();
            Object.Destroy(cfg);
            yield break;
        }

        [UnityTest]
        public IEnumerator Ruleta_SinHabilidadConectadaOTodoACeroNoSeBloquea()
        {
            StartRun(1);
            var cfg = QuickCfg();
            var wheel = new RouletteWheel(G, cfg, G.FloorRoot);
            var ctl = new RouletteController(cfg, wheel, new Rng(5u).Next);
            LogAssert.Expect(LogType.Warning, new Regex("no tiene habilidad conectada"));
            Assert.IsTrue(ctl.TrySpin());
            Run(ctl, wheel, RouletteState.Cooldown);
            Run(ctl, wheel, RouletteState.Idle);
            Assert.AreEqual(0, ctl.AbilitiesFired);
            // Todos los pesos a 0 (o negativos): no gira, avisa y vuelve a estar disponible tras el descanso
            foreach (var s in cfg.Segments) s.Weight = 0f;
            cfg.Segments[2].Weight = -4f;
            bool refused = false;
            ctl.Refused += () => refused = true;
            LogAssert.Expect(LogType.Warning, new Regex("Todos los pesos son 0"));
            Assert.IsFalse(ctl.TrySpin());
            Assert.IsTrue(refused);
            Assert.AreEqual(RouletteState.Cooldown, ctl.State);
            Run(ctl, wheel, RouletteState.Idle);
            wheel.Destroy();
            Object.Destroy(cfg);
            yield break;
        }

        [UnityTest]
        public IEnumerator Crupier_ElPatronRuletaRetieneAlJefeHastaQueTermina()
        {
            var b = Crupier(true);
            b.Roulette.TestForce = RouletteOutcome.Negro;
            int guard = 0;
            while (!b.Is("ruleta") && guard++ < 600) Step(1);
            Assert.IsTrue(b.Is("ruleta"), "el jefe elige la ruleta");
            Assert.AreEqual(RouletteState.Preparing, b.Roulette.State);
            while (b.Roulette.DealerBusy) { Assert.IsTrue(b.Is("ruleta")); Step(1); }
            Step(1);
            Assert.IsFalse(b.Is("ruleta"), "vuelve a su comportamiento normal");
            Assert.AreEqual(1, b.Roulette.AbilitiesFired);
            yield break;
        }

        [UnityTest]
        public IEnumerator Crupier_SiMuereAMitadDeTiradaSeCancelaTodo()
        {
            var b = Crupier();
            Spin(b, RouletteOutcome.Rojo);
            Assert.Greater(b.Casino.Hazard.Count(HazardFloor.CellState.Warn), 0, "el rojo avisa");
            b.Die("hit");
            Step(30);
            Assert.AreEqual(RouletteState.Disabled, b.Roulette.State);
            Assert.AreEqual(b.Casino.Hazard.CellCount, b.Casino.Hazard.Count(HazardFloor.CellState.Safe), "ya no quema");
            Assert.IsFalse(b.Wheel.Visible, "la rueda se va");
            Assert.IsFalse(b.Roulette.TrySpin());
            Assert.AreEqual("won", G.BossFloor.Phase);
            yield break;
        }

        // ---------- Habilidades ----------
        [UnityTest]
        public IEnumerator Rojo_QuemaLasCasillasRojasPeroNoLasNegras()
        {
            var b = Crupier();
            var d = G.Dungeon;
            var red = b.Casino.RedTiles.First(t => d.TypeAt(t.x + 1, t.y) == TileType.Floor && d.Blocked[d.Idx(t.x + 1, t.y)] == 0
                && MathUtil.Hypot(Grid.X(t.x) - b.X, Grid.Z(t.y) - b.Z) > 5f);
            Player p1 = G.Players[0], p2 = G.Players[1];
            System.Action hold = () => { PutU(p1, Grid.X(red.x), Grid.Z(red.y)); PutU(p2, Grid.X(red.x + 1), Grid.Z(red.y)); };
            hold();
            Spin(b, RouletteOutcome.Rojo);
            for (int i = 0; i < Mathf.CeilToInt((HouseBurnsAbility.Warn + 1.5f) * 60f); i++) { hold(); Step(1); }
            Assert.Less(p1.Hp, 100f, "en rojo quema");
            Assert.AreEqual(100f, p2.Hp, "en negro no");
            RunBoss(b, RouletteState.Cooldown);
            Assert.AreEqual(b.Casino.Hazard.CellCount, b.Casino.Hazard.Count(HazardFloor.CellState.Safe));
            yield break;
        }

        [UnityTest]
        public IEnumerator Verde_EscudoAbsorbeYAlRompersePasaDanoYAturde()
        {
            var b = Crupier();
            var p = G.Players[0];
            Spin(b, RouletteOutcome.Verde);
            Step(5);
            Assert.IsTrue(b.Shielded);
            float hp0 = b.Hp;
            b.TakeHit(By(p, 10, 0, 1));
            Assert.AreEqual(hp0, b.Hp, "el escudo para el golpe");
            Assert.AreEqual(b.ShieldMax - 10f, b.ShieldHp, 0.01f);
            b.TakeHit(By(p, b.ShieldMax, 0, 1));
            Assert.IsFalse(b.Shielded, "se rompe");
            Assert.Greater(b.Stun, 0f, "y le deja aturdido");
            b.TakeHit(By(p, 10, 0, 1));
            Assert.Less(b.Hp, hp0, "ya recibe daño");
            RunBoss(b, RouletteState.Cooldown);
            yield break;
        }

        [UnityTest]
        public IEnumerator Negro_TresRafagasDeFichasReflejables()
        {
            var b = Crupier();
            Spin(b, RouletteOutcome.Negro);
            var tax = (HouseTaxAbility)b.Roulette.Abilities.Get(RouletteOutcome.Negro);
            int maxLive = 0;
            while (b.Roulette.State == RouletteState.ApplyingEffect) { Step(1); maxLive = Mathf.Max(maxLive, G.Projectiles.List.Count); }
            Assert.AreEqual(15, tax.Fired);
            Assert.Greater(maxLive, 4);
            Assert.IsTrue(G.Projectiles.List.All(pr => pr.Reflectable));
            yield break;
        }

        [UnityTest]
        public IEnumerator Dorado_FichasConPremioQueSeRecogen()
        {
            var b = Crupier();
            Spin(b, RouletteOutcome.Dorado);
            Assert.AreEqual(4, b.Casino.Chips.Count, "cuatro fichas");
            var p = G.Players[0];
            int n = b.Casino.Chips.Count;
            foreach (var chip in b.Casino.Chips.ToArray())
            {
                p.Hp = 50f; p.HasteT = 0f; p.Iframes = 0f;
                int gold0 = G.RewardService.Log.Count(l => l.Key.Contains("crupier:ficha"));
                PutU(p, chip.X, chip.Z); Step(2);
                Assert.AreEqual(--n, b.Casino.Chips.Count, "se recoge");
                if (chip.Kind == "heal") Assert.Greater(p.Hp, 50f);
                else if (chip.Kind == "haste") Assert.Greater(p.HasteT, 0f);
                else if (chip.Kind == "guard") Assert.Greater(p.Iframes, 2f);
                else Assert.AreEqual(gold0 + 1, G.RewardService.Log.Count(l => l.Key.Contains("crupier:ficha")));
            }
            Step(40);
            Assert.AreEqual(RouletteState.Cooldown, b.Roulette.State, "sin fichas, termina");
            yield break;
        }

        [UnityTest]
        public IEnumerator Azul_MesasConColisionQueDesaparecen()
        {
            var b = Crupier();
            Spin(b, RouletteOutcome.Azul);
            Step(70);
            Assert.Greater(b.Casino.Tables.Count, 2, "suben mesas");
            var bodies = b.Casino.Tables.Select(t => t.Body).ToList();
            Assert.IsTrue(bodies.All(x => G.Physics.Bodies.Contains(x) && x.IsStatic));
            Step(Mathf.CeilToInt(TableChangeAbility.Life * 60f) + 10);
            Assert.AreEqual(0, b.Casino.Tables.Count);
            Assert.IsFalse(bodies.Any(x => G.Physics.Bodies.Contains(x)), "sin cuerpos sueltos");
            Assert.AreEqual(RouletteState.Cooldown, b.Roulette.State);
            yield break;
        }

        [UnityTest]
        public IEnumerator Morado_IlusionQueSeDeshaceDeUnGolpe()
        {
            var b = Crupier();
            Spin(b, RouletteOutcome.Morado);
            Step(2);
            var ill = b.Illusion;
            Assert.IsNotNull(ill);
            Assert.IsTrue(G.Enemies.Contains(ill));
            Assert.IsFalse(G.BossFloor.BossList.Contains(ill), "no cuenta para la barra ni para la victoria");
            float hp0 = b.Hp;
            ill.TakeHit(By(G.Players[0], 5, 0, 1));
            Assert.IsTrue(ill.Dead, "un golpe la deshace");
            Assert.AreEqual(hp0, b.Hp);
            Assert.IsFalse(b.Dead);
            Assert.AreNotEqual("won", G.BossFloor.Phase);
            Step(2);
            Assert.AreEqual(RouletteState.Cooldown, b.Roulette.State);
            yield break;
        }
    }
}
