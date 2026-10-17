using System;
using System.Collections.Generic;

namespace Delvefolk
{
    // Configuración del equipo de juego (DF.GEAR_CONFIG)
    public static class GearConfig
    {
        public const float BuffDuration = 150f;     // segundos de juego que dura una mejora temporal
        public const int MaxBuffStacks = 2;         // pilas máximas de una misma mejora
        public const int MaxActiveBuffs = 3;        // mejoras distintas a la vez (la que menos le queda se sustituye)
        public const int MaxItems = 4;              // objetos especiales distintos
        public const int MaxShieldCharges = 2;
        public const float LuckFoodBonus = 0.15f;   // amuleto: +15 % de probabilidad de comida en barriles
        public const float CompassAggroRange = 4f;  // brújula: los enemigos la prefieren aunque esté hasta 4 u más lejos
    }

    // Una recompensa de los pisos bonus (DF.Rewards): armadura, objeto, mejora temporal o al momento
    public sealed class RewardDef
    {
        public string Id, Kind, Category, Name, Icon, Text;
        public float Weight;
        public bool Rare, Curse, Brittle;
        // Armaduras
        public float Reduction, TrapResist, StunResist;
        public int Durability, Color;
        // Mejoras (valor por pila) e instantáneas
        public float Per, Heal, Gold, Hurt;
    }

    public static class Rewards
    {
        public static readonly Dictionary<string, RewardDef> Defs = new Dictionary<string, RewardDef>();
        public static readonly List<RewardDef> All = new List<RewardDef>();   // orden de registro (sorteos)

        public static RewardDef Get(string id) { RewardDef d; return id != null && Defs.TryGetValue(id, out d) ? d : null; }

        static void Add(RewardDef d) { Defs[d.Id] = d; All.Add(d); }

        // Lista para sortear: filter decide qué entra; luck sube el peso de lo raro
        public static List<KeyValuePair<string, float>> Pool(Func<RewardDef, bool> filter, float luck = 0f)
        {
            var l = new List<KeyValuePair<string, float>>();
            foreach (var d in All)
            {
                if (d.Weight <= 0f || (filter != null && !filter(d))) continue;
                l.Add(new KeyValuePair<string, float>(d.Id, d.Weight * (d.Rare ? 1f + luck * 0.6f : 1f)));
            }
            return l;
        }

        public static string Roll(Rng rnd, Func<RewardDef, bool> filter, float luck = 0f)
        {
            var pool = Pool(filter, luck);
            if (pool.Count == 0) return "gold_small";
            return rnd.Weighted(pool, kv => kv.Value).Key;
        }

        static RewardDef Armor(string id, string name, string icon, float reduction, int durability, int color, float weight, bool rare = false,
            float trapResist = 0f, float stunResist = 0f, string desc = null)
        {
            return new RewardDef
            {
                Id = id, Kind = "armor", Category = "Armadura", Name = name, Icon = icon, Reduction = reduction, Durability = durability,
                Color = color, Weight = weight, Rare = rare, TrapResist = trapResist, StunResist = stunResist,
                Text = "-" + MathUtil.RoundJs(reduction * 100f) + " % daño durante " + durability + " golpes" + (desc != null ? " " + desc : ""),
            };
        }

        static RewardDef Item(string id, string name, string icon, float weight, string text, bool rare = false)
        {
            return new RewardDef { Id = id, Kind = "item", Category = "Objeto especial", Name = name, Icon = icon, Weight = weight, Text = text, Rare = rare };
        }

        static RewardDef Buff(string id, string name, string icon, float per, float weight, string text)
        {
            return new RewardDef { Id = id, Kind = "buff", Category = "Mejora temporal", Name = name, Icon = icon, Per = per, Weight = weight, Text = text };
        }

        static Rewards()
        {
            // ---- Armaduras ----
            Add(Armor("armor_chain", "Cota de anillas oxidada", "⛓️", 0.25f, 8, 0x8a8f96, 3));
            Add(Armor("armor_pots", "Peto de cacerolas", "🥘", 0.35f, 6, 0xb0b8c0, 2, rare: true));
            Add(Armor("armor_cork", "Armadura de corcho", "🪵", 0.15f, 12, 0xc49a6c, 3, trapResist: 0.5f, desc: "y la mitad de daño de trampas"));
            Add(Armor("armor_gambeson", "Gambesón acolchado", "🧶", 0.2f, 10, 0x9a7a5a, 3, stunResist: 0.4f, desc: "y aturdimientos un 40 % más cortos"));
            // ---- Objetos especiales ----
            Add(Item("item_shield", "Escudo de emergencia", "🛡️", 4, "Bloquea por completo un impacto"));
            Add(Item("item_boots", "Botas de rebote", "👢", 3, "+12 % velocidad; rebotas sin daño contra las paredes"));
            Add(Item("item_luck", "Amuleto de fortuna", "🍀", 2, "Mejores recompensas y más comida en los barriles", rare: true));
            Add(Item("item_gloves", "Guantes de fuerza", "🥊", 3, "Tus golpes y cargas empujan un 40 % más"));
            Add(Item("item_compass", "Brújula maldita", "🧭", 2, "Señala la salida y revela casillas seguras... pero atrae a los monstruos"));
            Add(Item("item_idol", "Ídolo inestable", "🗿", 2, "+35 % daño hecho, pero +30 % daño recibido", rare: true));
            // ---- Mejoras temporales (valor por pila) ----
            Add(Buff("buff_speed", "Patas ligeras", "💨", 0.15f, 3, "+15 % velocidad de movimiento"));
            Add(Buff("buff_anchor", "Pies de plomo", "⚓", 0.25f, 3, "-25 % empujones recibidos"));
            Add(Buff("buff_headstrong", "Cabeza dura", "🤕", 0.3f, 3, "Te recuperas antes de los aturdimientos"));
            Add(Buff("buff_soles", "Suelas gruesas", "🥾", 0.4f, 3, "-40 % daño de trampas"));
            Add(Buff("buff_cook", "Brazo de cocinero", "🍳", 0.2f, 3, "+20 % daño con objetos"));
            // ---- Instantáneas y maldiciones (las maldiciones no salen en sorteos normales: peso 0) ----
            Add(new RewardDef { Id = "heal_big", Kind = "instant", Category = "Al momento", Name = "Estofado reconfortante", Icon = "🥣", Weight = 3, Text = "+40 de vida", Heal = 40 });
            Add(new RewardDef { Id = "gold_small", Kind = "instant", Category = "Al momento", Name = "Puñado de monedas", Icon = "🪙", Weight = 4, Text = "+15 monedas", Gold = 15 });
            Add(new RewardDef { Id = "gold_big", Kind = "instant", Category = "Al momento", Name = "Cofrecillo de monedas", Icon = "💰", Weight = 1, Rare = true, Text = "+50 monedas", Gold = 50 });
            Add(new RewardDef { Id = "curse_frail", Kind = "instant", Category = "Maldición", Name = "Maldición del flojo", Icon = "💀", Weight = 0, Text = "-20 de vida", Hurt = 20 });
            Add(new RewardDef { Id = "curse_slow", Kind = "buff", Category = "Maldición", Name = "Pies de barro", Icon = "🐌", Per = -0.2f, Weight = 0, Text = "-20 % velocidad un rato", Curse = true });
            Add(new RewardDef { Id = "curse_brittle", Kind = "instant", Category = "Maldición", Name = "Óxido instantáneo", Icon = "🦠", Weight = 0, Text = "Tu arma pierde la mitad de sus usos", Brittle = true });
        }
    }
}
