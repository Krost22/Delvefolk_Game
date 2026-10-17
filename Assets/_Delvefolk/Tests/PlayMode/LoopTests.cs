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
            BossConfig.DemoFinalBoss = null;   // las pruebas usan los jefes normales salvo que pidan la demo
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
            BossConfig.DemoFinalBoss = BossConfig.DemoBossDefault;
        }

        protected void Step(int frames = 1) { Loop.Steps(frames); }

        // Pulsación de un frame (como un keydown sin repetición)
        protected void Press(string code) { G.Input.Press(code); }

        // ---- Ayudas equivalentes a las del arnés de pruebas del original (tests/harness.js) ----
        // Partida nueva sin los enemigos del mapa (las pruebas crean los suyos)
        protected Game StartRun(int n = 1)
        {
            G.Start(n);
            Step(2);
            foreach (var e in G.Enemies) e.Destroy();
            G.Enemies.Clear();
            foreach (var r in G.Dungeon.Rooms) r.Enemies.Clear();
            return G;
        }

        // Coordenadas del web (x, z) → Unity (x, −z)
        protected static Vector2 W(float x, float z) { return new Vector2(x, -z); }

        protected void Put(Entity e, float webX, float webZ) { PutU(e, webX, -webZ); }

        protected void PutU(Entity e, float x, float z)
        {
            e.Body.X = x; e.Body.Z = z;
            e.Body.Kx = e.Body.Kz = 0f;
            e.SyncRoot();
        }

        // Pulsar, un frame y soltar (T.tap)
        protected void Tap(string code)
        {
            G.Input.Tap(code);
            Step(1);
            G.Input.Release(code);
        }

        // Varias teclas a la vez (T.tap('Space', 'Enter'))
        protected void TapAll(params string[] codes)
        {
            foreach (var c in codes) G.Input.Tap(c);
            Step(1);
            foreach (var c in codes) G.Input.Release(c);
        }

        // Mantener teclas n frames (T.press … T.step(n) … T.release)
        protected void HoldAll(int frames, params string[] codes)
        {
            foreach (var c in codes) G.Input.Tap(c);
            Step(frames);
            foreach (var c in codes) G.Input.Release(c);
        }

        protected Enemy SpawnAt(char ch, float webX, float webZ)
        {
            var e = G.SpawnEnemy(ch, webX, -webZ);
            e.Wake(false);
            e.SetState(EnemyState.Chase);
            return e;
        }

        protected static void Face(Player p, float x, float z) { p.Facing = Mathf.Atan2(x - p.X, z - p.Z); }
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
            // Misma semilla, misma aleatoriedad de juego (la vista previa del título ya gasta números en ambas)
            var a = new GameObject("OtroA").AddComponent<GameLoop>();
            var b = new GameObject("OtroB").AddComponent<GameLoop>();
            try
            {
                Assert.AreEqual(1234u, a.Game.Seed);
                for (int i = 0; i < 5; i++) Assert.AreEqual(a.Game.Rand.NextDouble(), b.Game.Rand.NextDouble());
            }
            finally { Object.DestroyImmediate(a.gameObject); Object.DestroyImmediate(b.gameObject); }
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
