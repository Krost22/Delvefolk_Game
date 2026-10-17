using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Base común para jugador, enemigos y objetos físicos: cuerpo, modelo, sombra y destello al recibir daño.
    // Raíz → modelo (squash, giros) → piezas; la sombra "blob" cuelga de la raíz.
    public abstract class Entity
    {
        public readonly Game G;
        public readonly Transform Root;
        public readonly Transform Model;
        public readonly Transform Shadow;
        public readonly Body Body;
        public bool Dead, Removed;

        readonly List<Renderer> flashRenderers = new List<Renderer>();
        float flashT, flashDur;
        Color flashColor;
        static Mesh shadowMesh;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        protected Entity(Game g, float x, float z, Body body, string name)
        {
            G = g;
            Root = new GameObject(name).transform;
            Root.SetParent(g.FloorRoot, false);
            Root.position = new Vector3(x, 0f, z);
            Model = new GameObject("Modelo").transform;
            Model.SetParent(Root, false);
            body.X = x; body.Z = z; body.Owner = this;
            Body = g.Physics.Add(body);
            Shadow = BlobShadow(Root, body.Radius * 1.15f);
        }

        public float X { get { return Body.X; } }
        public float Z { get { return Body.Z; } }
        public float Radius { get { return Body.Radius; } }
        public float KnockSpeed { get { return Body.KnockSpeed; } }

        // Sombra circular barata bajo las entidades (negro al 38 %)
        public static Transform BlobShadow(Transform parent, float radius)
        {
            if (!shadowMesh)
            {
                var md = ProcMesh.Circle(1f, 20).RotateX(-Mathf.PI / 2f);
                shadowMesh = new MeshBuilder().Add(md, Vector3.zero, new Color(0, 0, 0, 0.38f)).ToMesh("sombra");
            }
            var go = CharacterModel.MeshObject(parent, "Sombra", shadowMesh, Mats.Transparent, new Vector3(0, 0.02f, 0));
            go.transform.localScale = new Vector3(radius, 1f, radius);
            return go.transform;
        }

        // Los renderizadores del modelo que se iluminan al recibir un golpe
        public void RegisterFlash(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r.transform != Shadow && !(r is ParticleSystemRenderer)) flashRenderers.Add(r);
        }

        public void RegisterFlash(IEnumerable<Renderer> rs) { flashRenderers.AddRange(rs); }

        public void Flash(float t = 0.12f, int color = 0xffffff)
        {
            flashT = t; flashDur = t;
            flashColor = MathUtil.Hex(color).linear;
            ApplyFlash(1f);
        }

        public bool Flashing { get { return flashT > 0f; } }

        // Brillo fijo (aviso de ataque): mismo canal que el destello, sin temporizador
        public void SetFlashColor(Color c)
        {
            var lin = c.linear;
            var v = new Vector4(lin.r, lin.g, lin.b, 0f);
            for (int i = 0; i < flashRenderers.Count; i++)
            {
                var r = flashRenderers[i];
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetVector(Mats.FlashId, v);
                r.SetPropertyBlock(mpb);
            }
        }

        public void UpdateFlash(float dt)
        {
            if (flashT <= 0f) return;
            flashT -= dt;
            float k = Mathf.Max(0f, flashT / flashDur);
            ApplyFlash(flashT <= 0f ? 0f : k);
        }

        void ApplyFlash(float k)
        {
            var c = new Vector4(flashColor.r * k, flashColor.g * k, flashColor.b * k, 0f);
            for (int i = 0; i < flashRenderers.Count; i++)
            {
                var r = flashRenderers[i];
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetVector(Mats.FlashId, c);
                r.SetPropertyBlock(mpb);
            }
        }

        public void SyncRoot()
        {
            var p = Root.position;
            p.x = Body.X; p.z = Body.Z;
            Root.position = p;
        }

        public void Knock(float x, float z) { Body.Kx += x; Body.Kz += z; }

        public virtual void Destroy()
        {
            if (Removed) return;
            Removed = true;
            G.Physics.Remove(Body);
            if (Root) Object.Destroy(Root.gameObject);
        }
    }
}
