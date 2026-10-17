using NUnit.Framework;
using UnityEngine;

namespace Delvefolk.Tests
{
    // Fase 0: utilidades puras, RNG con semilla, entrada con códigos virtuales y primitivas
    public class CoreTests
    {
        [Test]
        public void Rng_ReproduceMulberry32DelOriginal()
        {
            // Valores calculados con Utils.js (DF.Utils.rng) en Node
            var r = new Rng(990u);
            double[] expected = { 0.80082759819924831, 0.63511460740119219, 0.26307516288943589, 0.11140116071328521, 0.56457316223531961, 0.56198505964130163 };
            foreach (var e in expected) Assert.AreEqual(e, r.NextDouble(), 1e-15);
            var r2 = new Rng(42u);
            double[] e2 = { 0.60110375192016363, 0.44829055899754167, 0.85246579349040985 };
            foreach (var e in e2) Assert.AreEqual(e, r2.NextDouble(), 1e-15);
        }

        [Test]
        public void Rng_NextNuncaLlegaA1YRangoEnteroIncluyeExtremos()
        {
            var r = new Rng(123u);
            int lo = 0, hi = 0;
            for (int i = 0; i < 20000; i++)
            {
                float f = r.Next();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                int k = r.IntIncl(2, 5);
                Assert.That(k, Is.InRange(2, 5));
                if (k == 2) lo++; if (k == 5) hi++;
            }
            Assert.Greater(lo, 0); Assert.Greater(hi, 0);
        }

        [Test]
        public void Rng_MismaSemillaMismaSecuencia()
        {
            var a = new Rng(7u); var b = new Rng(7u);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextDouble(), b.NextDouble());
        }

        [Test]
        public void MathUtil_DampYAngulos()
        {
            Assert.AreEqual(5f, MathUtil.Damp(0f, 10f, Mathf.Log(2f), 1f), 1e-4f);
            Assert.AreEqual(0.5f, MathUtil.AngleDiff(0f, 0.5f), 1e-6f);
            // Atraviesa ±π por el camino corto
            Assert.AreEqual(-0.2f, MathUtil.AngleDiff(Mathf.PI - 0.1f, -Mathf.PI + 0.1f) * -1f, 1e-5f);
            Assert.AreEqual(0.2f, MathUtil.AngleDiff(Mathf.PI - 0.1f, -Mathf.PI + 0.1f), 1e-5f);
            float a = MathUtil.DampAngle(Mathf.PI - 0.1f, -Mathf.PI + 0.1f, 15f, 1f);
            Assert.AreEqual(Mathf.PI + 0.1f, a, 1e-4f);
        }

        [Test]
        public void MathUtil_EasingsYRedondeoJs()
        {
            Assert.AreEqual(0f, MathUtil.EaseOutCubic(0f), 1e-6f);
            Assert.AreEqual(1f, MathUtil.EaseOutCubic(1f), 1e-6f);
            Assert.AreEqual(0.875f, MathUtil.EaseOutCubic(0.5f), 1e-6f);
            Assert.AreEqual(0.5f, MathUtil.EaseInOut(0.5f), 1e-6f);
            Assert.AreEqual(1f, MathUtil.EaseOutBack(1f), 1e-6f);
            Assert.Greater(MathUtil.EaseOutBack(0.7f), 1f);   // se pasa y vuelve
            // Math.round de JS: .5 hacia +∞, también en negativos
            Assert.AreEqual(1, MathUtil.RoundJs(0.5f));
            Assert.AreEqual(0, MathUtil.RoundJs(-0.5f));
            Assert.AreEqual(-1, MathUtil.RoundJs(-1.5f));
            Assert.AreEqual(3, MathUtil.RoundJs(2.5f));
        }

        [Test]
        public void Input_PulsacionesMantenidasYConsumo()
        {
            var input = new InputHub { ReadDevices = false };
            input.Press("Space");
            Assert.IsTrue(input.WasPressed("Space"));
            Assert.IsTrue(input.AnyPressed(new[] { "KeyE", "Space" }));
            Assert.IsTrue(input.Consume("Space"));
            Assert.IsFalse(input.Consume("Space"));   // una pulsación solo se consume una vez
            input.Hold("KeyW");
            input.Press("KeyE");
            input.Poll();                              // sin dispositivos: no toca nada
            Assert.IsTrue(input.IsDown("KeyW"));
            input.EndFrame();
            Assert.IsFalse(input.WasPressed("KeyE"));  // las pulsaciones duran un frame
            Assert.IsTrue(input.IsDown("KeyW"));       // las mantenidas siguen
            input.Release("KeyW");
            Assert.IsFalse(input.IsDown("KeyW"));
            input.Press("KeyE"); input.Press("GP0:X");
            Assert.IsTrue(input.ConsumeAny(new[] { "KeyE", "GP0:X" }));
            Assert.IsFalse(input.WasPressed("GP0:X"));
            input.Hold("KeyA"); input.Clear();
            Assert.IsFalse(input.IsDown("KeyA"));
        }

