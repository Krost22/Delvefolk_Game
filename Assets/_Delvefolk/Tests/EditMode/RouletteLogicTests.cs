using NUnit.Framework;
using UnityEngine;

namespace Delvefolk.Tests
{
    // Ruleta del Crupier: selección ponderada y cálculo del ángulo de parada (lógica pura)
    public class RouletteLogicTests
    {
        static RouletteSegment[] Segs(params float[] w)
        {
            var s = new RouletteSegment[w.Length];
            for (int i = 0; i < w.Length; i++) s[i] = new RouletteSegment { Outcome = (RouletteOutcome)i, Weight = w[i] };
            return s;
        }

        [Test]
        public void Pesos_NegativosYNaNCuentanComoCeroYSeNormalizan()
        {
            var p = RouletteMath.Probabilities(Segs(30, -5, 20, float.NaN, 50, 0));
            Assert.AreEqual(0.3f, p[0], 1e-5f);
            Assert.AreEqual(0f, p[1]);
            Assert.AreEqual(0.2f, p[2], 1e-5f);
            Assert.AreEqual(0f, p[3]);
            Assert.AreEqual(0.5f, p[4], 1e-5f);
            Assert.AreEqual(0f, p[5]);
        }

        [Test]
        public void Seleccion_PesoCeroNuncaSaleYTodoCeroDevuelveMenosUno()
        {
            var w = RouletteMath.Weights(Segs(0, 3, 0, 1, -2, 0));
            var rng = new Rng(7u);
            for (int i = 0; i < 20000; i++)
            {
                int k = RouletteMath.Pick(w, rng.Next());
                Assert.IsTrue(k == 1 || k == 3, "salió " + k);
            }
            Assert.AreEqual(3, RouletteMath.Pick(w, 1f), "r = 1 cae en el último con peso");
            Assert.AreEqual(1, RouletteMath.Pick(w, 0f));
            Assert.AreEqual(-1, RouletteMath.Pick(RouletteMath.Weights(Segs(0, 0, -1)), 0.5f));
            Assert.AreEqual(-1, RouletteMath.Pick(new float[0], 0.5f));
        }

        [Test]
        public void Seleccion_FrecuenciasSiguenLosPesosPorDefecto()
        {
            var w = RouletteMath.Weights(RouletteConfig.DefaultSegments());
            var n = new int[w.Length];
            var rng = new Rng(1234u);
            const int N = 100000;
            for (int i = 0; i < N; i++) n[RouletteMath.Pick(w, rng.Next())]++;
            float[] expect = { 0.30f, 0.25f, 0.20f, 0.10f, 0.10f, 0.05f };
            for (int i = 0; i < w.Length; i++) Assert.AreEqual(expect[i], n[i] / (float)N, 0.01f, "segmento " + i);
        }

        [Test]
        public void Angulo_ElDestinoDejaElSegmentoBajoElIndicador()
        {
            var rng = new Rng(99u);
            for (int k = 0; k < 2000; k++)
            {
                int n = 2 + (int)(rng.Next() * 10);
                var angles = new float[n];
                for (int i = 0; i < n; i++) angles[i] = i * 360f / n + rng.Range(-5f, 5f) + 37f;
                int sign = rng.Next() < 0.5f ? 1 : -1;
                float pointer = rng.Range(-400f, 400f), current = rng.Range(-1000f, 5000f);
                int seg = (int)(rng.Next() * n), turns = (int)(rng.Next() * 6);
                float target = RouletteMath.Target(current, angles[seg], pointer, sign, turns);
                Assert.GreaterOrEqual(target - current, 360f * turns - 1e-3f, "da al menos las vueltas pedidas");
                Assert.Less(target - current, 360f * (turns + 1), "y no más de una vuelta extra");
                Assert.AreEqual(seg, RouletteMath.IndexAt(target, angles, pointer, sign));
                Assert.AreEqual(0f, Mathf.DeltaAngle(angles[seg] + sign * target, pointer), 0.05f);
            }
        }

        [Test]
        public void Curva_ProgresoMonotonoYExactoEnLosExtremos()
        {
            var c = RouletteConfig.DefaultCurve();
            float prev = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float p = RouletteMath.Progress(c, i / 100f);
                Assert.GreaterOrEqual(p, prev - 1e-4f, "no retrocede");
                prev = p;
            }
            Assert.AreEqual(0f, RouletteMath.Progress(c, 0f));
            Assert.AreEqual(1f, RouletteMath.Progress(c, 1f));
            float early = RouletteMath.Progress(c, 0.1f) - RouletteMath.Progress(c, 0f);
            float late = RouletteMath.Progress(c, 1f) - RouletteMath.Progress(c, 0.9f);
            Assert.Greater(early, late * 5f, "frena al final");
            Assert.AreEqual(3f, RouletteMath.StartSlope(c), 0.2f);
        }
    }
}
