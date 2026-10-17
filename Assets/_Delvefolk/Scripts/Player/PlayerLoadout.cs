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

        [System.Serializable]
        sealed class Saved
        {
            public string ability;
            public string head, body, acc, palette, material, fx;
        }

        public PlayerLoadout(int index)
        {
            Index = index;
            Ability = DefaultAbility(index);
            Cosmetics = Delvefolk.Cosmetics.Defaults(index);
        }

        public static string DefaultAbility(int index) { return index == 0 ? "quake" : "shout"; }

        public void SetAbility(string id) { if (!string.IsNullOrEmpty(id)) Ability = id; }

        public static PlayerLoadout Load(int index)
        {
            var l = new PlayerLoadout(index);
            var s = Storage.Get<Saved>("loadout." + index, null);
            if (s != null)
            {
                if (!string.IsNullOrEmpty(s.ability)) l.Ability = s.ability;
                var sel = new Dictionary<string, string>
                {
                    { "head", s.head }, { "body", s.body }, { "acc", s.acc }, { "palette", s.palette }, { "material", s.material }, { "fx", s.fx },
                };
                l.Cosmetics = Delvefolk.Cosmetics.Validate(sel, index, null);
            }
            return l;
        }

        public void Save()
        {
            string v;
            var s = new Saved { ability = Ability };
            if (Cosmetics.TryGetValue("head", out v)) s.head = v;
            if (Cosmetics.TryGetValue("body", out v)) s.body = v;
            if (Cosmetics.TryGetValue("acc", out v)) s.acc = v;
            if (Cosmetics.TryGetValue("palette", out v)) s.palette = v;
            if (Cosmetics.TryGetValue("material", out v)) s.material = v;
            if (Cosmetics.TryGetValue("fx", out v)) s.fx = v;
            Storage.Set("loadout." + Index, s);
        }
    }
}
