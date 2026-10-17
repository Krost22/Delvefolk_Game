using UnityEngine;

namespace Delvefolk
{
    // BARRIL: se puede golpear y lanzar. Si va rápido hace daño a lo que toque. Se rompe (Props.js).
    // El de pólvora prende la mecha al recibir un golpe y explota (reacción en cadena con otros).
    public sealed class Barrel : Entity, IHittable
    {
        public readonly bool Explosive;
        public int Hp;
        public float ImpactCd;
        public bool Falling;
        float fallT;
        public float Spin;
        public float Fuse = -1f;            // < 0: sin mecha encendida
        public Player MagnetBy;             // atraído por el Imán de Chatarra
        public float MagnetT;
        Transform spark;
        static Mesh stripeMesh, fuseMesh, sparkMesh, skullMesh, barrelMesh;

        public Barrel(Game g, float x, float z, bool explosive)
            : base(g, x, z, new Body { Radius = 0.5f, Mass = 2.2f, Kind = BodyKind.Prop, KnockDrag = 2.6f, Restitution = 0.55f }, explosive ? "BarrilPolvora" : "Barril")
        {
            Explosive = explosive;
            Hp = explosive ? 1 : 3;
            Body.PitSolid = () => false;   // los barriles sí caen al foso
            Body.OnBodyImpact = OnBodyImpact;
            Body.OnWallImpact = OnWallImpact;
            Build();
            RegisterFlash(Model.gameObject);
        }

        void Build()
        {
            var art = Art.Catalog;
            if (art != null && Art.Has(art.Barrel))
            {
                var go = Art.Spawn(art.Barrel, Model);
                var b = Art.LocalBounds(art.Barrel);
                float s = 1.0f / Mathf.Max(0.01f, b.size.x);
                go.transform.localScale = new Vector3(s, 1.1f / Mathf.Max(0.01f, b.size.y), s);
                if (Explosive)
                {
                    var mpb = new MaterialPropertyBlock();
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        r.GetPropertyBlock(mpb);
                        var c = MathUtil.Hex(0xa0281e).linear;
                        mpb.SetVector(Mats.RecolorId, new Vector4(c.r, c.g, c.b, 0.8f));
                        r.SetPropertyBlock(mpb);
                    }
                }
            }
            else
            {
                if (!barrelMesh)
                {
                    var pts = new Vector2[7];
                    for (int i = 0; i <= 6; i++) { float t = i / 6f; pts[i] = new Vector2(0.4f + Mathf.Sin(t * Mathf.PI) * 0.1f, t * 1.1f); }
                    var mb = new MeshBuilder();
                    mb.Add(ProcMesh.Lathe(pts, 10), Vector3.zero, Color.white);
                    mb.Add(ProcMesh.Circle(0.41f, 10).RotateX(-Mathf.PI / 2), new Vector3(0, 1.1f, 0), Color.white);
                    foreach (var y in new[] { 0.18f, 0.92f }) mb.Add(ProcMesh.Torus(0.45f, 0.035f, 4, 12).RotateX(Mathf.PI / 2), new Vector3(0, y, 0), MathUtil.Hex(0x3a3c40) * 1.6f);
                    barrelMesh = mb.ToMesh("barril");
                }
                var mat = Explosive ? Mats.Solid(0xa0281e) : Mats.Textured(ProcTex.Wood, MathUtil.Hex(0xd8b48a), true);
                CharacterModel.MeshObject(Model, "Barril", barrelMesh, mat, Vector3.zero);
            }
            if (Explosive)
            {
                if (!stripeMesh)
                {
                    stripeMesh = new MeshBuilder().AddDouble(ProcMesh.Cylinder(0.505f, 0.505f, 0.2f, 10, 1, true), Matrix4x4.identity, MathUtil.Hex(0xe8c547)).ToMesh("franja");
                    fuseMesh = new MeshBuilder().Add(ProcMesh.Cylinder(0.03f, 0.03f, 0.3f, 4), Vector3.zero, MathUtil.Hex(0x111111)).ToMesh("mecha");
                    sparkMesh = new MeshBuilder().Add(ProcMesh.Octahedron(0.08f), Vector3.zero, MathUtil.Hex(0xfff1a0)).ToMesh("chispa");
                    skullMesh = new MeshBuilder().AddDouble(ProcMesh.Circle(0.13f, 6), Matrix4x4.identity, Color.white).ToMesh("calavera");
                }
                CharacterModel.MeshObject(Model, "Franja", stripeMesh, Mats.Lit, new Vector3(0, 0.55f, 0));
                var fuse = CharacterModel.MeshObject(Model, "Mecha", fuseMesh, Mats.Lit, new Vector3(0.15f, 1.25f, 0));
                fuse.transform.localRotation = ProcMesh.EulerXYZ(0, 0, 0.4f);
                spark = CharacterModel.MeshObject(Model, "Chispa", sparkMesh, Mats.Unlit, new Vector3(0.22f, 1.4f, 0)).transform;
                CharacterModel.MeshObject(Model, "Calavera", skullMesh, Mats.Unlit, new Vector3(0, 0.55f, 0.51f));
            }
        }

