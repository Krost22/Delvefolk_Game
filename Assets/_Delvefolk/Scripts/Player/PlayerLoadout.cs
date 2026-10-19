using System.Collections.Generic;

namespace Delvefolk
{
    // Elección de la sala de preparación de un jugador: una habilidad + cosméticos (PlayerLoadout.js).
    // Se guarda en PlayerPrefs (clave delvefolk.loadout.N).
    public sealed class PlayerLoadout
    {
        public readonly int Index;
        public string Ability;
        public Dictionary<string, string> Cosmetics;
        public string Hero;   // personaje (Resources/Heroes); su habilidad pasa a ser la del jugador

        [System.Serializable]
        sealed class Saved
        {
            public string ability, hero;
            public string head, body, acc, palette, material, fx;
        }

        public PlayerLoadout(int index) : this(index, null, null) { }

        // Con datos guardados: lo desconocido vuelve a lo de por defecto (habilidad y piezas)
        public PlayerLoadout(int index, string ability, Dictionary<string, string> cosmetics)
        {
            Index = index;
            Ability = Abilities.Get(ability) != null ? ability : DefaultAbility(index);
            Cosmetics = Delvefolk.Cosmetics.Validate(cosmetics, index, null);
            SetHero(HeroCatalog.DefaultFor(index));
        }

        public static string DefaultAbility(int index) { return Abilities.DefaultFor(index); }

        // Personaje: uno del catálogo (lo desconocido vuelve al de por defecto del hueco). Con personaje, la habilidad
        // es la suya (la que enseña su ficha en la sala)
        public bool SetHero(string id)
        {
            bool ok = HeroCatalog.Get(id) != null || (id == HeroCatalog.Classic && HeroCatalog.ClassicAllowed);
            Hero = ok ? id : HeroCatalog.DefaultFor(Index);
            var ab = HeroCatalog.AbilityOf(Hero);
            if (ab != null) Ability = ab;
            return ok;
        }

        // Una sola habilidad equipada: elegir otra sustituye a la anterior
        public bool SetAbility(string id)
        {
            if (Abilities.Get(id) == null) return false;
            Ability = id;
            return true;
        }

        // Cambia una pieza cosmética; devuelve las piezas retiradas por incompatibles (p. ej. capa + mochila)
        public List<Delvefolk.Cosmetics.Removed> SetCosmetic(string slot, string id)
        {
            var next = new Dictionary<string, string>(Cosmetics);
            next[slot] = id;
            var removed = new List<Delvefolk.Cosmetics.Removed>();
            Cosmetics = Delvefolk.Cosmetics.Validate(next, Index, slot, removed);
            return removed;
        }

        public static PlayerLoadout Load(int index)
        {
            var s = Storage.Get<Saved>("loadout." + index, null);
            if (s == null) return new PlayerLoadout(index);
            var sel = new Dictionary<string, string>
            {
                { "head", s.head }, { "body", s.body }, { "acc", s.acc }, { "palette", s.palette }, { "material", s.material }, { "fx", s.fx },
            };
            var lo = new PlayerLoadout(index, s.ability, sel);
            lo.SetHero(s.hero);
            return lo;
        }

        public void Save()
        {
            string v;
            var s = new Saved { ability = Ability, hero = Hero };
            if (Cosmetics.TryGetValue("head", out v)) s.head = v;
            if (Cosmetics.TryGetValue("body", out v)) s.body = v;
            if (Cosmetics.TryGetValue("acc", out v)) s.acc = v;
            if (Cosmetics.TryGetValue("palette", out v)) s.palette = v;
            if (Cosmetics.TryGetValue("material", out v)) s.material = v;
            if (Cosmetics.TryGetValue("fx", out v)) s.fx = v;
            Storage.Set("loadout." + Index, s);
        }
    }

    // Ajustes de la partida que elige el anfitrión en la sala (DF.MatchSettings); se guardan en "settings"
    [System.Serializable]
    public sealed class MatchSettings
    {
        // Pisos bonus: "normal", "always" o "never"
        public string bonusMode = "normal";
        public bool exclusiveAbilities = false;
        public bool swapAllies = true;
        public bool bosses = true;   // peleas de jefe cada pocos pisos

        public string BonusMode { get { return bonusMode; } set { bonusMode = value; } }
        public bool ExclusiveAbilities { get { return exclusiveAbilities; } set { exclusiveAbilities = value; } }
        public bool SwapAllies { get { return swapAllies; } set { swapAllies = value; } }

        public static MatchSettings Load() { return Storage.Get<MatchSettings>("settings", null) ?? new MatchSettings(); }
        public void Save() { Storage.Set("settings", this); }
        public MatchSettings Clone() { return (MatchSettings)MemberwiseClone(); }
    }
}
