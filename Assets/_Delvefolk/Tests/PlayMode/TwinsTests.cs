using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Grum y Grom, los Gemelos Encadenados (port de tests/boss-twins.test.js)
    public class TwinsTests : BossTestBase
    {
        BossFloor bf; GrumBoss grum; GromBoss grom; Player p1, p2;

        void Setup(bool freeze = true)
        {
            bf = StartBoss("twins");
            grum = bf.BossList.OfType<GrumBoss>().First(); grom = bf.BossList.OfType<GromBoss>().First();
            p1 = G.Players[0]; p2 = G.Players[1];
            if (freeze) foreach (var b in new Boss[] { grum, grom }) { b.PatternCd = 1e9f; b.HoldStill = true; }
        }

        [UnityTest]
        public IEnumerator Cadena_TensaDanaAlQueEstaEnElSueloNoAlQueSalta()
        {
            Setup();
            Put(grum, 10, 16); Put(grom, 20, 16);
            Put(p1, 14, 16); Put(p2, 17, 16);
            for (int i = 0; i < 60; i++) { p1.Iframes = 0; p2.Iframes = 0; p2.JumpY = 1; p2.JumpV = 0; p2.Body.Airborne = true; Step(1); }
            Assert.IsTrue(grum.ChainTense, "la cadena está tensa");
            Assert.Less(p1.Hp, 100f, "el que va por el suelo recibe daño");
            Assert.AreEqual(100f, p2.Hp, "el que salta se libra");
            yield break;
        }

        [UnityTest]
        public IEnumerator Cadena_FlojaNoDana()
        {
            Setup();
            Put(grum, 12, 16); Put(grom, 15, 16);
            Put(p1, 13.5f, 16);
            for (int i = 0; i < 60; i++) { p1.Iframes = 0; Step(1); }
            Assert.IsFalse(grum.ChainTense);
            Assert.AreEqual(100f, p1.Hp);
            yield break;
        }

        [UnityTest]
        public IEnumerator Escudo_ReduceElDanoAGrumYGolpearAGromLoInterrumpe()
        {
            Setup();
            Put(grum, 10, 16); Put(grom, 14, 16); Put(p1, 26, 24); Put(p2, 27, 24);
            grom.CancelPattern(); grom.StartPattern(Pattern(grom, "escudo"));
            Step(60);
            Assert.IsTrue(grom.Channeling, "Grom está canalizando"); Assert.IsFalse(grom.ShieldOn, "sin escudo mientras canaliza");
            Step(150);
            Assert.IsTrue(grom.ShieldOn, "tras 3 s, escudo activo"); Assert.IsTrue(grum.BubbleVisible, "Grum muestra la burbuja");
            float h0 = grum.Hp; grum.TakeHit(By(p1, 50, 0, 1));
            Assert.That(h0 - grum.Hp, Is.InRange(6f, 9f), "el golpe de 50 se queda en ~8 (-85 %)");
            grom.ShieldOn = false; grom.ShieldT = 0; grom.CancelPattern(); grom.StartPattern(Pattern(grom, "escudo"));
            Step(60);
            grom.TakeHit(By(p1, 5, 0, 1));
            Assert.IsTrue(!grom.Channeling && grom.Current == null, "el golpe interrumpe la canalización");
            Assert.GreaterOrEqual(grom.Stun, 0.9f, "Grom aturdido ~1 s");
            Assert.IsFalse(grom.ShieldOn);
            grom.PatternCd = 1e9f;
            Step(60 * 4);
            Assert.IsFalse(grom.ShieldOn, "y no se activa después");
            yield break;
        }

        [UnityTest]
        public IEnumerator Bolas_SonDevolviblesYDanAGrum()
        {
            Setup();
            Put(grum, 8, 14); Put(grom, 16, 14); Put(p1, 27, 25); Put(p2, 29, 25);
            grom.CancelPattern(); grom.StartPattern(Pattern(grom, "bolas"));
            Step(40);
            var bolts = G.Projectiles.List.Where(p => p.Reflectable && p.Hostile).ToList();
            Assert.AreEqual(3, bolts.Count, "tres bolas en abanico (fase 1)");
            var pr = bolts[0];
            float l = MathUtil.Hypot(pr.Vx, pr.Vz);
            var h = Hit.Of(0, 0, 0, -pr.Vx / l, -pr.Vz / l, p1);
            pr.TakeHit(h);
            Assert.IsFalse(pr.Hostile, "un golpe de jugador la devuelve");
            float h0 = grum.Hp;
            PutU(grum, pr.PX, pr.PZ);
            Step(3);
            Assert.IsTrue(pr.Dead, "la bola devuelta se consume al golpear");
            Assert.Greater(h0 - grum.Hp, 0f, "la bola devuelta daña a Grum");
            yield break;
        }

        [UnityTest]
        public IEnumerator Vinculo_SiCaeUnoElOtroSeEnfureceYSiNoLoMataEn8sRevive()
        {
            Setup();
            Put(grum, 10, 16); Put(grom, 14, 16); Put(p1, 26, 24); Put(p2, 27, 24);
            grom.TakeHit(By(p1, 999, 0, 1));
            Assert.IsTrue(grom.Dead); Assert.IsTrue(grom.HoldVictory, "el caído retiene la victoria");
            Assert.IsNotNull(grum.ReviveCd, "empieza la cuenta atrás"); Assert.IsTrue(grum.Furious);
            Assert.Greater(grum.Cfg.Speed, 2.6f);
            Step(60 * 7);
            Assert.IsTrue(grom.Dead); Assert.IsNotNull(grum.ReviveCd, "a los 7 s la cuenta sigue"); Assert.AreEqual("fight", bf.Phase);
            Step(60 * 2);
            var nb = bf.BossList.OfType<GromBoss>().FirstOrDefault(x => !x.Dead);
            Assert.IsNotNull(nb, "tras 8 s, el caído vuelve");
            Assert.AreEqual(MathUtil.RoundJs(nb.MaxHp * 0.4f), nb.Hp, "revive con el 40 %");
            Assert.IsTrue(nb.Twin == grum && grum.Twin == nb, "quedan enlazados de nuevo");
            Assert.IsFalse(nb.HoldVictory);
            Assert.IsNull(grum.ReviveCd, "la marca de la cuenta atrás se quita");
            Assert.IsFalse(grom.HoldVictory);
            Assert.AreEqual("fight", bf.Phase);
            Assert.AreEqual(2, bf.BossList.Count(x => !x.Dead));
            yield break;
        }

        [UnityTest]
        public IEnumerator Vinculo_MatarALosDosEnPlazoDaLaVictoria()
        {
            Setup();
            Put(grum, 10, 16); Put(grom, 14, 16); Put(p1, 26, 24); Put(p2, 27, 24);
            grom.TakeHit(By(p1, 999, 0, 1));
            Step(60 * 3);
            grum.TakeHit(By(p1, 999, 0, 1));
            Step(120);
            Assert.AreEqual("won", bf.Phase);
            Assert.IsTrue(bf.BossList.All(x => x.Dead));
            Assert.IsNull(grum.ReviveCd); Assert.IsFalse(grom.HoldVictory);
            yield break;
        }

        [UnityTest]
        public IEnumerator Barrido_LadosOpuestosYLuegoBajanJuntos()
        {
            Setup(false);
            Put(p1, 16, 26); Put(p2, 17, 26);
            grum.CancelPattern(); grum.StartPattern(Pattern(grum, "barrido"));
            Step(Mathf.RoundToInt(60 * 2.3f));
            Assert.IsNull(grom.Current, "Grom no ataca durante el barrido");
            float gx = grum.X, rx = grom.X, gz = -grum.Z, rz = -grom.Z;
            Assert.IsTrue(gx < 6 && rx > 26, "Grum a la izquierda y Grom a la derecha: " + gx + " / " + rx);
            Step(90);
            Assert.Greater(-grum.Z, gz + 4); Assert.Greater(-grom.Z, rz + 4);
            Assert.IsTrue(Mathf.Abs(grum.X - 4) < 1.5f && Mathf.Abs(grom.X - 28) < 1.5f, "mantienen su lado al bajar");
            Step(180);
            Assert.IsTrue(grum.Current == null || grum.Current.Id != "barrido", "el barrido termina");
            Assert.IsTrue(-grum.Z > 20 && -grom.Z > 20, "llegan al fondo de la arena");
            yield break;
        }

        [UnityTest]
        public IEnumerator Reintentar_RepiteElMismoJefeConVidaLlena()
        {
            Setup(false);
            foreach (var p in G.Players) p.Hurt(999, p.X, p.Z, 0, new HurtOpts { Raw = true, Pierce = true });
            Step(120);
            Assert.AreEqual(GameState.Dead, G.State);
            G.Retry(); Step(2);
            Assert.IsTrue(G.InBoss);
            Assert.AreEqual("twins", G.BossFloor.Entry.Id);
            Assert.IsTrue(G.BossFloor.BossList.All(x => x.Hp == x.MaxHp));
            Assert.AreEqual("intro", G.BossFloor.Phase);
            yield break;
        }
    }
}
