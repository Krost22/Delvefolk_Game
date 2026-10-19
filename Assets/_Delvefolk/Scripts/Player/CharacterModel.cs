using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Partes animables del aventurero
    public sealed class CharacterParts
    {
        public Transform FootL, FootR, LHand, ArmPivot, Holder, Armor, Stars, Cape, Shell;
        public MeshRenderer StarsRenderer, ShellRenderer;
        public CosmeticFx Fx;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        // Personaje personalizado: su esqueleto (null en el aventurero clásico)
        public HeroRig Rig;
    }

    // Modelo del aventurero (CharacterModel.build): lo usan el jugador en partida y la vista previa de la sala.
    // Todo con primitivas; las piezas fijas se fusionan en una sola malla con color por vértice.
    public static class CharacterModel
    {
        static Mesh footMesh, handMesh, starsMesh, shellMesh;

        public static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            Effects.NoShadows(r);
            return go;
        }

        // hero: id de Resources/Heroes (null o "classic" = el aventurero hecho por código)
        public static CharacterParts Build(Transform model, Dictionary<string, string> cosmetics, string hero = null)
        {
            var cos = cosmetics ?? Cosmetics.Defaults(0);
            var he = HeroCatalog.Get(hero);
            if (he != null)
            {
                var hp = HeroModel.Build(model, he);
                if (hp != null)
                {
                    AddCommon(model, hp, cos);
                    // Estrellas de aturdimiento y caparazón a la altura del personaje
                    float h = HeroCatalog.Height;
                    hp.Stars.localPosition = new Vector3(0f, h + 0.3f, 0f);
                    hp.Shell.localPosition = new Vector3(0f, h * 0.5f, 0f);
                    hp.Shell.localScale = Vector3.one * (h / 1.85f);
                    return hp;
                }
            }
            var palette = Cosmetics.Get(Get(cos, "palette")) ?? Cosmetics.Get("moss");
            var matDef = Cosmetics.Get(Get(cos, "material")) ?? Cosmetics.Get("cloth");
            var b = new MeshBuilder();
            var ctx = new CosmeticCtx { B = b, Primary = palette.Primary, Secondary = palette.Secondary, Material = matDef };
            Color skin = MathUtil.Hex(0xf0c08a), brown = MathUtil.Hex(0x5a3a22);

            b.Add(ProcMesh.Cylinder(0.3f, 0.42f, 0.85f, 8), new Vector3(0, 0.72f, 0), ctx.Mat(palette.Primary));
            b.Add(ProcMesh.Cylinder(0.36f, 0.37f, 0.1f, 8), new Vector3(0, 0.6f, 0), brown);
            b.Add(ProcMesh.Icosahedron(0.33f, 1), new Vector3(0, 1.42f, 0), skin);
            b.Add(ProcMesh.Sphere(0.1f, 6, 5), Matrix4x4.TRS(new Vector3(0, 1.38f, 0.33f), Quaternion.identity, new Vector3(1, 0.9f, 1.3f)), MathUtil.Hex(0xe8a070));
            var eye = ProcMesh.Sphere(0.05f, 5, 4);
            b.Add(eye, new Vector3(0.12f, 1.5f, 0.28f), MathUtil.Hex(0x111111));
            b.Add(eye, new Vector3(-0.12f, 1.5f, 0.28f), MathUtil.Hex(0x111111));
            if (matDef.Patches)
            {
                b.Add(ProcMesh.Box(0.14f, 0.12f, 0.03f), Matrix4x4.TRS(new Vector3(-0.2f, 0.55f, 0.3f), Quaternion.Euler(0, 0.4f * Mathf.Rad2Deg, 0), Vector3.one), MathUtil.Hex(palette.Secondary));
                b.Add(ProcMesh.Box(0.14f, 0.12f, 0.03f), Matrix4x4.TRS(new Vector3(0.25f, 0.85f, -0.22f), Quaternion.Euler(0, 2.4f * Mathf.Rad2Deg, 0), Vector3.one), MathUtil.Hex(palette.Secondary));
            }
            foreach (var slot in new[] { "body", "head", "acc" })
            {
                var it = Cosmetics.Get(Get(cos, slot));
                if (it != null && it.Build != null) it.Build(ctx);
            }

            var parts = new CharacterParts();
            var body = MeshObject(model, "Cuerpo", b.ToMesh("aventurero"), Mats.Lit, Vector3.zero);
            parts.Renderers.Add(body.GetComponent<MeshRenderer>());

            if (ctx.Cape != null)
            {
                var cb = new MeshBuilder();
                cb.AddDouble(ctx.Cape, Matrix4x4.identity, ctx.CapeColor);
                var cape = MeshObject(model, "Capa", cb.ToMesh("capa"), Mats.Lit, Cosmetics.AnchorPos(Anchor.Back) + new Vector3(0, 0.18f, -0.02f));
                parts.Cape = cape.transform;
                parts.Renderers.Add(cape.GetComponent<MeshRenderer>());
            }

            if (!footMesh) footMesh = new MeshBuilder().Add(ProcMesh.Box(0.18f, 0.14f, 0.3f), Vector3.zero, brown).ToMesh("pie");
            if (!handMesh) handMesh = new MeshBuilder().Add(ProcMesh.Icosahedron(0.12f), Vector3.zero, skin).ToMesh("mano");
            var fl = MeshObject(model, "PieI", footMesh, Mats.Lit, new Vector3(0.16f, 0.07f, 0));
            var fr = MeshObject(model, "PieD", footMesh, Mats.Lit, new Vector3(-0.16f, 0.07f, 0));
            var lh = MeshObject(model, "ManoI", handMesh, Mats.Lit, new Vector3(0.42f, 0.8f, 0.1f));
            parts.FootL = fl.transform; parts.FootR = fr.transform; parts.LHand = lh.transform;
            parts.Renderers.Add(fl.GetComponent<MeshRenderer>());
            parts.Renderers.Add(fr.GetComponent<MeshRenderer>());
            parts.Renderers.Add(lh.GetComponent<MeshRenderer>());

            // Brazo del objeto: pivote en el hombro derecho (orden de rotación YXZ)
            var arm = new GameObject("Brazo").transform;
            arm.SetParent(model, false);
            arm.localPosition = new Vector3(-0.18f, 1.0f, 0.05f);
            var holder = new GameObject("Mano").transform;
            holder.SetParent(arm, false);
            holder.localPosition = new Vector3(0, 0, 0.45f);
            var rh = MeshObject(holder, "ManoD", handMesh, Mats.Lit, Vector3.zero);
            parts.Renderers.Add(rh.GetComponent<MeshRenderer>());
            parts.ArmPivot = arm; parts.Holder = holder;

            AddCommon(model, parts, cos);
            return parts;
        }

        // Lo que comparten el aventurero clásico y los personajes personalizados
        static void AddCommon(Transform model, CharacterParts parts, Dictionary<string, string> cos)
        {
            // Equipo de JUEGO (armadura ganada): grupo aparte, nunca mezclado con los cosméticos
            parts.Armor = new GameObject("Armadura").transform;
            parts.Armor.SetParent(model, false);

            // Estrellas de aturdimiento (ocultas por defecto)
            if (!starsMesh)
            {
                var sb = new MeshBuilder();
                for (int i = 0; i < 3; i++) sb.Add(ProcMesh.Octahedron(0.09f), new Vector3(Mathf.Cos(i * 2.1f) * 0.42f, 0, Mathf.Sin(i * 2.1f) * 0.42f), Color.white);
                starsMesh = sb.ToMesh("estrellas");
            }
            var stars = MeshObject(model, "Estrellas", starsMesh, Mats.Unlit, new Vector3(0, 2.15f, 0));
            stars.SetActive(false);
            parts.Stars = stars.transform;
            parts.StarsRenderer = stars.GetComponent<MeshRenderer>();

            // Caparazón de "Último recurso"
            if (!shellMesh) shellMesh = new MeshBuilder().Add(ProcMesh.Icosahedron(1.05f, 1), Vector3.zero, new Color(1f, 0.816f, 0.25f, 0.25f)).ToMesh("caparazon");
            var shell = MeshObject(model, "Caparazon", shellMesh, Mats.Transparent, new Vector3(0, 0.95f, 0));
            shell.SetActive(false);
            parts.Shell = shell.transform;
            parts.ShellRenderer = shell.GetComponent<MeshRenderer>();

            var fxDef = Cosmetics.Get(Get(cos, "fx"));
            parts.Fx = fxDef != null ? fxDef.Fx : null;
            // Las piezas de armadura están hechas a la medida del aventurero clásico
            if (parts.Rig != null) parts.Armor.gameObject.SetActive(false);
        }

        static string Get(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : null; }
    }
}
