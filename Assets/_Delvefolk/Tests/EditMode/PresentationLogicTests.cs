using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Delvefolk.Tests
{
    // Fase 4: selección de la sala, habilidades y sonidos sintetizados (port de tests/logic.test.js)
    public class PresentationLogicTests
    {
        const string TestPrefix = "delvefolk.pruebas.";

        [Test]
        public void Cosmeticos_CapaYMochilaOcupanLaEspalda()
        {
            var lo = new PlayerLoadout(0, null, new Dictionary<string, string> { { "body", "cape" } });
            var removed = lo.SetCosmetic("acc", "backpack");
            Assert.AreEqual("backpack", lo.Cosmetics["acc"]);
            Assert.AreEqual("tunic", lo.Cosmetics["body"], "la nueva elección retira la otra");
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual("cape", removed[0].Item.Id);
        }

        [Test]
        public void Cosmeticos_PiezasDesconocidasVuelvenALasDePorDefecto()
        {
            var lo = new PlayerLoadout(1, "nope", new Dictionary<string, string> { { "head", "casco-inexistente" }, { "palette", "pot" } });
            Assert.AreEqual("funnel", lo.Cosmetics["head"]);
            Assert.AreEqual("river", lo.Cosmetics["palette"]);
            Assert.AreEqual("shout", lo.Ability);
        }

        [Test]
        public void Habilidad_UnaSolaEquipada()
        {
            var lo = new PlayerLoadout(0);
            Assert.IsTrue(lo.SetAbility("magnet"));
            Assert.AreEqual("magnet", lo.Ability);
            Assert.IsFalse(lo.SetAbility("volar"));
            Assert.AreEqual("magnet", lo.Ability);
        }

        [Test]
        public void Seleccion_SeGuardaYSeRecupera()
        {
            string prefix = Storage.Prefix; bool enabled = Storage.Enabled;
            Storage.Prefix = TestPrefix; Storage.Enabled = true;
            try
            {
                var lo = new PlayerLoadout(0);
                lo.SetAbility("swap"); lo.SetCosmetic("head", "horns"); lo.Save();
                var back = PlayerLoadout.Load(0);
                Assert.AreEqual("swap", back.Ability);
                Assert.AreEqual("horns", back.Cosmetics["head"]);
                var st = new MatchSettings { ExclusiveAbilities = true, BonusMode = "never" };
                st.Save();
                var st2 = MatchSettings.Load();
                Assert.IsTrue(st2.ExclusiveAbilities);
                Assert.AreEqual("never", st2.BonusMode);
                Assert.IsTrue(st2.SwapAllies);
            }
            finally
            {
                Storage.Delete("loadout.0"); Storage.Delete("settings");
                Storage.Prefix = prefix; Storage.Enabled = enabled;
            }
        }

        [Test]
        public void Ajustes_PorDefecto()
        {
            var st = new MatchSettings();
            Assert.AreEqual("normal", st.BonusMode);
            Assert.IsFalse(st.ExclusiveAbilities);
            Assert.IsTrue(st.SwapAllies);
        }

        [Test]
        public void Cosmeticos_NoTienenNingunCampoDeEstadisticas()
        {
            var bad = new[] { "damage", "speed", "hp", "reduction", "armor", "knock", "cooldown" };
            var fields = typeof(CosmeticDef).GetFields().Select(f => f.Name.ToLowerInvariant()).ToList();
            foreach (var k in bad) Assert.IsFalse(fields.Contains(k), "las piezas tienen " + k);
            Assert.Greater(Cosmetics.Items.Count, 30);
        }

        [Test]
        public void Habilidades_LasSieteConDatosCompletos()
        {
            Assert.AreEqual(7, Abilities.Order.Count);
            CollectionAssert.AreEqual(new[] { "quake", "blast", "charge", "shout", "swap", "magnet", "guard" }, Abilities.Order);
            foreach (var id in Abilities.Order)
            {
                var a = Abilities.Get(id);
                Assert.IsTrue(!string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.Icon) && !string.IsNullOrEmpty(a.Short) &&
                    a.Pros.Length > 0 && a.Cons.Length > 0 && !string.IsNullOrEmpty(a.Allies), id + " sin textos");
                Assert.Greater(a.Cooldown, 0f, id + " sin enfriamiento");
                Assert.Greater(a.P.Count, 0, id + " sin parámetros configurables");
                Assert.IsNotNull(a.Preview, id + " sin demostración para la sala");
            }
            Assert.AreEqual("quake", Abilities.DefaultFor(0));
            Assert.AreEqual("shout", Abilities.DefaultFor(1));
        }

        [Test]
        public void Audio_TodasLasRecetasSuenan()
        {
            Assert.AreEqual(59, AudioSynth.Ids.Length);
            foreach (var id in AudioSynth.Ids)
            {
                var pcm = AudioSynth.Render(id);
                Assert.IsNotNull(pcm, id);
                Assert.Greater(pcm.Length, 0, id);
                float peak = 0f;
                foreach (var v in pcm)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), id + " con muestras no válidas");
                    peak = System.Math.Max(peak, System.Math.Abs(v));
                }
                Assert.Greater(peak, 1e-4f, id + " en silencio");
                Assert.Less(peak, 1.5f, id + " satura");
            }
            Assert.IsNull(AudioSynth.Render("nada"));
        }

        [Test]
        public void Audio_DuracionesComoLasRecetas()
        {
            float sr = AudioSynth.SampleRate;
            // explosión: ruido de 0.9 s (+0.05 de cola)
            Assert.AreEqual(0.95f, AudioSynth.Render("explosion").Length / sr, 0.01f);
            // descenso: 5 notas de 0.25 s separadas 0.09 s
            Assert.AreEqual(4 * 0.09f + 0.25f + 0.05f, AudioSynth.Render("descend").Length / sr, 0.01f);
            // el silbido del arma dura más cuanto más pega (0.16 + 0.08·p)
            Assert.AreEqual(0.16f + 0.08f * 1.4f + 0.05f, AudioSynth.Render("swing", 1.4f).Length / sr, 0.01f);
            Assert.AreEqual(0.16f + 0.08f * 0.3f + 0.05f, AudioSynth.Render("swing", 0.3f).Length / sr, 0.01f);
            Assert.AreSame(AudioSynth.Render("swing", 0.3f), AudioSynth.Render("swing", 0.31f), "variantes cacheadas");
        }
    }
}