        [Test]
        public void Controles_EjeDeTecladoYEsquemaAlternativo()
        {
            var input = new InputHub { ReadDevices = false };
            var c = new PlayerControls(input, ControlSchemes.All[0]);
            input.Hold("KeyW"); input.Hold("KeyD");
            Assert.AreEqual(new Vector2(1, 1), c.Axis());
            input.Clear();
            input.Hold("ArrowLeft");
            Assert.AreEqual(Vector2.zero, c.Axis());           // sin alternativo, las flechas no son del J1
            c.Alt = ControlSchemes.All[1];
            Assert.AreEqual(new Vector2(-1, 0), c.Axis());     // en solitario el J1 también usa las flechas
            input.Press("Enter");
            var c2 = new PlayerControls(input, ControlSchemes.All[1]);
            Assert.IsTrue(c2.AttackPressed());
            Assert.IsFalse(c.AttackPressed());
            input.Press("GP1:UP");
            Assert.IsTrue(c2.MenuPressed(0));
            // El stick del mando, si se mueve, sustituye al teclado; Clear lo suelta
            input.Clear();
            input.Hold("KeyW");
            input.Pads[0].Connected = true; input.Pads[0].X = 0.6f; input.Pads[0].Y = -0.8f;
            Assert.AreEqual(new Vector2(0.6f, -0.8f), c.Axis());
            input.Clear();
            Assert.AreEqual(Vector2.zero, c.Axis());
            Assert.IsTrue(input.Pads[0].Connected);
        }

        [Test]
        public void ProcMesh_PrimitivasConLosMismosConteosQueThree()
        {
            var box = ProcMesh.Box(2, 0.3f, 2);
            Assert.AreEqual(24, box.Count); Assert.AreEqual(36, box.I.Count);
            var cyl = ProcMesh.Cylinder(0.3f, 0.42f, 0.85f, 8);
            Assert.AreEqual(9 * 2 + 8 * 2 + 9 * 2, cyl.Count);
            var ico = ProcMesh.Icosahedron(0.33f, 0);
            Assert.AreEqual(60, ico.Count);
            foreach (var v in ico.V) Assert.AreEqual(0.33f, v.magnitude, 1e-4f);
            Assert.AreEqual(240, ProcMesh.Icosahedron(1f, 1).Count);
            Assert.AreEqual(36 * 3, ProcMesh.Dodecahedron(0.25f).Count);
            var torus = ProcMesh.Torus(1f, 0.2f, 4, 8, Mathf.PI);
            Assert.AreEqual(5 * 9, torus.Count);
            var ring = ProcMesh.Ring(0.55f, 0.68f, 24);
            Assert.AreEqual(50, ring.Count);
            var sphere = ProcMesh.Sphere(1f, 8, 6);
            Assert.AreEqual(9 * 7, sphere.Count);
            var lathe = ProcMesh.Lathe(new[] { new Vector2(0.1f, 0), new Vector2(0.3f, 0.5f), new Vector2(0.1f, 1f) }, 6);
            Assert.AreEqual(7 * 3, lathe.Count);
            // Cono: radio superior 0, con tapa inferior
            Assert.AreEqual(0f, ProcMesh.Cone(0.16f, 0.45f, 5).V[0].x, 1e-6f);
        }

        [Test]
        public void MeshBuilder_FusionaPiezasConColor()
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Box(1, 1, 1), new Vector3(0, 1, 0), Color.red);
            b.Add(ProcMesh.Box(1, 1, 1), Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(-1, 1, 1)), Color.green);
            var m = b.ToMesh();
            Assert.AreEqual(48, m.vertexCount);
            Assert.AreEqual(72, m.triangles.Length);
            Assert.AreEqual(1.5f, m.bounds.max.y, 1e-5f);
            Object.DestroyImmediate(m);
        }
    }
}
