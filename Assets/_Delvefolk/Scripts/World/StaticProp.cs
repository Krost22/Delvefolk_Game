using UnityEngine;

namespace Delvefolk
{
    // Obstáculos fijos: columnas (o) y montones de cajas (c), con colisión circular estática
    public sealed class StaticProp
    {
        public readonly Body Body;
        public readonly Transform Tr;
        public readonly bool Pillar;

        public StaticProp(Game g, float x, float z, bool pillar)
        {
            Pillar = pillar;
            const float H = Cfg.WallH;
            Tr = new GameObject(pillar ? "Columna" : "Cajas").transform;
            Tr.SetParent(g.FloorRoot, false);
            Tr.position = new Vector3(x, 0, z);
            var art = Art.Catalog;
            float radius;
            if (pillar)
            {
                radius = 0.62f;
                if (art != null && Art.Has(art.Pillar))
                {
                    var b = Art.LocalBounds(art.Pillar);
                    float s = 1.25f / Mathf.Max(0.01f, b.size.x);
                    Spawn(art.Pillar, Vector3.zero, Quaternion.Euler(0, 90f * (int)(g.FxRand.Next() * 4), 0), new Vector3(s, H / Mathf.Max(0.01f, b.size.y), s));
                }
                else
                {
                    var mb = new MeshBuilder();
                    mb.Add(ProcMesh.Box(1.4f, 0.35f, 1.4f), new Vector3(0, 0.17f, 0), MathUtil.Hex(0x45403a));
                    mb.Add(ProcMesh.Cylinder(0.5f, 0.56f, H, 8), new Vector3(0, H / 2f, 0), MathUtil.Hex(0xb9b0a4));
                    mb.Add(ProcMesh.Box(1.3f, 0.3f, 1.3f), new Vector3(0, H - 0.05f, 0), MathUtil.Hex(0x45403a));
                    if (g.FxRand.Next() < 0.5f) mb.Add(ProcMesh.Box(0.05f, 0.9f, 0.02f), Matrix4x4.TRS(new Vector3(0.1f, 1.4f, 0.53f), ProcMesh.EulerXYZ(0, 0, 0.3f), Vector3.one), MathUtil.Hex(0x111111));
                    CharacterModel.MeshObject(Tr, "Piedra", mb.ToMesh("columna"), Mats.Lit, Vector3.zero);
                }
                g.View.AddOccluder(Tr, x, z, 0.12f);
            }
            else
            {
                radius = 0.72f;
                if (art != null && art.Crates != null && art.Crates.Length > 0 && Art.Has(art.Crates[0]))
                {
                    var b = Art.LocalBounds(art.Crates[0]);
                    float unit = 1f / Mathf.Max(0.01f, b.size.x);
                    // Igual que el original: una grande, una encima y una al lado (las cajas del kit tienen el pivote en el centro)
                    SpawnCrate(art, g, unit, 1.1f, new Vector3(-0.15f, 0.55f, 0.1f), 0.3f);
                    SpawnCrate(art, g, unit, 0.7f, new Vector3(0.15f, 1.45f, 0f), -0.4f);
                    SpawnCrate(art, g, unit, 0.6f, new Vector3(0.45f, 0.3f, -0.35f), 0.8f);
                }
                else
                {
                    var mb = new MeshBuilder();
                    var wood = MathUtil.Hex(0xc8a070);
                    mb.Add(ProcMesh.Box(1.1f, 1.1f, 1.1f), Matrix4x4.TRS(new Vector3(-0.15f, 0.55f, 0.1f), Quaternion.Euler(0, 0.3f * Mathf.Rad2Deg, 0), Vector3.one), wood);
                    mb.Add(ProcMesh.Box(0.7f, 0.7f, 0.7f), Matrix4x4.TRS(new Vector3(0.15f, 1.45f, 0f), Quaternion.Euler(0, -0.4f * Mathf.Rad2Deg, 0), Vector3.one), wood);
                    mb.Add(ProcMesh.Box(0.6f, 0.6f, 0.6f), Matrix4x4.TRS(new Vector3(0.45f, 0.3f, -0.35f), Quaternion.Euler(0, 0.8f * Mathf.Rad2Deg, 0), Vector3.one), wood);
                    CharacterModel.MeshObject(Tr, "Madera", mb.ToMesh("cajas"), Mats.Textured(ProcTex.Wood, Color.white, true), Vector3.zero);
                }
            }
            Body = g.Physics.Add(new Body { X = x, Z = z, Radius = radius, IsStatic = true, Kind = BodyKind.Static, Owner = this });
        }

        void Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = Art.Spawn(prefab, Tr);
            if (!go) return;
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
        }

        void SpawnCrate(DelvefolkArt art, Game g, float unit, float size, Vector3 pos, float ry)
        {
            var p = Art.Pick(art.Crates, g.FxRand);
            Spawn(p, pos, Quaternion.Euler(0, ry * Mathf.Rad2Deg, 0), Vector3.one * unit * size);
        }
    }
}
