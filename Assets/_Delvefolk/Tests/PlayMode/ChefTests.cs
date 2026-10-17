using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Monsieur Bocado, el Chef Carnicero (port de tests/boss-chef.test.js). El caldero está en (9, 17) del web.
    public class ChefTests : BossTestBase
    {
        ChefBoss boss; ChefCauldron C; Player p1, p2;

        void Begin()
        {
            var bf = StartBoss("chef");
            boss = (ChefBoss)bf.BossList[0]; C = boss.Cauldron;
            p1 = G.Players[0]; p2 = G.Players[1];
        }

        // Sin patrones automáticos: la prueba fuerza el que quiere probar
        void Calm() { boss.PatternGap = 999; boss.PatternCd = 999; boss.CancelPattern(); }

        // Jefe a 4 u del caldero mirando hacia él; J1 justo delante (lo agarra al aviso); J2 lejos
        void GrabSetup()
        {
            Calm();
            PutU(boss, C.X + 4, C.Z); boss.Facing = -Mathf.PI / 2f;
            PutU(p1, C.X + 2.8f, C.Z); p1.Facing = Mathf.PI / 2f;
            Put(p2, 26, 22);
        }

        [UnityTest]
        public IEnumerator Agarrar_LoLlevaAlCalderoYAllíSeCuece()
        {
            Begin(); GrabSetup();
            boss.StartPattern(Pattern(boss, "agarrar"));
            Step(60);
            Assert.AreSame(boss, p1.GrabbedBy, "el aviso agarra al jugador de delante");
            Step(60);
            Assert.AreSame(C, p1.GrabbedBy, "lo lleva al caldero");
            float hpIn = p1.Hp;
            Step(120);
            Assert.Less(p1.Hp, hpIn - 5, "dentro del caldero pierde vida");
            Assert.IsFalse(p1.Dead);
            yield break;
        }

        [UnityTest]
        public IEnumerator Rescate_ElCompaneroVuelcaElCalderoCon3Golpes()
        {
            Begin(); GrabSetup();
            boss.StartPattern(Pattern(boss, "agarrar")); Step(180);
            Assert.AreSame(C, p1.GrabbedBy, "el jugador está en el caldero");
            Put(boss, 26, 8);
            PutU(p2, C.X - 1.8f, C.Z); p2.Facing = Mathf.PI / 2f;
            var counts = new int[3]; bool heldAfterTwo = false;
            for (int i = 0; i < 3; i++)
            {
                p2.Cooldown = 0; Tap("Enter");
                for (int k = 0; k < 90 && !(p2.Attack != null && p2.Attack.Phase == AttackPhase.Active); k++) Step(1);
                for (int k = 0; k < 90 && p2.Attack != null; k++) Step(1);
                counts[i] = C.Rescues;
                if (i == 1) heldAfterTwo = p1.GrabbedBy == C;
            }
            Step(5);
            Assert.AreEqual("1,2,3", string.Join(",", counts), "cada golpe junto al caldero cuenta");
            Assert.IsTrue(heldAfterTwo, "con dos golpes sigue dentro");
            Assert.IsNull(p1.GrabbedBy, "al tercero sale libre");
            Assert.IsNull(C.Held);
            Assert.Greater(C.TipT, 0f, "el caldero queda volcado un rato");
            yield break;
        }

        [UnityTest]
        public IEnumerator Forcejeo_SaleMachacandoEnElAireYEnElCaldero()
        {
            Begin(); GrabSetup();
            boss.StartPattern(Pattern(boss, "agarrar")); Step(60);
            Assert.AreSame(boss, p1.GrabbedBy);
            for (int i = 0; i < 14 && p1.GrabbedBy != null; i++) Tap("KeyX");
            Assert.IsNull(p1.GrabbedBy, "sale machacando mientras lo lleva");
            Step(30);
            GrabSetup();
            boss.StartPattern(Pattern(boss, "agarrar")); Step(180);
            Assert.AreSame(C, p1.GrabbedBy);
            for (int i = 0; i < 16 && p1.GrabbedBy != null; i++) Tap("KeyX");
            Assert.IsNull(p1.GrabbedBy, "sale machacando dentro del caldero");
            yield break;
        }

        [UnityTest]
        public IEnumerator Platos_CaenYDejanComida()
        {
            Begin(); GrabSetup();
            Put(p1, 14, 16);
            int n0 = G.Pickups.Count;
            boss.StartPattern(Pattern(boss, "platos"));
            Step(60 * 5);
            var added = G.Pickups.Skip(n0).Select(pk => pk.Item.Id).ToList();
            Assert.AreEqual(4, added.Count, "cuatro platos en fase 1 (dejan comida)");
            Assert.IsTrue(added.All(id => id == "chicken" || id == "rottencheese"), string.Join(",", added));
            Assert.IsNull(boss.Current, "el patrón termina cuando caen todos");
            yield break;
        }

        [UnityTest]
        public IEnumerator QuesoPodrido_ConfundeYHaceDano()
        {
            Begin(); Calm();
            Put(boss, 26, 8); Put(p2, 4, 22); Put(p1, 16, 20);
            p1.Hp = 60;
            p1.SetWeapon(WeaponRegistry.Create("rottencheese"));
            p1.Cooldown = 0;
            Tap("KeyX");
            Assert.IsNotNull(p1.Eating, "empieza a comer");
            Step(90);
            Assert.That(60f - p1.Hp, Is.InRange(10f, 14f), "unos 12 de daño");
            Assert.Greater(p1.ConfusedT, 1f, "queda confundido");
            Assert.AreEqual("fists", p1.Weapon.Id, "se lo come entero");
            yield break;
        }

        [UnityTest]
        public IEnumerator Cuchillas_UnaDevueltaAturdeAlChef()
        {
            Begin(); Calm();
            Put(boss, 16, 10); boss.Facing = Mathf.PI; Put(p2, 26, 24);
            Put(p1, 16, 16); p1.Facing = 0f; p1.Cooldown = 0;
            float hp0 = boss.Hp;
            boss.StartPattern(Pattern(boss, "cuchillos"));
            Step(40);
            var knives = G.Projectiles.List.Where(pr => pr.Reflectable).ToList();
            Assert.AreEqual(3, knives.Count, "tres cuchillas");
            float maxStun = 0f;
            for (int i = 0; i < 80 && knives.Any(k => k.Hostile && !k.Dead); i++)
            {
                Step(1);
                maxStun = Mathf.Max(maxStun, boss.Stun);
                var k = knives.FirstOrDefault(kn => kn.Hostile && !kn.Dead && MathUtil.Hypot(kn.PX - p1.X, kn.PZ - p1.Z) < 2.0f);
                if (k != null && p1.Attack == null) { p1.Cooldown = 0; Tap("KeyX"); }
            }
            Assert.IsTrue(knives.Any(k => !k.Hostile), "un golpe devuelve una cuchilla");
            for (int i = 0; i < 60; i++) { Step(1); maxStun = Mathf.Max(maxStun, boss.Stun); }
            Assert.Greater(hp0 - boss.Hp, 0f, "la cuchilla devuelta daña al chef");
            Assert.GreaterOrEqual(maxStun, 0.9f, "queda aturdido ~1 s");
            yield break;
        }

        [UnityTest]
        public IEnumerator Fase2_FogonesQueAvisanYQueman()
        {
            Begin(); Calm();
            Put(boss, 26, 8); Put(p2, 28, 24);
            boss.Damage(Mathf.Ceil(boss.MaxHp * 0.6f), false, null);
            Assert.AreEqual(2, boss.Phase);
            Assert.That(boss.Hazard.CellCount, Is.InRange(6, 8));
            bool warn = false, danger = false;
            for (int i = 0; i < 16; i++)
            {
                Step(30);
                warn |= boss.Hazard.Count(HazardFloor.CellState.Warn) > 0;
                danger |= boss.Hazard.Count(HazardFloor.CellState.Danger) > 0;
            }
            Assert.IsTrue(warn && danger, "los fogones avisan y luego queman");
            Put(p1, 26, 20); p1.Hp = 100; p1.Iframes = 0;
            for (int i = 0; i < 700 && boss.Hazard.StateOf(1, 4) != HazardFloor.CellState.Danger; i++) Step(1);
            Put(p1, 2, 8);
            float hp0 = p1.Hp;
            Step(90);
            Assert.Greater(hp0 - p1.Hp, 0f, "pisar un fogón encendido quema");
            yield break;
        }

        [UnityTest]
        public IEnumerator Vencer_ConAlguienEnElCalderoLoLibera()
        {
            Begin(); GrabSetup();
            boss.StartPattern(Pattern(boss, "agarrar")); Step(180);
            Assert.AreSame(C, p1.GrabbedBy);
            boss.Hp = 1; boss.Damage(999, false, null);
            Step(60);
            Assert.IsTrue(boss.Dead);
            Assert.IsNull(p1.GrabbedBy, "al morir el jefe, quien está en el caldero queda libre");
            yield break;
        }
    }
}
