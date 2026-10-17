using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Al vencer al Crupier: lluvia de monedas sobre la sala y la RULETA DEL PREMIO flotando sobre la corona del
    // centro. Cada jugador puede girarla una vez ({E} junto a la corona); el color decide su premio. Usa la misma
    // ruleta (pesos, giro y rueda) que el combate, con "habilidades" que dan premios en vez de atacar.
    public sealed class CrupierPrize
    {
        public const float RainTime = 3.5f, InteractRange = 3.2f;
        public const int RainCoins = 50;

        readonly Game G;
        public readonly RouletteWheel Wheel;
        public readonly RouletteController Roulette;
        public readonly Vector3 Center;
        public readonly HashSet<Player> Spun = new HashSet<Player>();
        public Player Spinner { get; private set; }
        public int Landed;                 // monedas de la lluvia que ya han llegado al suelo
        public int CoinsDropped;           // monedas reales que ha soltado la lluvia
        readonly List<Falling> rain = new List<Falling>();
        float rainT, rainAcc;
        int prizeSeq;
        static Mesh coinMesh;

        sealed class Falling { public Transform Tr; public float X, Z, Y, Vy, Spin; }

        public CrupierPrize(Game g, Vector3 center)
        {
            G = g; Center = center;
            var cfg = RouletteConfig.Load();
            Wheel = new RouletteWheel(g, cfg, g.FloorRoot);
            Roulette = new RouletteController(cfg, Wheel, () => G.Rand.Next());
            foreach (RouletteOutcome o in System.Enum.GetValues(typeof(RouletteOutcome))) Roulette.Abilities.Connect(o, new PrizeAbility(this, o));
            Roulette.Revealed += OnRevealed;
            Roulette.CooldownMult = 0.35f;
            Wheel.Show(true);
            var bf = g.BossFloor;
            if (bf != null) { bf.Ticks += Update; bf.Interact = Interact; }
            G.Audio.Play("chest");
            G.Ui.Banner("¡LA BANCA HA QUEBRADO!", "Lluvia de monedas");
            G.Ui.Toast("🎡 Id a la corona del centro: cada uno puede girar la ruleta una vez y llevarse un premio.", 6f);
        }

        // ---------- Lluvia de monedas ----------
        void UpdateRain(float dt)
        {
            if (rainT < RainTime)
            {
                rainT += dt;
                rainAcc += dt * RainCoins / RainTime;
                while (rainAcc >= 1f) { rainAcc -= 1f; SpawnFalling(); }
            }
            for (int i = rain.Count - 1; i >= 0; i--)
            {
                var c = rain[i];
                c.Vy -= 22f * dt;
                c.Y += c.Vy * dt;
                c.Spin += dt * 12f;
                c.Tr.position = new Vector3(c.X, c.Y, c.Z);
                c.Tr.rotation = Quaternion.Euler(c.Spin * 57f, c.Spin * 30f, 0f);
                if (c.Y > 0.2f) continue;
                // Al tocar el suelo se convierte en una moneda de verdad
                Landed++;
                if (!G.Dungeon.IsWallLike(Grid.TileX(c.X), Grid.TileZ(c.Z)) && !G.Dungeon.IsPitAt(c.X, c.Z)) { G.SpawnCoins(c.X, c.Z, 1); CoinsDropped++; }
                if (G.FxRand.Next() < 0.3f) G.Audio.Play("coin");
                Object.Destroy(c.Tr.gameObject);
                rain.RemoveAt(i);
            }
        }

        void SpawnFalling()
        {
            if (!coinMesh)
                coinMesh = new MeshBuilder().Add(ProcMesh.Cylinder(0.28f, 0.28f, 0.07f, 12).RotateX(Mathf.PI / 2f), Vector3.zero, MathUtil.Hex(0xf2c230)).ToMesh("monedaLluvia");
            // Sobre todo alrededor de los jugadores, el resto por la sala
            float x, z;
            var ps = G.Players.FindAll(p => !p.Removed);
            if (ps.Count > 0 && G.Rand.Next() < 0.6f)
            {
                var p = ps[(int)(G.Rand.Next() * ps.Count)];
                x = p.X + G.Rand.Range(-5f, 5f); z = p.Z + G.Rand.Range(-5f, 5f);
            }
            else { x = Center.x + G.Rand.Range(-14f, 14f); z = Center.z + G.Rand.Range(-11f, 11f); }
            var go = CharacterModel.MeshObject(G.FloorRoot, "MonedaCayendo", coinMesh, Mats.Lit, new Vector3(x, 9f, z));
            rain.Add(new Falling { Tr = go.transform, X = x, Z = z, Y = G.Rand.Range(8f, 12f), Vy = 0f, Spin = G.Rand.Next() * 6f });
        }

        public bool Raining { get { return rainT < RainTime || rain.Count > 0; } }

        // ---------- Ruleta del premio ----------
        public void Update(float dt)
        {
            UpdateRain(dt);
            Roulette.Tick(dt);
            if (Roulette.IsIdle && HasSpinsLeft() && !Wheel.Visible) Wheel.Show(true);
            Wheel.Update(dt, Center.x, Center.z);
        }

        public bool HasSpinsLeft()
        {
            foreach (var p in G.Players) if (!p.Removed && !Spun.Contains(p)) return true;
            return false;
        }

        public bool CanSpin(Player p) { return p != null && !p.Dead && !Spun.Contains(p) && Roulette.IsIdle; }

        // Gira la ruleta para este jugador (una vez por jugador)
        public bool TrySpin(Player p)
        {
            if (!CanSpin(p)) return false;
            Spinner = p;
            if (!Roulette.TrySpin()) return false;
            Spun.Add(p);
            G.Ui.FloatText(p.X, 2.8f, p.Z, "¡Hagan juego!", TextStyle.Label);
            return true;
        }

        // Interacción de cada jugador junto a la corona
        bool Interact(Player p)
        {
            if (MathUtil.Hypot(p.X - Center.x, p.Z - Center.z) > InteractRange) return false;
            if (Spun.Contains(p)) return false;
            if (!Roulette.IsIdle) { G.Ui.SetPrompt(p.Index, "La ruleta está girando…"); return true; }
            G.Ui.SetPrompt(p.Index, "Girar la ruleta del premio");
            if (p.Controls.ConsumeInteract()) TrySpin(p);
            return true;
        }

        void OnRevealed(RouletteOutcome o)
        {
            var info = PrizeInfo(o);
            G.Ui.Banner(info.Name, info.Title);
            G.Audio.Play(o == RouletteOutcome.Negro ? "lose" : "win");
            var wp = Wheel.Root ? Wheel.Root.position : Center + Vector3.up * 4f;
            G.Effects.Burst(wp.x, wp.y, wp.z, BurstOpts.Of(30, 0xffe070, 7f, 5f, 0.16f));
        }

        public static CrupierAbilityInfo PrizeInfo(RouletteOutcome o)
        {
            switch (o)
            {
                case RouletteOutcome.Dorado: return new CrupierAbilityInfo { Name = "¡JACKPOT!", Title = "Dos cofres de monedas y una lluvia extra" };
                case RouletteOutcome.Rojo: return new CrupierAbilityInfo { Name = "ROJO", Title = "Botín raro" };
                case RouletteOutcome.Verde: return new CrupierAbilityInfo { Name = "VERDE", Title = "Vida al máximo y un estofado" };
                case RouletteOutcome.Azul: return new CrupierAbilityInfo { Name = "AZUL", Title = "Botín raro" };
                case RouletteOutcome.Morado: return new CrupierAbilityInfo { Name = "MORADO", Title = "Doble o nada: ¡dos botines raros!" };
                default: return new CrupierAbilityInfo { Name = "NEGRO", Title = "La banca gana… te deja un puñado de monedas" };
            }
        }

        // Entrega el premio del resultado al jugador que giró
        void Give(RouletteOutcome o)
        {
            var p = Spinner;
            if (p == null || p.Removed) return;
            if (p.Dead) p.Revive(40f);
            System.Func<RewardDef, bool> rare = d => d.Category != "Maldición" && (d.Rare || d.Kind != "instant");
            switch (o)
            {
                case RouletteOutcome.Dorado:
                    Claim(p, "gold_big"); Claim(p, "gold_big");
                    G.SpawnCoins(p.X, p.Z, 20);
                    break;
                case RouletteOutcome.Verde:
                    p.Heal(p.MaxHp);
                    Claim(p, "heal_big");
                    break;
                case RouletteOutcome.Morado:
                    Claim(p, Rewards.Roll(G.Rand, rare, p.Gear.Luck()));
                    Claim(p, Rewards.Roll(G.Rand, rare, p.Gear.Luck()));
                    break;
                case RouletteOutcome.Negro:
                    Claim(p, "gold_small");
                    break;
                default:
                    Claim(p, Rewards.Roll(G.Rand, rare, p.Gear.Luck()));
                    break;
            }
        }

        void Claim(Player p, string id) { G.RewardService.ClaimReward(p, id, "crupier:premio:" + p.Index + ":" + (prizeSeq++)); }

        // "Habilidad" de la ruleta que da el premio del color
        sealed class PrizeAbility : IRouletteAbility
        {
            readonly CrupierPrize owner;
            readonly RouletteOutcome outcome;
            public PrizeAbility(CrupierPrize owner, RouletteOutcome o) { this.owner = owner; outcome = o; }
            public string Title { get { return PrizeInfo(outcome).Title; } }
            public bool HoldsDealer { get { return false; } }
            public void Begin() { owner.Give(outcome); }
            public bool Tick(float dt) { return true; }
            public void Cancel() { }
        }
    }
}
