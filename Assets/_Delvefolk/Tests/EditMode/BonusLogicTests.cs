using System.Linq;
using NUnit.Framework;

namespace Delvefolk.Tests
{
    // Fase 6: lógica pura del Buscaminas y del equipo de juego (port de tests/logic.test.js)
    public class BonusLogicTests
    {
        static MinefieldBoard Board(int mines, uint seed, bool specials = false)
        {
            return new MinefieldBoard(new MinefieldBoard.Options
            {
                Cols = 8, Rows = 6, Mines = mines, Seed = seed, RequireSolvable = true, MaxTries = 300,
                Hint = specials ? 1 : 0, Shield = specials ? 1 : 0, Gold = specials ? 2 : 0,
            });
        }

        // ---------- Buscaminas ----------
        [Test]
        public void Buscaminas_300TablerosResolublesSinAdivinarConTodasLasMinas()
        {
            for (uint s = 1; s <= 300; s++)
            {
                var b = Board(10, s);
                Assert.IsTrue(b.Solvable, "semilla " + s + " no resoluble");
                Assert.AreEqual(10, b.MineCount, "semilla " + s);
            }
        }

        [Test]
        public void Buscaminas_LaCasillaInicialYSusVecinasNuncaTienenMinaYEmpiezanReveladas()
        {
            for (uint s = 1; s <= 100; s++)
            {
                var b = Board(12, s);
                var st = b.CellAt(b.StartC, b.StartR);
                Assert.IsTrue(!st.Mine && st.State == MinefieldBoard.CellState.Revealed);
                foreach (var nb in b.Neighbors(b.StartC, b.StartR)) Assert.IsFalse(b.CellAt(nb.Key, nb.Value).Mine);
            }
        }

        [Test]
        public void Buscaminas_UnaCasillaNoSeRevelaDosVeces()
        {
            var b = Board(8, 42);
            int i = System.Array.FindIndex(b.Cells, k => k.State == MinefieldBoard.CellState.Hidden && !k.Mine);
            int c = i % 8, r = i / 8;
            int before = b.RevealedSafe;
            var r1 = b.Reveal(c, r, 0);
            var r2 = b.Reveal(c, r, 1);
            Assert.IsTrue(r1.Ok && !r2.Ok, "la segunda debe rechazarse");
            Assert.AreEqual("ya revelada", r2.Reason);
            Assert.AreEqual(before + r1.Revealed.Count, b.RevealedSafe);
        }

        [Test]
        public void Buscaminas_LasBanderasBloqueanElSondeoPeroNoLaPisada()
        {
            var b = Board(8, 7);
            int i = System.Array.FindIndex(b.Cells, k => k.State == MinefieldBoard.CellState.Hidden && !k.Mine);
            int c = i % 8, r = i / 8;
            bool flagged;
            Assert.IsTrue(b.ToggleFlag(c, r, out flagged) && flagged);
            Assert.AreEqual("bandera", b.Reveal(c, r, 0).Reason);
            Assert.IsTrue(b.Reveal(c, r, 0, true).Ok, "pisar ignora la bandera");
            Assert.AreEqual(0, b.Flags);
        }

        [Test]
        public void Buscaminas_RevelarTodasLasSegurasCompletaElTablero()
        {
            var b = Board(8, 3);
            for (int i = 0; i < b.Cells.Length; i++) if (!b.Cells[i].Mine) b.Reveal(i % 8, i / 8, 0, true);
            Assert.IsTrue(b.Complete);
            int m = System.Array.FindIndex(b.Cells, k => k.Mine);
            Assert.IsTrue(b.Reveal(m % 8, m / 8, 0).Mine);
        }

        [Test]
        public void Buscaminas_LasEspecialesCaenEnSegurasFueraDeLaApertura()
        {
            var b = Board(8, 11, true);
            var sp = b.Cells.Where(k => k.Special != null).ToList();
            Assert.AreEqual(4, sp.Count);
            Assert.IsTrue(sp.All(k => !k.Mine && k.State == MinefieldBoard.CellState.Hidden));
        }

        [Test]
        public void Buscaminas_LaPistaMarcaUnaMinaOcultaConBandera()
        {
            var b = Board(8, 5);
            int i = b.HintMine(new Rng(1u));
            Assert.IsTrue(b.Cells[i].Mine && b.Cells[i].State == MinefieldBoard.CellState.Flagged && b.Cells[i].Hinted);
        }

        // ---------- Equipo de juego ----------
        [Test]
        public void Equipo_MejorasConMaximo2PilasY3Distintas()
        {
            var g = new PlayerGear(null);
            for (int i = 0; i < 5; i++) g.Apply("buff_speed");
            Assert.AreEqual(GearConfig.MaxBuffStacks, g.BuffStacks("buff_speed"));
            g.Apply("buff_anchor"); g.Apply("buff_soles"); g.Apply("buff_cook");
            Assert.AreEqual(GearConfig.MaxActiveBuffs, g.Buffs.Count);
        }

