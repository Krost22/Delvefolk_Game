using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 3: cooperativo local (port de tests/e2e.test.js y de las reglas del §6.3)
    public class CoopTests : GameTestBase
    {
        static readonly HurtOpts Kill = new HurtOpts { Raw = true, Pierce = true };

        static float Dist(Player a, Player b) { return MathUtil.Hypot(a.X - b.X, a.Z - b.Z); }

        // Mantener una tecla durante n frames (T.press … T.release)
        void HoldFor(string code, int frames) { G.Input.Tap(code); Step(frames); G.Input.Release(code); }

        [UnityTest]
        public IEnumerator J2_SeUneAMitadDePartidaYCuentaParaReintentar()
        {
            StartRun(1);
            var p1 = G.Players[0];
            Assert.AreEqual(1, G.Players.Count);
            Assert.IsNotNull(p1.Controls.Alt, "en solitario el J1 también se mueve con las flechas");
            Tap("Enter");
            Assert.AreEqual(2, G.Players.Count, "¡J2 se une!");
            Assert.AreEqual(2, G.NumPlayers);
            var p2 = G.Players[1];
            Assert.Less(Dist(p1, p2), 3f, "aparece junto al J1");
            Assert.IsFalse(G.Dungeon.IsSolidFor(Grid.TileX(p2.X), Grid.TileZ(p2.Z), p2.Body), "en una casilla libre");
            Assert.IsNull(p1.Controls.Alt, "con dos, las flechas son del J2");
            Assert.IsTrue(G.Ui.HasFloatText("¡J2 se une!"));
            Assert.AreEqual(2, G.FloorStart.Length, "entra en lo que se restaura al reintentar");
            Assert.AreEqual("fists", G.FloorStart[1].Item.id);
            // El J2 se mueve con las flechas y el J1 se queda quieto
            float x1 = p1.X, z1 = p1.Z, x2 = p2.X, z2 = p2.Z;
            HoldFor("ArrowUp", 20);
            Assert.Greater(MathUtil.Hypot(p2.X - x2, p2.Z - z2), 1f);
            Assert.Less(MathUtil.Hypot(p1.X - x1, p1.Z - z1), 0.05f);
            // Un segundo Enter ya no añade a nadie
            Tap("Enter");
            Assert.AreEqual(2, G.Players.Count);
            // Reintentar el piso vuelve con los dos
            foreach (var p in G.Players) p.Hurt(999, p.X, p.Z, 0, Kill);
            Step(100);
            Assert.AreEqual(GameState.Dead, G.State);
            Press("KeyR"); Step(1);
            Assert.AreEqual(2, G.Players.Count);
            yield break;
        }

        [UnityTest]
        public IEnumerator J2_ConHabilidadesExclusivasTomaLaSiguienteLibre()
        {
            StartRun(1);
            G.Settings.ExclusiveAbilities = true;
            G.Loadouts[1].SetAbility(G.Players[0].Ability.Id);
            Tap("Enter");
            Assert.AreEqual("quake", G.Players[0].Ability.Id);
            Assert.AreEqual("blast", G.Players[1].Ability.Id, "la siguiente del orden que no tiene el J1");
            yield break;
        }

        [UnityTest]
        public IEnumerator Correa_NoSeAlejaMasDe17DelCompanero()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            // Fila 22 del piso 1: un pasillo largo y despejado de oeste a este
            PutU(p1, Grid.X(6), Grid.Z(22)); PutU(p2, Grid.X(16), Grid.Z(22));
            Step(1);
            float d0 = Dist(p1, p2);
            Assert.Greater(d0, 17f);
            // Derecha + abajo de la cámara = +X del mundo: alejarse en línea recta
            G.Input.Tap("ArrowRight"); G.Input.Tap("ArrowDown"); Step(60);
            G.Input.Release("ArrowRight"); G.Input.Release("ArrowDown");
            Assert.Less(Dist(p1, p2) - d0, 0.3f, "la correa anula la componente que aleja");
            // Hacia el compañero sí puede ir
            G.Input.Tap("ArrowLeft"); G.Input.Tap("ArrowUp"); Step(60);
            G.Input.Release("ArrowLeft"); G.Input.Release("ArrowUp");
            Assert.Less(Dist(p1, p2), d0 - 3f);
            // Con el compañero derribado no hay correa
            Step(30);
            float d1 = Dist(p1, p2);
            p1.Hurt(999, p1.X, p1.Z, 0, Kill);
            G.Input.Tap("ArrowRight"); G.Input.Tap("ArrowDown"); Step(60);
            G.Input.Release("ArrowRight"); G.Input.Release("ArrowDown");
            Assert.Greater(Dist(p1, p2), d1 + 3f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Agarrar_SoltarYLanzarAlCompaneroContraUnMonstruo()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 70); Put(p2, 69.2f, 70);
            Step(1);
            Assert.AreEqual("Agarrar a J2", G.Ui.PromptOf(0));
            Tap("KeyE");
            Assert.AreSame(p2, p1.Carrying); Assert.AreSame(p1, p2.CarriedBy);
            Assert.IsFalse(p2.Body.Enabled);
            Assert.IsFalse(p2.Targetable);
            Step(1);
            Assert.AreEqual("{A} Lanzar · {E} Soltar", G.Ui.PromptOf(0));
            Assert.AreEqual(1.75f, p2.Root.position.y, 0.01f, "va en alto sobre el J1");
            // Soltar: aparece delante con un empujoncito
            Face(p1, p1.X + 1f, p1.Z);
            Tap("KeyE"); Step(20);
            Assert.IsNull(p2.CarriedBy); Assert.IsNull(p1.Carrying);
            Assert.IsTrue(p2.Body.Enabled);
            Assert.Greater(p2.X - p1.X, 0.5f, "lo deja delante");
            // Lanzar contra un goblin: ¡COMPAÑERAZO! (a los dos les duele)
            Put(p1, 68, 70); Put(p2, 68.8f, 70); Step(1);
            p1.Cooldown = 0f; p1.Grab(p2); Step(1);
            var e = SpawnAt('G', 68, 66); e.Stun = 5f;
            float hp0 = e.Hp;
            Face(p1, e.X, e.Z); p1.Cooldown = 0f;
            Tap("Space");
            Assert.IsTrue(p2.IsThrown, "sale volando");
            Assert.IsNull(p1.Carrying);
            bool hit = false;
            for (int i = 0; i < 60 && !hit; i++) { Step(1); hit = e.Hp < hp0; }
            Assert.IsTrue(hit, "el lanzado arrolla al monstruo");
            Assert.GreaterOrEqual(hp0 - e.Hp, 20f);
            Assert.IsTrue(G.Ui.HasFloatText("¡COMPAÑERAZO!"));
            Assert.AreEqual(90f, p2.Hp, "el lanzado recibe 10");
            Step(90);
            Assert.IsFalse(p2.IsThrown, "acaba aterrizando");
            yield break;
        }

        [UnityTest]
        public IEnumerator Forcejeo_UnaPulsacionNoBastaMachacandoSeSuelta()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 70); Put(p2, 69, 70);
            p1.Grab(p2); Step(1);
            Tap("Enter"); Step(2);
            Assert.IsNotNull(p2.CarriedBy, "una pulsación no basta");
            StringAssert.Contains("Machaca", G.Ui.PromptOf(1) ?? "", "aviso de forcejeo");
            for (int i = 0; i < 3; i++) Tap("Enter");
            Assert.IsTrue(p2.CarriedBy == null && p1.Carrying == null, "machacando se suelta");
            // Con pausas largas entre pulsaciones, la barra se vacía y no se suelta
            p1.Grab(p2); Step(1);
            for (int i = 0; i < 3; i++) { Tap("ShiftRight"); Step(90); }   // 4.5 s: antes de la suelta automática de los 6 s
            Assert.IsNotNull(p2.CarriedBy, "pulsar muy despacio no sirve");
            Step(100);
            Assert.IsNull(p2.CarriedBy, "a los 6 s se escapa solo");
            yield break;
        }

        [UnityTest]
        public IEnumerator PisotonAlCompanero_DanaAturdeYSiLoDerribaQuedaAplastado()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p2, 68, 70); Put(p1, 68, 70);
            p1.JumpY = 1.4f; p1.JumpV = -1f; p1.Body.Airborne = true;
            Step(1);
            Assert.AreEqual(90f, p2.Hp, "-10 de vida");
            Assert.Greater(p2.StunT, 0f, "aturdido");
            Assert.Greater(p1.JumpV, 5f, "rebota");
            // Lo derriba de un pisotón: aplastado
            Step(120); p2.StunT = 0f; p2.StunGuard = 0f; p2.Hp = 5f; p2.Iframes = 0f;
            PutU(p1, p2.X, p2.Z); p1.EndJump(); p1.JumpY = 1.4f; p1.JumpV = -1f; p1.Body.Airborne = true; p1.StompCd.Clear();
            Step(15);
            Assert.IsTrue(p2.Dead);
            Assert.Less(p2.Model.localScale.y, 0.2f, "aplastado");
            Assert.Greater(p2.Model.localScale.x, 1.3f);
            p2.Revive(40f); Step(2);
            Assert.Less(Mathf.Abs(p2.Model.localScale.y - 1f), 0.2f, "al reanimarle vuelve a su forma");
            yield break;
        }

        [UnityTest]
        public IEnumerator Reanimar_MantenerDosSegundosLoLevantaCon40()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 70); Put(p2, 69.2f, 70);
            p2.Hurt(999, p2.X, p2.Z, 0, Kill);
            Step(5);   // tras la pausa de impacto del golpe
            Assert.IsTrue(p2.Dead);
            Assert.AreEqual(GameState.Play, G.State, "con el compañero en pie la partida sigue");
            Assert.AreEqual("Mantén {E} para reanimar a J2", G.Ui.PromptOf(0));
            HoldFor("KeyE", 60);
            Assert.IsTrue(p2.Dead, "un segundo no basta");
            Assert.AreEqual(0.5f, G.Ui.PlayerBarOf(1), 0.05f, "barra de reanimar sobre el derribado");
            Step(1);
            Assert.AreEqual(0f, p2.ReviveT, "al soltar se pierde lo avanzado");
            Assert.Less(G.Ui.PlayerBarOf(1), 0f);
            HoldFor("KeyE", 125);
            Assert.IsFalse(p2.Dead, "¡De vuelta!");
            Assert.AreEqual(40f, p2.Hp);
            Assert.Greater(p2.Iframes, 0f);
            Assert.IsFalse(p2.Body.Ghost);
            Step(100);
            Assert.AreEqual(GameState.Play, G.State, "no hay derrota");
            yield break;
        }

        [UnityTest]
        public IEnumerator DarDeComer_CuraLevantaAlDerribadoYUnGolpeLoInterrumpe()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 70); Put(p2, 69.4f, 70);
            p2.Hp = 50f; G.Ui.SetHealth(1, 50f, 100f);
            p1.SetWeapon(WeaponRegistry.Create("chicken"));
            Step(1);
            StringAssert.Contains("dar de comer a J2", G.Ui.PromptOf(0) ?? "");
            G.Input.Tap("KeyE"); Step(20);
            Assert.AreEqual(50f, p2.Hp, "mantener un rato no cura todavía");
            p1.Hurt(5, p1.X - 1f, p1.Z, 0);
            Assert.AreEqual(0f, p1.FeedT, "el golpe reinicia la acción");
            p1.Iframes = 0f; Step(Mathf.CeilToInt(FeedConfig.Time * 60f) + 4); G.Input.Release("KeyE");
            Assert.AreEqual(85f, p2.Hp, "+35 del muslo de pollo");
            Assert.AreEqual("fists", p1.Weapon.Id, "la comida se gasta");
            Assert.AreEqual(1, G.Stats.Fed);
            // Compañero derribado: con queso se levanta con 40 + 20
            p2.Hurt(999, p2.X, p2.Z, 0, Kill);
            p1.SetWeapon(WeaponRegistry.Create("cheese"));
            Put(p1, 68, 70); Step(5);
            StringAssert.Contains("(¡arriba!)", G.Ui.PromptOf(0) ?? "");
            HoldFor("KeyE", Mathf.CeilToInt(FeedConfig.ReviveTime * 60f) + 4);
            Assert.IsFalse(p2.Dead);
            Assert.AreEqual(60f, p2.Hp, "derribado: 40 + 20 del queso");
            // Con la vida llena no se ofrece (sale "Agarrar")
            p2.Hp = 100f; p1.SetWeapon(WeaponRegistry.Create("chicken")); Step(2);
            StringAssert.DoesNotContain("comer", G.Ui.PromptOf(0) ?? "");
            yield break;
        }

        [UnityTest]
        public IEnumerator VidaSobreLaCabeza_SigueLaVidaYSeOcultaAlCaer()
        {
            StartRun(2);
            var p2 = G.Players[1];
            p2.Hp = 100f; p2.Iframes = 0f; p2.Hurt(40, p2.X + 1f, p2.Z, 0);
            Step(2);
            Assert.IsTrue(G.Ui.HeadShown(1));
            Assert.AreEqual(0.6f, G.Ui.HeadFill(1), 1e-3f);
            Assert.AreEqual("J2", G.Ui.HeadName(1));
            p2.Hurt(999, p2.X, p2.Z, 0, Kill); Step(2);
            Assert.IsFalse(G.Ui.HeadShown(1), "oculta al caer");
            Assert.IsTrue(G.Ui.HeadShown(0));
            yield break;
        }

        [UnityTest]
        public IEnumerator Camara_EncuadraALosDosYSeAlejaSiSeSeparan()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            PutU(p1, Grid.X(6), Grid.Z(22)); PutU(p2, Grid.X(14), Grid.Z(22));
            Step(150);
            var cam = G.CameraCtl;
            float mx = (p1.X + p2.X) / 2f, mz = (p1.Z + p2.Z) / 2f;
            Assert.Less(MathUtil.Hypot(cam.TargetX - mx, cam.TargetZ - mz), 0.5f, "mira al punto medio");
            float d = Dist(p1, p2);
            Assert.AreEqual(Mathf.Clamp(d * 0.6f - 4f, 0f, 10f), cam.Extra, 0.2f, "se aleja para que quepan");
            // Si uno cae, encuadra al que sigue en pie
            p2.Hurt(999, p2.X, p2.Z, 0, Kill);
            Step(150);
            Assert.Less(MathUtil.Hypot(cam.TargetX - p1.X, cam.TargetZ - p1.Z), 0.5f);
            Assert.Less(cam.Extra, 0.2f);
            yield break;
        }
    }
}
