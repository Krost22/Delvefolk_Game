using System;
using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Registro de objetos de mano (armas y comida) por id y por letra del mapa (WeaponManager.js).
    // Añadir contenido = añadir una entrada.
    public static class WeaponRegistry
    {
        static readonly Dictionary<string, Func<Weapon>> byId = new Dictionary<string, Func<Weapon>>();
        public static readonly Dictionary<char, string> ByMapChar = new Dictionary<char, string>();

        public static void Register(char code, string id, Func<Weapon> factory)
        {
            byId[id] = factory;
            if (code != '\0') ByMapChar[code] = id;
        }

        public static bool Has(string id) { return id != null && byId.ContainsKey(id); }

        static WeaponRegistry()
        {
            Register('\0', "fists", () => new FistsWeapon());
            Register('B', "broom", () => new BroomWeapon());
            Register('F', "pan", () => new PanWeapon());
            Register('U', "bucket", () => new BucketWeapon());
            Register('K', "stick", () => new StickWeapon());
            Register('T', "book", () => new BookWeapon());
            Register('J', "chairleg", () => new ChairLegWeapon());
            Register('h', "chicken", () => new ChickenFood());
            Register('q', "cheese", () => new CheeseFood());
            RegisterExtra();
        }

        // Armas especiales de la tienda, la escopeta y la comida del Chef (las registran sus fases)
        static void RegisterExtra() { ExtraRegistrations?.Invoke(); }
        public static Action ExtraRegistrations;

        // state: { durability, broken } para conservar el desgaste entre pisos
        public static Weapon Create(string id, ItemState state = null)
        {
            Func<Weapon> f;
            var w = id != null && byId.TryGetValue(id, out f) ? f() : new FistsWeapon();
            if (state != null)
            {
                if (state.broken) w.BreakWeapon();
                else if (!float.IsInfinity(state.durability) && !float.IsNaN(state.durability) && w.HasDurability) w.Durability = state.durability;
                w.LoadState(state);
            }
            return w;
        }

        public static Weapon FromState(ItemState s) { return s != null ? Create(s.id, s) : Create("fists"); }
    }

    // Soltar y coger objetos (una sola cosa en la mano)
    public sealed partial class Game
    {
        // Suelta lo que lleve en la mano (si no son las manos vacías) delante del jugador
        public void DropHeld(Player player)
        {
            var prev = player.Weapon;
            if (prev == null || prev.Id == "fists") return;
            float dx = player.X + Mathf.Sin(player.Facing) * 0.9f, dz = player.Z + Mathf.Cos(player.Facing) * 0.9f;
            if (Dungeon.IsWallLike(Grid.TileX(dx), Grid.TileZ(dz)) || Dungeon.IsPitAt(dx, dz)) { dx = player.X; dz = player.Z; }
            SpawnItemPickup(prev, dx, dz, new Vector2(player.X, player.Z));
            Audio.Play("drop");
            player.SetWeapon(WeaponRegistry.Create("fists"));
        }

        // Coge el objeto del suelo. Lo que llevaba antes vuelve al suelo.
        public void EquipFromPickup(Player player, ItemPickup pickup)
        {
            RemovePickup(pickup);
            DropHeld(player);
            player.SetWeapon(pickup.Item);
            Audio.Play("pickup");
            Ui.FloatText(player.X, 2.6f, player.Z, pickup.Item.Name + "!", TextStyle.Pickup);
        }
    }
}
