using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public static class CoinConfig
    {
        public const float MagnetRange = 1.7f;    // a esta distancia vuelan hacia el jugador
        public const float CollectRange = 0.55f;
        public const float Life = 40f;            // segundos en el suelo antes de desaparecer (parpadean los últimos 5)
        public static readonly int[] EnemyDefault = { 1, 2 };
        public static readonly int[] Barrel = { 0, 2 };
        public const float LuckBonus = 0.5f;      // el amuleto de fortuna da un 50 % más
    }

    // MONEDAS: saltan al matar enemigos y al romper muebles y barriles. Se recogen al pasar cerca
    // (vuelan hacia el jugador) y se suman a su bolsa (Gear.Gold), que viaja con él entre pisos.
    // No son cuerpos físicos: solo un arco de salida que respeta muros. Los objetos visuales se reutilizan.
    public sealed class Coin
    {
        static Mesh mesh;
        static Material mat;
        static readonly Stack<GameObject> pool = new Stack<GameObject>();

        readonly Game g;
        public float X, Z, Y = 0.3f, Vx, Vz, Vy, T;
        public bool Collected;
        public Player Target;
        GameObject go;
        Transform tr;
        float spin;

        public Coin(Game game, float x, float z, float vx, float vz)
        {
            g = game;
            X = x; Z = z; Vx = vx; Vz = vz;
            Vy = 4f + game.Rand.Next() * 2.5f;
            if (!mesh)
            {
                mesh = new MeshBuilder().Add(ProcMesh.Cylinder(0.17f, 0.17f, 0.05f, 10), Vector3.zero, Color.white).ToMesh("moneda");
                mat = Mats.Make("df_moneda", false, false, false, false, false);
                mat.SetColor(Mats.BaseColorId, MathUtil.Hex(0xf2c230));
                mat.SetColor(Mats.EmissionId, MathUtil.Hex(0x4a3200));
            }
            go = pool.Count > 0 ? pool.Pop() : null;
            if (!go) go = CharacterModel.MeshObject(null, "Moneda", mesh, mat, Vector3.zero);
            tr = go.transform;
            tr.SetParent(game.FloorRoot, false);
            go.SetActive(true);
            tr.position = new Vector3(x, Y, z);
        }

        public void Update(float dt)
        {
            var d = g.Dungeon;
            T += dt;
            if (Target != null)
            {
                // Volando hacia quien la recoge
                var p = Target;
                float dx = p.X - X, dz = p.Z - Z, dist = MathUtil.Hypot(dx, dz);
                float sp = Mathf.Min(dist, (8f + T * 6f) * dt);
                if (dist > 1e-3f) { X += dx / dist * sp; Z += dz / dist * sp; }
                Y = MathUtil.Damp(Y, 1.0f + p.JumpY, 12f, dt);
                if (dist < CoinConfig.CollectRange || p.Dead || p.Removed) { Collect(p); return; }
            }
            else
            {
                // Arco de salida: rebota en el suelo, se para en los muros
                if (Vy != 0f || Y > 0.12f)
                {
                    float nx = X + Vx * dt, nz = Z + Vz * dt;
                    if (d.IsWallLike(Grid.TileX(nx), Grid.TileZ(nz)) || d.IsPitAt(nx, nz)) { Vx *= -0.3f; Vz *= -0.3f; }
                    else { X = nx; Z = nz; }
                    Vy -= 22f * dt;
                    Y += Vy * dt;
                    if (Y <= 0.12f)
                    {
                        Y = 0.12f;
                        if (Mathf.Abs(Vy) > 2f) { Vy = -Vy * 0.35f; Vx *= 0.5f; Vz *= 0.5f; }
                        else { Vy = 0f; Vx = 0f; Vz = 0f; }
                    }
                }
                // ¿Hay alguien cerca? (no recogen los fuera de juego)
                if (T > 0.35f)
                {
                    Player best = null;
                    float bd = CoinConfig.MagnetRange;
                    foreach (var p in g.Players)
                    {
                        if (p.Dead || p.Spectator || p.CarriedBy != null || p.GrabbedBy != null) continue;
                        float dist = MathUtil.Hypot(p.X - X, p.Z - Z);
                        if (dist < bd) { bd = dist; best = p; }
                    }
                    if (best != null) Target = best;
                }
                if (T > CoinConfig.Life) { Remove(); return; }
            }
            float bob = Vy == 0f && Target == null ? Mathf.Sin(T * 4f) * 0.05f + 0.1f : 0f;
            tr.position = new Vector3(X, Y + bob, Z);
            spin += dt * 5f;
            tr.rotation = Quaternion.Euler(0, spin * Mathf.Rad2Deg, 0) * Quaternion.Euler(0, 0, 90f);
            bool vis = T < CoinConfig.Life - 5f || Mathf.FloorToInt(T * 8f) % 2 == 0;
            if (go.activeSelf != vis) go.SetActive(vis);
        }

        public void Collect(Player p)
        {
            if (Collected) return;
            Collected = true;
            p.Gear.Gold += 1;
            g.Stats.Coins++;
            g.Ui.SetGold(p.Index, p.Gear.Gold);
            g.Audio.Play("coin");
            g.Effects.Burst(X, 1.1f, Z, BurstOpts.Of(3, 0xffe08a, 1.5f, 2f, 0.06f));
            Remove();
        }

        public void Remove()
        {
            Collected = true;
            if (go)
            {
                go.SetActive(false);
                tr.SetParent(null, false);
                pool.Push(go);
                go = null;
            }
        }
    }
}
