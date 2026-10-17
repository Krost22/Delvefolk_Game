using UnityEngine;

namespace Delvefolk
{
    // Cofre con tapa articulada (la tapa gira sobre la bisagra trasera para abrirse)
    public sealed class ChestProp
    {
        public Transform Root, Lid, Lock;
        public bool Open;
        public float LidAngle;   // radianes (0 cerrado, −1.9 abierto)

        public void UpdateLid(float dt, float target = -1.9f)
        {
            if (!Open || !Lid) return;
            LidAngle = MathUtil.Damp(LidAngle, target, 6f, dt);
            Lid.localRotation = Quaternion.Euler(LidAngle * Mathf.Rad2Deg, 0, 0);
        }
    }

    // Piezas low-poly reutilizables por los minijuegos (BonusProps.js, solo visuales)
    public static class BonusProps
    {
        static GameObject Obj(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
        {
            return CharacterModel.MeshObject(parent, name, mesh, mat, pos);
        }

        // Cofre de madera con herrajes del color trim. Mira hacia la cámara (al sur)
        public static ChestProp Chest(Transform parent, int trim = 0x8a8f96, float scale = 1f)
        {
            var c = new ChestProp();
            c.Root = new GameObject("Cofre").transform;
            c.Root.SetParent(parent, false);
            c.Root.localRotation = Quaternion.Euler(0, 180f, 0);
            Color wood = MathUtil.Hex(0xb88a5a), metal = MathUtil.Hex(trim);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Box(1.1f, 0.6f, 0.75f), new Vector3(0, 0.3f, 0), wood);
            foreach (float x in new[] { -0.42f, 0.42f }) b.Add(ProcMesh.Box(0.1f, 0.62f, 0.78f), new Vector3(x, 0.3f, 0), metal);
            Obj(c.Root, "Base", b.ToMesh("cofre"), Mats.Lit, Vector3.zero);
            c.Lid = new GameObject("Bisagra").transform;
            c.Lid.SetParent(c.Root, false);
            c.Lid.localPosition = new Vector3(0, 0.6f, -0.375f);
            var lid = new MeshBuilder().Add(ProcMesh.Cylinder(0.375f, 0.375f, 1.1f, 8, 1, false, 0f, Mathf.PI),
                Matrix4x4.TRS(new Vector3(0, 0, 0.375f), ProcMesh.EulerXYZ(0, Mathf.PI / 2f, Mathf.PI / 2f), Vector3.one), wood).ToMesh("tapaCofre");
            Obj(c.Lid, "Tapa", lid, Mats.LitDouble, Vector3.zero);
            c.Lock = Obj(c.Root, "Cerradura", new MeshBuilder().Add(ProcMesh.Box(0.18f, 0.2f, 0.06f), Vector3.zero, metal).ToMesh("cerradura"), Mats.Lit, new Vector3(0, 0.55f, 0.39f)).transform;
            Entity.BlobShadow(c.Root, 0.75f);
            c.Root.localScale = Vector3.one * scale;
            return c;
        }

        static Mesh mineMesh, scorchMesh, flagPole;

        // Mina destapada: bola negra con pinchos y mancha de hollín
        public static Transform Mine(Transform parent)
        {
            if (!mineMesh)
            {
                var b = new MeshBuilder();
                var dark = MathUtil.Hex(0x1a1a1a);
                b.Add(ProcMesh.Icosahedron(0.32f, 0), new Vector3(0, 0.28f, 0), dark);
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    b.Add(ProcMesh.Cone(0.06f, 0.22f, 4), Matrix4x4.TRS(new Vector3(0, 0.28f, 0) + dir * 0.32f, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one), dark);
                }
                mineMesh = b.ToMesh("mina");
                scorchMesh = new MeshBuilder().Add(ProcMesh.Circle(0.9f, 10).RotateX(-Mathf.PI / 2f), Vector3.zero, new Color(0, 0, 0, 0.5f)).ToMesh("hollin");
            }
            var root = new GameObject("Mina").transform;
            root.SetParent(parent, false);
            Obj(root, "Bola", mineMesh, Mats.Lit, Vector3.zero);
            Obj(root, "Hollin", scorchMesh, Mats.Transparent, new Vector3(0, 0.03f, 0));
            return root;
        }

        // Bandera triangular sobre un palo
        public static Transform Flag(Transform parent, int color = 0xe2533b)
        {
            if (!flagPole) flagPole = new MeshBuilder().Add(ProcMesh.Cylinder(0.03f, 0.03f, 0.9f, 4), new Vector3(0, 0.45f, 0), MathUtil.Hex(0x5a3a22)).ToMesh("mastil");
            var root = new GameObject("Bandera").transform;
            root.SetParent(parent, false);
            root.localRotation = Quaternion.Euler(0, 45f, 0);
            Obj(root, "Mastil", flagPole, Mats.Lit, Vector3.zero);
            var tri = new MeshData();
            tri.Add(new Vector3(0, 0.9f, 0), Vector3.forward, Vector2.zero);
            tri.Add(new Vector3(0, 0.55f, 0), Vector3.forward, Vector2.zero);
            tri.Add(new Vector3(0.45f, 0.72f, 0), Vector3.forward, Vector2.zero);
            tri.I.Add(0); tri.I.Add(1); tri.I.Add(2);
            Obj(root, "Tela", new MeshBuilder().Add(tri, Vector3.zero, MathUtil.Hex(color)).ToMesh("tela"), Mats.UnlitDouble, Vector3.zero);
            return root;
        }

        // Losa de color (zonas peligrosas, plataformas del puente)
        public static Transform Slab(Transform parent, int color, float w = 1.9f, float h = 0.25f)
        {
            var m = new MeshBuilder().Add(ProcMesh.Box(w, h, w), Vector3.zero, MathUtil.Hex(color)).ToMesh("losa");
            var go = Obj(parent, "Losa", m, Mats.Lit, new Vector3(0, -h / 2f + 0.01f, 0));
            return go.transform;
        }
    }
}
