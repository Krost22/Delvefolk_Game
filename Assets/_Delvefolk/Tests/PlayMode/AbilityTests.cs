using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 5: las 7 habilidades especiales (port de tests/e2e.test.js)
    public class AbilityTests : GameTestBase
    {
        // T.start(n, [habilidades])
        void StartWith(int n, params string[] abilities)
        {
            for (int i = 0; i < abilities.Length; i++) G.Loadouts[i].SetAbility(abilities[i]);
            StartRun(n);
        }

        [UnityTest]
        public IEnumerator Terremoto_AturdeEnemigosYCompaneroEnfriamientoSinDobleActivacionYTope()
        {
            StartWith(2, "quake", "guard");
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 68); Put(p2, 69.6f, 68);
            var e = SpawnAt('S', 68, 66.4f);
            float hp0 = e.Hp;
            Tap("KeyQ"); Step(2);
            Assert.Greater(e.Stun, 0.5f, "enemigo aturdido");
            Assert.AreEqual(8f, hp0 - e.Hp, "daño del terremoto");
            Assert.Greater(p2.StunT, 0f, "el compañero también queda aturdido");
            Assert.Greater(p1.Ability.Cd, 13f, "enfriamiento iniciado");
            Assert.AreEqual(1, p1.Ability.Serial);
            Assert.AreEqual(1, G.Stats.Abilities);
            Tap("KeyQ");
            Assert.AreEqual(1, p1.Ability.Serial, "no se puede reactivar durante el enfriamiento");
            Step(5);   // la pulsación espera a que acabe la pausa de impacto
            Assert.AreEqual(1, p1.Ability.Serial);
            Assert.AreEqual("ya activa", p1.Ability.BlockReason(), "mientras dura el temblor");
            Step(20);
            Tap("KeyQ");
            Assert.AreEqual(1, p1.Ability.Serial);
            Assert.IsTrue(G.Ui.HasFloatText("Recargando"), "aviso de recarga");
            p2.StunT = 0f; p2.StunGuard = 0f; p2.ApplyStun(10f);
            Assert.LessOrEqual(p2.StunT, 1.2f + 1e-6f, "aturdimiento con tope");
            Step(80);
            Assert.Greater(p2.StunGuard, 0f);
            Assert.IsFalse(p2.ApplyStun(1f), "inmunidad breve tras el aturdimiento");
            yield break;
        }

        [UnityTest]
        public IEnumerator Explosion_MuchoDanoSeHiereASiMismoYAlCompanero()
        {
            StartWith(2, "blast", "quake");
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 68); Put(p2, 69.5f, 68);
            var e = SpawnAt('G', 68, 66.6f);
            float hp0 = e.Hp;
            Tap("KeyQ"); Step(3);
            Assert.AreEqual(82f, p1.Hp, "se quita 18 a sí mismo");
            Assert.Less(p2.Hp, 100f, "el compañero recibe daño");
            Assert.GreaterOrEqual(hp0 - e.Hp, 27f, "daño alto al enemigo");
            yield break;
        }

        [UnityTest]
        public IEnumerator Carga_ArrollaAlEnemigoYChocarContraLaParedAturde()
        {
            StartWith(1, "charge");
            var p1 = G.Players[0];
            Put(p1, 68, 70);
            var e = SpawnAt('S', 68, 66);
            Face(p1, e.X, e.Z);
            float hp0 = e.Hp, z0 = p1.Z;
            Tap("KeyQ"); Step(12);
            Assert.Greater(p1.Z - z0, 2f, "avanza en línea recta");
            Assert.GreaterOrEqual(hp0 - e.Hp, 12f, "arrolla al enemigo");
            Assert.Greater(e.Stun, 0f);
            Step(60); p1.Ability.Cd = 0f; p1.StunT = 0f; p1.StunGuard = 0f;
            // Pared norte de la sala de la Entrada (fila 30): carga hacia ella
            e.Destroy(); G.Enemies.Clear();
            Put(p1, 64, 64.2f); p1.Facing = 0f;   // el norte del web (facing π) es +Z en Unity
            Tap("KeyQ"); Step(30);
            Assert.IsTrue(p1.StunT > 0f || !p1.Ability.Active, "se queda aturdido al chocar");
            Assert.Less(p1.Hp, 100f, "el golpe contra la pared duele");
            yield break;
        }

        [UnityTest]
        public IEnumerator Grito_InterrumpeAlEnemigoYAceleraAlCompanero()
        {
            StartWith(2, "shout", "quake");
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 68); Put(p2, 70, 68);
            var e = SpawnAt('G', 68, 65.5f);
            e.SetState(EnemyState.Windup);
            Tap("KeyQ"); Step(1);
            Assert.AreEqual(EnemyState.Chase, e.State, "ataque interrumpido");
            Assert.Greater(p2.HasteMult, 1.3f);
            Assert.Greater(p1.HasteMult, 1.3f, "velocidad para el equipo");
            yield break;
        }

        [UnityTest]
        public IEnumerator Intercambio_ConEnemigoConAliadoYSinObjetivoNoSeGasta()
        {
            StartWith(2, "swap", "quake");
            G.Settings.SwapAllies = true;
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 70); Put(p2, 64, 72);
            var e = SpawnAt('S', 68, 65);
            Face(p1, e.X, e.Z);
            Tap("KeyQ");
            Assert.Less(Mathf.Abs(p1.Z - (-65f)), 0.6f, "J1 pasa al sitio del enemigo");
            Assert.Less(Mathf.Abs(e.Z - (-70f)), 0.6f, "el enemigo pasa al de J1");
            Assert.Greater(e.Stun, 0f, "el enemigo queda desorientado");
            // Aliado: sin enemigos, J2 delante
            e.Destroy(); G.Enemies.Clear(); p1.Ability.Cd = 0f;
            Put(p1, 68, 70); Put(p2, 68, 66); Face(p1, p2.X, p2.Z);
            Tap("KeyQ");
            Assert.Less(Mathf.Abs(p2.Z - (-70f)), 0.6f, "intercambio con el aliado");
            Assert.Greater(p2.ConfusedT, 0f, "y se marea");
            // Sin objetivos válidos: no se activa ni gasta enfriamiento
            G.Settings.SwapAllies = false; p1.Ability.Cd = 0f;
            int s0 = p1.Ability.Serial;
            Tap("KeyQ");
            Assert.AreEqual(s0, p1.Ability.Serial);
            Assert.Less(p1.Ability.Cd, 1f, "sin objetivo no se gasta");
            Assert.IsTrue(G.Ui.HasFloatText("Nadie a quien cambiar"));
            yield break;
        }

        [UnityTest]
        public IEnumerator Iman_AtraeUnArmaQueGolpeaAlEnemigoDelCaminoYNoMueveLoFijo()
        {
            StartWith(1, "magnet");
            var p1 = G.Players[0];
            Put(p1, 68, 72);
            foreach (var k in G.Pickups.ToList()) G.RemovePickup(k);
            var pk = G.SpawnItemPickup(WeaponRegistry.Create("pan"), 68f, -65f);
            var e = SpawnAt('S', 68, 68.5f); e.Stun = 5f;   // quieto en medio del camino
            float hp0 = e.Hp;
            var statics = G.Statics.Select(s => new Vector2(s.Body.X, s.Body.Z)).ToList();
            Tap("KeyQ"); Step(90);
            Assert.Less(MathUtil.Hypot(pk.X - p1.X, pk.Z - p1.Z), 1.6f, "el arma llega al usuario");
            Assert.Greater(hp0 - e.Hp, 0f, "golpea al enemigo en su trayecto");
            for (int i = 0; i < statics.Count; i++)
                Assert.AreEqual(statics[i], new Vector2(G.Statics[i].Body.X, G.Statics[i].Body.Z), "columnas y cajas no se mueven");
            yield break;
        }

        [UnityTest]
        public IEnumerator UltimoRecurso_MenosDanoSinEmpujeSinInterrupcionYDespuesLentitud()
        {
            StartWith(1, "guard");
            var p1 = G.Players[0];
            Put(p1, 68, 70);
            Tap("KeyQ");
            Assert.IsTrue(p1.Parts.Shell.gameObject.activeSelf, "caparazón visible");
            var f = W(68, 69);
            p1.Hurt(20, f.x, f.y, 12);
            Assert.AreEqual(93f, p1.Hp, "20 de daño → 7");
            Assert.AreEqual(0f, MathUtil.Hypot(p1.Body.Kx, p1.Body.Kz), "inmune al empuje");
            Assert.IsFalse(p1.ApplyStun(1f), "inmune al aturdimiento");
            // Los golpes no interrumpen lo que haces (comer)
            p1.Hp = 50f; p1.SetWeapon(WeaponRegistry.Create("cheese"));
            Step(5);   // tras la pausa de impacto del golpe
            Tap("Space");
            Assert.IsTrue(p1.IsEating);
            p1.Iframes = 0f; p1.Hurt(5, p1.X - 1f, p1.Z, 0);
            Assert.IsTrue(p1.IsEating, "comer sigue aunque le peguen");
            Step(200);
            Assert.Less(p1.SlowMult, 1f, "ralentizado al terminar");
            Assert.IsFalse(p1.Parts.Shell.gameObject.activeSelf);
            yield break;
        }

        [UnityTest]
        public IEnumerator UltimoRecurso_ImpideQueElAbrazonTeAgarre()
        {
            StartWith(1, "guard");
            var p = G.Players[0];
            Put(p, 68, 70); Tap("KeyQ");
            var e = SpawnAt('A', 68, 68.4f);
            e.Facing = Mathf.Atan2(p.X - e.X, p.Z - e.Z); e.SetState(EnemyState.Attack);
            Step(2);
            Assert.IsNull(p.GrabbedBy, "el caparazón impide el agarre");
            Assert.Greater(e.Stun, 0f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Reglas_BloqueosTopeEnemigoYAvisoDeListo()
        {
            StartWith(1, "quake");
            var p = G.Players[0];
            // Aturdido: "¡Estoy mareado!" y no se activa
            p.StunT = 0.5f;
            Tap("KeyQ");
            Assert.AreEqual(0, p.Ability.Serial);
            Assert.IsTrue(G.Ui.HasFloatText("¡Estoy mareado!"));
            Assert.AreEqual("aturdido", p.Ability.BlockReason());
            p.StunT = 0f; Step(40);
            // Tope de aturdimiento de los enemigos
            var e = SpawnAt('G', 66, 66);
            Abilities.StunEnemy(e, 5f);
            Assert.AreEqual(1.6f, e.Stun, 1e-5f);
            // Lista otra vez: suena el aviso
            Tap("KeyQ");
            Assert.AreEqual(1, p.Ability.Serial);
            Step(30);   // el aviso solo suena con la habilidad ya terminada (0.35 s) y tras la pausa de impacto
            Assert.IsFalse(p.Ability.Active);
            p.Ability.Cd = 0.02f;
            Step(3);
            Assert.AreEqual(0f, p.Ability.Cd);
            Assert.AreEqual("abilityReady", G.Audio.LastPlayed);
            Assert.AreEqual(0f, p.Ability.CooldownK);
            yield break;
        }
    }
}
