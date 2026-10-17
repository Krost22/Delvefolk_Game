using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Fase 2: combate y mazmorra (port de tests/e2e.test.js)
    public class CombatTests : GameTestBase
    {
        Player P1 { get { return G.Players[0]; } }

        [UnityTest]
        public IEnumerator MVP_EscobaGolpeaSlimePuasYBajarAlPiso2()
        {
            G.Settings.BonusMode = "never";   // como T.start(..., { bonusMode: 'never' }) del original
            StartRun(1);
            var p = P1;
            var broom = G.Pickups.First(k => k.Item.Id == "broom");
            PutU(p, broom.X, broom.Z - 1f); Step(1);
            Tap("KeyE");
            Assert.AreEqual("broom", p.Weapon.Id);
            var e = G.SpawnEnemy('S', p.X, p.Z + 1.6f); e.Wake(false); e.SetState(EnemyState.Chase);
            float hp0 = e.Hp;
            Face(p, e.X, e.Z);
            Tap("Space"); Step(30);
            Assert.Greater(hp0 - e.Hp, 0f, "el golpe hace daño");
            Assert.Less(p.Weapon.Durability, 14f, "gasta durabilidad");
            // Unas púas cualquiera del piso
            var trap = G.Traps[0];
            p.Iframes = 0f; float hpb = p.Hp; PutU(p, trap.X, trap.Z); Step(40);
            Assert.Greater(hpb - p.Hp, 0f, "las púas dañan");
            PutU(p, G.Dungeon.ExitX, G.Dungeon.ExitZ - 1f); Step(1);
            Tap("KeyE");
            Assert.AreEqual(GameState.Play, G.State);
            Assert.AreEqual(2, G.Floor);
            Assert.IsFalse(G.InBonus);
            Assert.AreEqual("broom", G.Players[0].Weapon.Id, "el arma viaja al piso siguiente con su desgaste");
            yield break;
        }

        [UnityTest]
        public IEnumerator Muebles_SeRompenSueltanObjetosYLiberanLaNavegacion()
        {
            StartRun(1);
            var p = P1;
            var types = string.Join(",", G.Breakables.Select(x => x.TypeId).Distinct().OrderBy(x => x));
            Assert.AreEqual("bookshelf,rack,table,vase", types, "los cuatro tipos en el piso 1");
            Assert.GreaterOrEqual(G.Breakables.Count, 15);
            // Un armero: golpes con la sartén hasta romperlo (siempre suelta al menos un arma)
            var rack = G.Breakables.First(x => x.TypeId == "rack");
            var d = G.Dungeon;
            int tile = d.Idx(rack.Tx, rack.Tz);
            Assert.AreEqual(1, d.Blocked[tile], "el armero bloquea la navegación");
            p.SetWeapon(WeaponRegistry.Create("pan"));
            int before = G.Pickups.Count;
            int swings = 0;
            while (!rack.Dead && swings < 10)
            {
                PutU(p, rack.X, rack.Z);   // la física lo aparta del cuerpo estático
                Step(2);
                Face(p, rack.X, rack.Z); p.Cooldown = 0f;
                Tap("Space"); Step(40); swings++;
            }
            Assert.IsTrue(rack.Dead, "el armero se rompe a golpes (" + swings + " golpes)");
            Assert.GreaterOrEqual(G.Pickups.Count - before, 1, "suelta al menos un arma");
            Assert.AreEqual(0, d.Blocked[tile], "al romperse libera la casilla");
            Assert.IsFalse(G.Breakables.Contains(rack));
            // Una estantería: una explosión la destroza
            var shelf = G.Breakables.First(x => x.TypeId == "bookshelf");
            G.Combat.Explosion(shelf.X, shelf.Z);
            Step(1);
            Assert.IsTrue(shelf.Dead, "la explosión destroza la estantería");
            Assert.IsTrue(G.Pickups.Any(k => k.Item.Id == "book" || k.Item.Id == "stick" || k.Item.Id == "broom"), "la estantería suelta un arma");
            // Los objetos caídos nunca quedan dentro de un muro
            Assert.IsFalse(G.Pickups.Any(k => d.IsWallLike(Grid.TileX(k.X), Grid.TileZ(k.Z))), "nada cae dentro de un muro");
            yield break;
        }

        [UnityTest]
        public IEnumerator Armas_TomoAturdeMuchoPataGolpeaYAmbasSeRompen()
        {
            StartRun(1);
            var p = P1;
            foreach (var id in new[] { "book", "chairleg" })
            {
                p.SetWeapon(WeaponRegistry.Create(id));
                Put(p, 68, 70);
                var e = SpawnAt('L', 68, 68.2f); e.Cfg.StunMult = 1f;
                float hp0 = e.Hp, dur0 = p.Weapon.Durability;
                Face(p, e.X, e.Z); p.Cooldown = 0f;
                Tap("Space"); Step(30);
                float dmg = hp0 - e.Hp;
                if (id == "book") { Assert.GreaterOrEqual(dmg, 14f); Assert.Greater(e.Stun, 0.5f, "el tomo aturde mucho"); }
                else Assert.GreaterOrEqual(dmg, 11f);
                Assert.AreEqual(1f, dur0 - p.Weapon.Durability, "un uso por ataque que acierta");
                e.Destroy(); G.Enemies.Clear();
                p.Weapon.Durability = 1; p.Weapon.Wear();
                Assert.IsTrue(p.Weapon.Broken);
                Assert.AreEqual(id == "book" ? "Tapas sueltas" : "Astilla", p.Weapon.Name);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator ArmaRota_ApenasHaceDanoYSigueUsandose()
        {
            StartRun(1);
            var w = WeaponRegistry.Create("pan");
            w.BreakWeapon();
            Assert.AreEqual(Mathf.Max(1, Mathf.Round(28 * 0.18f)), w.Damage);
            Assert.AreEqual(26f * 0.35f, w.KnockbackForce, 1e-4f);
            Assert.AreEqual(2.3f * 0.75f, w.AttackRange, 1e-4f);
            Assert.AreEqual("thwack", w.HitSound);
            Assert.IsNull(w.HitText);
            Assert.AreEqual("Rota · apenas hace daño", w.Style);
            // El estado se conserva entre pisos
            var copy = WeaponRegistry.FromState(w.GetState());
            Assert.IsTrue(copy.Broken);
            Assert.AreEqual("Mango de sartén", copy.Name);
            var used = WeaponRegistry.Create("broom"); used.Wear(); used.Wear();
            Assert.AreEqual(12f, WeaponRegistry.FromState(used.GetState()).Durability);
            yield break;
        }

        [UnityTest]
        public IEnumerator Salto_PasaPorEncimaEsquivaYNoActivaPuas()
        {
            StartRun(1);
            var p = P1;
            Put(p, 68, 70);
            Tap("ShiftLeft"); Step(9);
            Assert.Greater(p.JumpY, 0.5f); Assert.IsTrue(p.Body.Airborne);
            Step(60);
            Assert.AreEqual(0f, p.JumpY); Assert.IsFalse(p.Body.Airborne);
            // Un slime justo debajo: en el aire no hay empujón entre cuerpos
            var e = SpawnAt('S', 68, 70); e.Stun = 5f;
            p.JumpY = 1.2f; p.JumpV = 2f; p.Body.Airborne = true;
            float x0 = p.X, z0 = p.Z; Step(1);
            Assert.Less(MathUtil.Hypot(p.X - x0, p.Z - z0), 0.05f, "pasa por encima del enemigo");
            // Esquiva: muy alto, el ataque no le alcanza; en el suelo, sí
            p.JumpY = 1.3f; p.JumpV = 0.5f; p.Iframes = 0f; e.Facing = Mathf.Atan2(p.X - e.X, p.Z - e.Z); e.AttackHit.Clear();
            e.TryHitPlayer(0.5f);
            Assert.AreEqual(100f, p.Hp, "esquiva el ataque saltando");
            p.EndJump(); p.Iframes = 0f; e.AttackHit.Clear(); e.TryHitPlayer(0.5f);
            Assert.Less(p.Hp, 100f, "en el suelo sí le pegan");
            e.Destroy(); G.Enemies.Clear();
            // Púas: saltando por encima no se activan
            var trap = G.Traps[0];
            p.Hp = 100f; p.Iframes = 0f; PutU(p, trap.X, trap.Z);
            p.JumpY = 1.0f; p.JumpV = 0f; p.Body.Airborne = true;
            for (int i = 0; i < 3; i++) { p.JumpY = 1.0f; p.JumpV = 0f; Step(1); }
            Assert.AreEqual(TrapState.Idle, trap.State, "las púas no se activan bajo un salto");
            yield break;
        }

        void FallOn(Player p, Enemy e)
        {
            PutU(p, e.X, e.Z);
            p.JumpY = e.StompTop() + 0.1f; p.JumpV = -1f; p.Body.Airborne = true;
            Step(1);
        }

        [UnityTest]
        public IEnumerator Pisoton_DanaAturdeYRebotaMasEnSlimes()
        {
            StartRun(1);
            var p = P1;
            var s = SpawnAt('S', 68, 68); float hp0 = s.Hp;
            FallOn(p, s);
            Assert.AreEqual(10f, hp0 - s.Hp, "daño del pisotón");
            Assert.Greater(s.Stun, 0.3f, "aturdido");
            Assert.Greater(p.JumpV, 9f, "rebote de slime");
            float slimeBounce = p.JumpV;
            p.EndJump(); s.Destroy(); G.Enemies.Clear(); Step(5);
            var gob = SpawnAt('G', 66, 68); float hg = gob.Hp;
            FallOn(p, gob);
            Assert.AreEqual(10f, hg - gob.Hp);
            Assert.That(p.JumpV, Is.GreaterThan(6f).And.LessThan(slimeBounce), "rebote normal");
            Assert.AreEqual(2, G.Stats.Stomps);
            yield break;
        }

        [UnityTest]
        public IEnumerator Energia_PisotonesEncadenadosAgotan()
        {
            StartRun(1);
            var p = P1;
            var s = SpawnAt('L', 68, 68); s.Hp = s.MaxHp = 9999f;
            PutU(p, s.X, s.Z);
            p.JumpY = s.StompTop() + 0.1f; p.JumpV = -1f; p.Body.Airborne = true;
            var bounces = new List<float>();
            for (int i = 0; i < 600 && !(p.StunT > 0f); i++)
            {
                PutU(p, s.X, s.Z); s.Stun = 5f; s.Body.Kx = s.Body.Kz = 0f;
                float v = p.JumpV; Step(1);
                if (p.JumpY > 0f && p.JumpV > v + 1f) bounces.Add(Mathf.Round(p.JumpV * 10f) / 10f);
            }
            Assert.That(bounces.Count, Is.InRange(4, 6), "rebota unas cuantas veces: " + string.Join(",", bounces));
            Assert.AreEqual(3f, bounces[bounces.Count - 1], "el último ya sin fuerzas");
            Assert.Greater(p.StunT, 0f, "cae agotado y aturdido");
            yield break;
        }

        [UnityTest]
        public IEnumerator MuertePorPisoton_QuedaPlanoUnRato()
        {
            StartRun(1);
            var p = P1;
            var e = SpawnAt('G', 68, 68); e.Hp = 5f;
            FallOn(p, e); Step(10);
            Assert.IsTrue(e.Dead); Assert.IsTrue(e.Flattened);
            Assert.Less(e.Model.localScale.y, 0.15f); Assert.Greater(e.Model.localScale.x, 1.5f);
            Step(50); Assert.IsFalse(e.Removed, "se queda un rato en el suelo");
            Step(80); Assert.IsTrue(e.Removed, "luego desaparece");
            // Una muerte normal (golpe) no aplasta
            var e2 = SpawnAt('G', 64, 70); e2.Hp = 1f; e2.TakeHit(Hit.Of(5, 1, 0, 1, 0)); Step(5);
            Assert.IsFalse(e2.Flattened);
            yield break;
        }

        [UnityTest]
        public IEnumerator Abrazon_AgarraEstrujaYMachacandoTeSueltas()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            Put(p1, 68, 70); Put(p2, 62, 72);
            var e = (Grabber)SpawnAt('A', 68, 68.4f);
            e.Facing = Mathf.Atan2(p1.X - e.X, p1.Z - e.Z); e.SetState(EnemyState.Attack);
            Step(2);
            Assert.AreSame(e, p1.GrabbedBy, "le agarra");
            Assert.IsFalse(p1.Targetable);
            Assert.IsNull(G.Combat.FindAimTarget(p2, 99f, Mathf.Atan2(p1.X - p2.X, p1.Z - p2.Z)) as object == p1 ? null : null);
            float hp0 = p1.Hp; Step(80);
            Assert.GreaterOrEqual(hp0 - p1.Hp, 6f, "le estruja");
            for (int i = 0; i < 12 && p1.GrabbedBy != null; i++) Tap("Space");
            Assert.IsNull(p1.GrabbedBy, "machacando se suelta");
            Assert.IsNull(e.Held);
            Assert.Greater(e.Stun, 0.5f, "el Abrazón queda aturdido");
            Assert.IsTrue(p1.Body.Enabled);
            yield break;
        }

        [UnityTest]
        public IEnumerator Abrazon_RescateLanzamientoYSaltoQueEsquiva()
        {
            StartRun(2);
            Player p1 = G.Players[0], p2 = G.Players[1];
            System.Func<Grabber> setup = () =>
            {
                Put(p1, 68, 70);
                var g = (Grabber)SpawnAt('A', 68, 68.4f);
                g.Facing = Mathf.Atan2(p1.X - g.X, p1.Z - g.Z); g.SetState(EnemyState.Attack);
                Step(2);
                return g;
            };
            // Rescate: J2 le pega con los puños
            var e = setup();
            PutU(p2, e.X + 1.3f, e.Z); Face(p2, e.X, e.Z); p2.Cooldown = 0f;
            Tap("Enter"); Step(20);
            Assert.IsNull(p1.GrabbedBy, "un golpe del compañero le hace soltar");
            e.Destroy(); G.Enemies.Clear(); Step(30);
            // Nadie hace nada: lo lanza
            Put(p2, 60, 74);
            e = setup();
            Assert.AreSame(e, p1.GrabbedBy);
            bool thrown = false;
            for (int i = 0; i < 260 && !thrown; i++) { Step(1); thrown = p1.IsThrown; }
            Assert.IsTrue(thrown, "acaba lanzándolo");
            Assert.IsNull(p1.GrabbedBy);
            e.Destroy(); G.Enemies.Clear(); Step(90);
            // Saltando a tiempo no le pilla
            Put(p1, 68, 70); p1.EndJump();
            e = (Grabber)SpawnAt('A', 68, 68.4f); e.Facing = Mathf.Atan2(p1.X - e.X, p1.Z - e.Z);
            p1.JumpY = 1.2f; p1.JumpV = 3f; p1.Body.Airborne = true;
            e.SetState(EnemyState.Attack); Step(2);
            Assert.IsNull(p1.GrabbedBy, "saltar esquiva el agarre");
            yield break;
        }

        [Test]
        public void Abrazon_HayUnoEnCadaPiso()
        {
            Assert.AreEqual(1, string.Concat(LevelData.Levels[0].Map).Count(c => c == 'A'));
            Assert.AreEqual(1, string.Concat(LevelData.Levels[1].Map).Count(c => c == 'A'));
        }

        [UnityTest]
        public IEnumerator Monedas_SaltanSeRecogenYNoSiCaeAlFoso()
        {
            StartRun(1);
            var p = P1;
            Put(p, 60, 72);
            var e = SpawnAt('G', 68, 66); e.TakeHit(Hit.Of(999, 0, 0, 0, 1));
            Step(2);
            int fromEnemy = G.Coins.Count;
            Assert.That(fromEnemy, Is.InRange(2, 3), "goblin: 2-3 monedas");
            var vase = G.Breakables.First(x => x.TypeId == "vase"); vase.EnvHit(99); Step(2);
            int fromVase = G.Coins.Count - fromEnemy;
            Assert.GreaterOrEqual(fromVase, 2, "el jarrón es una hucha");
            int gold0 = p.Gear.Gold;
            for (int i = 0; i < 400 && G.Coins.Count > 0; i++)
            {
                var c = G.Coins[0];
                if (c != null && c.Target == null) PutU(p, c.X, c.Z - 0.5f);
                Step(1);
            }
            Assert.AreEqual(fromEnemy + fromVase, p.Gear.Gold - gold0, "se recogen todas");
            Assert.AreEqual(0, G.Coins.Count);
            var e2 = SpawnAt('S', 68, 66); e2.Die("pit");
            Assert.AreEqual(0, G.Coins.Count, "lo que cae al foso no suelta monedas");
            yield break;
        }

        [UnityTest]
        public IEnumerator Salas_DespiertanAlEntrarYAlLimpiarseAbrenElRastrillo()
        {
            G.Start(1); Step(2);
            var p = P1;
            var room = G.Dungeon.Rooms.First(r => r.Name == "Sala de Combate");
            Assert.AreEqual(3, room.Enemies.Count);
            Assert.IsTrue(room.Enemies.All(e => e.State == EnemyState.Sleep));
            PutU(p, Grid.X(10), Grid.Z(22)); Step(2);
            Assert.IsTrue(room.Entered);
            Assert.AreEqual("Sala de Combate", G.Ui.BannerText);
            Assert.IsTrue(room.Enemies.All(e => e.WakeDelay > 0f || e.State != EnemyState.Sleep), "se despiertan escalonados");
            Step(60);
            Assert.IsTrue(room.Enemies.All(e => e.State != EnemyState.Sleep));
            foreach (var e in room.Enemies.ToList()) e.Damage(999, false, null);
            Step(2);
            Assert.IsTrue(room.Cleared);
            var gate = G.Dungeon.Gates['1'];
            Assert.IsTrue(gate.Open);
            Assert.IsTrue(G.Ui.ToastText.StartsWith("¡Sala despejada!"));
            Assert.IsTrue(G.Dungeon.IsSolidFor(gate.Tiles[0].x, gate.Tiles[0].y, p.Body), "todavía no se puede pasar");
            Step(70);
            Assert.IsFalse(G.Dungeon.IsSolidFor(gate.Tiles[0].x, gate.Tiles[0].y, p.Body), "ya se puede pasar");
            yield break;
        }

        [UnityTest]
        public IEnumerator Derrota_YReintentoRestauraElPiso()
        {
            G.Start(1); Step(2);
            var p = P1;
            p.SetWeapon(WeaponRegistry.Create("broom"));
            p.Hurt(999, p.X, p.Z, 0, new HurtOpts { Raw = true, Pierce = true });
            Assert.IsTrue(p.Dead);
            Step(70);
            Assert.AreEqual(GameState.Play, G.State, "la derrota llega 1.3 s después de caer");
            Step(20);   // + la pausa de impacto del golpe, que congela el temporizador
            Assert.AreEqual(GameState.Dead, G.State);
            Assert.AreEqual("death", G.Ui.ShownScreen);
            Press("KeyR"); Step(1);
            Assert.AreEqual(GameState.Play, G.State);
            Assert.AreEqual(1, G.Floor);
            Assert.AreEqual(100f, G.Players[0].Hp);
            Assert.AreEqual("fists", G.Players[0].Weapon.Id, "se vuelve con lo que se traía al empezar el piso");
            Assert.IsNull(G.Ui.ShownScreen);
            yield break;
        }

        [UnityTest]
        public IEnumerator Victoria_AlLlegarAlPiso3YLuegoPisosEspejo()
        {
            G.Settings.BonusMode = "never";
            G.Settings.bosses = false;   // con jefes, la victoria llega tras el del piso 2 (BossTests)
            G.Start(1); Step(2);
            G.LoadFloor(2, G.CollectCarry()); Step(2);
            var p = P1;
            PutU(p, G.Dungeon.ExitX, G.Dungeon.ExitZ - 1f); Step(1);
            Tap("KeyE");
            Assert.AreEqual(GameState.Win, G.State);
            Assert.AreEqual("win", G.Ui.ShownScreen);
            Press("Space"); Step(1);
            Assert.AreEqual(GameState.Play, G.State);
            Assert.AreEqual(3, G.Floor);
            Assert.AreEqual(1.25f, G.HpMult, 1e-5f);
            Assert.AreEqual("Piso 3 — Más abajo todavía", G.Def.Name);
            // Espejo del piso 1: la salida pasa al otro lado
            Assert.Greater(G.Dungeon.ExitX, Grid.X(30));
            Assert.IsTrue(G.Enemies.All(e => Mathf.Approximately(e.MaxHp, Mathf.Round(e.Cfg.Hp * 1.25f))));
            yield break;
        }

        [UnityTest]
        public IEnumerator Polvora_ExplotaEnCadenaYHaceKoAmbiental()
        {
            StartRun(1);
            Put(P1, 68, 72);
            var b1 = new Barrel(G, 60f, -44f, true); G.Barrels.Add(b1);
            var b2 = new Barrel(G, 62.5f, -44f, true); G.Barrels.Add(b2);
            // Dormido para que no se aparte antes de la mecha; la primera explosión lo aleja de la segunda,
            // así que con 25 de vida le basta una (45 de daño a quemarropa, ~31 a esa distancia)
            var gob = G.SpawnEnemy('G', 61f, -46.5f); gob.Hp = 25f;
            b1.TakeHit(Hit.Of(5, 1, 0, 1, 0));
            Assert.AreEqual(0.55f, b1.Fuse, 1e-4f, "golpe: mecha de 0.55 s");
            Step(40);
            Assert.IsTrue(b1.Dead, "la mecha explota");
            Assert.IsTrue(b2.Dead || (b2.Fuse >= 0f && b2.Fuse <= 0.18f), "la explosión enciende la otra");
            Step(15);
            Assert.IsTrue(b2.Dead, "reacción en cadena");
            Assert.IsTrue(gob.Dead, "la explosión de pólvora lo tumba");
            Assert.AreEqual(1, G.Stats.EnvKills);
            yield break;
        }

        [UnityTest]
        public IEnumerator Entorno_FosoYParedComoArmas()
        {
            StartRun(1);
            Put(P1, 68, 72);
            // Empujado al foso de la Sala de Combate (columnas 14–16, filas 25–27)
            // (el foso solo deja pasar mientras el empujón supera 4.5: desde la casilla vecina)
            var s = G.SpawnEnemy('S', Grid.X(13), Grid.Z(26)); s.Wake(false);
            s.Knock(12f, 0f);
            bool fell = false;
            for (int i = 0; i < 30 && !fell; i++) { Step(1); fell = s.State == EnemyState.Falling; }
            Assert.IsTrue(fell, "¡AL FOSO!");
            Step(70);
            Assert.IsTrue(s.Dead && s.Removed);
            Assert.AreEqual(1, G.Stats.EnvKills);
            Assert.GreaterOrEqual(G.Stats.EnvDamage, 40f, "su vida entera cuenta como daño del entorno");
            Assert.AreEqual(0, G.Coins.Count);
            // Contra la pared este de la Entrada: ¡PLAF!
            var g = G.SpawnEnemy('G', Grid.X(37), Grid.Z(34)); g.Wake(false);
            float hp0 = g.Hp;
            g.Knock(20f, 0f);
            Step(10);
            Assert.Less(g.Hp, hp0, "el choque contra la pared hace daño de entorno");
            Assert.IsTrue(G.Ui.HasFloatText("¡PLAF!"));
            yield break;
        }

        [UnityTest]
        public IEnumerator Comer_CuraYUnGolpeLoInterrumpe()
        {
            StartRun(1);
            var p = P1;
            p.Hp = 50f;
            p.SetWeapon(WeaponRegistry.Create("cheese"));
            Tap("Space");
            Assert.IsTrue(p.IsEating);
            Step(20);
            p.Iframes = 0f;
            p.Hurt(5, p.X - 1f, p.Z, 0);
            Assert.IsFalse(p.IsEating, "un golpe interrumpe la comida");
            Assert.AreEqual("cheese", p.Weapon.Id, "la comida sigue en la mano");
            Step(40);
            Tap("Space");
            Step(70);
            Assert.AreEqual(65f, p.Hp, "+20 del queso (tras perder 5)");
            Assert.AreEqual("fists", p.Weapon.Id);
            // Con la vida llena no se come
            p.Hp = p.MaxHp;
            p.SetWeapon(WeaponRegistry.Create("chicken"));
            Step(30);
            Tap("Space"); Step(1);
            Assert.IsFalse(p.IsEating);
            Assert.IsTrue(G.Ui.HasFloatText("Estoy lleno"));
            yield break;
        }
    }
}
