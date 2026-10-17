using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 8: sistema de jefes y Gólem (port de tests/bosses.test.js y tests/boss-golem.test.js)
    public abstract class BossTestBase : GameTestBase
    {
        // T.boss: combate de jefe directo (sin pasar por los pisos), con el jefe ya despierto
        protected BossFloor StartBoss(string id, int n = 2)
        {
            G.Settings.bonusMode = "never";
            StartRun(n);
            G.EnterBoss(2, 3, G.CollectCarry(), id);
            Step(Mathf.CeilToInt(BossConfig.IntroTime * 60f) + 2);
            return G.BossFloor;
        }

        protected static Hit By(Player p, float dmg, float dirWebX, float dirWebZ)
        {
            return Hit.Of(dmg, 0, 0, dirWebX, -dirWebZ, p);
        }

        protected BossPattern Pattern(Boss b, string id) { return b.Patterns.First(p => p.Id == id); }

        protected void Airborne(Player p, float webX, float webZ)
        {
            p.JumpY = 1.2f; p.JumpV = 0; p.Body.Airborne = true; Put(p, webX, webZ);
        }
    }

    public class BossTests : BossTestBase
    {

        // ---------- Sistema común ----------
        [UnityTest]
        public IEnumerator Jefe_TrasPiso2ArenaCerradaPresentacionSinDanoLuegoCombate()
        {
            G.Settings.bonusMode = "never";
            StartRun(2);
            G.LoadFloor(2, G.CollectCarry()); Step(2);
            var p = G.Players[0];
            PutU(p, G.Dungeon.ExitX, G.Dungeon.ExitZ - 1f); Step(2);
            Tap("KeyE"); Step(2);
            var bf = G.BossFloor;
            Assert.IsTrue(G.InBoss);
            Assert.AreEqual("golem", bf.Entry.Id, "el primero de BossConfig.Order");
            Assert.AreEqual("JEFE", G.Ui.FloorShown);
            Assert.IsFalse(G.Dungeon.GateOpen(bf.Cx, 2), "la salida empieza cerrada");
            var boss = bf.BossList[0];
            Assert.IsFalse(boss.IsAwake);
            float hp0 = boss.Hp; boss.TakeHit(Hit.Of(50, 0, 0, 0, 1));
            Assert.AreEqual(hp0, boss.Hp, "no recibe daño en la presentación");
            Step(Mathf.CeilToInt(BossConfig.IntroTime * 60f) + 2);
            Assert.IsTrue(boss.IsAwake);
            Assert.IsTrue(G.Ui.BossShown, "barra de jefe");
            Assert.Greater(boss.MaxHp, 0f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Jefe_VencerAbreRastrilloBotinParaCadaUnoYBajarLlevaALaVictoria()
        {
            var bf = StartBoss("golem");
            int log0 = G.RewardService.Log.Count;
            foreach (var b in bf.BossList) { b.Hp = 1; b.Invulnerable = false; b.Damage(999, false, null); }
            Step(60);
            Assert.AreEqual("won", bf.Phase);
            Assert.IsTrue(G.Dungeon.GateOpen(bf.Cx, 2));
            Assert.AreEqual(2, G.RewardService.Log.Count - log0, "un botín por jugador");
            Assert.IsFalse(G.Ui.BossShown);
            var p = G.Players[0];
            PutU(p, G.Dungeon.ExitX, G.Dungeon.ExitZ - 1f); Step(2);
            Tap("KeyE"); Step(2);
            Assert.AreEqual(GameState.Win, G.State, "tras el jefe del piso 2: pantalla de victoria");
            yield break;
        }

        [UnityTest]
        public IEnumerator Avisos_GolpeanAlTerminarOndasSeEsquivanSaltandoProyectilesDevueltosDanan()
        {
            var bf = StartBoss("golem");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var boss = bf.BossList[0];
            boss.IsAwake = false;   // quieto para la prueba
            Put(p1, 16, 18); Put(p2, 24, 18);
            bool fired = false;
            G.Telegraphs.Add(Shape.Circle(16, -18, 1.5f), 0.5f, h => { fired = true; BossHits.HitPlayersIn(G, h.Shape, 10, 5, 16, -17); });
            Step(20); Assert.AreEqual(100f, p1.Hp, "antes de terminar el aviso no hay daño");
            Step(15); Assert.IsTrue(fired); Assert.AreEqual(90f, p1.Hp);
            // Onda: J1 en el suelo recibe, J2 saltando no
            p1.Hp = 100; p2.Hp = 100; p1.Iframes = 0; p2.Iframes = 0;
            Put(p1, 16, 22); Put(p2, 16, 14);
            G.Telegraphs.AddWave(16, -18, new Wave { Speed = 8, MaxR = 6, Damage = 12 });
            for (int i = 0; i < 60; i++) { Airborne(p2, 16, 14); Step(1); }
            Assert.Less(p1.Hp, 100f, "la onda golpea a quien está en el suelo");
            Assert.AreEqual(100f, p2.Hp, "saltando se esquiva");
            // Proyectil: J1 lo devuelve de un golpe y daña al jefe
            p2.EndJump(); Put(p2, 26, 24);
            boss.IsAwake = true; boss.Stun = 99; Put(boss, 16, 10); Put(p1, 16, 16); p1.Facing = 0f; p1.Cooldown = 0;
            float hp0 = boss.Hp;
            var pr = G.Projectiles.Spawn(new Projectile { PX = 16, PZ = -12, Vx = 0, Vz = -5, Reflectable = true, Damage = 10 });
            for (int i = 0; i < 30 && pr.Hostile; i++)
            {
                Step(1);
                if (Mathf.Abs(pr.PZ - p1.Z) < 2.2f && p1.Attack == null) { p1.Cooldown = 0; Tap("Space"); }
            }
            Assert.IsFalse(pr.Hostile, "el golpe devuelve el proyectil");
            Step(40);
            Assert.Greater(hp0 - boss.Hp, 0f, "el proyectil devuelto daña al jefe");
            yield break;
        }

        [UnityTest]
        public IEnumerator Jefes_TodosRegistradosSeCreanYLaVidaEscalaConJugadores()
        {
            foreach (var id in BossConfig.Order)
            {
                if (Bosses.Get(id) == null) continue;   // se registran según se portan
                var bf1 = StartBoss(id, 1); float hp1 = bf1.BossList.Sum(x => x.MaxHp); Step(30);
                Object.DestroyImmediate(Loop.gameObject);
                Loop = new GameObject("Bootstrap").AddComponent<GameLoop>(); G.InstantTransitions = true;
                var bf2 = StartBoss(id, 2); float hp2 = bf2.BossList.Sum(x => x.MaxHp); Step(120);
                Assert.Greater(hp2, hp1, id + ": más vida con 2 jugadores");
                Object.DestroyImmediate(Loop.gameObject);
                Loop = new GameObject("Bootstrap").AddComponent<GameLoop>(); G.InstantTransitions = true;
            }
            yield break;
        }

        // ---------- Gólem ----------
        [UnityTest]
        public IEnumerator Golem_ArmaduraPorDelanteCasiNadaPorLaEspaldaElDoble()
        {
            var bf = StartBoss("golem");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var boss = bf.BossList[0];
            boss.Stun = 99; boss.Facing = Mathf.PI;   // mira a +z del web
            Put(boss, 16, 16); Put(p1, 16, 22); Put(p2, 16, 10);
            Step(2);
            boss.Facing = Mathf.PI;
            boss.Hp = boss.MaxHp; boss.TakeHit(By(p1, 40, 0, -1)); Assert.AreEqual(4f, boss.MaxHp - boss.Hp, "por delante: 40 x 0,1");
            boss.Hp = boss.MaxHp; boss.TakeHit(By(p1, 5, 0, -1)); Assert.AreEqual(boss.MaxHp, boss.Hp, "un golpe flojo por delante se bloquea");
            boss.Hp = boss.MaxHp; boss.TakeHit(By(p2, 40, 0, 1)); Assert.AreEqual(80f, boss.MaxHp - boss.Hp, "por detrás: 40 x 2");
            yield break;
        }

        [UnityTest]
        public IEnumerator Golem_RabiaPersigueAlUltimoQueLeGolpeoConRotulo()
        {
            var bf = StartBoss("golem");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var boss = (GolemBoss)bf.BossList[0];
            boss.CancelPattern(); boss.Stun = 99;
            Put(boss, 16, 16); Put(p1, 16, 19); Put(p2, 16, 8);
            Step(35);
            Assert.AreSame(p1, boss.Target, "antes de golpear va al más cercano");
            boss.TakeHit(By(p2, 1, 0, -1)); Step(1);
            Assert.AreSame(p2, boss.Target); Assert.AreSame(p2, boss.ForcedTarget, "tras el golpe de p2, le persigue a él");
            Assert.AreEqual("¡TE ODIA!", boss.TauntText); Assert.AreSame(p2, boss.TauntFor, "rótulo sobre p2");
            boss.TakeHit(By(p1, 1, 0, 1)); Step(1);
            Assert.AreSame(p1, boss.Target); Assert.AreSame(p1, boss.TauntFor, "si p1 le golpea después, pasa a perseguir a p1");
            boss.Destroy();
            Assert.IsNull(boss.TauntText, "el rótulo se quita al destruir al jefe");
            yield break;
        }

        [UnityTest]
        public IEnumerator Golem_RocaDevueltaLeAturde22()
        {
            var bf = StartBoss("golem");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var boss = (GolemBoss)bf.BossList[0];
            boss.CancelPattern(); boss.Stun = 0; boss.Facing = Mathf.PI;
            Put(boss, 16, 10); Put(p1, 16, 17); Put(p2, 26, 24);
            boss.LastRock = null;
            boss.StartPattern(Pattern(boss, "roca"));
            for (int i = 0; i < 90 && boss.LastRock == null; i++) Step(1);
            var r = boss.LastRock;
            Assert.IsNotNull(r, "la roca se lanza");
            r.TakeHit(By(p1, 1, 0, -1));
            Step(2);
            Assert.IsFalse(r.Hostile, "la roca queda devuelta");
            Assert.IsTrue(r.Dead, "la roca desaparece al golpear al gólem");
            Assert.That(boss.Stun, Is.GreaterThan(2.0f).And.LessThanOrEqualTo(2.2f), "aturdido ~2,2 s");
            Assert.IsNull(boss.Current, "el ataque se corta al aturdirle");
            yield break;
        }

        [UnityTest]
        public IEnumerator Golem_PisotonOndaGolpeaEnElSueloYNoAlQueSalta()
        {
            var bf = StartBoss("golem");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var boss = bf.BossList[0];
            boss.CancelPattern(); boss.Stun = 0;
            Put(boss, 16, 14);
            Put(p1, 16, 19); p1.Hp = 100; p1.Iframes = 0;
            Put(p2, 21, 14); p2.Hp = 100; p2.Iframes = 0;
            boss.StartPattern(Pattern(boss, "pisoton"));
            for (int i = 0; i < 150; i++) { Airborne(p2, 21, 14); Step(1); }
            Assert.Less(p1.Hp, 100f, "la onda golpea a quien está en el suelo");
            Assert.AreEqual(100f, p2.Hp, "saltando se esquiva la onda");
            yield break;
        }

        [UnityTest]
        public IEnumerator Golem_EmbestidaContraColumnaAturde15()
        {
            var bf = StartBoss("golem");
            Player p1 = G.Players[0], p2 = G.Players[1];
            var boss = bf.BossList[0];
            boss.Damage(Mathf.Ceil(boss.Hp - boss.MaxHp * 0.45f), false, null);
            Assert.AreEqual(2, boss.Phase, "fase 2");
            boss.CancelPattern(); boss.Stun = 0;
            Put(boss, 6, 24); Put(p1, 6, 12); Put(p2, 26, 24);
            boss.ForcedTarget = p1;   // la columna (6,18) queda entre el jefe y p1
            boss.StartPattern(Pattern(boss, "embestida"));
            for (int i = 0; i < 200 && boss.Current != null; i++) Step(1);
            Assert.That(boss.Stun, Is.GreaterThan(1.4f).And.LessThanOrEqualTo(1.5f), "aturdido ~1,5 s tras el choque");
            Assert.Less(boss.Z, -19f, "se detiene antes de la columna");
            yield break;
        }

        [UnityTest]
        public IEnumerator Golem_Fase2BajoLaMitadMasRapido()
        {
            var bf = StartBoss("golem");
            var boss = bf.BossList[0];
            Assert.AreEqual(1.9f, boss.Cfg.Speed);
            boss.Damage(Mathf.Ceil(boss.Hp - boss.MaxHp * 0.6f), false, null);
            Assert.AreEqual(1, boss.Phase, "con el 60 % de vida sigue en fase 1");
            boss.Damage(Mathf.Ceil(boss.Hp - boss.MaxHp * 0.45f), false, null);
            Assert.AreEqual(2, boss.Phase);
            Assert.AreEqual(2.4f, boss.Cfg.Speed, "más rápido en fase 2");
            yield break;
        }

        [UnityTest]
        public IEnumerator Golem_BarrilesTrasElAvisoCaenDos()
        {
            var bf = StartBoss("golem");
            var boss = bf.BossList[0];
            boss.Damage(Mathf.Ceil(boss.Hp - boss.MaxHp * 0.45f), false, null);
            boss.CancelPattern(); boss.Stun = 0;
            int count0 = G.Barrels.Count(x => x.Explosive && !x.Dead);
            boss.StartPattern(Pattern(boss, "barriles"));
            Step(30); Assert.AreEqual(0, G.Barrels.Count(x => x.Explosive && !x.Dead) - count0, "antes del aviso no cae nada");
            Step(60); Assert.AreEqual(2, G.Barrels.Count(x => x.Explosive && !x.Dead) - count0, "caen dos barriles explosivos");
            Assert.IsTrue(G.Barrels.Where(x => x.Explosive && !x.Dead).All(x => x.X > 1 && x.X < 31 && x.Z < -5 && x.Z > -27), "dentro de la zona jugable");
            yield break;
        }
    }
}
