using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 4: título, sala de preparación, pausa, HUD y sonido (port de tests/e2e.test.js)
    public class PresentationTests : GameTestBase
    {
        const string TestPrefix = "delvefolk.pruebas.";
        string savedPrefix;

        // Guardado real (con un prefijo propio para no tocar la selección del jugador)
        void UseTestStorage()
        {
            savedPrefix = Storage.Prefix;
            Storage.Prefix = TestPrefix;
            Storage.Enabled = true;
        }

        [TearDown]
        public void CleanStorage()
        {
            if (savedPrefix == null) return;
            Storage.Delete("loadout.0"); Storage.Delete("loadout.1"); Storage.Delete("settings");
            Storage.Prefix = savedPrefix;
            savedPrefix = null;
        }

        [UnityTest]
        public IEnumerator Titulo_DeFondoLaMazmorraYEspacioOEnterAbrenLaSala()
        {
            Assert.AreEqual(GameState.Title, G.State);
            Assert.IsTrue(G.Ui.TitleShown);
            Assert.IsFalse(G.Ui.HudShown, "sin HUD en el título");
            Assert.AreEqual(1, G.Players.Count, "piso 1 de fondo con un jugador");
            float t0 = G.CameraCtl.TargetX;
            Step(60);
            Assert.AreNotEqual(t0, G.CameraCtl.TargetX, "la cámara pasea");
            Assert.IsFalse(G.Ui.HeadShown(0), "vida sobre la cabeza oculta en el título");
            Tap("KeyX");
            Assert.AreEqual(GameState.Lobby, G.State);
            Assert.IsFalse(G.Ui.TitleShown);
            Assert.IsTrue(G.Lobby.IsOpen);
            Assert.IsTrue(G.Lobby.Joined(0)); Assert.IsFalse(G.Lobby.Joined(1));
            Assert.IsFalse(G.Lobby.Ready(0), "el ESPACIO que abre la sala no marca listo");
            Tap("Escape");
            Tap("Enter");
            Assert.IsTrue(G.Lobby.Joined(0) && G.Lobby.Joined(1), "ENTER abre la sala con dos");
            yield break;
        }

        [UnityTest]
        public IEnumerator Sala_J2SeUneCambiaHabilidadModoExclusivoSaleYVuelve()
        {
            UseTestStorage();
            G.Settings = new MatchSettings(); G.Settings.Save();
            // La habilidad va con el personaje: J1 Sombra Carmesí (intercambio), J2 Alquimista (explosión)
            G.Loadouts[0].SetHero("crimson_shadow_rogue"); G.Loadouts[0].Save();
            G.Loadouts[1].SetHero("embercloak_alchemist"); G.Loadouts[1].Save();
            G.State = GameState.Title; G.OpenLobby(1);
            var L = G.Lobby;
            Assert.IsTrue(L.Joined(0) && !L.Joined(1), "empieza solo J1");
            Tap("Enter");
            Assert.IsTrue(L.Joined(1), "J2 se une con ENTER");
            // J1 activa el modo exclusivo y avanza: el Alquimista (explosión, de J2) se salta → Forajido (imán)
            Tap("KeyV");
            Assert.IsTrue(G.Settings.ExclusiveAbilities, "modo exclusivo activado");
            Tap("KeyD");
            Assert.AreEqual("feathered_outlaw", G.Loadouts[0].Hero, "J1 salta el personaje de habilidad ocupada");
            Assert.AreEqual("magnet", G.Loadouts[0].Ability);
            // J2 avanza: el Forajido (imán, de J1) se salta → Doncella Élfica (último recurso)
            Tap("ArrowRight");
            Assert.AreEqual("guard", G.Loadouts[1].Ability, "J2 salta la habilidad ocupada");
            // J2 sale: su hueco se libera y no cuenta
            Tap("ShiftRight");
            Assert.IsFalse(L.Joined(1), "J2 sale de la sala");
            Tap("KeyV");
            Assert.IsFalse(G.Settings.ExclusiveAbilities, "modo exclusivo desactivado");
            Tap("Enter");
            Assert.AreEqual("guard", G.Loadouts[1].Ability, "al volver recupera su elección guardada");
            Tap("KeyX");
            Assert.IsFalse(L.AllReady(), "falta J2");
            Assert.IsFalse(L.StartEnabled);
            Tap("Enter");
            Assert.IsTrue(L.AllReady(), "todos listos");
            Assert.IsTrue(L.StartEnabled);
            Step(200);
            Assert.AreEqual(GameState.Play, G.State, "la cuenta atrás inicia la partida");
            Assert.AreEqual(2, G.Players.Count);
            Assert.AreEqual("magnet,guard", string.Join(",", G.Players.Select(p => p.Ability.Id)), "cada jugador lleva la habilidad de su personaje");
            Assert.AreEqual("feathered_outlaw", G.Players[0].Parts.Rig.Entry.Id, "y entra con el personaje elegido");
            Assert.IsTrue(G.Ui.HudShown);
            Assert.IsFalse(L.IsOpen);
            yield break;
        }

        [UnityTest]
        public IEnumerator Sala_EscVuelveAlTituloYLaPausaNoSeActiva()
        {
            G.State = GameState.Title; G.OpenLobby(1);
            Tap("Escape");
            Assert.AreEqual(GameState.Title, G.State);
            Assert.IsFalse(G.Lobby.IsOpen, "la sala se oculta");
            Assert.IsTrue(G.Ui.TitleShown, "vuelve el título");
            Assert.IsFalse(G.Ui.PauseShown);
            yield break;
        }

        [UnityTest]
        public IEnumerator Sala_AjustesQuitanElListoYLaCuentaAtrasSeCancela()
        {
            G.State = GameState.Title; G.OpenLobby(1);
            var L = G.Lobby;
            Tap("KeyB"); Assert.AreEqual("always", G.Settings.BonusMode);
            StringAssert.Contains("Siempre", L.SettingText("bonusMode"));
            Tap("KeyB"); Assert.AreEqual("never", G.Settings.BonusMode);
            Tap("KeyB"); Assert.AreEqual("normal", G.Settings.BonusMode);
            Tap("KeyC"); Assert.IsFalse(G.Settings.SwapAllies);
            Tap("KeyC");
            // Listo y cuenta atrás; cambiar un ajuste quita el "listo"
            Tap("KeyX");
            Assert.IsTrue(L.AllReady());
            Step(30);
            Assert.IsNotNull(L.Countdown);
            StringAssert.StartsWith("¡Todos listos! Empieza en", L.StatusText);
            Tap("KeyB");
            Assert.IsFalse(L.Ready(0)); Assert.IsNull(L.Countdown);
            // Listo otra vez y desmarcar a tiempo cancela la cuenta atrás
            Tap("KeyX"); Step(60);
            Tap("KeyX");
            Assert.IsNull(L.Countdown);
            Step(200);
            Assert.AreEqual(GameState.Lobby, G.State, "sin todos listos no empieza");
            // Estando listo no se puede cambiar nada
            Tap("KeyX");
            string ab = G.Loadouts[0].Ability;
            Tap("KeyD");
            Assert.AreEqual(ab, G.Loadouts[0].Ability);
            StringAssert.StartsWith("Estás listo", L.NoteOf(0));
            yield break;
        }

        [UnityTest]
        public IEnumerator Sala_VistaPreviaSigueLaSeleccionYDemuestraLaHabilidad()
        {
            G.State = GameState.Title; G.OpenLobby(1);
            var pv = G.Lobby.PreviewOf(0);
            Step(2);
            Assert.IsTrue(pv.Active, "la cámara de la vista previa pinta");
            StringAssert.StartsWith("pot|", pv.ModelKey);
            // La ficha solo tiene el personaje (y "listo"): cambiar de personaje cambia el modelo y su habilidad
            Assert.AreEqual(0, G.Lobby.Row(0));
            string before = G.Loadouts[0].Hero;
            Tap("KeyD");
            string after = G.Loadouts[0].Hero;
            Assert.AreNotEqual(before, after);
            Assert.AreEqual(HeroCatalog.AbilityOf(after), G.Loadouts[0].Ability, "la habilidad es la del personaje");
            Step(1);
            StringAssert.EndsWith("|" + after, pv.ModelKey, "el modelo cambia con la selección");
            Step(200);
            Assert.GreaterOrEqual(pv.Demos, 2, "demostración del efecto cada 2.4 s");
            Tap("Escape");
            Assert.IsFalse(pv.Active, "al cerrar la sala la vista previa deja de pintar");
            yield break;
        }

        [UnityTest]
        public IEnumerator Cosmeticos_ElMunecoEnPartidaUsaLaSeleccionEIncompatiblesNoConviven()
        {
            G.Loadouts[0].SetCosmetic("body", "cape"); G.Loadouts[0].SetCosmetic("acc", "backpack"); G.Loadouts[0].SetCosmetic("head", "horns");
            var lo = G.Loadouts[0].Cosmetics;
            StartRun(1);
            var p = G.Players[0];
            Assert.AreEqual("backpack", lo["acc"]);
            Assert.AreEqual("tunic", lo["body"], "la capa se retira al poner la mochila");
            Assert.AreEqual("horns", lo["head"]);
            Assert.IsNull(p.Parts.Cape);
            Assert.AreSame(G.Loadouts[0], p.Loadout);
            yield break;
        }

        [UnityTest]
        public IEnumerator Pausa_PantallaYSimulacionCongelada()
        {
            StartRun(1);
            Tap("Escape");
            Assert.AreEqual(GameState.Paused, G.State);
            Assert.IsTrue(G.Ui.PauseShown);
            float t = G.Time;
            Step(30);
            Assert.AreEqual(t, G.Time);
            Tap("KeyP");
            Assert.AreEqual(GameState.Play, G.State);
            Assert.IsFalse(G.Ui.PauseShown);
            yield break;
        }

        [UnityTest]
        public IEnumerator Hud_PanelesSiguenVidaManoHabilidadEstadoYOro()
        {
            StartRun(1);
            var p = G.Players[0];
            Step(1);
            Assert.IsTrue(G.Ui.HudShown);
            Assert.IsFalse(G.Ui.TitleShown);
            Assert.AreEqual("1", G.Ui.FloorShown);
            Assert.IsTrue(G.Ui.PanelShown(0)); Assert.IsFalse(G.Ui.PanelShown(1), "J2 oculto en solitario");
            Assert.IsTrue(G.Ui.JoinHintShown, "J2: pulsa ENTER para unirte");
            Assert.AreEqual("100 / 100", G.Ui.PanelHpText(0));
            Assert.AreEqual("MANOS DESNUDAS", G.Ui.PanelWeaponName(0));
            Assert.AreEqual("TERREMOTO BRUTAL", G.Ui.PanelAbilityName(0));
            p.Iframes = 0f; p.Hurt(25, p.X + 1f, p.Z, 0);
            Assert.AreEqual("75 / 100", G.Ui.PanelHpText(0));
            p.SetWeapon(WeaponRegistry.Create("broom"));
            Assert.AreEqual("ESCOBA", G.Ui.PanelWeaponName(0));
            p.StunT = 0.8f; Step(5);
            StringAssert.Contains("aturdido", G.Ui.PanelStatus(0));
            p.Gear.Gold = 7; G.Ui.SetGold(0, 7);
            Assert.AreEqual("🪙 7", G.Ui.PanelGold(0));
            // J2 se une: aparece su panel; el aviso sigue mientras quepan más (hasta 4)
            Tap("Enter");
            Assert.IsTrue(G.Ui.PanelShown(1));
            Assert.IsTrue(G.Ui.JoinHintShown);
            Assert.IsFalse(G.Ui.PanelShown(2));
            Assert.AreEqual("GRITO DE GUERRA", G.Ui.PanelAbilityName(1));
            // Derribado
            var p2 = G.Players[1];
            p2.Hurt(999, p2.X, p2.Z, 0, new HurtOpts { Raw = true, Pierce = true });
            Assert.IsTrue(G.Ui.PanelDowned(1));
            // Al reanimarle, el panel deja de decir "DERRIBADO" (corrección respecto al original)
            p2.Revive(40f);
            Assert.IsFalse(G.Ui.PanelDowned(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator Sonido_SuenaSilenciaYNoSeSaturaConElMismoSonido()
        {
            StartRun(1);
            var a = G.Audio;
            int n0 = a.PlayedCount;
            a.Play("pickup");
            Assert.AreEqual("pickup", a.LastPlayed);
            Assert.AreEqual(n0 + 1, a.PlayedCount);
            a.Play("pickup");
            Assert.AreEqual(n0 + 1, a.PlayedCount, "el mismo sonido no se repite en menos de 35 ms");
            a.Play("coin");
            Assert.AreEqual(n0 + 2, a.PlayedCount);
            // El salto suena
            Tap("Space");
            Assert.AreEqual("jump", a.LastPlayed);
            // M silencia: no suena nada; otra M lo devuelve
            Tap("KeyM");
            Assert.IsTrue(a.Muted);
            int n1 = a.PlayedCount;
            a.Play("explosion");
            Assert.AreEqual(n1, a.PlayedCount);
            Tap("KeyM");
            Assert.IsFalse(a.Muted);
            yield break;
        }
    }
}
