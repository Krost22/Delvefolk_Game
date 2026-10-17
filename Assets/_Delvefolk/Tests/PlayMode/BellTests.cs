using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // La Campana del Juicio (port de tests/boss-bell.test.js). Centro (16, 15) del web; placas (6,16) y (26,16).
    public class BellTests : BossTestBase
    {
        BossFloor bf; BellBoss boss; Player p1, p2;

        void Begin()
        {
            bf = StartBoss("bell");
            boss = (BellBoss)bf.BossList[0];
            p1 = G.Players[0]; p2 = G.Players[1];
            boss.CancelPattern(); boss.PatternCd = 1e9f;
        }

        // Sin patrones salvo los que la prueba fuerza
        void Go(int n) { for (int i = 0; i < n; i++) { if (boss.Current == null) boss.PatternCd = 1e9f; Step(1); } }

        [UnityTest]
        public IEnumerator Cerrada_GolpesSinDanoYNoSeMueve()
        {
            Begin();
            Assert.AreEqual("closed", boss.Stage);
            Assert.IsTrue(boss.Invulnerable);
            Assert.AreEqual(16f, boss.X, 1e-4f); Assert.AreEqual(-15f, boss.Z, 1e-4f);
            float x0 = boss.X, z0 = boss.Z, hp0 = boss.Hp;
            boss.TakeHit(By(p1, 50, 0, 1));
            Assert.AreEqual(hp0, boss.Hp, "cerrada no recibe daño");
            Go(120);
            Assert.Less(MathUtil.Hypot(boss.X - x0, boss.Z - z0), 0.01f, "nunca se mueve");
            Assert.AreEqual("closed", boss.Stage);
            yield break;
        }

        [UnityTest]
        public IEnumerator DosPlacas_SeAbreRecibeDanoCierraA5sYPausaDe2s()
        {
            Begin();
            BellPlate A = boss.Plates[0], B = boss.Plates[1];
            PutU(p1, A.X, A.Z); PutU(p2, B.X, B.Z);
            int f = 0;
            while (boss.Stage == "closed" && f < 120) { Go(1); f++; }
            Assert.AreEqual("opening", boss.Stage);
            Assert.IsFalse(boss.Invulnerable);
            float hp0 = boss.Hp;
            boss.TakeHit(By(p1, 30, 0, 1));
            Assert.AreEqual(30f, hp0 - boss.Hp, "recibe daño con la campana abierta");
            int f2 = 0;
            while (boss.Stage != "closing" && f2 < 900) { Go(1); f2++; }
            Assert.That(f2 + 1, Is.InRange(290, 310), "se cierra a los ~5 s");
            Go(40);
            Assert.AreEqual("lockout", boss.Stage); Assert.IsTrue(boss.Invulnerable);
            Go(100);
            Assert.AreEqual("lockout", boss.Stage, "las placas no funcionan durante la pausa");
            Go(30);
            Assert.IsTrue(boss.Stage == "opening" || boss.Stage == "open", "tras la pausa vuelve a abrirse: " + boss.Stage);
            yield break;
        }

        [UnityTest]
        public IEnumerator Barril_EnUnaPlacaCuentaComoPisada()
        {
            Begin();
            BellPlate A = boss.Plates[0], B = boss.Plates[1];
            PutU(p2, B.X, B.Z);
            Go(90);
            Assert.AreEqual("closed", boss.Stage, "un jugador solo no abre");
            var bar = G.Barrels.Where(x => !x.Dead && !x.Removed).OrderBy(u => MathUtil.Hypot(u.X - A.X, u.Z - A.Z)).FirstOrDefault();
            Assert.IsNotNull(bar, "hay un barril cerca de la placa");
            PutU(bar, A.X, A.Z);
            Go(30);
            Assert.IsTrue(A.Pressed, "el barril pisa la placa");
            Assert.IsTrue(boss.IsOpen, "jugador + barril abren la campana: " + boss.Stage);   // aquí abre en el primer frame: a los 30 ya está "open"
            yield break;
        }

        [UnityTest]
        public IEnumerator Salto_SobreUnaPlacaNoLaPisa()
        {
            Begin();
            BellPlate A = boss.Plates[0], B = boss.Plates[1];
            PutU(p2, B.X, B.Z);
            for (int i = 0; i < 90; i++) { p1.JumpY = 1; p1.JumpV = 0; p1.Body.Airborne = true; PutU(p1, A.X, A.Z); Go(1); }
            Assert.IsFalse(A.Pressed, "saltando no pisa la placa");
            Assert.AreEqual("closed", boss.Stage);
            p1.JumpY = 0; p1.JumpV = 0; p1.Body.Airborne = false;
            PutU(p1, A.X, A.Z);
            Go(2);
            Assert.AreEqual("opening", boss.Stage, "al aterrizar sobre ella sí");
            yield break;
        }

        [UnityTest]
        public IEnumerator Tanido_OndasDananEnElSueloNoAlQueSalta()
        {
            Begin();
            p1.Hp = 100; p2.Hp = 100;
            Put(p1, 16, 19); Put(p2, 16, 11);
            boss.StartPattern(Pattern(boss, "tanido"));
            for (int i = 0; i < 150; i++)
            {
                p1.Iframes = 0; p2.Iframes = 0;
                p2.JumpY = 1.2f; p2.JumpV = 0; p2.Body.Airborne = true;
                if (boss.Current == null) boss.PatternCd = 1e9f;
                Step(1);
            }
            Assert.Less(p1.Hp, 100f, "la onda golpea a quien está en el suelo");
            Assert.AreEqual(100f, p2.Hp, "saltando se esquiva la onda");
            yield break;
        }

        [UnityTest]
        public IEnumerator Fase2_LasPlacasCambianDeSitioAlCerrar()
        {
            Begin();
            BellPlate A = boss.Plates[0], B = boss.Plates[1];
            var before = boss.Plates.Select(p => new Vector2(p.X, p.Z)).ToArray();
            boss.Hp = MathUtil.RoundJs(boss.MaxHp * 0.6f);
            PutU(p1, A.X, A.Z); PutU(p2, B.X, B.Z);
            Go(2);
            Assert.AreEqual("opening", boss.Stage);
            boss.TakeHit(By(p1, MathUtil.RoundJs(boss.MaxHp * 0.2f), 0, 1));
            Assert.AreEqual(2, boss.Phase);
            int f = 0;
            while (boss.Stage != "lockout" && f < 900) { Go(1); f++; }
            Assert.IsTrue(boss.Plates.Select((p, i) => p.X != before[i].x || p.Z != before[i].y).Any(m => m), "al cerrar, las placas cambian de sitio");
            Assert.IsTrue(Mathf.Abs(boss.Plates[0].X - boss.Plates[1].X) > 3 || Mathf.Abs(boss.Plates[0].Z - boss.Plates[1].Z) > 3, "siguen en lados opuestos");
            yield break;
        }

        [UnityTest]
        public IEnumerator Vencerla_AbiertaDaLaVictoria()
        {
            Begin();
            BellPlate A = boss.Plates[0], B = boss.Plates[1];
            boss.Hp = 2;
            PutU(p1, A.X, A.Z); PutU(p2, B.X, B.Z);
            Go(2);
            boss.TakeHit(By(p1, 10, 0, 1));
            Assert.IsTrue(boss.Dead);
            Assert.AreEqual("won", bf.Phase);
            Go(60);
            Assert.IsTrue(G.Dungeon.GateOpen(bf.Cx, 2), "el rastrillo se abre");
            yield break;
        }

        [UnityTest]
        public IEnumerator Fantasmas_NuncaMasDe4()
        {
            Begin();
            var pat = Pattern(boss, "fantasmas");
            int max = 0;
            for (int k = 0; k < 4; k++)
            {
                boss.CancelPattern(); boss.StartPattern(pat);
                Step(90);
                max = Mathf.Max(max, boss.AliveMinions());
            }
            Assert.GreaterOrEqual(max, 2, "invoca ecos");
            Assert.LessOrEqual(max, 4, "como mucho 4 vivos");
            yield break;
        }

        [UnityTest]
        public IEnumerator Barriles_SinNingunoCaeUnoCercaDeUnaPlaca()
        {
            Begin();
            foreach (var x in G.Barrels.ToArray()) { x.Dead = true; x.Destroy(); }
            boss.BarrelT = 0.05f;
            Go(150);
            var alive = G.Barrels.Where(x => !x.Dead && !x.Removed).ToList();
            Assert.GreaterOrEqual(alive.Count, 1, "aparece un barril nuevo");
            Assert.IsTrue(alive.Any(x => boss.Plates.Any(p => MathUtil.Hypot(x.X - p.X, x.Z - p.Z) < 2.5f)), "cerca de una placa");
            yield break;
        }

        [UnityTest]
        public IEnumerator Morir_LimpiaAvisosYPlacas()
        {
            Begin();
            boss.Hp = 1; boss.Invulnerable = false;
            boss.Damage(999, false, null);
            Assert.IsTrue(boss.Dead);
            Step(60);
            Assert.IsTrue(boss.Removed, "al morir se destruye tras la animación");
            Assert.AreEqual(0, boss.Plates.Count, "sus placas ya no están");
            yield break;
        }
    }
}
