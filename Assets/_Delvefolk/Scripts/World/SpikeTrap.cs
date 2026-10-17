using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public enum TrapState { Idle, Armed, Up, Cooldown }

    // PÚAS (Trap.js): se activan cuando algo las pisa. Dañan a enemigos, jugador y barriles.
    // Algo que llega volando las dispara al instante; si se pisa caminando hay un "clic" de aviso.
    public sealed class SpikeTrap
    {
        readonly Game g;
        public readonly float X, Z;
        public TrapState State = TrapState.Idle;
        public float T;
        float armTime;
        readonly HashSet<object> hitSet = new HashSet<object>();
        readonly List<Body> occBuf = new List<Body>();
        readonly Transform group, spikes;
        readonly Renderer plate;
        readonly float spikeUnit;   // altura de las púas en unidades locales (−0.8 → abajo, 0 → arriba)
        static Mesh plateMesh, holesMesh, spikesMesh;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public SpikeTrap(Game game, float x, float z)
        {
            g = game;
            X = x; Z = z;
            group = new GameObject("Puas").transform;
            group.SetParent(game.FloorRoot, false);
            group.position = new Vector3(x, 0, z);
            var art = Art.Catalog;
            const float T = Cfg.Tile;
            if (art != null && Art.Has(art.Spikes))
            {
                // Placa del kit (con su hijo de pinchos), escalada a la casilla
                var go = Art.Spawn(art.Spikes, group);
                var b = Art.LocalBounds(art.Spikes);
                float s = T * 0.9f / Mathf.Max(0.01f, b.size.x);
                go.transform.localScale = Vector3.one * s;
                plate = go.GetComponent<Renderer>();
                spikes = go.transform.childCount > 0 ? go.transform.GetChild(0) : null;
                spikeUnit = 0.4f;
            }
            if (spikes == null)
            {
                if (group.childCount > 0) Object.Destroy(group.GetChild(0).gameObject);
                if (!plateMesh)
                {
                    plateMesh = new MeshBuilder().Add(ProcMesh.Box(T * 0.94f, 0.06f, T * 0.94f), new Vector3(0, 0.01f, 0), MathUtil.Hex(0x4a4440)).ToMesh("placa");
                    var hb = new MeshBuilder();
                    var sb = new MeshBuilder();
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                        {
                            float px = (i - 1) * 0.58f, pz = (j - 1) * 0.58f;
                            hb.Add(ProcMesh.Circle(0.11f, 6).RotateX(-Mathf.PI / 2), new Vector3(px, 0.045f, pz), MathUtil.Hex(0x0d0b0a));
                            sb.Add(ProcMesh.Cone(0.12f, 0.75f, 4), new Vector3(px, 0.37f, pz), MathUtil.Hex(0xb8bcc4));
                        }
                    holesMesh = hb.ToMesh("agujeros");
                    spikesMesh = sb.ToMesh("pinchos");
                }
                plate = CharacterModel.MeshObject(group, "Placa", plateMesh, Mats.Lit, Vector3.zero).GetComponent<Renderer>();
                CharacterModel.MeshObject(group, "Agujeros", holesMesh, Mats.Unlit, Vector3.zero);
                spikes = CharacterModel.MeshObject(group, "Pinchos", spikesMesh, Mats.Lit, Vector3.zero).transform;
                spikeUnit = 0.8f;
            }
            SetSpikes(-1f);
        }

        // k: −1 abajo, 0 arriba (en unidades del original: −0.8 → 0)
        Vector3 spikeBase = new Vector3(float.NaN, 0, 0);
        void SetSpikes(float k)
        {
            if (float.IsNaN(spikeBase.x)) spikeBase = spikes.localPosition;
            spikes.localPosition = spikeBase + new Vector3(0, k * spikeUnit, 0);
        }

        // Cuerpos que están sobre la casilla
        List<Body> Occupants()
        {
            const float half = Cfg.Tile / 2f;
            occBuf.Clear();
            foreach (var b in g.Physics.Bodies)
            {
                if (!b.Enabled || b.IsStatic || b.Owner == null) continue;
                var pl = b.Owner as Player;
                if (pl != null && pl.JumpY > 0.4f) continue;   // saltando por encima de las púas
                // Cuenta si al menos un tercio del cuerpo está sobre la placa
                float lim = half + b.Radius * 0.3f;
                if (Mathf.Abs(b.X - X) < lim && Mathf.Abs(b.Z - Z) < lim) occBuf.Add(b);
            }
            return occBuf;
        }

        public void Update(float dt)
        {
            T += dt;
            var occ = Occupants();
            if (State == TrapState.Idle)
            {
                if (occ.Count > 0)
                {
                    bool fast = false;
                    foreach (var b in occ) if (b.KnockSpeed > 4f) fast = true;
                    State = TrapState.Armed;
                    T = 0f;
                    armTime = fast ? 0.02f : 0.32f;
                    if (!fast) g.Audio.Play("spikeArm");
                }
            }
            else if (State == TrapState.Armed)
            {
                SetSpikes(-1f + (Mathf.Sin(T * 80f) * 0.03f + 0.12f * (T / armTime)) / 0.8f);
                if (T >= armTime) Fire();
            }
            else if (State == TrapState.Up)
            {
                float k = Mathf.Min(1f, T / 0.06f);
                SetSpikes(Mathf.Lerp(-1f, 0f, k));
                foreach (var b in occ) Hurt(b);
                SetPlateGlow(Mathf.Max(0f, 0.6f - T * 1.5f));
                if (T > 0.75f) { State = TrapState.Cooldown; T = 0f; }
            }
            else if (State == TrapState.Cooldown)
            {
                SetSpikes(Mathf.Lerp(0f, -1f, Mathf.Min(1f, T / 0.35f)));
                SetPlateGlow(0f);
                if (T > 0.6f) { State = TrapState.Idle; T = 0f; }
            }
        }

        void SetPlateGlow(float r)
        {
            if (!plate) return;
            plate.GetPropertyBlock(mpb);
            mpb.SetVector(Mats.FlashId, new Vector4(r * r, 0, 0, 0));
            plate.SetPropertyBlock(mpb);
        }

        void Fire()
        {
            State = TrapState.Up; T = 0f;
            hitSet.Clear();
            g.Audio.Play("spikes");
            g.Effects.Burst(X, 0.3f, Z, BurstOpts.Of(10, 0xd8dde6, 3f, 7f, 0.1f));
            g.Effects.Ring(X, Z, new RingOpts { Color = 0xff5040, Radius = 1.5f, Life = 0.25f, Y = 0.06f, R0 = 0.3f, Opacity = 0.8f });
        }

        void Hurt(Body b)
        {
            var o = b.Owner;
            if (hitSet.Contains(o)) return;
            hitSet.Add(o);
            // Rebote: lanza hacia fuera de la trampa (y un poco más allá en la dirección en la que venía)
            float dx = b.X - X, dz = b.Z - Z;
            float l = MathUtil.Hypot(dx, dz);
            if (l < 0.2f) { float a = g.Rand.Next() * 6.28f; dx = Mathf.Cos(a); dz = Mathf.Sin(a); } else { dx /= l; dz /= l; }
            var e = o as Enemy;
            var p = o as Player;
            var br = o as Barrel;
            if (e != null)
            {
                if (e.Dead) return;
                b.Kx = b.Kx * 0.4f + dx * 7f; b.Kz = b.Kz * 0.4f + dz * 7f;
                e.Stun = Mathf.Max(e.Stun, 0.5f);
                e.EnvDamage(24f, "¡PÚAS!");
                g.Effects.Burst(b.X, 0.7f, b.Z, BurstOpts.Of(12, e.Cfg.Color, 4f, 6f, 0.14f));
                g.Effects.Flash(X, 1f, Z, 0xff4030, 3f, 0.15f);
                g.RequestHitstop(0.06f);
                g.CameraCtl.AddShake(0.22f);
            }
            else if (p != null)
            {
                if (p.Hurt(15f, X, Z, 8f, new HurtOpts { Kind = "trap" })) g.Ui.FloatText(p.X, 2.6f, p.Z, "¡Ay, púas!", TextStyle.Small);
            }
            else if (br != null) br.EnvHit(30f);
        }
    }
}
