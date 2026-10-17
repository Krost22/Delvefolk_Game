using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // TNT en la mano, modo solitario y premio del Crupier
    public class TntTests : BossTestBase
    {
        // Sala del Crupier quieto: la fila 12 está despejada de punta a punta
        CrupierBoss Arena(int players = 2)
        {
            BossConfig.DemoFinalBoss = BossConfig.DemoBossDefault;
            var b = (CrupierBoss)StartBoss("crupier", players).BossList[0];
            b.Patterns.Clear();
            b.HoldStill = true;
            return b;
        }

        Barrel Tnt(float x, float z)
        {
            var t = new Barrel(G, x, z, true);
            G.Barrels.Add(t);
            return t;
        }

        int Frames(float s) { return Mathf.CeilToInt(s * 60f); }

        [UnityTest]
        public IEnumerator Tnt_SeCogeConEArdeTresSegundosYExplotaEnLaMano()
        {
            Arena(1);
            var p = G.Players[0];
            PutU(p, Grid.X(5), Grid.Z(12));
            var t = Tnt(Grid.X(5) + 1.2f, Grid.Z(12));
            Step(2);
            Assert.AreEqual("Coger la TNT (¡3 s!)", G.Ui.PromptOf(0));
            Tap("KeyE");
            Assert.AreEqual(t, p.CarryingTnt, "la lleva en la mano");
            Assert.IsFalse(t.Body.Enabled);
            Assert.AreEqual(Barrel.HandFuse, t.Fuse, 0.05f);
            Step(1);
            Assert.IsTrue(G.Ui.HasLabel("3"), "cuenta atrás");
            Step(Frames(Barrel.HandFuse - 0.3f));
            Assert.IsFalse(t.Dead, "aún no");
            Assert.Greater(t.Root.position.y, 1.5f, "sobre la cabeza");
            Step(Frames(0.5f));
            Assert.IsTrue(t.Dead, "a los 3 s explota");
            Assert.IsNull(p.CarryingTnt);
            Assert.Less(p.Hp, 100f, "le explota en la mano");
            yield break;
        }

        [UnityTest]
        public IEnumerator Tnt_SeLanzaLejosYExplotaContraUnEnemigo()
        {
            Arena(1);
            var p = G.Players[0];
            PutU(p, Grid.X(4), Grid.Z(12));
            var e = G.SpawnEnemy('G', Grid.X(4) + 9f, Grid.Z(12));
            e.Wake(false);
            float hp0 = e.Hp;
            var t = Tnt(p.X, p.Z);
            p.GrabTnt(t);
            Step(10);
            p.Facing = Mathf.PI / 2f;   // hacia +X
            Tap("KeyX");
            Assert.IsNull(p.CarryingTnt, "lanzada");
            Assert.IsTrue(t.FlyT >= 0f, "vuela");
            int guard = 0;
            float maxD = 0f;
            while (!t.Dead && guard++ < 240) { Step(1); if (!t.Dead) maxD = Mathf.Max(maxD, MathUtil.Hypot(t.X - p.X, t.Z - p.Z)); }
            Assert.IsTrue(t.Dead, "explota al chocar");
            Assert.Less(guard, Frames(Barrel.HandFuse), "por el choque, no por la mecha");
            Assert.Greater(maxD, 5f, "llega lejos");
            Assert.Less(e.Hp, hp0, "daña al enemigo");
            Assert.AreEqual(100f, p.Hp, "quien la lanza, a salvo");
            yield break;
        }

        [UnityTest]
        public IEnumerator Tnt_LanzadaContraUnCompanero()
        {
            Arena(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            PutU(p1, Grid.X(4), Grid.Z(12));
            PutU(p2, Grid.X(4) + 7f, Grid.Z(12));
            var t = Tnt(p1.X, p1.Z);
            p1.GrabTnt(t);
            Step(5);
            p1.Facing = Mathf.PI / 2f;
            Tap("KeyX");
            int guard = 0;
            while (!t.Dead && guard++ < 240) { PutU(p2, Grid.X(4) + 7f, Grid.Z(12)); Step(1); }
            Assert.IsTrue(t.Dead);
            Assert.Less(p2.Hp, 100f, "al compañero le explota encima");
            Assert.AreEqual(100f, p1.Hp);
            yield break;
        }

        [UnityTest]
        public IEnumerator Tnt_SoltarConESigueArdiendoYNoSeCogeEnElAire()
        {
            Arena(1);
            var p = G.Players[0];
            PutU(p, Grid.X(5), Grid.Z(12));
            var t = Tnt(p.X, p.Z);
            p.GrabTnt(t);
            Step(30);
            Tap("KeyE");
            Assert.IsNull(p.CarryingTnt, "la suelta");
            Assert.IsTrue(t.Body.Enabled);
            Assert.Greater(t.Fuse, 0f, "la mecha sigue");
            Assert.IsFalse(t.CanBeGrabbed && t.FlyT >= 0f);
            PutU(p, Grid.X(12), Grid.Z(12));   // se aparta
            Step(Frames(Barrel.HandFuse));
            Assert.IsTrue(t.Dead, "explota sola");
            Assert.AreEqual(100f, p.Hp);
            // Un barril normal no se coge
            var b = new Barrel(G, p.X + 1f, p.Z, false); G.Barrels.Add(b);
            Step(2);
            Assert.IsFalse(b.CanBeGrabbed);
            Assert.AreNotEqual("Coger la TNT (¡3 s!)", G.Ui.PromptOf(0));
            yield break;
        }

        // ---------- Modo solitario ----------
        [UnityTest]
        public IEnumerator Solo_MasDificilYNieblaDensa()
        {
            G.Settings.bonusMode = "never";
            SoloConfig.Enabled = true;
            StartRun(1);
            Assert.IsTrue(G.SoloHard);
            var p = G.Players[0];
            var hard = G.SpawnEnemy('G', p.X, p.Z + 3f);
            p.Iframes = 0f; p.Hp = 100f;
            p.Hurt(20f, p.X, p.Z + 1f, 0f);
            float soloDmg = 100f - p.Hp;
            Step(30);
            Assert.Less(RenderSettings.fogStartDistance, 24f, "la niebla empieza más cerca");
            Assert.Less(RenderSettings.fogEndDistance, 40f);
            SoloConfig.Enabled = false;
            Assert.IsFalse(G.SoloHard);
            var normal = G.SpawnEnemy('G', p.X, p.Z + 3f);
            Assert.AreEqual(Mathf.Round(normal.MaxHp * SoloConfig.EnemyHpMult), hard.MaxHp, "enemigos con más vida");
            p.Iframes = 0f; p.Hp = 100f;
            p.Hurt(20f, p.X, p.Z + 1f, 0f);
            Assert.Greater(soloDmg, 100f - p.Hp, "pega más fuerte");
            Step(30);   // pasa la pausa de impacto del golpe
            Assert.GreaterOrEqual(RenderSettings.fogStartDistance, 24f, "sin la niebla del modo solo");
            yield break;
        }

        [UnityTest]
        public IEnumerator Solo_ConDosJugadoresNoHayNiebla()
        {
            G.Settings.bonusMode = "never";
            SoloConfig.Enabled = true;
            StartRun(2);
            Assert.IsFalse(G.SoloHard);
            Step(2);
            Assert.GreaterOrEqual(RenderSettings.fogStartDistance, 24f);
            yield break;
        }

        // ---------- Premio del Crupier ----------
        [UnityTest]
        public IEnumerator Crupier_AlVencerLluviaDeMonedasYUnaTiradaDePremioPorJugador()
        {
            var b = Arena(2);
            b.Die("hit");
            Step(2);
            var prize = b.Prize;
            Assert.IsNotNull(prize, "aparece el premio");
            Step(Frames(CrupierPrize.RainTime + 1.5f));
            Assert.IsFalse(prize.Raining);
            Assert.AreEqual(CrupierPrize.RainCoins, prize.Landed, "llueven todas las monedas");
            Assert.Greater(prize.CoinsDropped, CrupierPrize.RainCoins / 2, "y quedan en el suelo");
            Player p1 = G.Players[0], p2 = G.Players[1];
            // J1 gira desde la corona con {E}
            PutU(p1, prize.Center.x + 1f, prize.Center.z);
            PutU(p2, prize.Center.x - 6f, prize.Center.z + 1f);
            Step(2);
            Assert.AreEqual("Girar la ruleta del premio", G.Ui.PromptOf(0));
            Tap("KeyE");
            Assert.IsTrue(prize.Spun.Contains(p1));
            Assert.IsFalse(prize.TrySpin(p1), "una sola vez");
            Assert.IsFalse(prize.TrySpin(p2), "no mientras gira");
            int guard = 0;
            while (prize.Roulette.State != RouletteState.Cooldown && guard++ < 1000) Step(1);
            Assert.AreEqual(1, prize.Roulette.AbilitiesFired, "un premio");
            Assert.IsTrue(G.RewardService.Log.Any(l => l.Player == 0 && l.Key.Contains("crupier:premio")), "premio para J1");
            Assert.AreEqual(0, prize.Roulette.Mismatches);
            while (!prize.Roulette.IsIdle && guard++ < 2000) Step(1);
            Assert.IsTrue(prize.TrySpin(p2), "J2 también gira");
            while (prize.Roulette.State != RouletteState.Cooldown && guard++ < 3000) Step(1);
            Assert.IsTrue(G.RewardService.Log.Any(l => l.Player == 1 && l.Key.Contains("crupier:premio")), "premio para J2");
            Assert.IsFalse(prize.HasSpinsLeft());
            yield break;
        }
    }
}
