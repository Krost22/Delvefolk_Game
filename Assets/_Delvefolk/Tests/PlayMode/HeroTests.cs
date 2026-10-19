using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Personajes personalizados: selección, esqueleto propio y arma anclada al hueso de la mano
    public class HeroTests : GameTestBase
    {
        static readonly string[] Weapons = { "mop", "hellpan", "rake", "parasol", "vacuum", "broom", "chicken" };

        Player Play(string hero)
        {
            G.Loadouts[0].SetHero(hero);
            StartRun(1);
            var p = G.Players[0];
            var room = G.Dungeon.RoomAt(p.X, p.Z);
            PutU(p, Grid.X((room.X0 + room.X1) / 2), Grid.Z((room.Z0 + room.Z1) / 2));
            Step(2);
            return p;
        }

        [Test]
        public void Catalogo_LosOchoPersonajesTienenEsqueletoAvatarYAnimacion()
        {
            var ids = HeroCatalog.Order();
            Assert.IsFalse(ids.Contains(HeroCatalog.Classic), "la sala solo ofrece los personajes personalizados");
            Assert.AreEqual(8, ids.Count, "los 8 personajes de Meshy");
            foreach (var id in ids)
            {
                var h = HeroCatalog.Get(id);
                var anim = h.Prefab.GetComponent<Animator>();
                Assert.IsNotNull(anim, id + ": Animator");
                Assert.IsTrue(anim.avatar && anim.avatar.isValid, id + ": Avatar válido");
                Assert.IsTrue(h.Run && h.Run.isLooping, id + ": correr en bucle");
                Assert.IsTrue(h.Prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == h.HandBone), id + ": existe " + h.HandBone);
            }
        }

        [UnityTest]
        public IEnumerator Personaje_CadaUnoUsaSuEsqueletoYSujetaCadaArmaEnSuMano()
        {
            foreach (var id in HeroCatalog.Order())
            {
                var p = Play(id);
                var h = HeroCatalog.Get(id);
                var rig = p.Parts.Rig;
                Assert.IsNotNull(rig, id + ": rig");
                Assert.IsTrue(rig.Valid, id + ": huesos resueltos");
                Assert.AreEqual(h.HandBone, p.Parts.Holder.parent.name, id + ": socket hijo del hueso de la mano");
                Assert.IsTrue(p.Model.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 0, id + ": su modelo con piel");
                Assert.AreEqual(0, p.Root.GetComponentsInChildren<MeshRenderer>().Count(r => r.name == "Icosphere"), id + ": sin la esfera suelta de Meshy");
                foreach (var w in Weapons)
                {
                    p.SetWeapon(WeaponRegistry.Create(w));
                    yield return null;
                    var models = p.Parts.Holder.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToList();
                    Assert.AreEqual(1, models.Count, id + "/" + w + ": un solo modelo en el socket");
                    Assert.AreEqual(0, models[0].GetComponentsInChildren<Collider>(true).Length + models[0].GetComponentsInChildren<Rigidbody>(true).Length, id + "/" + w + ": sin física");
                    // Correr y atacar: el arma sigue colgando del socket de la mano
                    G.Input.Tap("KeyD"); Step(15); G.Input.Release("KeyD");
                    Tap("KeyX"); Step(8);
                    yield return null;   // LateUpdate del rig (animación + IK)
                    Assert.AreEqual(p.Parts.Holder, models[0].parent, id + "/" + w + ": sigue en el socket");
                    Assert.Less(Vector3.Distance(rig.Hand.position, rig.Socket.position), 0.2f, id + "/" + w + ": pegada a la mano");
                }
                // Puños: no se ve el puño del clásico (ya tiene mano)
                p.SetWeapon(WeaponRegistry.Create("fists"));
                yield return null;
                Assert.AreEqual(0, p.Parts.Holder.Cast<Transform>().Count(t => t.gameObject.activeSelf));
            }
        }

        // Altura real de la malla (horneada en su pose actual)
        static float MeshHeight(Player p)
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            var m = new Mesh();
            foreach (var smr in p.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(m, false);
                var M = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in m.vertices) { float y = M.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            }
            Object.Destroy(m);
            return hi - lo;
        }

        [UnityTest]
        public IEnumerator Escala_TodosMidenLoMismoYPisanElSuelo()
        {
            foreach (var id in HeroCatalog.Order())
            {
                var p = Play(id);
                yield return null;
                float h = MeshHeight(p);
                Assert.AreEqual(HeroCatalog.Height, h, 0.25f, id + ": altura común (" + h.ToString("0.00") + ")");
                Assert.Greater(h, 1.85f, id + ": más alto que el clásico (antes se veían pequeños)");
                Assert.AreEqual(0.42f, p.Body.Radius, 1e-4f, id + ": el cuerpo de colisión no cambia");
            }
        }

        [UnityTest]
        public IEnumerator Idle_QuietoReposaYAlMoverseCorre()
        {
            int withIdle = 0;
            foreach (var id in HeroCatalog.Order())
            {
                var p = Play(id);
                var rig = p.Parts.Rig;
                var h = HeroCatalog.Get(id);
                Assert.AreEqual(h.Idle != null, rig.HasIdle, id);
                if (!rig.HasIdle) continue;   // incompatible (documentado): sigue con la pose del correr
                withIdle++;
                // (por tiempo, no por frames: la transición dura ≈0,17 s)
                rig.Moving = 0f;
                for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 0.5f;) yield return null;
                Assert.Less(rig.RunWeight, 0.05f, id + ": quieto → reposo");
                for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 0.5f;) { rig.Moving = 1f; yield return null; }
                Assert.Greater(rig.RunWeight, 0.95f, id + ": moviéndose → correr");
                for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 0.5f;) { rig.Moving = 0f; yield return null; }
                Assert.Less(rig.RunWeight, 0.05f, id + ": y vuelve al reposo");
                Assert.Less(Vector3.Distance(rig.Hand.position, rig.Socket.position), 0.3f, id + ": el socket sigue en la mano");
            }
            Assert.AreEqual(7, withIdle, "Idle en los 7 personajes compatibles (Pícara Rosaviento no lo es)");
        }

        [UnityTest]
        public IEnumerator Personaje_ReiniciarYCambiarDePisoNoDuplica()
        {
            var p = Play("azure_wayfarer");
            p.SetWeapon(WeaponRegistry.Create("rake"));
            G.LoadFloor(2, G.CollectCarry());
            G.LoadFloor(2, G.CollectCarry());
            yield return null;
            var rigs = Object.FindObjectsByType<HeroRig>(FindObjectsSortMode.None).Where(r => r.isActiveAndEnabled && r.ModelRoot && r.ModelRoot.gameObject.scene.IsValid() && r.Entry != null).ToList();
            Assert.AreEqual(1, rigs.Count(r => r.ModelRoot == G.Players[0].Model), "un solo personaje");
            Assert.AreEqual("rake", G.Players[0].Weapon.Id, "el arma pasa de piso");
            Assert.AreEqual("azure_wayfarer", G.Players[0].Parts.Rig.Entry.Id, "y el personaje elegido");
            Assert.AreEqual(1, G.Players[0].Parts.Holder.Cast<Transform>().Count(t => t.gameObject.activeSelf));
        }

        [UnityTest]
        public IEnumerator Sala_LaFilaPersonajeRecorreLosPersonajesYLaVistaPreviaLosMonta()
        {
            G.State = GameState.Title; G.OpenLobby(1);
            Step(2);
            Assert.AreEqual(0, G.Lobby.Row(0), "fila Personaje (la primera)");
            var ids = HeroCatalog.Order();
            Tap("KeyD");
            Assert.AreEqual(ids[0], G.Loadouts[0].Hero);
            Tap("KeyD");
            Assert.AreEqual(ids[1], G.Loadouts[0].Hero);
            Assert.AreEqual(HeroCatalog.AbilityOf(ids[1]), G.Loadouts[0].Ability, "la habilidad es la del personaje");
            Step(2);
            Assert.IsNotNull(G.Lobby.PreviewOf(0).Parts.Rig, "la vista previa muestra el personaje");
            Tap("KeyA"); Tap("KeyA");
            Assert.AreEqual(ids.Last(), G.Loadouts[0].Hero, "vuelta atrás desde el primero");
            // Un id desconocido vuelve al de por defecto del hueco
            Assert.IsFalse(G.Loadouts[0].SetHero("no_existe"));
            HeroCatalog.ClassicAllowed = false;
            Assert.IsFalse(G.Loadouts[0].SetHero(HeroCatalog.Classic), "fuera de las pruebas antiguas no hay clásico");
            Assert.AreEqual(ids[0], G.Loadouts[0].Hero);
            yield break;
        }
    }
}
