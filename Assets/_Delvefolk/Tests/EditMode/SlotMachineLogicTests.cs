using System.Collections.Generic;
using NUnit.Framework;

namespace Delvefolk.Tests
{
    // Tragaperras de los pisos: selección ponderada (sin escena)
    public class SlotMachineLogicTests
    {
        static List<SlotOption> Opts(params float[] w)
        {
            var l = new List<SlotOption>();
            for (int i = 0; i < w.Length; i++) l.Add(new SlotOption { Id = "o" + i, Weight = w[i] });
            return l;
        }

        [Test]
        public void Pesos_NegativosYCeroNoSalenNunca()
        {
            var o = Opts(-5f, 0f, 2f, float.NaN);
            Assert.AreEqual(2f, SlotMachineMath.Total(o));
            for (int i = 0; i < 200; i++) Assert.AreEqual(2, SlotMachineMath.Pick(o, i / 200.0));
        }

        [Test]
        public void Pesos_TodosACeroNoEligeNada()
        {
            Assert.AreEqual(-1, SlotMachineMath.Pick(Opts(0f, -1f), 0.5));
            Assert.AreEqual(-1, SlotMachineMath.Pick(new List<SlotOption>(), 0.5));
            Assert.AreEqual(-1, SlotMachineMath.Pick(null, 0.5));
        }

        [Test]
        public void Pesos_LaFrecuenciaSigueAlPeso()
        {
            var o = Opts(1f, 3f, 0f, 4f);
            var n = new int[4];
            const int N = 8000;
            for (int i = 0; i < N; i++) n[SlotMachineMath.Pick(o, (i + 0.5) / N)]++;
            Assert.AreEqual(N / 8, n[0], 2);
            Assert.AreEqual(N * 3 / 8, n[1], 2);
            Assert.AreEqual(0, n[2]);
            Assert.AreEqual(N / 2, n[3], 2);
        }

        [Test]
        public void Forzar_BuscaPorIdYSinoNada()
        {
            var o = Opts(1f, 1f);
            Assert.AreEqual(1, SlotMachineMath.IndexOf(o, "o1"));
            Assert.AreEqual(-1, SlotMachineMath.IndexOf(o, "nada"));
            Assert.AreEqual(-1, SlotMachineMath.IndexOf(o, ""));
        }

        [Test]
        public void Aparicion_LaProbabilidadSeCumplePisoAPiso()
        {
            // Con 0,75 de probabilidad, en 300 partidas × 10 pisos deben salir ≈ 75 % (y en cada piso por separado también)
            for (int f = 2; f <= 11; f++)
            {
                int n = 0;
                for (uint seed = 1; seed <= 300; seed++) if (Game.SlotRng(seed, f).Next() < 0.75f) n++;
                Assert.AreEqual(0.75f, n / 300f, 0.08f, "piso " + f);
            }
            int seguidos = 0;
            for (int f = 2; f <= 11; f++) if (Game.SlotRng(1234u, f).Next() < 0.75f) seguidos++;
            Assert.GreaterOrEqual(seguidos, 4, "una misma partida no se queda casi sin máquinas");
        }

        [Test]
        public void Afinidades_PorDefectoTienenCincoTemasConArma()
        {
            var t = FloorThemeCatalog.Defaults();
            Assert.AreEqual(5, t.Count);
            foreach (var e in new[] { Element.Water, Element.Fire, Element.Plant, Element.Poison, Element.Future })
            {
                var th = t.Find(x => x.Element == e);
                Assert.IsNotNull(th, e.ToString());
                Assert.IsTrue(WeaponRegistry.Has(th.WeaponId), th.WeaponId);
            }
        }
    }
}