        [Test]
        public void Equipo_LasMejorasCaducanEnTiempoDeJuego()
        {
            var g = new PlayerGear(null);
            g.Apply("buff_speed");
            Assert.Greater(g.SpeedMult(), 1f);
            Assert.IsTrue(g.Update(GearConfig.BuffDuration + 1f), "avisa de que hay que refrescar el panel");
            Assert.AreEqual(0, g.Buffs.Count);
            Assert.AreEqual(1f, g.SpeedMult());
        }

        [Test]
        public void Equipo_LaArmaduraReduceSeGastaYSeRompe()
        {
            var g = new PlayerGear(null);
            g.Apply("armor_chain");
            var r = g.ModifyIncoming(20, "hit");
            Assert.AreEqual(15f, r.Amount);
            Assert.IsTrue(r.ArmorHit);
            for (int i = 0; i < 7; i++) g.ModifyIncoming(20, "hit");
            Assert.IsNull(g.ArmorId, "rota tras 8 golpes");
        }

        [Test]
        public void Equipo_ElEscudoBloqueaUnImpactoEnteroYSeConsume()
        {
            var g = new PlayerGear(null);
            g.Apply("item_shield");
            Assert.IsTrue(g.ModifyIncoming(50, "hit").Blocked);
            Assert.IsFalse(g.ModifyIncoming(50, "hit").Blocked);
        }

        [Test]
        public void Equipo_ObjetosRepetidosSeConviertenEnOro()
        {
            var g = new PlayerGear(null);
            g.Apply("item_gloves"); g.Apply("item_gloves");
            Assert.AreEqual(1, g.Charges("item_gloves"));
            Assert.AreEqual(25, g.Gold);
            // Escudo de sobra (2 cargas): +15
            g.Apply("item_shield"); g.Apply("item_shield"); g.Apply("item_shield");
            Assert.AreEqual(2, g.Charges("item_shield"));
            Assert.AreEqual(40, g.Gold);
        }

        [Test]
        public void Equipo_IdoloInestableMasDanoHechoYRecibido()
        {
            var g = new PlayerGear(null);
            g.Apply("item_idol");
            Assert.Greater(g.DamageMult(), 1.3f);
            Assert.AreEqual(13f, g.ModifyIncoming(10, "hit").Amount);
        }

        [Test]
        public void Equipo_ElEstadoSobreviveAlCambioDePiso()
        {
            var g = new PlayerGear(null);
            g.Apply("armor_cork"); g.Apply("item_luck"); g.Apply("buff_cook"); g.Apply("gold_big");
            var json = UnityEngine.JsonUtility.ToJson(g.GetState());
            var g2 = new PlayerGear(null);
            g2.SetState(UnityEngine.JsonUtility.FromJson<GearState>(json));
            Assert.AreEqual("armor_cork", g2.ArmorId);
            Assert.IsTrue(g2.Has("item_luck"));
            Assert.AreEqual(1, g2.BuffStacks("buff_cook"));
            Assert.AreEqual(50, g2.Gold);
        }

        [Test]
        public void Equipo_ProteccionContraTrampasSuelasYCorcho()
        {
            var g = new PlayerGear(null);
            g.Apply("buff_soles"); g.Apply("armor_cork");
            Assert.AreEqual(MathUtil.RoundJs(20f * 0.6f * 0.5f * 0.85f), g.ModifyIncoming(20, "trap").Amount);
        }

        [Test]
        public void Equipo_UnaSolaArmaduraSeQuedaLaMejor()
        {
            var g = new PlayerGear(null);
            g.Apply("armor_pots");            // 0.35 × 6 = 2.1
            string t = g.Apply("armor_cork"); // 0.15 × 12 = 1.8: peor
            Assert.AreEqual("armor_pots", g.ArmorId);
            StringAssert.Contains("+20 monedas", t);
            Assert.AreEqual(20, g.Gold);
        }

        [Test]
        public void Recompensas_ElSorteoNuncaDaMaldicionesYLoRaroPesaMasConSuerte()
        {
            var rng = new Rng(9u);
            for (int i = 0; i < 500; i++) Assert.AreNotEqual("Maldición", Rewards.Get(Rewards.Roll(rng, null)).Category);
            var pool0 = Rewards.Pool(null, 0f);
            var pool1 = Rewards.Pool(null, 1f);
            float w0 = pool0.First(kv => kv.Key == "item_idol").Value, w1 = pool1.First(kv => kv.Key == "item_idol").Value;
            Assert.AreEqual(w0 * 1.6f, w1, 1e-4f, "lo raro pesa ×1.6 con el amuleto");
            Assert.AreEqual(pool0.First(kv => kv.Key == "item_boots").Value, pool1.First(kv => kv.Key == "item_boots").Value);
        }
    }
}
