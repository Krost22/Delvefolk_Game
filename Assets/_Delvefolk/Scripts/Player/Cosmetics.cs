using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Delvefolk
{
    // Personalización cosmética (Cosmetics.js): definiciones de piezas y construcción del modelo del aventurero.
    // Todo es puramente visual: ninguna pieza toca vida, daño, velocidad ni nada del juego.
    public enum Anchor { Model, Head, Chest, Back, Belt, ShoulderL, ShoulderR }

    public sealed class CosmeticFx
    {
        public float Every, Up, Size, Y = 0.8f;
        public int Color;
    }

    public sealed class CosmeticDef
    {
        public string Id, Slot, Name, Icon;
        public string[] Occupies = new string[0];
        public int Primary, Secondary;           // paletas
        public float ColorMul = 1f;              // materiales (cuero oscurece)
        public float DSat, DLight;               // materiales (remendado: offsetHSL)
        public bool Patches;
        public CosmeticFx Fx;
        public Action<CosmeticCtx> Build;
    }

    // Contexto de construcción: las piezas se fusionan en una sola malla con color por vértice
    public sealed class CosmeticCtx
    {
        public MeshBuilder B;
        public int Primary, Secondary;
        public CosmeticDef Material;
        public MeshData Cape;            // la capa es aparte: ondea al correr
        public Color CapeColor;

        public Color Mat(int c) { return Cosmetics.ApplyMaterial(Material, c); }

        public void Part(Anchor a, MeshData md, float x, float y, float z, Color c, float rx = 0, float ry = 0, float rz = 0, float sx = 1, float sy = 1, float sz = 1, bool doubleSided = false)
        {
            var m = Matrix4x4.TRS(Cosmetics.AnchorPos(a) + new Vector3(x, y, z), ProcMesh.EulerXYZ(rx, ry, rz), new Vector3(sx, sy, sz));
            if (doubleSided) B.AddDouble(md, m, c); else B.Add(md, m, c);
        }

        public void Part(Anchor a, MeshData md, float x, float y, float z, int hex, float rx = 0, float ry = 0, float rz = 0, float sx = 1, float sy = 1, float sz = 1, bool doubleSided = false)
        {
            Part(a, md, x, y, z, MathUtil.Hex(hex), rx, ry, rz, sx, sy, sz, doubleSided);
        }
    }

    public static class Cosmetics
    {
        public static readonly string[] Slots = { "head", "body", "acc", "palette", "material", "fx" };
        public static readonly Dictionary<string, string> SlotNames = new Dictionary<string, string>
        {
            { "head", "Cabeza" }, { "body", "Cuerpo" }, { "acc", "Accesorio" }, { "palette", "Colores" }, { "material", "Material" }, { "fx", "Efecto" },
        };

        public static readonly Dictionary<string, CosmeticDef> Items = new Dictionary<string, CosmeticDef>();
        public static readonly Dictionary<string, List<CosmeticDef>> BySlot = new Dictionary<string, List<CosmeticDef>>();

        static void Add(CosmeticDef d)
        {
            Items[d.Id] = d;
            List<CosmeticDef> l;
            if (!BySlot.TryGetValue(d.Slot, out l)) { l = new List<CosmeticDef>(); BySlot[d.Slot] = l; }
            l.Add(d);
        }

        public static CosmeticDef Get(string id) { CosmeticDef d; return id != null && Items.TryGetValue(id, out d) ? d : null; }
        public static List<CosmeticDef> List(string slot) { List<CosmeticDef> l; return BySlot.TryGetValue(slot, out l) ? l : new List<CosmeticDef>(); }

        // Selección por defecto de cada jugador (J1 con olla y musgo, J2 con embudo y río)
        public static Dictionary<string, string> Defaults(int index)
        {
            return index == 0
                ? new Dictionary<string, string> { { "head", "pot" }, { "body", "tunic" }, { "acc", "none_acc" }, { "palette", "moss" }, { "material", "cloth" }, { "fx", "none_fx" } }
                : new Dictionary<string, string> { { "head", "funnel" }, { "body", "tunic" }, { "acc", "none_acc" }, { "palette", "river" }, { "material", "cloth" }, { "fx", "none_fx" } };
        }

        public struct Removed { public string Slot; public CosmeticDef Item, BlockedBy; }

        // Corrige una selección: piezas desconocidas → por defecto; dos piezas en la misma posición física →
        // se quita la otra. prefer: hueco que el jugador acaba de cambiar (su pieza se respeta).
        public static Dictionary<string, string> Validate(Dictionary<string, string> sel, int index, string prefer, List<Removed> removed = null)
        {
            var def = Defaults(index);
            var outp = new Dictionary<string, string>();
            foreach (var s in Slots)
            {
                string id = null;
                if (sel != null) sel.TryGetValue(s, out id);
                var it = Get(id);
                outp[s] = it != null && it.Slot == s ? it.Id : def[s];
            }
            var order = Slots.OrderBy(s => s == prefer ? 0 : 1).ToList();
            var taken = new Dictionary<string, string>();
            foreach (var slot in order)
            {
                var it = Get(outp[slot]);
                string clash = it.Occupies.FirstOrDefault(o => taken.ContainsKey(o));
                if (clash != null)
                {
                    if (removed != null) removed.Add(new Removed { Slot = slot, Item = it, BlockedBy = Get(outp[taken[clash]]) });
                    outp[slot] = List(slot).First(c => c.Occupies.Length == 0).Id;
                    continue;
                }
                foreach (var o in it.Occupies) taken[o] = slot;
            }
            return outp;
        }

        public static Vector3 AnchorPos(Anchor a)
        {
            switch (a)
            {
                case Anchor.Head: return new Vector3(0, 1.56f, 0);
                case Anchor.Chest: return new Vector3(0, 0.85f, 0);
                case Anchor.Back: return new Vector3(0, 0.95f, -0.32f);
                case Anchor.Belt: return new Vector3(0, 0.6f, 0);
                case Anchor.ShoulderL: return new Vector3(0.36f, 1.08f, 0);
                case Anchor.ShoulderR: return new Vector3(-0.36f, 1.08f, 0);
                default: return Vector3.zero;
            }
        }

        public static Color ApplyMaterial(CosmeticDef mat, int hex)
        {
            Color c = MathUtil.Hex(hex);
            if (mat == null) return c;
            if (mat.ColorMul != 1f) c = new Color(c.r * mat.ColorMul, c.g * mat.ColorMul, c.b * mat.ColorMul);
            if (mat.DSat != 0f || mat.DLight != 0f) c = OffsetHsl(c, 0f, mat.DSat, mat.DLight);
            return c;
        }

        // THREE.Color.offsetHSL
        public static Color OffsetHsl(Color c, float dh, float ds, float dl)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float h = 0, s, l = (min + max) / 2f;
            if (min == max) { h = 0; s = 0; }
            else
            {
                float delta = max - min;
                s = l <= 0.5f ? delta / (max + min) : delta / (2f - max - min);
                if (max == c.r) h = (c.g - c.b) / delta + (c.g < c.b ? 6 : 0);
                else if (max == c.g) h = (c.b - c.r) / delta + 2;
                else h = (c.r - c.g) / delta + 4;
                h /= 6f;
            }
            h = Mathf.Repeat(h + dh, 1f); s = Mathf.Clamp01(s + ds); l = Mathf.Clamp01(l + dl);
            return Hsl(h, s, l);
        }

        static Color Hsl(float h, float s, float l)
        {
            if (s == 0) return new Color(l, l, l);
            float p = l <= 0.5f ? l * (1 + s) : l + s - l * s;
            float q = 2 * l - p;
            return new Color(Hue(q, p, h + 1f / 3f), Hue(q, p, h), Hue(q, p, h - 1f / 3f));
        }

        static float Hue(float p, float q, float t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1f / 6f) return p + (q - p) * 6 * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * 6 * (2f / 3f - t);
            return p;
        }

        static Cosmetics()
        {
            const float PI = Mathf.PI;
            // ---------------- Paletas y materiales ----------------
            foreach (var p in new[]
            {
                ("moss", "Musgo", 0x5b7f3c, 0x9a6b3a), ("river", "Río", 0x3c6a9f, 0x8a6a4a), ("brick", "Ladrillo", 0x9a3b2c, 0xd8b060),
                ("plum", "Ciruela", 0x6a3a7a, 0xc9a227), ("sand", "Arena", 0xb89a62, 0x5a3a22), ("coal", "Carbón", 0x3a3a40, 0xb03030),
                ("pumpkin", "Calabaza", 0xd0702a, 0x3a5a2a), ("mint", "Menta", 0x5ab09a, 0xf0e0c0),
            })
                Add(new CosmeticDef { Id = p.Item1, Slot = "palette", Name = p.Item2, Icon = "🎨", Primary = p.Item3, Secondary = p.Item4 });

            Add(new CosmeticDef { Id = "cloth", Slot = "material", Name = "Tela basta", Icon = "🧵" });
            Add(new CosmeticDef { Id = "silk", Slot = "material", Name = "Seda brillante", Icon = "✨" });
            Add(new CosmeticDef { Id = "leather", Slot = "material", Name = "Cuero curtido", Icon = "🟫", ColorMul = 0.72f });
            Add(new CosmeticDef { Id = "patched", Slot = "material", Name = "Remendado", Icon = "🩹", Patches = true, DSat = -0.1f, DLight = 0.05f });

            // ---------------- Efectos cosméticos discretos ----------------
            Add(new CosmeticDef { Id = "none_fx", Slot = "fx", Name = "Ninguno", Icon = "·" });
            Add(new CosmeticDef { Id = "sparks", Slot = "fx", Name = "Chispitas", Icon = "✦", Fx = new CosmeticFx { Every = 0.35f, Color = 0xffe08a, Up = 2.5f, Size = 0.06f } });
            Add(new CosmeticDef { Id = "flies", Slot = "fx", Name = "Moscas fieles", Icon = "🪰", Fx = new CosmeticFx { Every = 0.5f, Color = 0x222222, Up = 1.2f, Size = 0.05f, Y = 1.9f } });
            Add(new CosmeticDef { Id = "petals", Slot = "fx", Name = "Pétalos", Icon = "🌸", Fx = new CosmeticFx { Every = 0.45f, Color = 0xff9ac8, Up = 1.5f, Size = 0.07f, Y = 1.6f } });

            // ---------------- Cabeza ----------------
            Add(new CosmeticDef { Id = "none_head", Slot = "head", Name = "Pelo al aire", Icon = "🙂", Build = c =>
            {
                c.Part(Anchor.Head, ProcMesh.Icosahedron(0.3f), 0, 0.1f, -0.04f, 0x4a2a14, sx: 1.05f, sy: 0.5f, sz: 1.05f);
            } });
            Add(new CosmeticDef { Id = "pot", Slot = "head", Name = "Olla de cocina", Icon = "🍲", Build = c =>
            {
                c.Part(Anchor.Head, ProcMesh.Cylinder(0.36f, 0.33f, 0.3f, 10), 0, 0.14f, 0, 0x7d848c);
                c.Part(Anchor.Head, ProcMesh.Torus(0.36f, 0.03f, 4, 12), 0, 0, 0, 0x7d848c, rx: PI / 2);
                c.Part(Anchor.Head, ProcMesh.Box(0.4f, 0.06f, 0.08f), 0.5f, 0.14f, 0, 0x3a3a3a);
            } });
            Add(new CosmeticDef { Id = "funnel", Slot = "head", Name = "Embudo", Icon = "🔻", Build = c =>
            {
                c.Part(Anchor.Head, ProcMesh.Cone(0.4f, 0.45f, 10, 1, true), 0, 0.22f, 0, 0xb87333, doubleSided: true);
                c.Part(Anchor.Head, ProcMesh.Cylinder(0.05f, 0.05f, 0.3f, 6), 0, 0.54f, 0, 0xb87333);
            } });
            Add(new CosmeticDef { Id = "helmet", Slot = "head", Name = "Casco abollado", Icon = "⛑️", Build = c =>
            {
                c.Part(Anchor.Head, ProcMesh.Sphere(0.37f, 8, 5, 0, PI * 2, 0, PI / 2), 0, 0, 0, 0x9aa3ad, sy: 0.85f);
                c.Part(Anchor.Head, ProcMesh.Sphere(0.08f, 5, 4), 0.18f, 0.22f, 0.2f, 0x6a737d);
                c.Part(Anchor.Head, ProcMesh.Box(0.07f, 0.3f, 0.05f), 0, -0.08f, 0.36f, 0x9aa3ad);
                c.Part(Anchor.Head, ProcMesh.Box(0.05f, 0.12f, 0.5f), 0, 0.36f, 0, c.Secondary);
            } });
            Add(new CosmeticDef { Id = "explorer", Slot = "head", Name = "Sombrero de aventurero", Icon = "🤠", Build = c =>
            {
                c.Part(Anchor.Head, ProcMesh.Cylinder(0.62f, 0.62f, 0.05f, 12), 0, 0.02f, 0, 0x7a5a34);
                c.Part(Anchor.Head, ProcMesh.Cylinder(0.28f, 0.34f, 0.32f, 10), 0, 0.2f, 0, 0x7a5a34);
                c.Part(Anchor.Head, ProcMesh.Cylinder(0.345f, 0.345f, 0.07f, 10), 0, 0.09f, 0, c.Secondary);
                c.Part(Anchor.Head, ProcMesh.Cone(0.05f, 0.4f, 4), 0.26f, 0.32f, -0.05f, 0xd04a3a, rz: -0.6f);
            } });
            Add(new CosmeticDef { Id = "hood", Slot = "head", Name = "Capucha", Icon = "🧥", Build = c =>
            {
                var m = c.Mat(c.Primary);
                c.Part(Anchor.Head, ProcMesh.Sphere(0.4f, 8, 6, PI * 0.15f, PI * 1.7f, 0, PI * 0.62f), 0, -0.12f, 0, m, ry: PI, doubleSided: true);
                c.Part(Anchor.Head, ProcMesh.Cone(0.16f, 0.38f, 6), 0, 0.12f, -0.3f, m, rx: -1.1f, doubleSided: true);
            } });
            Add(new CosmeticDef { Id = "mask", Slot = "head", Name = "Máscara de madera", Icon = "🎭", Build = c =>
            {
                c.Part(Anchor.Head, ProcMesh.Box(0.46f, 0.4f, 0.06f), 0, -0.16f, 0.36f, 0xb08850);
                foreach (var s in new[] { -1f, 1f }) c.Part(Anchor.Head, ProcMesh.Box(0.1f, 0.05f, 0.02f), s * 0.11f, -0.08f, 0.4f, 0x111111);
                c.Part(Anchor.Head, ProcMesh.Box(0.47f, 0.06f, 0.065f), 0, -0.28f, 0.36f, c.Secondary);
                c.Part(Anchor.Head, ProcMesh.Torus(0.34f, 0.025f, 3, 12), 0, -0.1f, 0, 0x3a2a1a, rx: PI / 2);
            } });
            Add(new CosmeticDef { Id = "horns", Slot = "head", Name = "Cuernos fantásticos", Icon = "😈", Build = c =>
            {
                foreach (var s in new[] { -1f, 1f })
                {
                    c.Part(Anchor.Head, ProcMesh.Cone(0.08f, 0.42f, 5), s * 0.25f, 0.06f, 0, 0xe8dcc0, rz: -s * 0.7f);
                    c.Part(Anchor.Head, ProcMesh.Cone(0.04f, 0.14f, 5), s * 0.4f, 0.23f, 0, 0x3a3030, rz: -s * 0.7f);
                }
            } });
            Add(new CosmeticDef { Id = "crown", Slot = "head", Name = "Corona de cartón", Icon = "👑", Build = c =>
            {
                c.Part(Anchor.Head, ProcMesh.Cylinder(0.3f, 0.31f, 0.14f, 8, 1, true), 0, 0.05f, 0, 0xe0b840, doubleSided: true);
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * PI * 2;
                    c.Part(Anchor.Head, ProcMesh.Cone(0.06f, 0.16f, 3), Mathf.Cos(a) * 0.29f, 0.19f, Mathf.Sin(a) * 0.29f, 0xe0b840);
                }
                c.Part(Anchor.Head, ProcMesh.Octahedron(0.05f), 0, 0.06f, 0.31f, 0xd03050);
            } });

            // ---------------- Cuerpo ----------------
            Add(new CosmeticDef { Id = "tunic", Slot = "body", Name = "Túnica sencilla", Icon = "👕", Build = c =>
            {
                c.Part(Anchor.Chest, ProcMesh.Box(0.16f, 0.16f, 0.05f), 0.12f, 0.13f, 0.33f, c.Secondary);
            } });
            Add(new CosmeticDef { Id = "vest", Slot = "body", Name = "Chaleco de cuero", Icon = "🦺", Build = c =>
            {
                foreach (var s in new[] { -1f, 1f }) c.Part(Anchor.Chest, ProcMesh.Box(0.2f, 0.5f, 0.06f), s * 0.17f, 0, 0.34f, 0x6a4426, ry: s * 0.25f);
                c.Part(Anchor.Chest, ProcMesh.Cylinder(0.34f, 0.4f, 0.5f, 8, 1, true, PI * 0.6f, PI * 0.8f), 0, 0, 0, 0x6a4426, doubleSided: true);
                c.Part(Anchor.Chest, ProcMesh.Sphere(0.035f, 4, 3), 0.07f, 0.05f, 0.37f, c.Secondary);
            } });
            Add(new CosmeticDef { Id = "plate", Slot = "body", Name = "Peto de hojalata", Icon = "🛡️", Build = c =>
            {
                c.Part(Anchor.Chest, ProcMesh.Cylinder(0.34f, 0.4f, 0.46f, 8, 1, false, -PI * 0.45f, PI * 0.9f), 0, 0.02f, 0, 0xb0b8c0);
                c.Part(Anchor.Chest, ProcMesh.Sphere(0.03f, 4, 3), 0, 0.12f, 0.38f, 0x4a4a4a);
                c.Part(Anchor.ShoulderL, ProcMesh.Sphere(0.16f, 6, 4, 0, PI * 2, 0, PI / 2), 0, 0, 0, 0xb0b8c0, rz: 0.4f);
                c.Part(Anchor.ShoulderR, ProcMesh.Sphere(0.16f, 6, 4, 0, PI * 2, 0, PI / 2), 0, 0, 0, 0xb0b8c0, rz: -0.4f);
            } });
            Add(new CosmeticDef { Id = "cape", Slot = "body", Name = "Capa de héroe", Icon = "🦸", Occupies = new[] { "back" }, Build = c =>
            {
                var cape = ProcMesh.Plane(0.7f, 1.0f, 1, 3);
                for (int i = 0; i < cape.Count; i++)
                {
                    var v = cape.V[i];
                    cape.V[i] = new Vector3(v.x, v.y, -Mathf.Max(0, -v.y) * 0.35f - Mathf.Abs(v.x) * 0.15f);
                }
                cape.Translate(0, -0.5f, 0);   // pivote arriba: ondea desde los hombros
                c.Cape = cape;
                c.CapeColor = c.Mat(c.Secondary);
                c.Part(Anchor.Chest, ProcMesh.Torus(0.33f, 0.04f, 3, 10, PI), 0, 0.17f, 0.05f, 0xc9a227, rx: PI / 2, rz: PI);
            } });
            Add(new CosmeticDef { Id = "fancy", Slot = "body", Name = "Atuendo extravagante", Icon = "🎩", Build = c =>
            {
                c.Part(Anchor.Chest, ProcMesh.Torus(0.3f, 0.09f, 4, 14), 0, 0.3f, 0, 0xf4f0e6, rx: PI / 2);
                c.Part(Anchor.ShoulderL, ProcMesh.Icosahedron(0.15f), 0, 0, 0, c.Secondary);
                c.Part(Anchor.ShoulderR, ProcMesh.Icosahedron(0.15f), 0, 0, 0, c.Secondary);
                for (int i = 0; i < 5; i++)
                {
                    float ang = (i - 2) * 0.35f;
                    c.Part(Anchor.Chest, ProcMesh.Circle(0.035f, 6), Mathf.Sin(ang) * 0.37f, -0.15f + (i % 2) * 0.16f, Mathf.Cos(ang) * 0.37f, 0xffffff, ry: ang);
                }
            } });
            Add(new CosmeticDef { Id = "robe", Slot = "body", Name = "Túnica de mago", Icon = "🧙", Build = c =>
            {
                c.Part(Anchor.Chest, ProcMesh.Cylinder(0.42f, 0.55f, 0.45f, 9, 1, true), 0, -0.44f, 0, c.Mat(c.Primary), doubleSided: true);
                c.Part(Anchor.Chest, ProcMesh.Octahedron(0.07f), -0.12f, 0.08f, 0.35f, 0xffe14a);
                c.Part(Anchor.Chest, ProcMesh.Octahedron(0.07f), 0.2f, -0.45f, 0.48f, 0xffe14a);
            } });

            // ---------------- Accesorios ----------------
            Add(new CosmeticDef { Id = "none_acc", Slot = "acc", Name = "Ninguno", Icon = "·" });
            Add(new CosmeticDef { Id = "backpack", Slot = "acc", Name = "Mochila", Icon = "🎒", Occupies = new[] { "back" }, Build = c =>
            {
                c.Part(Anchor.Back, ProcMesh.Box(0.46f, 0.5f, 0.24f), 0, -0.08f, -0.12f, 0x7a5230);
                c.Part(Anchor.Back, ProcMesh.Box(0.48f, 0.16f, 0.26f), 0, 0.16f, -0.12f, 0x5a3a22);
                c.Part(Anchor.Back, ProcMesh.Cylinder(0.08f, 0.08f, 0.5f, 6), 0, 0.3f, -0.12f, c.Secondary, rz: PI / 2);
            } });
            Add(new CosmeticDef { Id = "toolbelt", Slot = "acc", Name = "Cinturón de herramientas", Icon = "🧰", Build = c =>
            {
                foreach (var a in new[] { -0.9f, 0.2f, 1.3f })
                    c.Part(Anchor.Belt, ProcMesh.Box(0.12f, 0.14f, 0.08f), Mathf.Sin(a) * 0.38f, -0.06f, Mathf.Cos(a) * 0.38f, 0x6a4426, ry: a);
                c.Part(Anchor.Belt, ProcMesh.Box(0.12f, 0.1f, 0.04f), 0, 0, 0.38f, 0xc9a227);
                c.Part(Anchor.Belt, ProcMesh.Cylinder(0.02f, 0.02f, 0.3f, 4), -0.36f, 0.05f, 0.12f, 0x9aa3ad, rz: 0.3f);
            } });
            Add(new CosmeticDef { Id = "amulet", Slot = "acc", Name = "Amuleto visible", Icon = "📿", Build = c =>
            {
                c.Part(Anchor.Chest, ProcMesh.Torus(0.2f, 0.015f, 3, 12, PI), 0, 0.28f, 0.3f, 0xc9a227, rx: -0.3f, rz: PI);
                c.Part(Anchor.Chest, ProcMesh.Octahedron(0.08f), 0, 0.08f, 0.37f, 0x40d0ff);
            } });
            Add(new CosmeticDef { Id = "coinbag", Slot = "acc", Name = "Bolsa de monedas", Icon = "💰", Build = c =>
            {
                c.Part(Anchor.Belt, ProcMesh.Icosahedron(0.14f), 0.33f, -0.12f, 0.15f, 0x8a6a3a, sy: 1.2f);
                c.Part(Anchor.Belt, ProcMesh.Cylinder(0.05f, 0.07f, 0.06f, 6), 0.33f, 0.04f, 0.15f, 0xc9a227);
                c.Part(Anchor.Belt, ProcMesh.Cylinder(0.05f, 0.05f, 0.015f, 8), 0.4f, -0.25f, 0.25f, 0xf2c230, rx: 1.2f);
            } });
            Add(new CosmeticDef { Id = "duck", Slot = "acc", Name = "Patito al hombro", Icon = "🦆", Build = c =>
            {
                c.Part(Anchor.ShoulderL, ProcMesh.Icosahedron(0.11f), 0, 0.1f, 0, 0xf2d030, sx: 1f, sy: 0.8f, sz: 1.3f);
                c.Part(Anchor.ShoulderL, ProcMesh.Icosahedron(0.075f), 0, 0.21f, 0.08f, 0xf2d030);
                c.Part(Anchor.ShoulderL, ProcMesh.Cone(0.035f, 0.08f, 4), 0, 0.2f, 0.17f, 0xe07020, rx: PI / 2);
            } });
        }
    }
}
