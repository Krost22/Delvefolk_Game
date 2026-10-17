using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 1: movimiento, cámara, colisiones, salto y energía en una partida real (piso 1)
    public class MovementTests : GameTestBase
    {
        Player P1 { get { return G.Players[0]; } }

        void StartRun(int players = 1)
        {
            G.Start(players);
            Step(2);
        }

        void Teleport(Player p, int tx, int tz)
        {
            p.Body.X = Grid.X(tx); p.Body.Z = Grid.Z(tz);
            p.Body.Kx = p.Body.Kz = 0f; p.Vx = p.Vz = 0f;
            p.SyncRoot();
        }

        void AssertNotInSolid(Player p, string ctx)
        {
            int tx = Grid.TileX(p.X), tz = Grid.TileZ(p.Z);
            Assert.IsFalse(G.Dungeon.IsSolidFor(tx, tz, p.Body), ctx + ": dentro de una casilla sólida (" + tx + "," + tz + ")");
            Assert.IsFalse(G.Dungeon.IsPitAt(p.X, p.Z), ctx + ": dentro del foso");
        }

        [UnityTest]
        public IEnumerator Movimiento_RelativoALaCamaraConAceleracionYFrenado()
        {
            StartRun();
            Vector2 f, r;
            G.CameraCtl.GetBasis(out f, out r);
            // La cámara mira al "noroeste": adelante = (−x, +z) y derecha = (+x, +z)
            Assert.AreEqual(-0.7071f, f.x, 0.01f); Assert.AreEqual(0.7071f, f.y, 0.01f);
            Assert.AreEqual(0.7071f, r.x, 0.01f); Assert.AreEqual(0.7071f, r.y, 0.01f);
            float x0 = P1.X, z0 = P1.Z;
            G.Input.Hold("KeyW");
            Step(1);
            Assert.AreEqual(70f / 60f, MathUtil.Hypot(P1.Vx, P1.Vz), 0.02f);    // aceleración limitada a 70·dt
            Step(20);
            Assert.AreEqual(6.4f, MathUtil.Hypot(P1.Vx, P1.Vz), 0.01f);
            Assert.Less(P1.X, x0); Assert.Greater(P1.Z, z0);
            // Mira hacia donde camina
            Assert.AreEqual(Mathf.Atan2(f.x, f.y), MathUtil.AngleDiff(0, P1.Facing), 0.05f);
            G.Input.Release("KeyW");
            Step(1);
            Assert.AreEqual(6.4f - 55f / 60f, MathUtil.Hypot(P1.Vx, P1.Vz), 0.02f);   // frenado a 55·dt
            Step(10);
            Assert.AreEqual(0f, MathUtil.Hypot(P1.Vx, P1.Vz), 1e-4f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Movimiento_DiagonalNormalizadaYFlechasEnSolitario()
        {
            StartRun();
            G.Input.Hold("KeyW"); G.Input.Hold("KeyD");
            Step(20);
            Assert.AreEqual(6.4f, MathUtil.Hypot(P1.Vx, P1.Vz), 0.01f);
            G.Input.Clear();
            Step(15);
            // En solitario el J1 también se mueve con las flechas
            G.Input.Hold("ArrowLeft");
            Step(10);
            Assert.Greater(MathUtil.Hypot(P1.Vx, P1.Vz), 3f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Colisiones_NoAtraviesaMurosNiCaeAlFosoA40()
        {
            StartRun();
            // Junto al foso de la Cámara del Fondo (columnas 18–20, filas 6–8)
            Teleport(P1, 16, 7);
            G.Input.Hold("KeyD"); G.Input.Hold("KeyS");   // derecha + atrás de la cámara = este
            for (int i = 0; i < 180; i++)
            {
                if (i % 20 == 0) P1.Knock(40f, 0f);
                Step(1);
                AssertNotInSolid(P1, "foso, frame " + i);
            }
            G.Input.Clear();
            // Contra la esquina noroeste de la sala de los trastos y contra el muro del pasillo
            int[][] spots = { new[] { 28, 18 }, new[] { 39, 24 }, new[] { 33, 27 }, new[] { 22, 21 } };
            Vector2[] pushes = { new Vector2(-28, 28), new Vector2(28, -28), new Vector2(-40, 0), new Vector2(0, 40) };
            for (int k = 0; k < spots.Length; k++)
            {
                Teleport(P1, spots[k][0], spots[k][1]);
                for (int i = 0; i < 90; i++)
                {
                    if (i % 15 == 0) P1.Knock(pushes[k].x, pushes[k].y);
                    Step(1);
                    AssertNotInSolid(P1, "punto " + k + ", frame " + i);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator Salto_AlcanzaUnos167PasaPorEncimaYAterriza()
        {
            StartRun();
            Press("ShiftLeft");
            float maxY = 0f;
            bool wasAirborne = false;
            Step(1);
            for (int i = 0; i < 120 && P1.JumpY > 0f; i++)
            {
                maxY = Mathf.Max(maxY, P1.JumpY);
                if (P1.Body.Airborne) { wasAirborne = true; Assert.Greater(P1.JumpY, JumpConfig.PassHeight); }
                Step(1);
            }
            Assert.That(maxY, Is.InRange(1.5f, 1.7f), "altura máxima");   // v²/2g = 1.67 (1.58 integrando a 60 Hz)
            Assert.IsTrue(wasAirborne);
            Assert.AreEqual(0f, P1.JumpY);
            Assert.IsFalse(P1.Body.Airborne);
            Assert.AreEqual(JumpConfig.Stamina - JumpConfig.JumpCost, P1.Stamina, 0.01f);
            // La energía se recupera a 35/s cuando han pasado 0.7 s desde el último gasto y está en el suelo
            Step(60);
            Assert.Greater(P1.Stamina, JumpConfig.Stamina - JumpConfig.JumpCost + 20f);
            yield break;
        }

        int JumpAndLand()
        {
            Press("ShiftLeft");
            int frames = 0;
            Step(1); frames++;
            while (P1.JumpY > 0f && frames < 120) { Step(1); frames++; }
            return frames;
        }

        [UnityTest]
        public IEnumerator Energia_TresSaltosBienYElCuartoAgotaYAturde()
        {
            StartRun();
            for (int j = 0; j < 3; j++) JumpAndLand();
            Assert.AreEqual(10f, P1.Stamina, 0.01f);
            Assert.IsFalse(P1.Exhausted);
            Assert.AreEqual(0f, P1.StunT);
            JumpAndLand();
            // Al aterrizar agotado cae rendido: energía 0 y aturdido (el tope de aturdimiento del jugador es 1.2 s)
            Assert.AreEqual(0f, P1.Stamina, 1e-4f);
            Assert.IsFalse(P1.Exhausted);
            Assert.AreEqual(Mathf.Min(AbilityRules.PlayerStunCap, JumpConfig.ExhaustStun), P1.StunT, 0.02f);
            Assert.IsTrue(G.Ui.HasFloatText("¡Agotado!"));
            // Aturdido: sin control
            G.Input.Hold("KeyW");
            Step(30);
            Assert.Less(MathUtil.Hypot(P1.Vx, P1.Vz), 0.5f);
            G.Input.Clear();
            yield break;
        }

        [UnityTest]
        public IEnumerator Energia_SinAlientoNoSeSalta()
        {
            StartRun();
            P1.Stamina = 0f;
            P1.StaminaT = 0.6f;   // > 0.5 para el aviso, < 0.7 para que aún no se recupere
            Press("KeyF");
            Step(1);
            Assert.AreEqual(0f, P1.JumpY);
            Assert.IsTrue(G.Ui.HasFloatText("Sin aliento"));
            yield break;
        }

        [UnityTest]
        public IEnumerator Camara_SigueAlJugadorYVibraPorTrauma()
        {
            StartRun();
            G.Input.Hold("KeyD");
            Step(60);
            G.Input.Clear();
            Step(300);
            // El objetivo suavizado alcanza al jugador cuando se detiene
            Assert.AreEqual(P1.X, G.CameraCtl.TargetX, 0.1f);
            Assert.AreEqual(P1.Z, G.CameraCtl.TargetZ, 0.1f);
            var cam = G.CameraCtl.Cam.transform;
            Vector3 rest = cam.position;
            G.CameraCtl.AddShake(0.5f);
            G.CameraCtl.AddShake(0.8f);
            Assert.AreEqual(1f, G.CameraCtl.Trauma, 1e-6f);   // la vibración suma con tope 1
            Step(3);
            Assert.Greater(Vector3.Distance(rest, cam.position), 0.01f);
            Step(30);
            Assert.AreEqual(1f - 33f / 60f * 1.7f, G.CameraCtl.Trauma, 0.02f);   // decae 1.7/s
            Step(60);
            Assert.AreEqual(0f, G.CameraCtl.Trauma);
            Assert.AreEqual(rest.x, cam.position.x, 0.01f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Mapa_NoSeVeEspejadoRespectoAlAscii()
        {
            StartRun();
            G.CameraCtl.SnapTo(Grid.X(33), Grid.Z(21));
            var cam = G.CameraCtl.Cam;
            Vector3 west = cam.WorldToViewportPoint(Grid.ToWorld(30, 21));
            Vector3 east = cam.WorldToViewportPoint(Grid.ToWorld(36, 21));
            Vector3 north = cam.WorldToViewportPoint(Grid.ToWorld(33, 18));
            Vector3 south = cam.WorldToViewportPoint(Grid.ToWorld(33, 24));
            // Igual que en el web: el este (columna mayor) va a la derecha y abajo; el norte (fila menor), a la derecha y arriba
            Assert.Greater(east.x, west.x); Assert.Less(east.y, west.y);
            Assert.Greater(north.x, south.x); Assert.Greater(north.y, south.y);
            yield break;
        }

        [UnityTest]
        public IEnumerator Muros_SeRecortanEntreLaCamaraYElJugador()
        {
            StartRun();
            Step(60);
            DungeonView.Wall near = null, far = null;
            foreach (var w in G.View.Walls)
            {
                if (w.Tx == 34 && w.Tz == 38) near = w;   // puerta de entrada, 2 casillas al sur del jugador
                if (w.Tx == 36 && w.Tz == 30) far = w;    // muro norte de la sala
            }
            Assert.IsNotNull(near); Assert.IsNotNull(far);
            Assert.AreEqual(Cfg.WallLow / Cfg.WallH, near.F, 0.02f);
            Assert.AreEqual(1f, far.F, 0.02f);
            Assert.AreEqual(near.F, near.Tr.localScale.y, 0.01f);
            yield break;
        }

        [UnityTest]
        public IEnumerator DosJugadores_AparecenJuntosYSeMuevenPorSeparado()
        {
            StartRun(2);
            Assert.AreEqual(2, G.Players.Count);
            var p2 = G.Players[1];
            float d = MathUtil.Hypot(p2.X - P1.X, p2.Z - P1.Z);
            Assert.AreEqual(1.3f, d, 0.05f);
            Assert.IsTrue(G.Dungeon.NavWalkable(Grid.TileX(p2.X), Grid.TileZ(p2.Z)));
            Assert.IsNull(P1.Controls.Alt);   // con dos jugadores las flechas son del J2
            float x1 = P1.X, x2 = p2.X;
            G.Input.Hold("ArrowRight");
            Step(30);
            Assert.AreEqual(x1, P1.X, 1e-3f);
            Assert.Greater(p2.X, x2 + 1f);
            yield break;
        }
    }
}
