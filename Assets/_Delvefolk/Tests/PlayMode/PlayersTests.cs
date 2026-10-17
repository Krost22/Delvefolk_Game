using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Hasta 4 jugadores: dos en el teclado y el resto con mandos; cada uno elige su dispositivo
    public class PlayersTests : GameTestBase
    {
        [UnityTest]
        public IEnumerator Cuatro_JugadoresConTecladoYMandos()
        {
            G.Settings.bonusMode = "never";
            G.Bindings[0] = DeviceBinding.KeyA; G.Bindings[1] = DeviceBinding.KeyB;
            G.Bindings[2] = DeviceBinding.PadSlot(0); G.Bindings[3] = DeviceBinding.PadSlot(1);
            StartRun(4);
            Assert.AreEqual(4, G.Players.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(G.Bindings[i], G.Players[i].Controls.Binding);
                Assert.IsTrue(G.Ui.PanelShown(i), "panel de J" + (i + 1));
                Assert.AreEqual("J" + (i + 1), G.Players[i].Look.Name);
            }
            StringAssert.Contains("A atacar", G.Ui.PanelKeys(2));
            StringAssert.Contains("X atacar", G.Ui.PanelKeys(0));
            // El J3 ataca con la A de su mando; el J1 con X
            Tap("GP0:A"); Step(2);
            Assert.IsNotNull(G.Players[2].Attack, "J3 ataca con su mando");
            Assert.IsNull(G.Players[3].Attack);
            Tap("KeyX"); Step(1);
            Assert.IsNotNull(G.Players[0].Attack, "J1 ataca con X");
            // El stick del mando 2 mueve al J4 y a nadie más
            var p4 = G.Players[3];
            float x0 = p4.X, x1 = G.Players[0].X;
            G.Input.Pads[1].X = 1f;
            Step(30);
            G.Input.Pads[1].X = 0f;
            Assert.AreNotEqual(x0, p4.X, "J4 se mueve con su stick");
            Assert.Less(Mathf.Abs(G.Players[0].X - x1), 0.5f, "J1 no se mueve (como mucho, un empujón)");
            // ESPACIO: salta el J1
            Tap("Space"); Step(2);
            Assert.Greater(G.Players[0].JumpY, 0f, "J1 salta con ESPACIO");
            yield break;
        }

        [UnityTest]
        public IEnumerator Union_ConCualquierDispositivoLibreHastaCuatro()
        {
            G.Settings.bonusMode = "never";
            StartRun(1);
            Assert.IsTrue(G.Ui.JoinHintShown);
            Tap("Enter");
            Assert.AreEqual(2, G.Players.Count, "J2 con ENTER (teclado derecho)");
            Assert.AreEqual(DeviceBinding.KeyB, G.Players[1].Controls.Binding);
            Tap("GP2:START");
            Assert.AreEqual(3, G.Players.Count, "J3 con START del mando 3");
            Assert.AreEqual(DeviceBinding.PadSlot(2), G.Players[2].Controls.Binding);
            Tap("GP2:A"); Step(1);
            Assert.AreEqual(3, G.Players.Count, "su mando ya es suyo: no vuelve a unirse");
            Tap("GP0:A");
            Assert.AreEqual(4, G.Players.Count, "J4 con la A del mando 1");
            Assert.IsFalse(G.Ui.JoinHintShown, "con 4 ya no caben más");
            Tap("GP1:A");
            Assert.AreEqual(4, G.Players.Count, "máximo 4");
            yield break;
        }

        [UnityTest]
        public IEnumerator Titulo_ConLaADeUnMandoElJ1JuegaConEseMando()
        {
            G.State = GameState.Title;
            Tap("GP3:A");
            Assert.AreEqual(GameState.Lobby, G.State);
            Assert.AreEqual(DeviceBinding.PadSlot(3), G.Lobby.DeviceOf(0));
            yield break;
        }

        [UnityTest]
        public IEnumerator Sala_UnirseConMandosCambiarControlYEmpezarConCuatro()
        {
            G.Settings = new MatchSettings(); G.Settings.Save();
            G.State = GameState.Title; G.OpenLobby(1);
            var L = G.Lobby;
            Assert.AreEqual(DeviceBinding.KeyA, L.DeviceOf(0));
            Tap("GP0:A");
            Assert.IsTrue(L.Joined(1), "un mando libre se une");
            Assert.AreEqual(DeviceBinding.PadSlot(0), L.DeviceOf(1));
            Tap("Enter");
            Assert.IsTrue(L.Joined(2), "el teclado derecho también");
            Assert.AreEqual(DeviceBinding.KeyB, L.DeviceOf(2));
            Tap("GP1:START");
            Assert.IsTrue(L.Joined(3));
            Assert.AreEqual(4, L.JoinedCount);
            // J1 quiere jugar con mando: Z cambia de control, pero no hay otro libre hasta que se conecta uno
            Tap("KeyZ");
            Assert.AreEqual(DeviceBinding.KeyA, L.DeviceOf(0));
            StringAssert.Contains("conecta un mando", L.NoteOf(0));
            G.Input.Pads[2].Connected = true;
            Tap("KeyZ");
            Assert.AreEqual(DeviceBinding.PadSlot(2), L.DeviceOf(0), "J1 pasa al mando 3");
            Tap("KeyX");
            Assert.IsFalse(L.Ready(0), "ya no responde al teclado");
            // Todos listos con sus dispositivos
            Tap("GP2:A"); Tap("GP0:A"); Tap("Enter"); Tap("GP1:A");
            Assert.IsTrue(L.AllReady());
            Step(200);
            Assert.AreEqual(GameState.Play, G.State);
            Assert.AreEqual(4, G.Players.Count);
            Assert.AreEqual("Mando 3,Mando 1,Teclado (flechas),Mando 2", string.Join(",", G.Players.Select(p => p.Controls.S.Label)));
            yield break;
        }

        [UnityTest]
        public IEnumerator Sala_SoloDosEnElTecladoYSalirLiberaElHueco()
        {
            G.Settings = new MatchSettings(); G.Settings.Save();
            G.State = GameState.Title; G.OpenLobby(2);
            var L = G.Lobby;
            Assert.AreEqual(DeviceBinding.KeyA, L.DeviceOf(0));
            Assert.AreEqual(DeviceBinding.KeyB, L.DeviceOf(1));
            // El J2 no puede cambiar a otro teclado (los dos están ocupados) ni a un mando sin conectar
            Tap("ControlRight");
            Assert.AreEqual(DeviceBinding.KeyB, L.DeviceOf(1));
            Tap("ShiftRight");
            Assert.IsFalse(L.Joined(1), "J2 sale con su tecla de interactuar");
            Tap("GP0:A");
            Assert.IsTrue(L.Joined(1), "su hueco lo ocupa un mando");
            yield break;
        }
    }
}
