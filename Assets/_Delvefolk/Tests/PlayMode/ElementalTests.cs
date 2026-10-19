using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Armas elementales (agarre, básico, especial y efectos), afinidad del piso y tragaperras
    public class ElementalTests : GameTestBase
    {
        static readonly string[] Ids = { "mop", "hellpan", "rake", "parasol", "vacuum" };

        // Partida con el J1 en el centro de la sala inicial, mirando hacia +X
        Player Ready(string weapon)
        {
            StartRun(1);
            var p = G.Players[0];
            var room = G.Dungeon.RoomAt(p.X, p.Z);
            PutU(p, Grid.X((room.X0 + room.X1) / 2), Grid.Z((room.Z0 + room.Z1) / 2));
            p.Facing = Mathf.PI / 2f;
            if (weapon != null) p.SetWeapon(WeaponRegistry.Create(weapon));
            Step(2);
            return p;
        }

        Enemy Ahead(Player p, char ch, float dist)
        {
            var e = G.SpawnEnemy(ch, p.X + dist, p.Z);
            e.Wake(false);
            e.SetState(EnemyState.Chase);
            e.AttackCd = 99f;   // que no moleste durante la prueba
            Assert.IsTrue(G.Dungeon.LineClear(p.X, p.Z, e.X, e.Z), "precondición: sin muros delante");
            return e;
        }

        void HoldAttack(int frames) { HoldAll(frames, "KeyX"); Step(1); }

        Transform ModelOf(Player p, string id)
        {
            foreach (Transform c in p.Parts.Holder) if (c.name == id || c.name == id + "(Clone)") return c;
            return null;
        }

        [UnityTest]
        public IEnumerator Agarre_CadaArmaVaEnLaManoSinFisicaYLaSigue()
        {
            var p = Ready(null);
            foreach (var id in Ids)
            {
                p.SetWeapon(WeaponRegistry.Create(id));
                yield return null;   // Destroy del modelo anterior
                var m = p.Parts.Holder.Cast<Transform>().Where(c => c.name != "ManoD").ToList();
                Assert.AreEqual(1, m.Count, id + ": un solo modelo en la mano (sin duplicados)");
                Assert.AreEqual(0, m[0].GetComponentsInChildren<Collider>(true).Length, id + ": sin colisionadores");
                Assert.AreEqual(0, m[0].GetComponentsInChildren<Rigidbody>(true).Length, id + ": sin Rigidbody");
                Assert.Greater(m[0].GetComponentsInChildren<Renderer>(true).Length, 0, id + ": se ve");
                var local = m[0].localPosition;
                // Correr, girar y atacar: el arma sigue a la mano
                G.Input.Tap("KeyD"); Step(20); G.Input.Release("KeyD");
                G.Input.Tap("KeyW"); Step(15); G.Input.Release("KeyW");
                Tap("KeyX"); Step(10);
                Assert.AreEqual(p.Parts.Holder, m[0].parent, id + ": sigue colgando de la mano");
                Assert.AreEqual(local, m[0].localPosition, id + ": misma posición local");
                Assert.Less(Vector3.Distance(m[0].position, p.Parts.Holder.position), 0.5f, id + ": pegada a la mano");
            }
        }

        [UnityTest]
        public IEnumerator Agarre_RecogerCambiarYSoltarSinObjetosAbandonados()
        {
            var p = Ready(null);
            var a = G.SpawnItemPickup(WeaponRegistry.Create("rake"), p.X + 0.5f, p.Z);
            var b = G.SpawnItemPickup(WeaponRegistry.Create("parasol"), p.X - 0.5f, p.Z);
            int n0 = G.Pickups.Count;
            G.EquipFromPickup(p, a);
            Assert.AreEqual("rake", p.Weapon.Id);
            Assert.AreEqual(n0 - 1, G.Pickups.Count);
            G.EquipFromPickup(p, b);
            Assert.AreEqual("parasol", p.Weapon.Id);
            Assert.AreEqual(n0 - 1, G.Pickups.Count, "el rastrillo vuelve al suelo (uno por otro)");
            Assert.IsTrue(G.Pickups.Any(k => k.WeaponId == "rake"));
            G.DropHeld(p);
            Assert.AreEqual("fists", p.Weapon.Id);
            Assert.IsTrue(G.Pickups.Any(k => k.WeaponId == "parasol"));
            yield return null;
            Assert.AreEqual(1, p.Parts.Holder.Cast<Transform>().Count(c => c.name != "ManoD"), "solo el puño");
            // En el suelo el modelo cuelga del piso (no de la mano) y tampoco lleva física de Unity
            var rakeObj = G.FloorRoot.GetComponentsInChildren<WeaponGrip>(true).FirstOrDefault();
            Assert.IsNotNull(rakeObj);
            Assert.AreEqual(0, G.FloorRoot.GetComponentsInChildren<Rigidbody>(true).Length);
        }

        [UnityTest]
        public IEnumerator Especial_ToqueEsBasicoYMantenerEsEspecialConEnfriamiento()
        {
            var p = Ready("hellpan");
            var w = (ElementalWeapon)p.Weapon;
            var e = Ahead(p, 'S', 1.8f);
            float hp = e.Hp;
            // Toque: golpe básico
            HoldAll(2, "KeyX"); Step(1);
            Assert.IsNotNull(p.Attack, "toque = básico");
            Assert.IsNull(p.Special);
            Step(60);
            Assert.Less(e.Hp, hp, "el básico hace daño");
            e.Hp = e.MaxHp = 500f;
            PutU(e, p.X + 1.8f, p.Z);
            p.Facing = Mathf.PI / 2f;
            // Mantener y soltar: especial (preparación → efecto → enfriamiento)
            HoldAttack(32);
            Assert.IsNotNull(p.Special, "mantener = especial");
            Assert.IsNull(p.Attack);
            Step(40);
            Assert.IsTrue(G.Elemental.HasBurn(e), "la onda de calor quema");
            Assert.Greater(w.SpecialCd, 0f, "enfriamiento");
            float cd = w.SpecialCd;
            // Recargando: mantener otra vez solo da el básico
            HoldAttack(32);
            Assert.IsNull(p.Special);
            Assert.IsNotNull(p.Attack);
            Assert.Less(w.SpecialCd, cd);
            yield break;
        }

        [UnityTest]
        public IEnumerator Estados_QuemaduraYVenenoRenuevanSinAcumularse()
        {
            var p = Ready(null);
            var e = Ahead(p, 'S', 2f);
            e.Hp = e.MaxHp = 999f;
            for (int i = 0; i < 20; i++) { G.Elemental.Burn(e, 3f, 0.5f, 4f, p); G.Elemental.Poison(e, 4f, 0.5f, 3f, p); }
            Assert.LessOrEqual(G.Elemental.BurnLeft(e), ElementalSystem.MaxDotTime);
            Assert.LessOrEqual(G.Elemental.PoisonLeft(e), ElementalSystem.MaxDotTime);
            float hp = e.Hp;
            Step(60);   // 1 s: 2 ticks de cada uno
            Assert.AreEqual(hp - 2 * 4f - 2 * 3f, e.Hp, 0.01f, "daño de un solo efecto de cada tipo, no veinte");
            Step(400);
            Assert.IsFalse(G.Elemental.HasBurn(e));
            Assert.IsFalse(G.Elemental.HasPoison(e));
            Assert.AreEqual(1f, e.StatusMoveMult, "sin estados, se mueve como siempre");
            yield break;
        }

        [UnityTest]
        public IEnumerator Trapeador_TorbellinoEmpujaYDejaSueloMojadoQueRalentiza()
        {
            var p = Ready("mop");
            var e = Ahead(p, 'S', 2f);
            e.Hp = e.MaxHp = 500f;
            float d0 = Mathf.Abs(e.X - p.X);
            HoldAttack(32);
            Step(30);
            Assert.AreEqual(1, G.Elemental.ZoneCountOf(ElementalSystem.ZoneKind.Wet));
            Assert.Less(e.Hp, 500f);
            Assert.Greater(Mathf.Abs(e.X - p.X), d0, "lo aparta");
            PutU(e, p.X + 1.5f, p.Z); Step(2);
            Assert.Less(G.Elemental.SlowOf(e), 1f, "el charco ralentiza");
            Step(300);
            Assert.AreEqual(0, G.Elemental.ZoneCount, "el charco se seca");
            yield break;
        }

        [UnityTest]
        public IEnumerator Rastrillo_EnganchaAlLigeroElPesadoResisteYSiMuereNoFalla()
        {
            var p = Ready("rake");
            var e = Ahead(p, 'S', 4.2f);
            float d0 = e.X - p.X;
            HoldAttack(32);
            Step(20);
            Assert.Less(e.X - p.X, d0 - 1f, "lo arrastra hacia el jugador");
            Step(20);
            Assert.IsTrue(G.Elemental.IsRooted(e) || e.Dead, "ligero: inmovilizado al llegar · dist=" + (e.X - p.X) + " pulling=" + G.Elemental.IsPulling(e) + " slow=" + G.Elemental.SlowOf(e) + " cd=" + ((ElementalWeapon)p.Weapon).SpecialCd + " special=" + (p.Special != null));
            e.Destroy(); G.Enemies.Remove(e);
            // Pesado (abrazón): no se mueve
            var w = (ElementalWeapon)p.Weapon; w.SpecialCd = 0f;
            var big = Ahead(p, 'A', 4.2f);
            big.Stun = 99f;
            float bx = big.X;
            HoldAttack(32); Step(45);   // preparación + pose del especial
            Assert.AreEqual(bx, big.X, 0.3f, "el pesado resiste");
            // El objetivo muere a mitad del arrastre: se acaba sin errores
            big.Destroy(); G.Enemies.Remove(big);
            w.SpecialCd = 0f;
            var s = Ahead(p, 'S', 4.2f);
            HoldAttack(32); Step(18);   // preparación (0,25 s) y empieza a arrastrar
            Assert.IsTrue(G.Elemental.IsPulling(s), "special=" + (p.Special != null ? (p.Special.Windup ? "windup" : "active") : "no") + " cd=" + w.SpecialCd + " dist=" + (s.X - p.X) + " dz=" + (s.Z - p.Z) + " charge=" + p.ChargeT + " atk=" + (p.Attack != null) + " stun=" + p.StunT + " rooted=" + G.Elemental.IsRooted(s));
            s.Damage(9999f, false, null);
            Step(10);
            Assert.IsFalse(G.Elemental.IsPulling(s));
            yield break;
        }

        [UnityTest]
        public IEnumerator Paraguas_UnaNubePorEspecialYEnvenena()
        {
            var p = Ready("parasol");
            var e = Ahead(p, 'S', 1.6f);
            e.Hp = e.MaxHp = 500f;
            HoldAttack(32);
            Step(30);
            Assert.AreEqual(1, G.Elemental.ZoneCountOf(ElementalSystem.ZoneKind.Spores));
            // Mantener otra vez durante el enfriamiento no crea otra nube
            HoldAttack(32); Step(30);
            Assert.AreEqual(1, G.Elemental.ZoneCountOf(ElementalSystem.ZoneKind.Spores));
            PutU(e, p.X + 1f, p.Z);
            Step(40);
            Assert.IsTrue(G.Elemental.HasPoison(e));
            yield break;
        }

        [UnityTest]
        public IEnumerator Aspiradora_SuccionaGuardaSinIAYLanzaComoProyectil()
        {
            var p = Ready("vacuum");
            var v = (QuantumVacuumWeapon)p.Weapon;
            var s = Ahead(p, 'S', 3f);
            var big = Ahead(p, 'A', 3.5f);
            big.Stun = 99f;
            PutU(big, p.X + 3.5f, p.Z + 2.2f);
            // Mantener: succión
            G.Input.Tap("KeyX");
            for (int i = 0; i < 120 && v.Stored.Count == 0; i++) Step(1);
            G.Input.Release("KeyX");
            Assert.AreEqual(1, v.Stored.Count, "el slime acaba dentro");
            Assert.IsTrue(s.Captured);
            Assert.IsFalse(G.Enemies.Contains(s), "sin IA mientras está dentro");
            Assert.IsFalse(s.Root.gameObject.activeSelf);
            Assert.Less(v.Energy, v.MaxEnergy, "gasta energía");
            Assert.IsFalse(big.Captured, "el abrazón es demasiado pesado");
            Step(5);
            // Toque: descarga
            Tap("KeyX"); Step(20);
            Assert.AreEqual(0, v.Stored.Count);
            Assert.IsFalse(s.Captured);
            Assert.IsTrue(G.Enemies.Contains(s) || s.Dead, "vuelve al combate");
            if (!s.Dead) Assert.IsTrue(s.Body.Enabled);
            // Soltar el arma con algo dentro: sale sin errores
            v.SpecialCd = 0f;
            var s2 = Ahead(p, 'S', 2f);
            G.Input.Tap("KeyX");
            for (int i = 0; i < 120 && v.Stored.Count == 0; i++) Step(1);
            G.Input.Release("KeyX");
            Assert.AreEqual(1, v.Stored.Count);
            G.DropHeld(p);
            Step(3);
            Assert.IsFalse(s2.Captured);
            Assert.IsTrue(G.Enemies.Contains(s2));
            yield break;
        }

        [UnityTest]
        public IEnumerator Piso_CambiarDePisoLimpiaEfectosAfinidadYAlmacenado()
        {
            var p = Ready("vacuum");
            var e = Ahead(p, 'S', 2f);
            G.Elemental.Burn(e, 3f, 0.5f, 1f, p);
            G.Elemental.AddSporeCloud(p.X, p.Z, 2f, 10f, 2f, 0.5f, 1f, p);
            G.SetAffinity(Element.Poison);
            Assert.Greater(G.Theme.TintedCount, 0);
            G.LoadFloor(2, G.CollectCarry());
            Step(2);
            Assert.AreEqual(0, G.Elemental.ZoneCount);
            Assert.AreEqual(Element.None, G.Affinity);
            Assert.AreEqual(0, G.Theme.TintedCount);
            Assert.AreEqual("vacuum", G.Players[0].Weapon.Id, "el arma pasa de piso");
            Assert.AreEqual(0, ((QuantumVacuumWeapon)G.Players[0].Weapon).Stored.Count);
            yield break;
        }

        [UnityTest]
        public IEnumerator Afinidad_TineSoloElEntornoYSeDeshace()
        {
            Ready(null);
            var p = G.Players[0];
            var mpb = new MaterialPropertyBlock();
            var playerR = p.Parts.Renderers[0];
            foreach (var el in new[] { Element.Water, Element.Fire, Element.Plant, Element.Poison, Element.Future })
            {
                G.SetAffinity(el);
                Assert.AreEqual(el, G.Affinity);
                var surf = G.View.Surfaces.First(r => r);
                surf.GetPropertyBlock(mpb);
                Assert.Greater(mpb.GetVector(Mats.RecolorId).w, 0.3f, el + ": el suelo/muro se tiñe");
                playerR.GetPropertyBlock(mpb);
                Assert.AreEqual(0f, mpb.GetVector(Mats.RecolorId).w, el + ": el jugador no");
                Assert.IsNotNull(G.Theme.Decor, el + ": detalles decorativos");
            }
            G.SetAffinity(Element.None);
            var s0 = G.View.Surfaces.First(r => r);
            s0.GetPropertyBlock(mpb);
            Assert.AreEqual(0f, mpb.GetVector(Mats.RecolorId).w, "vuelve al aspecto base");
            yield return null;
            Assert.IsNull(G.Theme.Decor);
            yield break;
        }

        [UnityTest]
        public IEnumerator Partida_SoloSalenLasArmasNuevas()
        {
            WeaponRegistry.LegacyWeapons = false;
            var nuevas = new[] { "mop", "hellpan", "rake", "parasol", "vacuum", "chicken", "cheese", "rottencheese" };
            foreach (var kv in WeaponRegistry.LegacyReplacement)
                Assert.Contains(WeaponRegistry.Create(kv.Key).Id, nuevas, kv.Key + " se sustituye");
            StartRun(1);
            G.LoadFloor(1, null);
            Step(2);
            foreach (var k in G.Pickups) Assert.Contains(k.WeaponId, nuevas, "en el mapa: " + k.WeaponId);
            // Los muebles sueltan armas nuevas
            foreach (var b in G.Breakables.ToArray()) b.EnvHit(999f);
            Step(30);
            foreach (var k in G.Pickups) Assert.Contains(k.WeaponId, nuevas, "de un mueble: " + k.WeaponId);
            // Un estado guardado con un arma vieja se carga como la nueva (sin errores)
            Assert.AreEqual("hellpan", WeaponRegistry.FromState(new ItemState("pan") { durability = 3 }).Id);
            yield break;
        }

        SlotMachineConfig TestConfig()
        {
            var c = ScriptableObject.CreateInstance<SlotMachineConfig>();
            c.FillDefaults();
            c.SpawnChance = 1f;
            c.ReelSpin = 0.5f; c.ReelStagger = 0.2f; c.RevealPause = 0.3f;
            return c;
        }

        [UnityTest]
        public IEnumerator Tragaperras_ApareceGiraUnaVezYAplicaLoElegido()
        {
            var cfg = TestConfig();
            cfg.DebugForce = true; cfg.ForceAffinity = "Plant"; cfg.ForceAbility = "buff_speed"; cfg.ForceEnemies = "slime";
            SlotMachineConfig.Override = cfg;
            Game.SlotMachinesEnabled = true;
            try
            {
                StartRun(1);
                Assert.AreEqual(0, G.SlotMachines.Count, "el primer piso no tiene");
                G.LoadFloor(2, G.CollectCarry());
                Step(2);
                Assert.AreEqual(1, G.SlotMachines.Count);
                var m = G.SlotMachines[0];
                var p = G.Players[0];
                Assert.AreEqual(1, G.Dungeon.Blocked[G.Dungeon.Idx(m.Tx, m.Tz)], "ocupa su casilla");
                PutU(p, m.X + (m.X > p.X ? -1.5f : 1.5f), m.Z);
                // Puede que la casilla de delante sea la buena: se busca un lado libre
                foreach (var o in new[] { new Vector2(1.5f, 0), new Vector2(-1.5f, 0), new Vector2(0, 1.5f), new Vector2(0, -1.5f) })
                    if (!G.Dungeon.IsWallLike(Grid.TileX(m.X + o.x), Grid.TileZ(m.Z + o.y))) { PutU(p, m.X + o.x, m.Z + o.y); break; }
                Step(2);
                int pickups = G.Pickups.Count;
                Tap("KeyE");
                Assert.AreEqual(SlotState.Spinning, m.State);
                Assert.AreEqual("Plant", m.AffinityResult.Id, "el resultado se elige antes de la animación");
                Assert.AreEqual(Element.None, G.Affinity, "todavía no se aplica");
                Step(100);
                Assert.AreEqual(SlotState.Done, m.State);
                Assert.IsTrue(m.Applied);
                Assert.AreEqual(Element.Plant, G.Affinity);
                Assert.AreEqual(1, p.Gear.BuffStacks("buff_speed"));
                Assert.AreEqual("slime", G.SlotBounty);
                Assert.AreEqual(pickups + 1, G.Pickups.Count, "sale el arma de la afinidad");
                Assert.IsTrue(G.Pickups.Any(k => k.WeaponId == "rake"));
                // Una sola vez: otra pulsación no gira ni vuelve a dar la mejora
                Tap("KeyE"); Step(100);
                Assert.AreEqual(1, p.Gear.BuffStacks("buff_speed"));
                Assert.IsFalse(m.Spin(p));
                // Al bajar se limpia todo
                G.LoadFloor(3, G.CollectCarry());
                Step(2);
                Assert.AreEqual(Element.None, G.Affinity);
                Assert.IsNull(G.SlotBounty);
            }
            finally { SlotMachineConfig.Override = null; }
            yield break;
        }

        [UnityTest]
        public IEnumerator Tragaperras_SeVeEnPantallaAunqueSePauseYDevuelveElControl()
        {
            var cfg = TestConfig();
            SlotMachineConfig.Override = cfg;
            Game.SlotMachinesEnabled = true;
            try
            {
                StartRun(1);
                G.LoadFloor(2, G.CollectCarry());
                Step(2);
                var m = G.SlotMachines[0];
                var p = G.Players[0];
                Assert.IsTrue(m.Spin(p));
                Assert.IsTrue(G.SlotUi.Active && G.Ui.SlotShown, "el panel de los rodillos está en pantalla");
                Assert.IsFalse(m.Spin(p), "no se puede tirar otra vez durante la animación");
                float t0 = G.Time;
                Step(10);
                Assert.AreEqual(t0, G.Time, "la simulación espera mientras giran los rodillos");
                Press("Escape"); Step(1);
                Assert.AreEqual(GameState.Paused, G.State);
                Step(120);
                Assert.IsFalse(G.SlotUi.Active, "en pausa la animación también termina");
                Assert.IsFalse(G.Ui.SlotShown);
                Assert.IsTrue(m.Applied, "resultado aplicado");
                Assert.AreEqual(m.AffinityResult.Id, G.Affinity.ToString(), "lo que se ve es lo que se aplica");
                Press("Escape"); Step(1);
                Assert.AreEqual(GameState.Play, G.State);
                float x = p.X;
                G.Input.Tap("KeyD"); Step(20); G.Input.Release("KeyD");
                Assert.AreNotEqual(x, p.X, "el jugador vuelve a moverse");
                Assert.AreEqual(1, G.SlotUi.Opened, "una sola tirada");
            }
            finally { SlotMachineConfig.Override = null; }
            yield break;
        }

        [UnityTest]
        public IEnumerator Tragaperras_ConTodosLosPesosACeroNoGira()
        {
            var cfg = TestConfig();
            foreach (var o in cfg.Affinities) o.Weight = 0f;
            SlotMachineConfig.Override = cfg;
            Game.SlotMachinesEnabled = true;
            try
            {
                StartRun(1);
                G.LoadFloor(2, G.CollectCarry());
                Step(2);
                var m = G.SlotMachines[0];
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Tragaperras"));
                Assert.IsFalse(m.Spin(G.Players[0]));
                Assert.AreEqual(SlotState.Idle, m.State);
            }
            finally { SlotMachineConfig.Override = null; }
            yield break;
        }
    }
}
