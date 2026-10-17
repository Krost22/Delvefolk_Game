using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Minijuego C: EL BOTÍN MALDITO (CursedLootChallenge.js), y base de los de "elegir contenedores con sorpresa".
    // Varios cofres con contenido oculto: botín individual, botín compartido, trampa, maldición o monstruos.
    //   Mantener INTERACTUAR junto a un cofre lo abre (cada jugador tiene un número limitado de aperturas).
    //   Mantener ATACAR lo examina: da una pista (no siempre fiable) que ven TODOS → colaborar compensa.
    // El cofre dorado no se puede examinar: premio doble... o desastre proporcional.
    // Cada cofre se abre una sola vez: el primero que empieza a abrirlo lo bloquea para los demás, y el
    // contenido se cobra con una clave única (sin duplicados).
    public class CursedLootChallenge : BonusChallenge
    {
        public sealed class Item
        {
            public int Id, Tx, Tz;
            public float X, Z;
            public string Kind, Content, Hint;   // Kind: normal | special
            public string State = "closed";
            public int LockedBy = -1;
            public bool Examined;
            public Transform Mesh;
            public ChestProp Chest;              // los platos del banquete no tienen tapa
            public Transform Food;
            public WorldLabel Label;
            public Body Body;
        }

        sealed class Progress { public Item Item; public string Mode; public float T; }
        public struct ContentDef { public string Hint; public bool Good; }

        protected const int W = 13, H = 11;
        public readonly List<Item> Items = new List<Item>();
        public float TimeLeft;
        readonly Dictionary<int, Progress> progress = new Dictionary<int, Progress>();
        protected readonly List<Enemy> Spawned = new List<Enemy>();
        float lastDt = 1f / 60f;

        public CursedLootChallenge(BonusFloorController ctl) : base(ctl) { }

        public override BonusInfo Info
        {
            get
            {
                return new BonusInfo
                {
                    Id = "loot", Name = "El Botín Maldito", Icon = "🧰",
                    Tagline = "Una sala llena de cofres. Ninguno tiene cartel. Alguno muerde.",
                    Rules = new[]
                    {
                        "Mantén <b>{E}</b> junto a un cofre para abrirlo. Cada uno puede abrir " + BonusConfig.Loot.MaxOpensPerPlayer + ".",
                        "Mantén <b>{A}</b> para examinarlo: la pista aparece sobre el cofre para todos (acierta casi siempre).",
                        "Dentro puede haber botín para ti, botín para todo el equipo, una trampa, una maldición o monstruos.",
                        "El <b>cofre dorado</b> no se deja examinar: premio doble... o una catástrofe a su altura.",
                    },
                    Win = "Salir con vida. El desafío acaba al abrirse todo, al gastar las aperturas o a los " + BonusConfig.Loot.TimeLimit + " s.",
                    Lose = "Que caigáis todos (los monstruos de los cofres pegan de verdad).",
                    Risk = "Trampas, maldiciones (-vida, lentitud, arma oxidada) y monstruos.",
                    Reward = "Lo que saquéis de los cofres es vuestro. Los cofres \"compartidos\" dan algo a todos.",
                    Coop = "Decisiones individuales, información compartida: examinad para los demás.",
                };
            }
        }

        // ---- Puntos de personalización para variantes (el Banquete del Mimic los sobrescribe) ----
        protected virtual string WOpen { get { return "Abrir"; } }
        protected virtual string WExamine { get { return "Examinar"; } }
        protected virtual string WOpening { get { return "abriendo"; } }
        protected virtual string WOpens { get { return "aperturas"; } }
        protected virtual float OpenTime { get { return BonusConfig.Loot.OpenTime; } }
        protected virtual float ExamineTime { get { return BonusConfig.Loot.ExamineTime; } }
        protected virtual float HintAccuracy { get { return BonusConfig.Loot.HintAccuracy; } }
        protected virtual int MaxOpens { get { return BonusConfig.Loot.MaxOpensPerPlayer; } }
        protected virtual float TimeLimit { get { return BonusConfig.Loot.TimeLimit; } }
        protected virtual int Count { get { return BonusConfig.Loot.Chests; } }
        protected virtual float GoldenRisk { get { return BonusConfig.Loot.GoldenRisk; } }

        static readonly List<KeyValuePair<string, ContentDef>> lootDefs = new List<KeyValuePair<string, ContentDef>>
        {
            new KeyValuePair<string, ContentDef>("reward", new ContentDef { Hint = "🔔 Tintinea", Good = true }),
            new KeyValuePair<string, ContentDef>("shared", new ContentDef { Hint = "🎵 Suena alegre", Good = true }),
            new KeyValuePair<string, ContentDef>("trap", new ContentDef { Hint = "⏰ Hace tic-tac", Good = false }),
            new KeyValuePair<string, ContentDef>("curse", new ContentDef { Hint = "💀 Huele a azufre", Good = false }),
            new KeyValuePair<string, ContentDef>("monsters", new ContentDef { Hint = "👀 Algo respira", Good = false }),
        };

        protected virtual List<KeyValuePair<string, ContentDef>> ContentDefs { get { return lootDefs; } }
        protected virtual List<KeyValuePair<string, float>> ContentWeights { get { return BonusConfig.Loot.Contents; } }

        protected ContentDef DefOf(string content)
        {
            foreach (var kv in ContentDefs) if (kv.Key == content) return kv.Value;
            return new ContentDef();
        }

        public override LevelDef BuildLevel(int depth)
        {
            var rows = Frame(W, H);
            Put(rows, 0, 3, 't'); Put(rows, W - 1, 3, 't'); Put(rows, 0, 7, 't'); Put(rows, W - 1, 7, 't'); Put(rows, 6, 0, 't');
            Put(rows, 1, 1, 'c'); Put(rows, W - 2, 1, 'c');
            Put(rows, 6, H - 2, 'P'); Put(rows, 6, H - 1, 'N');
            return new LevelDef
            {
                Name = "Piso bonus — " + Info.Name, Fog = 0x0a0806, Map = rows,
                Rooms = new[] { new RoomDef("R", Info.Name, new[] { 1, 1, W - 2, H - 2 }, null) },
            };
        }

        // Posiciones (casillas) de los contenedores normales y del especial
        protected virtual Vector2Int[] NormalSpots { get { return new[] { new Vector2Int(2, 4), new Vector2Int(4, 3), new Vector2Int(8, 3), new Vector2Int(10, 4), new Vector2Int(3, 6), new Vector2Int(9, 6), new Vector2Int(5, 5), new Vector2Int(7, 5) }; } }
        protected virtual Vector2Int SpecialSpot { get { return new Vector2Int(6, 2); } }

        protected virtual void MakeMesh(Item it)
        {
            it.Chest = BonusProps.Chest(Root, it.Kind == "special" ? 0xffd040 : 0x8a8f96, it.Kind == "special" ? 1.35f : 1f);
            it.Mesh = it.Chest.Root;
        }

        public override void Setup()
        {
            int n = Count;
            var spots = NormalSpots;
            // Reparto del contenido: al menos dos buenos y dos malos
            var weights = ContentWeights;
            var contents = new string[n];
            for (int tries = 0; tries < 100; tries++)
            {
                for (int i = 0; i < n; i++) contents[i] = Rnd.Weighted(weights, kv => kv.Value).Key;
                int good = 0;
                foreach (var k in contents) if (DefOf(k).Good) good++;
                if (good >= 2 && n - good >= Mathf.Min(2, n - 2)) break;
            }
            Items.Clear();
            for (int i = 0; i < n; i++)
            {
                var c = TileCenter(spots[i].x, spots[i].y);
                Items.Add(new Item { Id = i, Tx = spots[i].x, Tz = spots[i].y, X = c.x, Z = c.y, Kind = "normal", Content = contents[i] });
            }
            var sc = TileCenter(SpecialSpot.x, SpecialSpot.y);
            Items.Add(new Item
            {
                Id = Items.Count, Tx = SpecialSpot.x, Tz = SpecialSpot.y, X = sc.x, Z = sc.y, Kind = "special",
                Content = Rnd.Next() < GoldenRisk ? "specialBad" : "specialGood",
            });
            var door = TileCenter(6, H - 2);
            foreach (var it in Items)
            {
                MakeMesh(it);
                it.Mesh.localPosition = new Vector3(it.X, 0, it.Z);
                // Mirando hacia la entrada
                it.Mesh.localRotation = Quaternion.Euler(0, Mathf.Atan2(door.x - it.X, door.y - it.Z) * Mathf.Rad2Deg, 0);
                // Los contenedores son obstáculos (cuerpo estático)
                it.Body = G.Physics.Add(new Body { X = it.X, Z = it.Z, Radius = it.Kind == "special" ? 0.75f : 0.55f, IsStatic = true, Kind = BodyKind.Static, Owner = it });
            }
            TimeLeft = TimeLimit;
            progress.Clear();
            Spawned.Clear();
        }

        public override void Start()
        {
            base.Start();
            foreach (var p in G.Players) Participant(p).Score = MaxOpens;   // Score = aperturas que le quedan
        }

        public int OpensLeft(Player p) { return Participant(p).Score; }

        public Item Nearest(Player p)
        {
            Item best = null;
            float bd = 1.9f;
            foreach (var it in Items)
            {
                if (it.State != "closed") continue;
                float d = MathUtil.Hypot(it.X - p.X, it.Z - p.Z);
                if (d < bd) { bd = d; best = it; }
            }
            return best;
        }

        bool CanAct(Player p) { return !p.Dead && !p.Spectator && p.CarriedBy == null && p.Carrying == null && !p.IsThrown && !IsOut(p); }

        // Mantener ATACAR junto a un contenedor = examinar (se bloquea el golpe mientras tanto)
        public override void PreUpdate(float dt)
        {
            lastDt = dt;
            foreach (var p in G.Players)
            {
                var it = CanAct(p) ? Nearest(p) : null;
                if (it == null) { StopProgress(p, "examine"); continue; }
                if (p.Controls.AttackDown() && it.Kind != "special" && !it.Examined)
                {
                    p.Controls.ConsumeAttack();
                    p.Cooldown = Mathf.Max(p.Cooldown, 0.25f);
                    p.Buffer = 0f;
                    Advance(p, it, "examine", dt, ExamineTime);
                }
                else StopProgress(p, "examine");
            }
        }

        public override bool HandleInteraction(Player p)
        {
            var it = CanAct(p) ? Nearest(p) : null;
            if (it == null) { StopProgress(p, "open"); return false; }
            var st = Participant(p);
            if (it.LockedBy >= 0 && it.LockedBy != p.Index)
            {
                G.Ui.SetPrompt(p.Index, Player.Looks[it.LockedBy].Name + " lo está " + WOpening + "...");
                p.Controls.ConsumeInteract();
                return true;
            }
            bool canExamine = it.Kind != "special" && !it.Examined;
            if (st.Score <= 0) G.Ui.SetPrompt(p.Index, "Sin " + WOpens + " · " + (canExamine ? "{A} " + WExamine : "mira y aprende"));
            else G.Ui.SetPrompt(p.Index, "Mantén {E} " + WOpen + (canExamine ? " · {A} " + WExamine : "") + " (" + st.Score + ")");
            if (p.Controls.InteractDown() && st.Score > 0) Advance(p, it, "open", lastDt, OpenTime);
            else StopProgress(p, "open");
            p.Controls.ConsumeInteract();
            return true;
        }

        void Advance(Player p, Item it, string mode, float dt, float total)
        {
            Progress pr;
            if (!progress.TryGetValue(p.Index, out pr) || pr == null || pr.Item != it || pr.Mode != mode)
            {
                pr = progress[p.Index] = new Progress { Item = it, Mode = mode };
                if (mode == "open") it.LockedBy = p.Index;
            }
            pr.T += dt;
            G.Ui.SetPlayerBar(p.Index, pr.T / total, mode == "open" ? BarKind.Eat : BarKind.Revive);
            if (pr.T >= total)
            {
                progress[p.Index] = null;
                G.Ui.SetPlayerBar(p.Index, -1f, BarKind.None);
                if (mode == "open") RequestOpen(p, it); else RequestExamine(p, it);
            }
        }

        void StopProgress(Player p, string mode)
        {
            Progress pr;
            if (!progress.TryGetValue(p.Index, out pr) || pr == null || pr.Mode != mode) return;
            if (pr.Item.LockedBy == p.Index) pr.Item.LockedBy = -1;
            progress[p.Index] = null;
            G.Ui.SetPlayerBar(p.Index, -1f, BarKind.None);
        }

        // ---- Acciones validadas ----
        public bool RequestExamine(Player p, Item it)
        {
            if (Status != "running" || it.Examined || it.State != "closed" || it.Kind == "special") return false;
            it.Examined = true;
            string shown = it.Content;
            if (Rnd.Next() > HintAccuracy)
            {
                var others = new List<string>();
                foreach (var kv in ContentDefs) if (kv.Key != it.Content) others.Add(kv.Key);
                shown = others[(int)(Rnd.Next() * others.Count)];
            }
            it.Hint = DefOf(shown).Hint;
            it.Label = G.Ui.Label(it.Hint, new Vector3(it.X, 2.3f, it.Z));
            var e = Log("examine"); e.Player = p.Index; e.Chest = it.Id; e.Success = shown == it.Content;
            G.Audio.Play("mineFlag");
            return true;
        }

        public bool RequestOpen(Player p, Item it)
        {
            var st = Participant(p);
            if (Status != "running" || it.State != "closed" || st.Score <= 0 || (it.LockedBy >= 0 && it.LockedBy != p.Index))
            {
                var r = Log("open-rejected"); r.Player = p.Index; r.Chest = it.Id;
                return false;
            }
            it.State = "open"; it.LockedBy = -1;
            st.Score--;
            var e = Log("open"); e.Player = p.Index; e.Chest = it.Id; e.How = it.Content;
            if (it.Label != null) it.Label.Remove();
            OpenVisual(it);
            Resolve(p, it);
            CheckEnd();
            return true;
        }

        protected virtual void OpenVisual(Item it)
        {
            if (it.Chest != null) it.Chest.Open = true;
            G.Audio.Play("chest");
            G.Effects.Burst(it.X, 0.8f, it.Z, BurstOpts.Of(14, 0xffe08a, 3f, 5f, 0.1f));
        }

        protected static bool NotCurse(RewardDef d) { return d.Category != "Maldición"; }

        // Efecto del contenido. Las claves de recompensa llevan el id del contenedor: nunca se cobran dos veces.
        protected virtual void Resolve(Player p, Item it)
        {
            string key = Source + ":item" + it.Id;
            float luck = p.Gear.Luck();
            var rs = G.RewardService;
            switch (it.Content)
            {
                case "reward":
                    rs.ClaimReward(p, Rewards.Roll(Rnd, NotCurse, luck), key);
                    break;
                case "shared":
                    G.Ui.Toast("🎵 ¡Botín compartido! Todo el equipo recibe algo.", 3f);
                    foreach (var q in G.Players) if (!IsOut(q)) rs.ClaimReward(q, Rewards.Roll(Rnd, NotCurse, luck), key);
                    break;
                case "trap":
                    Blast(it.X, it.Z, 2.8f, 18f, "¡TRAMPA!");
                    break;
                case "curse":
                    {
                        var curses = new[] { "curse_frail", "curse_slow", "curse_brittle" };
                        rs.ClaimReward(p, curses[(int)(Rnd.Next() * curses.Length)], key);
                        G.Effects.Burst(it.X, 1f, it.Z, BurstOpts.Of(20, 0x8a40c0, 3f, 5f, 0.14f));
                        break;
                    }
                case "monsters":
                    G.Ui.FloatText(it.X, 2.6f, it.Z, "¡¡SORPRESA!!", TextStyle.Big);
                    SpawnAt(it, 'S', new EnemyOpts { Scale = 0.8f, Cfg = c => { c.Hp = 24; c.Damage = 8; } });
                    SpawnAt(it, 'G', new EnemyOpts { Cfg = c => { c.Hp = 20; c.Damage = 6; } });
                    break;
                case "specialGood":
                    G.Ui.Toast("👑 ¡El cofre dorado era de verdad! Premio gordo para " + p.Look.Name + " y algo para el resto.", 4f);
                    for (int k = 0; k < 2; k++) rs.ClaimReward(p, Rewards.Roll(Rnd, d => NotCurse(d) && (d.Rare || d.Kind != "instant"), luck), key + ":" + k);
                    foreach (var q in G.Players) if (q != p && !IsOut(q)) rs.ClaimReward(q, Rewards.Roll(Rnd, NotCurse, luck), key + ":share");
                    break;
                case "specialBad":
                    Blast(it.X, it.Z, 3.6f, 30f, "¡ERA UN MIMIC!");
                    SpawnAt(it, 'L', new EnemyOpts { Scale = 1.3f, Cfg = c => { c.Hp = 70; c.Damage = 12; c.Splits = false; c.Color = 0xc9a227; } });
                    rs.ClaimReward(p, "gold_small", key + ":consolation");
                    break;
            }
        }

        protected void Blast(float x, float z, float r, float dmg, string text)
        {
            G.Audio.Play("explosion");
            G.CameraCtl.AddShake(0.6f);
            G.Effects.Flash(x, 2f, z, 0xff8a30, 5f, 0.3f);
            G.Effects.Ring(x, z, RingOpts.Of(0xffb040, r, 0.4f));
            G.Effects.Burst(x, 0.8f, z, BurstOpts.Of(28, 0xff8a20, 8f, 6f, 0.22f).WithColorVar(0.15f));
            G.Ui.FloatText(x, 2.8f, z, text, TextStyle.Big);
            foreach (var q in G.Players) if (q.Targetable && MathUtil.Hypot(q.X - x, q.Z - z) < r) q.Hurt(dmg, x, z, 13f, new HurtOpts { Kind = "trap" });
        }

        protected void SpawnAt(Item it, char ch, EnemyOpts opts)
        {
            float a = Rnd.Next() * Mathf.PI * 2f;
            var e = G.SpawnEnemy(ch, it.X + Mathf.Cos(a) * 1.3f, it.Z + Mathf.Sin(a) * 1.3f, opts);
            if (e == null) return;
            e.Wake(true); e.SetState(EnemyState.Chase);
            e.Knock(Mathf.Cos(a) * 6f, Mathf.Sin(a) * 6f);
            Spawned.Add(e);
        }

        void CheckEnd()
        {
            if (Status != "running") return;
            int closed = 0;
            foreach (var it in Items) if (it.State == "closed") closed++;
            int opensLeft = 0;
            foreach (var p in ActivePlayers()) opensLeft += Mathf.Max(0, Participant(p).Score);
            if (closed == 0) Finish(true, "No queda nada por " + WOpen.ToLowerInvariant());
            else if (opensLeft == 0 && LivingMonsters() == 0) Finish(true, "Se acabaron las " + WOpens);
        }

        public int LivingMonsters() { int n = 0; foreach (var e in Spawned) if (!e.Dead) n++; return n; }

        public override void Update(float dt)
        {
            base.Update(dt);
            TimeLeft -= dt;
            foreach (var it in Items)
            {
                if (it.Chest != null) it.Chest.UpdateLid(dt);
                if (it.Label != null && it.Label.Alive) it.Label.Pos = new Vector3(it.X, LabelY(it) + Mathf.Sin(Time * 3f + it.Id) * 0.06f, it.Z);
                // Un contenedor bloqueado por alguien tiembla un poco
                var pos = it.Mesh.localPosition;
                pos.x = it.LockedBy >= 0 && it.State == "closed" ? it.X + Mathf.Sin(Time * 60f) * 0.03f : it.X;
                it.Mesh.localPosition = pos;
            }
            CheckEnd();
            if (Status == "running" && TimeLeft <= 0f) { TimeLeft = 0f; Finish(ActivePlayers().Count > 0, "Se acabó el tiempo"); }
        }

        protected virtual float LabelY(Item it) { return 2.3f; }

        public override BonusHudState Hud()
        {
            int closed = 0;
            foreach (var it in Items) if (it.State == "closed") closed++;
            var h = new BonusHudState { Title = Info.Name, Icon = Info.Icon, Timer = Mathf.Max(0f, TimeLeft), HasProgress = true, ProgressLabel = "Abiertos", ProgressValue = Items.Count - closed, ProgressMax = Items.Count };
            int m = LivingMonsters();
            if (m > 0) h.Lines.Add("👹 Monstruos sueltos: " + m);
            HudPlayers(h, p =>
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < Mathf.Max(0, Participant(p).Score); i++) sb.Append("🔓");
                return sb.ToString();
            });
            return h;
        }

        public override void Dispose()
        {
            foreach (var it in Items) if (it.Body != null) G.Physics.Remove(it.Body);
        }
    }
}
