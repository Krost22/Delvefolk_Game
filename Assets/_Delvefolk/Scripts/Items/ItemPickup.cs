using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Objeto en el suelo (arma o comida). Guarda la instancia real: el desgaste del arma se conserva.
    public sealed class ItemPickup
    {
        sealed class TossState { public float Fx, Fz, T; }

        public sealed class MagnetState
        {
            public Player By;
            public float Speed, Damage, Knock;
            public readonly HashSet<object> HitSet = new HashSet<object>();
        }

        readonly Game g;
        public readonly Weapon Item;
        public float X, Z;
        float t;
        readonly Transform group, holder, ring;
        readonly MeshRenderer ringR;
        readonly int baseColor;
        int shownColor = -1;
        TossState toss;
        public MagnetState Magnet;
        static Mesh ringMesh;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public ItemPickup(Game game, Weapon item, float x, float z, Vector2? tossFrom = null)
        {
            g = game;
            Item = item;
            X = x; Z = z;
            t = game.FxRand.Next() * 6f;
            group = new GameObject("Objeto_" + item.Id).transform;
            group.SetParent(game.FloorRoot, false);
            group.position = new Vector3(x, 0, z);
            holder = new GameObject("Soporte").transform;
            holder.SetParent(group, false);
            var model = item.BuildDisplayModel(holder);
            // Centrado sobre el anillo
            var b = ModelBounds(model);
            model.transform.localPosition -= b.center;
            baseColor = item.IsFood ? 0x7dff8a : item.Broken ? 0x888888 : 0xffd36a;
            if (!ringMesh) ringMesh = new MeshBuilder().Add(ProcMesh.Ring(0.75f, 0.95f, 24).RotateX(-Mathf.PI / 2f), Vector3.zero, Color.white).ToMesh("anilloObjeto");
            var rg = CharacterModel.MeshObject(group, "Anillo", ringMesh, Mats.Transparent, new Vector3(0, 0.04f, 0));
            ring = rg.transform;
            ringR = rg.GetComponent<MeshRenderer>();
            Entity.BlobShadow(group, 0.7f);
            if (tossFrom.HasValue) toss = new TossState { Fx = tossFrom.Value.x, Fz = tossFrom.Value.y };
            SetRing(baseColor, 0.5f);
        }

        static Bounds ModelBounds(GameObject model)
        {
            var b = new Bounds();
            bool init = false;
            var inv = model.transform.parent.worldToLocalMatrix;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        public string WeaponId { get { return Item.Id; } }
        public string Label { get { return (Item.IsFood ? "Coger " : "Recoger ") + Item.Name; } }
        public bool Tossing { get { return toss != null; } }

        // Sale despedido hasta (x, z) con un pequeño arco. Nunca acaba dentro de un muro ni de un foso.
        public void BlowTo(float x, float z)
        {
            var d = g.Dungeon;
            const int steps = 8;
            float tx = X, tz = Z;
            for (int i = 1; i <= steps; i++)
            {
                float px = Mathf.Lerp(X, x, i / (float)steps), pz = Mathf.Lerp(Z, z, i / (float)steps);
                if (d.IsWallLike(Grid.TileX(px), Grid.TileZ(pz)) || d.IsPitAt(px, pz)) break;
                tx = px; tz = pz;
            }
            toss = new TossState { Fx = X, Fz = Z };
            X = tx; Z = tz;
        }

        // Vuelo hacia quien lo atrae con el imán: golpea a lo que encuentre por el camino (una vez a cada uno)
        void UpdateMagnet(float dt)
        {
            var m = Magnet;
            var by = m.By;
            float dx = by.X - X, dz = by.Z - Z, d = MathUtil.Hypot(dx, dz);
            if (d < 1.1f || by.Dead || by.Removed) { Magnet = null; return; }
            float step = Mathf.Min(d - 1.0f, m.Speed * dt);
            float nx = X + dx / d * step, nz = Z + dz / d * step;
            if (g.Dungeon.IsWallLike(Grid.TileX(nx), Grid.TileZ(nz))) { Magnet = null; return; }   // se queda contra el muro
            X = nx; Z = nz;
            float ux = dx / d, uz = dz / d;
            foreach (var e in g.Enemies)
            {
                if (e.Dead || e.State == EnemyState.Falling || m.HitSet.Contains(e) || MathUtil.Hypot(e.X - X, e.Z - Z) > e.Radius + 0.45f) continue;
                m.HitSet.Add(e);
                e.TakeHit(Hit.Of(m.Damage, m.Knock, 0.3f, ux, uz));
                g.Ui.FloatText(e.X, 2.4f, e.Z, "¡ZAS!", TextStyle.Label);
            }
            foreach (var p in g.Players)
            {
                if (p == by || !p.Targetable || m.HitSet.Contains(p) || MathUtil.Hypot(p.X - X, p.Z - Z) > p.Radius + 0.45f) continue;
                m.HitSet.Add(p);
                if (p.Hurt(Mathf.Round(m.Damage * 0.75f), X, Z, m.Knock))
                    g.Ui.FloatText(p.X, 2.6f, p.Z, "¡Au! ¡Una " + Item.Name.ToLowerInvariant() + "!", TextStyle.Small);
            }
        }

        public void Update(float dt)
        {
            t += dt;
            if (Magnet != null) UpdateMagnet(dt);
            var pos = group.position;
            if (Magnet != null || (toss == null && (pos.x != X || pos.z != Z))) { pos.x = X; pos.z = Z; }
            float y = 0.55f + Mathf.Sin(t * 2.5f) * 0.12f;
            if (toss != null)
            {
                toss.T += dt / 0.35f;
                float k = Mathf.Min(1f, toss.T);
                pos.x = Mathf.Lerp(toss.Fx, X, k);
                pos.z = Mathf.Lerp(toss.Fz, Z, k);
                y += Mathf.Sin(k * Mathf.PI) * 1.2f;
                if (k >= 1f) toss = null;
            }
            group.position = pos;
            holder.localPosition = new Vector3(0, y, 0);
            holder.localRotation = Quaternion.Euler(0, t * 1.2f * Mathf.Rad2Deg, 0) * Quaternion.Euler(0, 0, 0.5f * Mathf.Rad2Deg);
            float p = 0.5f + Mathf.Sin(t * 4f) * 0.5f;
            SetRing(shownColor < 0 ? baseColor : shownColor, 0.25f + p * 0.35f);
        }

        int highlightColor = -1;
        public void SetHighlight(bool on) { highlightColor = on ? 0xffffff : -1; shownColor = on ? 0xffffff : baseColor; }
        public bool Highlighted { get { return highlightColor >= 0; } }

        void SetRing(int color, float opacity)
        {
            var c = MathUtil.Hex(color).linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(c.r, c.g, c.b, opacity));
            ringR.SetPropertyBlock(mpb);
        }

        public void Destroy()
        {
            if (group) Object.Destroy(group.gameObject);
        }
    }
}
