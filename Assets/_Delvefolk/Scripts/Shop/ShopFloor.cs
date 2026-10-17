using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // TIENDA DEL DUENDE TENDERO (Shop.js): aparece justo después de cada piso bonus, antes de seguir bajando.
    // Vende armas especiales que no salen en la mazmorra (y algo de comida), una unidad de cada.
    // Comprar: acercarse a un pedestal y pulsar interactuar con monedas suficientes (RequestBuy lo valida todo).
    // Si alguien golpea al duende, la tienda cierra y empieza el combate contra el jefe. Si se le derrota,
    // suelta su escopeta y lo que quede en la tienda pasa a ser gratis (configurable).
    public static class ShopConfig
    {
        public static bool AfterBonus = true;      // tienda tras cada piso bonus
        public static bool FreeAfterBoss = true;   // derrotado el duende, lo que quede es gratis
        public static readonly KeyValuePair<string, int>[] Items =
        {
            new KeyValuePair<string, int>("sword", 35),
            new KeyValuePair<string, int>("spear", 40),
            new KeyValuePair<string, int>("chicken", 10),
            new KeyValuePair<string, int>("axe", 45),
            new KeyValuePair<string, int>("maul", 55),
        };
    }

    public sealed class ShopSlot
    {
        public int I, Price;
        public string Id, Name, Icon;
        public bool Sold;
        public float X, Z;
        public Transform Ped, Holder;
        public Body Body;
        public WorldLabel Label;
    }

    public sealed class ShopFloor
    {
        public struct LogEntry { public string Type, Id, Why; public int Player, Price; }

        readonly Game G;
        public readonly int Next;
        public bool Open = true;     // false mientras el duende está enfadado
        public bool Free;            // true cuando el duende ha sido derrotado (si FreeAfterBoss)
        public readonly List<LogEntry> Log = new List<LogEntry>();
        public readonly List<ShopSlot> Slots = new List<ShopSlot>();
        public readonly ShopKeeper Keeper;
        readonly Transform group;
        float t;

        public static LevelDef BuildLevel()
        {
            const int W = 15, H = 12;
            var rows = BonusChallenge.Frame(W, H);
            System.Action<int, int, char> put = (x, z, ch) => BonusChallenge.Put(rows, x, z, ch);
            put(4, 0, 't'); put(10, 0, 't'); put(0, 5, 't'); put(W - 1, 5, 't'); put(0, 9, 't');
            put(7, 3, 'm');                          // el mostrador (una mesa rompible)
            put(2, 2, 'l'); put(12, 2, 'l');          // estanterías del almacén
            put(1, 10, 'v'); put(13, 10, 'v'); put(1, 1, 'v');
            put(2, 9, 'b'); put(12, 1, 'e');
            put(3, 8, '^'); put(11, 8, '^');
            put(13, 7, 'X');                          // escaleras para seguir bajando
            put(7, 9, 'P'); put(7, H - 1, 'N');
            return new LevelDef
            {
                Name = "La Tienda — del Duende Tendero", Fog = 0x0a0806, Map = rows,
                Rooms = new[] { new RoomDef("T", "La Tienda del Duende", new[] { 1, 1, W - 2, H - 2 },
                    "Un duende vende armas de verdad. Monedas en mano y... mejor no le toques las narices.") },
            };
        }

        static Mesh pedMesh;

        public ShopFloor(Game g, int nextFloor)
        {
            G = g;
            Next = nextFloor;
            group = new GameObject("Tienda").transform;
            group.SetParent(g.FloorRoot, false);
            int[][] spots = { new[] { 3, 5 }, new[] { 5, 6 }, new[] { 7, 6 }, new[] { 9, 6 }, new[] { 11, 5 } };
            if (!pedMesh)
            {
                var b = new MeshBuilder();
                b.Add(ProcMesh.Cylinder(0.42f, 0.5f, 0.7f, 8), new Vector3(0, 0.35f, 0), MathUtil.Hex(0x6e665d));
                b.Add(ProcMesh.Cylinder(0.5f, 0.45f, 0.08f, 8), new Vector3(0, 0.74f, 0), MathUtil.Hex(0xc9a227));
                pedMesh = b.ToMesh("pedestal");
            }
            for (int i = 0; i < ShopConfig.Items.Length; i++)
            {
                var it = ShopConfig.Items[i];
                var sp = spots[i % spots.Length];
                var s = new ShopSlot { I = i, Id = it.Key, Price = it.Value, X = Grid.X(sp[0]), Z = Grid.Z(sp[1]) };
                var proto = WeaponRegistry.Create(it.Key);
                s.Name = proto.Name; s.Icon = proto.Icon;
                // Pedestal de piedra con el objeto flotando encima y su precio
                s.Ped = new GameObject("Pedestal_" + s.Id).transform;
                s.Ped.SetParent(group, false);
                s.Ped.position = new Vector3(s.X, 0, s.Z);
                CharacterModel.MeshObject(s.Ped, "Piedra", pedMesh, Mats.Lit, Vector3.zero);
                Entity.BlobShadow(s.Ped, 0.55f);
                s.Holder = new GameObject("Objeto").transform;
                s.Holder.SetParent(s.Ped, false);
                s.Holder.localPosition = new Vector3(0, 1.35f, 0);
                var item = proto.BuildDisplayModel(s.Holder);
                if (item)
                {
                    // Centrado sobre el pedestal
                    var rs = item.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        var bb = rs[0].bounds;
                        foreach (var r in rs) bb.Encapsulate(r.bounds);
                        item.transform.position -= bb.center - s.Holder.position;
                    }
                }
                s.Body = g.Physics.Add(new Body { X = s.X, Z = s.Z, Radius = 0.45f, IsStatic = true, Kind = BodyKind.Static, Owner = s });
                Slots.Add(s);
                SetLabel(s);
            }
            Keeper = new ShopKeeper(g, Grid.X(7), Grid.Z(2), this);
            g.Enemies.Add(Keeper);
        }

        public int PriceOf(ShopSlot s) { return Free ? 0 : s.Price; }

        public static string LabelText(ShopSlot s, bool open, bool free)
        {
            return s.Sold ? "VENDIDO" : !open ? "🔒 Cerrado" : free ? "¡GRATIS!" : "🪙 " + s.Price;
        }

        void SetLabel(ShopSlot s)
        {
            if (s.Label != null) s.Label.Remove();
            s.Label = G.Ui.Label(LabelText(s, Open, Free), new Vector3(s.X, 2.25f, s.Z), s.Sold ? 0x8a7a62 : Free ? 0x7dff8a : 0xffd36a);
        }

        public ShopSlot Nearest(Player p)
        {
            ShopSlot best = null; float bd = 1.7f;
            foreach (var s in Slots)
            {
                if (s.Sold) continue;
                float d = MathUtil.Hypot(s.X - p.X, s.Z - p.Z);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        // Aviso y botón de interactuar junto a un pedestal (lo llama la interacción del juego)
        public bool HandleInteraction(Player p)
        {
            if (p.Carrying != null) return false;
            var s = Nearest(p);
            if (s == null) return false;
            if (!Open)
            {
                G.Ui.SetPrompt(p.Index, "🔒 ¡Tienda cerrada! (el duende está furioso)");
                p.Controls.ConsumeInteract();
                return true;
            }
            int price = PriceOf(s), gold = p.Gear.Gold;
            G.Ui.SetPrompt(p.Index, gold >= price
                ? "{E} " + (price > 0 ? "Comprar " : "Coger ") + s.Name + (price > 0 ? " · " + price + " 🪙" : " gratis")
                : s.Name + " · " + price + " 🪙 (te faltan " + (price - gold) + ")");
            if (p.Controls.ConsumeInteract()) RequestBuy(p, s);
            return true;
        }

        // Compra validada: una sola unidad por objeto, tienda abierta y monedas suficientes
        public bool RequestBuy(Player p, ShopSlot s)
        {
            int price = PriceOf(s);
            string why = null;
            if (s.Sold) why = "agotado";
            else if (!Open) why = "cerrada";
            else if (p.Dead || p.CarriedBy != null || p.GrabbedBy != null || p.Spectator) why = "no puede";
            else if (p.Gear.Gold < price) why = "sin monedas";
            if (why != null)
            {
                Log.Add(new LogEntry { Type = "buy-rejected", Player = p.Index, Id = s.Id, Why = why });
                if (why == "sin monedas") { G.Audio.Play("denied"); G.Ui.FloatText(p.X, 2.6f, p.Z, "Te faltan " + (price - p.Gear.Gold) + " 🪙", TextStyle.Small); }
                return false;
            }
            p.Gear.Gold -= price;
            s.Sold = true;
            Log.Add(new LogEntry { Type = "buy", Player = p.Index, Id = s.Id, Price = price });
            s.Holder.gameObject.SetActive(false);
            SetLabel(s);
            G.DropHeld(p);
            p.SetWeapon(WeaponRegistry.Create(s.Id));
            G.Ui.SetGear(p.Index, p.Gear);
            G.Audio.Play("pickup");
            if (price > 0) G.Audio.Play("coin");
            G.Effects.Burst(s.X, 1.3f, s.Z, BurstOpts.Of(16, 0xffd36a, 3f, 4f, 0.1f));
            G.Ui.FloatText(p.X, 2.8f, p.Z, s.Icon + " " + s.Name + "!", TextStyle.Pickup);
            if (Keeper != null && Keeper.Peaceful) Keeper.Say(price > 0 ? "¡Gracias por tu compra!" : "...");
            return true;
        }

        public void OnKeeperAngry()
        {
            Open = false;
            foreach (var s in Slots) if (!s.Sold) SetLabel(s);
            G.Ui.Toast("¡El Duende Tendero se ha enfadado! La tienda está cerrada... hasta que le derrotes.", 5f);
        }

        public void OnKeeperDefeated(ShopKeeper k)
        {
            Open = true;
            Free = ShopConfig.FreeAfterBoss;
            foreach (var s in Slots) if (!s.Sold) SetLabel(s);
            G.SpawnItemPickup(WeaponRegistry.Create("shotgun"), k.X, k.Z, new Vector2(k.X, k.Z));
            G.Audio.Play("win");
            G.Ui.Banner("¡DUENDE DERROTADO!", "Su escopeta es tuya" + (Free ? " · y la tienda, gratis" : ""));
            G.Ui.Toast("🔫 La Escopeta del Duende no se rompe. Cada enemigo derrotado con ella en la mano te da una bala.", 6f);
        }

        public void Update(float dt)
        {
            t += dt;
            foreach (var s in Slots)
            {
                if (s.Sold) continue;
                s.Holder.localRotation = Quaternion.Euler(0, -t * 1.2f * Mathf.Rad2Deg, 0);
                s.Holder.localPosition = new Vector3(0, 1.35f + Mathf.Sin(t * 2.2f + s.I) * 0.08f, 0);
            }
        }
    }
}
