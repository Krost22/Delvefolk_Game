using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Delvefolk.Tests
{
    // Nuevo modelo del Crupier (solo visual) y su reposo: el combate tiene que ser idéntico al del placeholder
    public class CrupierModelTests : BossTestBase
    {
        CrupierBoss Enter()
        {
            BossConfig.DemoFinalBoss = BossConfig.DemoBossDefault;
            var bf = StartBoss("crupier");
            return (CrupierBoss)bf.BossList[0];
        }

        static string Signature(CrupierBoss b)
        {
            return "r=" + b.Body.Radius + " m=" + b.Body.Mass + " hp=" + b.MaxHp + " v=" + b.Cfg.Speed + " dmg=" + b.Cfg.Damage + " bar=" + b.Cfg.BarY +
                   " patrones=" + string.Join(",", b.Patterns.Select(p => p.Id + ":" + p.Weight + ":" + p.Cooldown));
        }

        [UnityTest]
        public IEnumerator Modelo_SeVeElNuevoYElCombateNoCambia()
        {
            var cfg = RouletteConfig.Load();
            Assert.IsNotNull(cfg.CrupierModel, "modelo asignado en Resources/CrupierRoulette");
            StringAssert.Contains("Crimson_Aristocrat", cfg.CrupierModel.name);
            var b = Enter();
            Assert.IsNotNull(b.Figure, "usa el modelo con esqueleto");
            Assert.Greater(b.Model.GetComponentsInChildren<SkinnedMeshRenderer>().Length, 0);
            Assert.AreEqual(0, b.Model.GetComponentsInChildren<Collider>(true).Length + b.Model.GetComponentsInChildren<Rigidbody>(true).Length, "sin física de Unity");
            Assert.AreEqual(0, b.Model.GetComponentsInChildren<MeshRenderer>().Count(r => r.name == "Icosphere"));
            string withModel = Signature(b);
            // El mismo jefe con el placeholder hecho por código
            Art.UseAssets = false;
            try
            {
                G.EnterBoss(2, 3, G.CollectCarry(), "crupier");
                Step(Mathf.CeilToInt(BossConfig.IntroTime * 60f) + 2);
                var p = (CrupierBoss)G.BossFloor.BossList[0];
                Assert.IsNull(p.Figure);
                Assert.AreEqual(Signature(p), withModel, "radio, masa, vida, velocidad, daño y patrones idénticos");
            }
            finally { Art.UseAssets = true; }
            yield break;
        }

        [UnityTest]
        public IEnumerator Idle_QuietoReposaAlMoverseAndaYNoMueveLaRaiz()
        {
            var b = Enter();
            var f = b.Figure;
            var holder = f.transform.parent;
            var pos0 = holder.localPosition;
            float x0 = b.X, z0 = b.Z;
            // (se espera por tiempo, no por frames: la transición dura ≈0,17 s)
            f.Speed = 0f;
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 0.6f;) yield return null;
            Assert.AreEqual(1f, f.IdleWeight, 0.01f, "quieto → reposo");
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 0.6f;) { f.Speed = f.SpeedRef; yield return null; }
            Assert.Greater(f.MoveWeight, 0.95f, "andando → andar");
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 0.6f;) { f.Speed = 0f; yield return null; }
            Assert.AreEqual(1f, f.IdleWeight, 0.01f, "y vuelve al reposo");
            Assert.AreEqual(pos0, holder.localPosition, "la animación no desplaza el modelo (sin root motion)");
            Assert.AreEqual(x0, b.X, 1e-4f, "ni al jefe");
            Assert.AreEqual(z0, b.Z, 1e-4f);
            // Altura: la configurada (CrupierHeight)
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            var m = new Mesh();
            foreach (var smr in b.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(m, false);
                var M = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in m.vertices) { float y = M.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            }
            Object.Destroy(m);
            Assert.AreEqual(RouletteConfig.Load().CrupierHeight, hi - lo, 0.3f, "altura configurada");
            Assert.AreEqual(0f, lo - b.Root.position.y, 0.15f, "pies en el suelo");
        }
    }
}
