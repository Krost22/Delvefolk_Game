using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Tirada de botín: probabilidad y pesos de cada objeto
    public sealed class LootRoll
    {
        public float Chance;
        public KeyValuePair<string, float>[] Items;
        public LootRoll(float chance, params KeyValuePair<string, float>[] items) { Chance = chance; Items = items; }
    }

    public sealed class BreakableType
    {
        public string Id, Name;
        public char Char;
        public int Hp;
        public int[] Coins;
        public float Radius;
        public bool Wall, Block;
        public int Debris;
        public LootRoll[] Loot;
    }

    // MUEBLES ROMPIBLES (Breakables.js): estanterías, armeros, mesas y jarrones. Cuerpo estático
    // que se rompe a golpes, con explosiones o cuando algo choca contra ellos lanzado. Sueltan botín.
    public sealed class Breakable : Entity, IHittable
    {
        static KeyValuePair<string, float> I(string id, float w) { return new KeyValuePair<string, float>(id, w); }

        public static readonly Dictionary<string, BreakableType> Types = new Dictionary<string, BreakableType>
        {
            { "bookshelf", new BreakableType { Id = "bookshelf", Char = 'l', Name = "Estantería", Hp = 4, Coins = new[] { 0, 2 }, Radius = 0.7f, Wall = true, Block = true, Debris = 0x6a4426,
                Loot = new[] { new LootRoll(1f, I("book", 6), I("stick", 2), I("broom", 1)), new LootRoll(0.3f, I("book", 2), I("cheese", 1)) } } },
            { "rack", new BreakableType { Id = "rack", Char = 'r', Name = "Armero viejo", Hp = 3, Coins = new[] { 1, 2 }, Radius = 0.6f, Wall = true, Block = true, Debris = 0x7a5a36,
                Loot = new[] { new LootRoll(1f, I("chairleg", 3), I("stick", 3), I("broom", 2), I("pan", 1), I("bucket", 1)),
                               new LootRoll(0.75f, I("chairleg", 2), I("stick", 2), I("broom", 2), I("pan", 1), I("bucket", 1), I("book", 1)) } } },
            { "table", new BreakableType { Id = "table", Char = 'm', Name = "Mesa", Hp = 3, Coins = new[] { 0, 2 }, Radius = 0.75f, Wall = false, Block = true, Debris = 0x8a6a3a,
                Loot = new[] { new LootRoll(0.8f, I("chairleg", 1)), new LootRoll(0.45f, I("chicken", 1), I("cheese", 2)) } } },
            { "vase", new BreakableType { Id = "vase", Char = 'v', Name = "Jarrón", Hp = 1, Coins = new[] { 2, 5 }, Radius = 0.38f, Wall = false, Block = false, Debris = 0xb8693a,
                Loot = new[] { new LootRoll(0.3f, I("cheese", 2), I("chicken", 1)), new LootRoll(0.2f, I("stick", 2), I("chairleg", 1), I("book", 1)) } } },
        };

        public static readonly Dictionary<char, string> ByChar = new Dictionary<char, string> { { 'l', "bookshelf" }, { 'r', "rack" }, { 'm', "table" }, { 'v', "vase" } };

        public readonly BreakableType Type;
        public readonly string TypeId;
        public readonly int Tx, Tz;
        public int Hp;
        float impactCd, wobble;
        readonly Transform holder;
        readonly DungeonView.Occluder occ;
        readonly float rotY;

        public Breakable(Game g, float x, float z, string typeId, int tx, int tz)
            : base(g, x + WallOffset(g, typeId, tx, tz).x, z + WallOffset(g, typeId, tx, tz).y,
                new Body { Radius = Types[typeId].Radius, IsStatic = true, Kind = BodyKind.Prop }, Types[typeId].Name)
        {
            Type = Types[typeId]; TypeId = typeId;
            Tx = tx; Tz = tz;
            Hp = Type.Hp;
            rotY = WallRotation(g, typeId, tx, tz);
            var rnd = new Rng((uint)(tx * 73 + tz * 151 + g.Floor));
            holder = new GameObject("Mueble").transform;
            holder.SetParent(Model, false);
            Build(holder, rnd);
            Root.rotation = Quaternion.Euler(0, rotY * Mathf.Rad2Deg, 0);
            RegisterFlash(Model.gameObject);
            Body.OnBodyImpact = (o, s, nx, nz) => OnBodyImpact(o, s);
            occ = Type.Wall ? g.View.AddOccluder(Root, X, Z, 0.15f) : null;
        }

        // Pegado a la pared: de espaldas a ella y desplazado 0.55 hacia ella (casillas: fila +1 = sur = −Z)
        static readonly int[] WallDx = { 0, 0, -1, 1 };
        static readonly int[] WallDz = { -1, 1, 0, 0 };

        static int WallIndex(Game g, string typeId, int tx, int tz)
        {
            if (!Types[typeId].Wall) return -1;
            for (int k = 0; k < 4; k++) if (g.Dungeon.IsWallLike(tx + WallDx[k], tz + WallDz[k])) return k;
            return -1;
        }

        static Vector2 WallOffset(Game g, string typeId, int tx, int tz)
        {
            int k = WallIndex(g, typeId, tx, tz);
            return k < 0 ? Vector2.zero : new Vector2(WallDx[k] * 0.55f, -WallDz[k] * 0.55f);
        }

        // Ángulo de la raíz: el frente (+Z local) mira lejos de la pared
        static float WallRotation(Game g, string typeId, int tx, int tz)
        {
            int k = WallIndex(g, typeId, tx, tz);
            if (k < 0) return 0f;
            return Mathf.Atan2(-WallDx[k], WallDz[k]);
        }

        void Build(Transform h, Rng rnd)
        {
            var art = Art.Catalog;
            switch (TypeId)
            {
                case "bookshelf":
                    if (art != null && Art.Pick(art.Bookshelves, rnd) != null) { Fit(Art.Pick(art.Bookshelves, rnd), h, 1.7f, 0f); return; }
                    BuildShelf(h, rnd);
                    return;
                case "table":
                    if (art != null && Art.Pick(art.Tables, rnd) != null) { Fit(Art.Pick(art.Tables, rnd), h, 1.45f, (rnd.Next() - 0.5f) * 0.5f); return; }
                    BuildTable(h, rnd);
                    return;
                case "vase":
                    if (art != null && Art.Pick(art.Vases, rnd) != null) { Fit(Art.Pick(art.Vases, rnd), h, 0.6f, 0f); return; }
                    BuildVase(h, rnd);
                    return;
                default:
                    BuildRack(h);
                    return;
            }
        }

        // Pieza del kit escalada a la anchura del mueble original
        static void Fit(GameObject prefab, Transform h, float width, float rotY)
        {
            var go = Art.Spawn(prefab, h);
            if (!go) return;
            var b = Art.LocalBounds(prefab);
            float s = width / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
            h.localRotation = Quaternion.Euler(0, rotY * Mathf.Rad2Deg, 0);
        }

        static void Mesh(Transform h, string name, MeshBuilder b) { CharacterModel.MeshObject(h, name, b.ToMesh(name), Mats.Lit, Vector3.zero); }

        static void BuildShelf(Transform h, Rng rnd)
        {
            var b = new MeshBuilder();
            var wood = MathUtil.Hex(0x5a3a22);
            const float W = 1.7f, H = 2.3f, D = 0.55f;
            b.Add(ProcMesh.Box(W, H, 0.08f), new Vector3(0, H / 2, -D / 2), wood);
            foreach (var sx in new[] { -1f, 1f }) b.Add(ProcMesh.Box(0.08f, H, D), new Vector3(sx * W / 2, H / 2, 0), wood);
            int[] books = { 0x8a2a2a, 0x2a4a8a, 0x2a6a3a, 0xc9a227, 0x6a3a7a, 0xd8c8a0 };
            for (int s = 0; s < 4; s++)
            {
                float y = 0.1f + s * 0.55f;
                b.Add(ProcMesh.Box(W, 0.06f, D), new Vector3(0, y, 0), wood);
                if (s == 3) continue;
                float x = -W / 2 + 0.1f;
                while (x < W / 2 - 0.15f)
                {
                    float bw = 0.08f + rnd.Next() * 0.08f, bh = 0.32f + rnd.Next() * 0.14f;
                    if (rnd.Next() < 0.12f) { x += bw + 0.05f; continue; }   // hueco
                    var col = MathUtil.Hex(books[(int)(rnd.Next() * books.Length)]);
                    float rz = rnd.Next() < 0.15f ? 0.25f : 0f;
                    b.Add(ProcMesh.Box(bw, bh, 0.36f), Matrix4x4.TRS(new Vector3(x + bw / 2, y + 0.03f + bh / 2, 0.02f), ProcMesh.EulerXYZ(0, 0, rz), Vector3.one), col);
                    x += bw + 0.01f;
                }
            }
            Mesh(h, "estanteria", b);
        }

        static void BuildRack(Transform h)
        {
            var b = new MeshBuilder();
            Color wood = MathUtil.Hex(0x7a5a36), dark = MathUtil.Hex(0x4a3420);
            foreach (var sx in new[] { -1f, 1f }) b.Add(ProcMesh.Box(0.12f, 1.8f, 0.12f), new Vector3(sx * 0.65f, 0.9f, -0.15f), wood);
            foreach (var y in new[] { 0.45f, 1.45f }) b.Add(ProcMesh.Box(1.45f, 0.1f, 0.14f), new Vector3(0, y, -0.12f), dark);
            // Lo que cuelga del armero (pista de lo que lleva dentro)
            b.Add(ProcMesh.Cylinder(0.04f, 0.05f, 1.5f, 5), Matrix4x4.TRS(new Vector3(-0.3f, 0.95f, 0.02f), ProcMesh.EulerXYZ(0, 0, 0.12f), Vector3.one), MathUtil.Hex(0x9a7040));
            b.Add(ProcMesh.Cone(0.2f, 0.35f, 6), Matrix4x4.TRS(new Vector3(0.25f, 0.35f, 0.02f), ProcMesh.EulerXYZ(0, 0, Mathf.PI), Vector3.one), MathUtil.Hex(0xd8b060));
            b.Add(ProcMesh.Cylinder(0.03f, 0.03f, 1.1f, 5), new Vector3(0.25f, 1.0f, 0.02f), wood);
            b.Add(ProcMesh.Cylinder(0.28f, 0.24f, 0.06f, 10), Matrix4x4.TRS(new Vector3(0f, 1.2f, 0.05f), ProcMesh.EulerXYZ(Mathf.PI / 2, 0, 0), Vector3.one), MathUtil.Hex(0x2e3036));
            Mesh(h, "armero", b);
        }

        static void BuildTable(Transform h, Rng rnd)
        {
            var b = new MeshBuilder();
            var wood = MathUtil.Hex(0x8a6a3a);
            b.Add(ProcMesh.Box(1.4f, 0.1f, 0.95f), new Vector3(0, 0.8f, 0), wood);
            foreach (var p in new[] { new Vector2(-0.6f, -0.38f), new Vector2(0.6f, -0.38f), new Vector2(-0.6f, 0.38f), new Vector2(0.6f, 0.38f) })
                b.Add(ProcMesh.Cylinder(0.05f, 0.06f, 0.8f, 5), new Vector3(p.x, 0.4f, p.y), wood);
            b.Add(ProcMesh.Cylinder(0.09f, 0.08f, 0.18f, 7), new Vector3(0.3f, 0.94f, 0.1f), MathUtil.Hex(0x9aa3ad));
            b.Add(ProcMesh.Icosahedron(0.15f), Matrix4x4.TRS(new Vector3(-0.3f, 0.92f, -0.1f), Quaternion.identity, new Vector3(1.4f, 0.7f, 1)), MathUtil.Hex(0xc8904a));
            Mesh(h, "mesa", b);
            h.localRotation = Quaternion.Euler(0, (rnd.Next() - 0.5f) * 0.5f * Mathf.Rad2Deg, 0);
        }

        static void BuildVase(Transform h, Rng rnd)
        {
            var b = new MeshBuilder();
            var clay = Cosmetics.OffsetHsl(MathUtil.Hex(0xb8693a), (rnd.Next() - 0.5f) * 0.08f, 0, 0);
            var pts = new Vector2[9];
            for (int i = 0; i <= 8; i++) { float t = i / 8f; pts[i] = new Vector2(0.12f + Mathf.Sin(t * Mathf.PI) * 0.22f + (t > 0.85f ? 0.06f : 0f), t * 0.85f); }
            b.Add(ProcMesh.Lathe(pts, 9), Vector3.zero, clay);
            b.Add(ProcMesh.Torus(0.33f, 0.025f, 3, 12), Matrix4x4.TRS(new Vector3(0, 0.42f, 0), ProcMesh.EulerXYZ(Mathf.PI / 2, 0, 0), Vector3.one), MathUtil.Hex(0x3a2a1a));
            Mesh(h, "jarron", b);
        }

        public bool HitDead { get { return Dead; } }

        // Golpe de un arma (como los barriles: un golpe fuerte cuenta doble)
        public void TakeHit(Hit hit)
        {
            if (Dead) return;
            DamageBy(hit.Damage >= 20f ? 2 : 1);
        }

        // Explosiones, habilidades y demás daño del entorno
        public void EnvHit(float amount)
        {
            if (Dead) return;
            DamageBy(amount >= 60f ? 99 : amount >= 30f ? 3 : amount >= 15f ? 2 : 1);
        }

        // Algo lanzado choca contra él (barril, enemigo volando, compañero lanzado)
        void OnBodyImpact(Body other, float speed)
        {
            if (Dead || impactCd > 0f || speed < 6f) return;
            impactCd = 0.2f;
            DamageBy(speed > 12f ? 2 : 1);
        }

        void DamageBy(int n)
        {
            Hp -= n;
            Flash(0.12f);
            wobble = 1f;
            G.Audio.Play("thwack");
            G.Effects.Burst(X, 1.0f, Z, BurstOpts.Of(6, Type.Debris, 3f, 3f, 0.12f));
            if (Hp <= 0) BreakApart();
        }

        void BreakApart()
        {
            if (Dead) return;
            Dead = true;
            var d = G.Dungeon;
            G.Audio.Play("barrelBreak");
            G.CameraCtl.AddShake(0.2f);
            G.Effects.Burst(X, 0.8f, Z, BurstOpts.Of(24, Type.Debris, 5f, 6f, 0.2f).WithColorVar(0.12f));
            if (TypeId == "bookshelf") G.Effects.Burst(X, 1.4f, Z, BurstOpts.Of(16, 0xf0e6c8, 3f, 5f, 0.12f).WithGravity(4f).WithLife(1.1f));
            if (TypeId == "vase") G.Ui.FloatText(X, 1.6f, Z, "¡CRASH!", TextStyle.Label);
            if (Type.Coins != null) G.SpawnCoins(X, Z, Type.Coins);
            Destroy();
            if (occ != null) occ.Disabled = true;
            // Ya no estorba: la navegación de los enemigos vuelve a pasar por aquí
            if (Type.Block) d.Unblock(Tx, Tz);
            var drops = RollLoot();
            if (drops.Count == 0 && TypeId != "vase") G.Ui.FloatText(X, 2.0f, Z, "Nada...", TextStyle.Small);
            var spots = DropSpots(drops.Count);
            for (int i = 0; i < drops.Count; i++)
                G.SpawnItemPickup(WeaponRegistry.Create(drops[i]), spots[i].x, spots[i].y, new Vector2(X, Z));
            if (drops.Count > 0) G.Audio.Play("pickup");
            G.OnBreakableBroken(this, drops);
        }

        // Cada tirada sale con su probabilidad (el amuleto de fortuna la mejora un poco)
        List<string> RollLoot()
        {
            bool lucky = false;
            foreach (var p in G.Players) if (p.Gear.Luck() > 0f) lucky = true;
            var outp = new List<string>();
            foreach (var roll in Type.Loot)
            {
                if (G.Rand.Next() >= Mathf.Min(1f, roll.Chance + (lucky ? 0.15f : 0f))) continue;
                var it = G.Rand.Weighted(roll.Items, kv => kv.Value);
                if (it.Key != null) outp.Add(it.Key);
            }
            return outp;
        }

        // Huecos de suelo alrededor (nunca dentro de un muro, un foso o unas púas)
        List<Vector2> DropSpots(int n)
        {
            var d = G.Dungeon;
            var outp = new List<Vector2>();
            // El ángulo del web (rotation.y) apunta igual que nuestro frente: se recalcula con la geometría de Unity
            float ang0 = rotY;
            for (int k = 0; k < 12 && outp.Count < n; k++)
            {
                float a = ang0 + (k % 2 != 0 ? 1 : -1) * Mathf.Ceil(k / 2f) * 0.7f;
                float r = 1.1f + (k > 5 ? 0.6f : 0f);
                float x = X + Mathf.Sin(a) * r, z = Z + Mathf.Cos(a) * r;
                int tx = Grid.TileX(x), tz = Grid.TileZ(z);
                if (d.IsWallLike(tx, tz) || d.IsPitAt(x, z) || (d.InBounds(tx, tz) && d.Spike[d.Idx(tx, tz)] != 0)) continue;
                outp.Add(new Vector2(x, z));
            }
            while (outp.Count < n) outp.Add(new Vector2(X, Z));
            return outp;
        }

        public void Update(float dt)
        {
            if (Dead) return;
            UpdateFlash(dt);
            impactCd = Mathf.Max(0f, impactCd - dt);
            if (wobble > 0f)
            {
                wobble = Mathf.Max(0f, wobble - dt * 4f);
                var e = holder.localEulerAngles;
                holder.localRotation = Quaternion.Euler(0, e.y, Mathf.Sin(wobble * 30f) * 0.05f * wobble * Mathf.Rad2Deg);
            }
        }
    }
}
