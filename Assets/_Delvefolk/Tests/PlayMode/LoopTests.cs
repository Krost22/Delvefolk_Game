using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Base de las pruebas de partida: un Bootstrap con el bucle avanzado a mano (Step) y entrada inyectada
    public abstract class GameTestBase
    {
        protected GameLoop Loop;
        protected Game G { get { return Loop.Game; } }

        [SetUp]
        public void BaseSetUp()
        {
            GameLoop.AutoRun = false;
            GameLoop.SeedOverride = 1234u;
            Storage.Enabled = false;
            Loop = new GameObject("Bootstrap").AddComponent<GameLoop>();
            G.InstantTransitions = true;
        }

        [TearDown]
        public void BaseTearDown()
        {
            if (Loop) Object.DestroyImmediate(Loop.gameObject);
            GameLoop.AutoRun = true;
            GameLoop.SeedOverride = 0;
            Storage.Enabled = true;
        }

        protected void Step(int frames = 1) { Loop.Steps(frames); }

        // Pulsación de un frame (como un keydown sin repetición)
        protected void Press(string code) { G.Input.Press(code); }
    }

    public class LoopTests : GameTestBase
    {
        [UnityTest]
        public IEnumerator Bucle_AvanzaDeterministaConStep()
        {
            G.State = GameState.Play;
            int f0 = G.FrameCount;
            Step(60);
            Assert.AreEqual(f0 + 60, G.FrameCount);
            Assert.AreEqual(1f, G.Time, 1e-4f);
            // AutoRun = false: los frames de Unity no avanzan la simulación
            float t = G.Time;
            yield return null;
            yield return null;
            Assert.AreEqual(t, G.Time);
            // Misma semilla, misma aleatoriedad de juego
            var other = new GameObject("Otro").AddComponent<GameLoop>();
            try
            {
                Assert.AreEqual(new Rng(1234u).NextDouble(), other.Game.Rand.NextDouble());
            }
            finally { Object.DestroyImmediate(other.gameObject); }
        }

        [UnityTest]
        public IEnumerator Entrada_InyectadaLlegaAlBucle()
        {
            G.State = GameState.Play;
            Press("Escape");
            Step();
            Assert.AreEqual(GameState.Paused, G.State);
            // En pausa la simulación no avanza
            float t = G.Time;
            Step(10);
            Assert.AreEqual(t, G.Time);
            Press("KeyP");
            Step();
            Assert.AreEqual(GameState.Play, G.State);
            bool muted = G.Audio.Muted;
            Press("KeyM");
            Step();
            Assert.AreNotEqual(muted, G.Audio.Muted);
            Press("KeyM");
            Step();
            Assert.AreEqual(muted, G.Audio.Muted);
            yield break;
        }

        [UnityTest]
        public IEnumerator Hitstop_CongelaYConservaLasPulsaciones()
        {
            G.State = GameState.Play;
            G.RequestHitstop(0.5f);
            Assert.AreEqual(0.16f, G.Hitstop, 1e-6f);   // tope de 0.16 s
            float t = G.Time;
            G.Input.Press("KeyE");
            Step();
            Assert.AreEqual(t, G.Time);                 // congelado
            Assert.IsTrue(G.Input.WasPressed("KeyE"));  // la pulsación espera a que se reanude
            Step(20);
            Assert.Greater(G.Time, t);
            Assert.IsFalse(G.Input.WasPressed("KeyE"));
            yield break;
        }
    }
}
