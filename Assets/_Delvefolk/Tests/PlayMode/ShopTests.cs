using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 7: tienda, Duende Tendero, escopeta y armas especiales (port de tests/e2e.test.js)
    public class ShopTests : GameTestBase
    {
        // toShop del original: bonus del piso 1 terminado → tienda
        ShopFloor ToShop(int gold)
        {
            StartRun(2);
            G.Players[0].Gear.Gold = gold;
            G.Bonus.ForceNext = "minefield";
            G.Descend();
            Step(1);
            TapAll("Space", "Enter");
            G.Bonus.Challenge.Finish(false, "x");
            Step(110);
            TapAll("Space", "Enter");
            Step(1);
            Assert.IsTrue(G.InShop);
            return G.Shop;
        }

        static Hit H(float dmg) { return Hit.Of(dmg, 0, 0, 0, 1); }

        [UnityTest]
        public IEnumerator Tienda_TrasBonusComprarExigeMonedasUnaUnidadYBajarLlevaAlSiguiente()
        {
            var sh = ToShop(50);
            Player p1 = G.Players[0], p2 = G.Players[1];
            Assert.AreEqual("sword,spear,chicken,axe,maul", string.Join(",", sh.Slots.Select(s => s.Id)));
            Assert.IsTrue(sh.Keeper.Peaceful);
            Assert.AreEqual("TIENDA", G.Ui.FloorShown);
            var maul = sh.Slots.First(s => s.Id == "maul"); var sword = sh.Slots.First(s => s.Id == "sword");
            Assert.IsFalse(sh.RequestBuy(p2, sword), "J2 no tiene monedas");
            Assert.IsFalse(sh.RequestBuy(p1, maul), "55 > 50");
            PutU(p1, sword.X + 1.2f, sword.Z); Step(2);
            Tap("KeyE");
            Assert.AreEqual("sword", p1.Weapon.Id, "compra con E");
            Assert.AreEqual(15, p1.Gear.Gold, "50 - 35");
            Assert.IsTrue(sword.Sold);
            p2.Gear.Gold = 99;
            Assert.IsFalse(sh.RequestBuy(p2, sword), "una sola unidad");
            Assert.AreEqual("sin monedas,sin monedas,agotado", string.Join(",", sh.Log.Where(l => l.Type == "buy-rejected").Select(l => l.Why)));
            // Bajar por las escaleras de la tienda
            PutU(p1, G.Dungeon.ExitX, G.Dungeon.ExitZ - 1f); Step(2);
            Tap("KeyE");
            Step(2);
            Assert.AreEqual(GameState.Play, G.State);
            Assert.AreEqual(2, G.Floor);
            Assert.IsFalse(G.InShop); Assert.IsFalse(G.InBonus);
            Assert.AreEqual("sword", G.Players[0].Weapon.Id, "con el arma comprada");
            yield break;
        }

        [UnityTest]
        public IEnumerator Duende_GolpearleLoEnfadaCierraLaTiendaDisparaConAvisoYSaltarEsquiva()
        {
            var sh = ToShop(99);
            var k = sh.Keeper;
            Player p1 = G.Players[0], p2 = G.Players[1];
            PutU(p1, k.X, k.Z - 1.6f);
            Assert.IsNull(G.Combat.FindAimTarget(p1, p1.Weapon.AttackRange, Mathf.PI), "la asistencia de puntería ignora al duende tranquilo");
            Face(p1, k.X, k.Z); p1.Cooldown = 0; Tap("Space"); Step(20);
            Assert.IsFalse(k.Peaceful, "golpearle lo enfada");
            Assert.GreaterOrEqual(k.MaxHp, 300f);
            Assert.IsFalse(sh.Open, "la tienda cierra");
            Assert.IsFalse(sh.RequestBuy(p1, sh.Slots[0]));
            Assert.IsTrue(G.Ui.BossShown);
            // Le dejamos apuntar a J2 (lejos de J1) y disparar
            PutU(p1, k.X - 6, k.Z - 6); PutU(p2, k.X + 4, k.Z - 1);
            bool aimed = false; float hp0 = p2.Hp;
            for (int i = 0; i < 400 && p2.Hp == hp0; i++) { Step(1); if (k.Mode == KeeperMode.Aim) aimed = true; k.Stun = 0; PutU(p2, k.X + 4, k.Z - 1); }
            Assert.IsTrue(aimed, "apunta antes de disparar");
            Assert.GreaterOrEqual(hp0 - p2.Hp, 8f, "el disparo hace daño");
            // Saltando justo al disparar no le da
            p2.Hp = 100; p2.Iframes = 0;
            for (int i = 0; i < 600 && k.Mode != KeeperMode.Aim; i++) { Step(1); PutU(p2, k.X + 4, k.Z - 1); }
            for (int i = 0; i < 120 && k.Mode == KeeperMode.Aim; i++) { p2.JumpY = 1.3f; p2.JumpV = 0; p2.Body.Airborne = true; PutU(p2, k.X + 4, k.Z - 1); Step(1); }
            Assert.AreEqual(100f, p2.Hp, "saltar esquiva el disparo");
            yield break;
        }

        [UnityTest]
        public IEnumerator Duende_DerrotadoSueltaEscopetaTiendaGratisYReintentarLaRecarga()
        {
            var sh = ToShop(0);
            var k = sh.Keeper;
            var p1 = G.Players[0];
            k.TakeHit(H(5));
            k.Hp = 1; k.TakeHit(H(5));
            Step(5);
            Assert.IsTrue(k.Dead);
            Assert.IsTrue(G.Pickups.Any(x => x.Item.Id == "shotgun"), "suelta su escopeta");
            Assert.GreaterOrEqual(G.Coins.Count, 25, "y monedas");
            Assert.IsTrue(sh.Free); Assert.IsTrue(sh.Open);
            Assert.IsFalse(G.Ui.BossShown);
            Assert.IsTrue(sh.RequestBuy(p1, sh.Slots.First(s => s.Id == "maul")), "gratis aunque no tengas monedas");
            Assert.AreEqual("maul", p1.Weapon.Id);
            // Morir en la tienda y reintentar: vuelve la tienda con el duende tranquilo
            foreach (var p in G.Players) p.Hurt(999, p.X, p.Z, 0, new HurtOpts { Raw = true, Pierce = true });
            Step(120);
            Assert.AreEqual(GameState.Dead, G.State);
            G.Retry(); Step(2);
            Assert.IsTrue(G.InShop && G.Shop != null && G.Shop.Keeper.Peaceful && G.Shop.Slots.All(s => !s.Sold));
            yield break;
        }

        [UnityTest]
        public IEnumerator Escopeta_DisparaLejosGastaBalasCulatazoMunicionPorEnemigoYNoSeRompe()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            p1.SetWeapon(WeaponRegistry.Create("shotgun"));
            Put(p1, 68, 72);
            var e = SpawnAt('L', 68, 66); e.Hp = e.MaxHp = 9999; e.Stun = 99;
            System.Action fire = () => { p1.Cooldown = 0; p1.Attack = null; Face(p1, e.X, e.Z); Tap("Space"); Step(40); e.Stun = 99; Put(e, 68, 66); Put(p1, 68, 72); };
            float hp0 = e.Hp; fire();
            Assert.GreaterOrEqual(hp0 - e.Hp, 9f, "dispara a 6 m");
            var sg = (ShotgunWeapon)p1.Weapon;
            Assert.AreEqual(5, sg.Ammo);
            for (int i = 0; i < 6; i++) fire();
            Assert.AreEqual(0, sg.Ammo);
            StringAssert.Contains("culatazo", sg.Style);
            float hp1 = e.Hp; fire();
            Assert.AreEqual(0f, hp1 - e.Hp, "sin balas no llega lejos");
            Assert.IsFalse(sg.HasDurability); Assert.IsFalse(sg.Broken);
            // +1 bala por enemigo derrotado, solo si la llevas en la mano
            var s1 = SpawnAt('S', 60, 70); s1.TakeHit(H(999)); Step(2);
            Assert.AreEqual(1, sg.Ammo, "+1 bala al derrotar un enemigo");
            Assert.IsFalse(p2.Weapon is ShotgunWeapon);
            var w2 = (ShotgunWeapon)WeaponRegistry.FromState(sg.GetState());
            Assert.AreEqual(1, w2.Ammo, "las balas viajan entre pisos");
            // Las armas especiales no salen en la mazmorra
            var special = new[] { "sword", "maul", "spear", "axe", "shotgun" };
            Assert.IsFalse(WeaponRegistry.ByMapChar.Values.Any(special.Contains));
            Assert.IsFalse(Breakable.Types.Values.Any(t => t.Loot != null && t.Loot.Any(r => r.Items.Any(it => special.Contains(it.Key)))));
            yield break;
        }

        [UnityTest]
        public IEnumerator ArmasDeTienda_EspadaTorbellinoMazoLanzaYHachaGolpean()
        {
            StartRun(1);
            var p = G.Players[0];
            System.Func<string, float, float> hitWith = (id, dist) =>
            {
                p.SetWeapon(WeaponRegistry.Create(id)); Put(p, 68, 70); p.Facing = 0f;   // π del web: hacia −z del web
                var en = SpawnAt('L', 68, 70 - dist); en.Hp = en.MaxHp = 9999; en.Stun = 99;
                float h0 = en.Hp; p.Cooldown = 0; Tap("Space"); Step(60);
                float d = h0 - en.Hp; en.Destroy(); G.Enemies.Clear(); return d;
            };
            Assert.GreaterOrEqual(hitWith("spear", 3.9f), 15f, "la lanza llega a 3,9");
            Assert.GreaterOrEqual(hitWith("axe", 1.8f), 22f, "hacha");
            Assert.GreaterOrEqual(hitWith("maul", 1.8f), 34f, "mazo");
            // Espada: los dos primeros golpes no dan detrás; el tercero (torbellino) sí
            p.SetWeapon(WeaponRegistry.Create("sword")); Put(p, 68, 70); p.Facing = 0f;
            var e = SpawnAt('L', 68, 71.6f); e.Hp = e.MaxHp = 9999; e.Stun = 99;
            var hits = new float[3];
            for (int i = 0; i < 3; i++) { float h = e.Hp; p.Cooldown = 0; p.Facing = 0f; Tap("Space"); Step(14); e.Stun = 99; Put(e, 68, 71.6f); hits[i] = h - e.Hp; }
            Assert.AreEqual(0f, hits[0], "golpe 1 no da detrás"); Assert.AreEqual(0f, hits[1], "golpe 2 tampoco");
            Assert.Greater(hits[2], 0f, "el torbellino sí");
            yield break;
        }
    }
}
