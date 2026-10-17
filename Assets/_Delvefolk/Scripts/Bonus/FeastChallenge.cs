using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Minijuego E: EL BANQUETE DEL MIMIC (FeastChallenge.js), variante de El Botín Maldito con la misma
    // mecánica de abrir/examinar. Platos mágicos sobre mesitas: unos dan mejoras temporales, otros son
    // imitadores que muerden, y alguno lleva una nota que revela la verdad de otros platos (para todos).
    // El "festín prohibido" (en la mesa grande) da un premio enorme a quien se lo coma... y castiga al resto.
    public sealed class FeastChallenge : CursedLootChallenge
    {
        public FeastChallenge(BonusFloorController ctl) : base(ctl) { }

        public override BonusInfo Info
        {
            get
            {
                return new BonusInfo
                {
                    Id = "feast", Name = "El Banquete del Mimic", Icon = "🍖",
                    Tagline = "Un banquete servido para nadie. Algún plato te está mirando.",
                    Rules = new[]
                    {
                        "Mantén <b>{E}</b> junto a un plato para comértelo. Cada uno tiene " + BonusConfig.Feast.MaxBitesPerPlayer + " bocados.",
                        "Mantén <b>{A}</b> para olfatearlo: la pista aparece sobre el plato para todos (casi siempre acierta).",
                        "Hay mejoras temporales, imitadores que muerden y notas que revelan la verdad de otros platos.",
                        "El <b>festín prohibido</b>: un premio enorme para quien lo coma... y una maldición para todos los demás.",
                    },
                    Win = "Salir con vida. Acaba al vaciar la mesa, al gastar los bocados o a los " + BonusConfig.Feast.TimeLimit + " s.",
                    Lose = "Que caigáis todos (los imitadores muerden).",
                    Risk = "Mordiscos, imitadores sueltos y la tentación de traicionar al equipo.",
                    Reward = "Mejoras temporales para quien come. El festín prohibido: botín raro doble.",
                    Coop = "Compartid lo que olfateáis... o guardaos el festín para vosotros.",
                };
            }
        }

        protected override string WOpen { get { return "Comer"; } }
        protected override string WExamine { get { return "Olfatear"; } }
        protected override string WOpening { get { return "comiendo"; } }
        protected override string WOpens { get { return "bocados"; } }
        protected override float OpenTime { get { return BonusConfig.Feast.EatTime; } }
        protected override float ExamineTime { get { return BonusConfig.Feast.SniffTime; } }
        protected override float HintAccuracy { get { return BonusConfig.Feast.SniffAccuracy; } }
        protected override int MaxOpens { get { return BonusConfig.Feast.MaxBitesPerPlayer; } }
        protected override float TimeLimit { get { return BonusConfig.Feast.TimeLimit; } }
        protected override int Count { get { return BonusConfig.Feast.Dishes; } }
        protected override float GoldenRisk { get { return 0.5f; } }

        static readonly List<KeyValuePair<string, ContentDef>> feastDefs = new List<KeyValuePair<string, ContentDef>>
        {
            new KeyValuePair<string, ContentDef>("buff", new ContentDef { Hint = "✨ Huele de maravilla", Good = true }),
            new KeyValuePair<string, ContentDef>("mimic", new ContentDef { Hint = "👅 Se ha movido...", Good = false }),
            new KeyValuePair<string, ContentDef>("clue", new ContentDef { Hint = "📜 Lleva una nota", Good = true }),
        };

        protected override List<KeyValuePair<string, ContentDef>> ContentDefs { get { return feastDefs; } }
        protected override List<KeyValuePair<string, float>> ContentWeights { get { return BonusConfig.Feast.Contents; } }
        protected override Vector2Int[] NormalSpots { get { return new[] { new Vector2Int(2, 4), new Vector2Int(4, 3), new Vector2Int(8, 3), new Vector2Int(10, 4), new Vector2Int(3, 6), new Vector2Int(9, 6), new Vector2Int(5, 6), new Vector2Int(7, 6) }; } }
        protected override Vector2Int SpecialSpot { get { return new Vector2Int(6, 3); } }

        // Mesita con plato y comida low-poly al azar (un cerdo asado con aura rosa en la mesa grande)
        protected override void MakeMesh(Item it)
        {
            Transform food;
            it.Mesh = BuildTable(Root, it.Kind == "special", Rnd, out food);
            it.Food = food;
        }

        // Mesita con plato y comida low-poly al azar (un cerdo asado con aura rosa en la mesa grande).
        // También la usan las viñetas ilustradas de la introducción.
        public static Transform BuildTable(Transform parent, bool big, Rng rnd, out Transform food)
        {
            var root = new GameObject(big ? "FestinProhibido" : "Plato").transform;
            root.SetParent(parent, false);
            var b = new MeshBuilder();
            var wood = MathUtil.Hex(0x6a4426);
            b.Add(ProcMesh.Cylinder(big ? 0.95f : 0.6f, big ? 0.95f : 0.6f, 0.1f, 10), new Vector3(0, 0.75f, 0), wood);
            b.Add(ProcMesh.Cylinder(0.12f, 0.18f, 0.75f, 6), new Vector3(0, 0.37f, 0), wood);
            b.Add(ProcMesh.Cylinder(big ? 0.7f : 0.42f, big ? 0.6f : 0.36f, 0.05f, 12), new Vector3(0, 0.83f, 0), MathUtil.Hex(0xe8e0d0));
            CharacterModel.MeshObject(root, "Mesa", b.ToMesh("mesita"), Mats.Lit, Vector3.zero);
            Entity.BlobShadow(root, big ? 0.9f : 0.6f);
            food = new GameObject("Comida").transform;
            food.SetParent(root, false);
            food.localPosition = new Vector3(0, big ? 1.15f : 0.98f, 0);
            var f = new MeshBuilder();
            int variant = big ? 4 : (int)(rnd.Next() * 4);
            if (variant == 0) f.Add(ProcMesh.Icosahedron(0.22f, 0), Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, 0.8f, 1.3f)), MathUtil.Hex(0xb5652e));
            else if (variant == 1) f.Add(ProcMesh.Cylinder(0.3f, 0.3f, 0.18f, 3, 1, false, 0f, Mathf.PI / 2f), Vector3.zero, MathUtil.Hex(0xf2c94c));
            else if (variant == 2)
            {
                f.Add(ProcMesh.Cylinder(0.3f, 0.32f, 0.16f, 10), Vector3.zero, MathUtil.Hex(0xc08040));
                f.Add(ProcMesh.Sphere(0.08f, 5, 4), new Vector3(0, 0.12f, 0), MathUtil.Hex(0xd03050));
            }
            else if (variant == 3) f.Add(ProcMesh.Cone(0.12f, 0.6f, 5), Matrix4x4.TRS(Vector3.zero, ProcMesh.EulerXYZ(0, 0, Mathf.PI / 2f), Vector3.one), MathUtil.Hex(0x8ab0c0));
            else
            {
                f.Add(ProcMesh.Icosahedron(0.45f, 1), Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1.4f, 0.8f, 0.9f)), MathUtil.Hex(0xc9703a));
                f.Add(ProcMesh.Sphere(0.12f, 6, 5), new Vector3(0.62f, 0.05f, 0), MathUtil.Hex(0xd02a20));
                // Brillo sospechoso
                var aura = new MeshBuilder().Add(ProcMesh.Icosahedron(0.75f, 1), Vector3.zero, new Color(1f, 0.376f, 0.627f, 0.18f)).ToMesh("aura");
                CharacterModel.MeshObject(food, "Aura", aura, Mats.Transparent, new Vector3(0, 0.1f, 0));
            }
            CharacterModel.MeshObject(food, "Plato", f.ToMesh("comidaBanquete"), Mats.Lit, Vector3.zero);
            return root;
        }

        public override void Setup()
        {
            base.Setup();
            Item sp = null;
            foreach (var it in Items) if (it.Kind == "special") sp = it;
            sp.Content = "forbidden";
            sp.Examined = true;           // no se olfatea: ya se sabe lo que es
            sp.Label = G.Ui.Label("☠️ Festín prohibido", new Vector3(sp.X, 2.6f, sp.Z), 0xff8ac0);
        }


        protected override void OpenVisual(Item it)
        {
            G.Audio.Play("eat");
            if (it.Food) it.Food.gameObject.SetActive(false);
            G.Effects.Burst(it.X, 1f, it.Z, BurstOpts.Of(12, 0xc08040, 2f, 3f, 0.08f));
        }

        protected override void Resolve(Player p, Item it)
        {
            string key = Source + ":item" + it.Id;
            float luck = p.Gear.Luck();
            var rs = G.RewardService;
            switch (it.Content)
            {
                case "buff":
                    rs.ClaimReward(p, Rewards.Roll(Rnd, d => d.Kind == "buff" && !d.Curse, luck), key);
                    G.Ui.FloatText(p.X, 2.4f, p.Z, "¡Ñam!", TextStyle.Heal);
                    break;
                case "mimic":
                    G.Ui.FloatText(it.X, 2.6f, it.Z, "¡ÑAC! ¡Era un imitador!", TextStyle.Big);
                    G.Audio.Play("hit");
                    p.Hurt(15, it.X, it.Z, 10f);
                    SpawnAt(it, 'S', new EnemyOpts { Scale = 0.7f, Cfg = c => { c.Hp = 18; c.Damage = 7; c.Color = 0xc08040; } });
                    break;
                case "clue":
                    {
                        // La nota revela la verdad de dos platos aún llenos (para todos)
                        var cands = new List<Item>();
                        foreach (var o in Items) if (o != it && o.State == "closed" && o.Kind != "special") cands.Add(o);
                        Rnd.Shuffle(cands);
                        for (int k = 0; k < cands.Count && k < 2; k++)
                        {
                            var o = cands[k];
                            if (o.Label != null) o.Label.Remove();
                            o.Examined = true; o.Hint = DefOf(o.Content).Hint;
                            o.Label = G.Ui.Label("✔ " + o.Hint, new Vector3(o.X, 2.3f, o.Z), 0x7dff8a);
                        }
                        G.Ui.Toast("📜 La nota dice la verdad sobre " + Mathf.Min(2, cands.Count) + " platos (rótulos verdes).", 3.5f);
                        G.Audio.Play("mineReveal");
                        break;
                    }
                case "forbidden":
                    G.Ui.Toast("☠️ " + p.Look.Name + " se ha zampado el festín prohibido. El resto lo va a pagar...", 4f);
                    for (int k = 0; k < 2; k++) rs.ClaimReward(p, Rewards.Roll(Rnd, d => d.Category != "Maldición" && (d.Rare || d.Kind != "instant"), luck), key + ":" + k);
                    p.Heal(40f);
                    foreach (var q in G.Players) if (q != p && !IsOut(q)) rs.ClaimReward(q, "curse_frail", key + ":curse");
                    SpawnAt(it, 'G', new EnemyOpts { Cfg = c => { c.Hp = 22; c.Damage = 7; } });
                    SpawnAt(it, 'S', new EnemyOpts { Scale = 0.8f, Cfg = c => { c.Hp = 22; c.Damage = 7; } });
                    break;
            }
        }
    }
}