        public bool HitDead { get { return Dead; } }

        public void TakeHit(Hit hit)
        {
            if (Dead) return;
            Knock(hit.DirX * hit.Knock * 1.15f / Body.Mass, hit.DirZ * hit.Knock * 1.15f / Body.Mass);
            Flash(0.1f);
            Spin = 1f;
            G.Audio.Play("thwack");
            if (Explosive) { Fuse = 0.55f; G.Ui.FloatText(X, 1.8f, Z, "¡¿...?!", TextStyle.Small); return; }
            Hp -= hit.Damage >= 20f ? 2 : 1;
            if (Hp <= 0) BreakApart();
        }

        // Daño del entorno (púas, explosiones)
        public void EnvHit(float amount)
        {
            if (Dead) return;
            if (Explosive) { Fuse = Fuse < 0f ? 0.12f : Mathf.Min(Fuse, 0.12f); return; }
            Hp -= amount >= 20f ? 3 : 1;
            if (Hp <= 0) BreakApart();
        }

        void OnBodyImpact(Body other, float speed, float nx, float nz)
        {
            if (Dead || ImpactCd > 0f) return;
            float mySpeed = KnockSpeed;
            var e = other.Owner as Enemy;
            var p = other.Owner as Player;
            if (e != null && mySpeed > 4.5f)
            {
                // ¡Barrilazo! El barril lanzado arrolla al enemigo
                ImpactCd = 0.12f;
                e.Knock(-nx * mySpeed * 0.6f, -nz * mySpeed * 0.6f);
                e.Stun = Mathf.Max(e.Stun, 0.6f);
                e.EnvDamage(Mathf.Min(30f, mySpeed * 2.2f), "¡BARRILAZO!");
                G.CameraCtl.AddShake(0.2f);
                G.RequestHitstop(0.05f);
                if (Explosive) Explode(); else { Hp -= 1; if (Hp <= 0) BreakApart(); }
            }
            else if (p != null && MagnetBy != null && p != MagnetBy && mySpeed > 5f)
            {
                // Barril atraído por un imán: también arrolla a los compañeros
                ImpactCd = 0.2f;
                p.Hurt(8f, X, Z, 7f);
            }
            else if (speed > 7f && Explosive) Fuse = 0.05f;
            else if (speed > 9f && !Explosive)
            {
                Hp -= 1; ImpactCd = 0.12f;
                if (Hp <= 0) BreakApart();
            }
        }

        void OnWallImpact(float speed, float nx, float nz)
        {
            if (Dead) return;
            if (speed > 4f) G.Audio.Play("thwack");
            if (Explosive && speed > 8f) Fuse = 0.05f;
            else if (speed > 10f) { Hp -= 1; if (Hp <= 0) BreakApart(); }
        }

        public void BreakApart()
        {
            if (Dead) return;
            Dead = true;
            G.Audio.Play("barrelBreak");
            G.Effects.Burst(X, 0.6f, Z, BurstOpts.Of(18, 0x9a6a3a, 5f, 5f, 0.22f).WithColorVar(0.1f));
            G.CameraCtl.AddShake(0.15f);
            // A veces dentro había comida
            if (G.Rand.Next() < G.FoodChance()) G.SpawnFood(X, Z);
            G.SpawnCoins(X, Z, CoinConfig.Barrel);
            Destroy();
        }

        public void Explode()
        {
            if (Dead) return;
            Dead = true;
            Destroy();
            G.Combat.Explosion(X, Z);
        }

        public void Update(float dt)
        {
            if (Dead) return;
            UpdateFlash(dt);
            ImpactCd = Mathf.Max(0f, ImpactCd - dt);
            if (MagnetT > 0f) { MagnetT -= dt; if (MagnetT <= 0f) MagnetBy = null; }
            if (Falling)
            {
                fallT += dt;
                var rp = Root.position; rp.y -= dt * (2f + fallT * 14f); Root.position = rp;
                Model.localRotation = Model.localRotation * Quaternion.Euler(dt * 5f * Mathf.Rad2Deg, 0, 0);
                if (fallT > 1f) { Dead = true; Destroy(); }
                return;
            }
            if (G.Dungeon.IsPitAt(X, Z))
            {
                Falling = true; fallT = 0f; Body.Enabled = false; Shadow.gameObject.SetActive(false);
                G.Audio.Play("fall");
                return;
            }
            if (Fuse >= 0f)
            {
                Fuse -= dt;
                Model.localScale = Vector3.one * (1f + Mathf.Sin(Fuse * 60f) * 0.06f);
                if (Fuse <= 0f) { Explode(); return; }
            }
            if (spark) spark.localScale = Vector3.one * (0.8f + G.FxRand.Next() * 0.6f);
            // Rueda/tambalea cuando se desliza
            float sp = KnockSpeed;
            Spin = Mathf.Max(0f, Spin - dt * 2f);
            Model.localRotation = ProcMesh.EulerXYZ(0, 0, Mathf.Sin(G.Time * 30f) * Mathf.Min(0.25f, sp * 0.03f) + Spin * 0.2f);
            SyncRoot();
        }
    }
}
