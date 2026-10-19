using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // 🎰 EL CRUPIER — jefe final provisional de la demo, en la sala del trono del casino (CasinoRoom).
    // Sus golpes normales: barrido con el rastrillo (de cerca) y abanico de cartas (se pueden devolver).
    // Cada pocos segundos gira su RULETA MÁGICA (RouletteController): el resultado se elige con los pesos de
    // Resources/CrupierRoulette y activa una de sus seis habilidades (CrupierAbilities.cs).
    //  Fase 2 (a la mitad de vida): más rápido, más cartas y menos descanso entre tiradas.
    public sealed class CrupierBoss : Boss
    {
        public readonly RouletteConfig RCfg;
        public readonly RouletteWheel Wheel;
        public readonly RouletteController Roulette;
        public readonly CasinoFloor Casino;
        public CrupierLook Look;
        public CrupierBody Figure;   // modelo con esqueleto (solo visual); null con el placeholder
        public float ShieldHp, ShieldMax, ShieldT;
        public CrupierIllusion Illusion;
        public CrupierPrize Prize;          // lluvia de monedas y ruleta del premio tras vencerle

        public CrupierBoss(Game g, float x, float z) : base(g, x, z, BossCfg(c =>
        {
            c.Name = "El Crupier"; c.Hp = 600; c.Speed = 2.3f; c.Radius = 0.8f; c.Mass = 7;
            c.BarY = 3.0f; c.Coins = new[] { 40, 55 }; c.HurtSound = "goblinHurt"; c.DieSound = "ogreDie";
        }))
        {
            PhaseThresholds = new[] { 0.5f };
            RCfg = RouletteConfig.Load();
            Casino = new CasinoFloor(g);
            Wheel = new RouletteWheel(g, RCfg, g.FloorRoot);
            Roulette = new RouletteController(RCfg, Wheel, () => G.Rand.Next());
            Roulette.Abilities.Connect(RouletteOutcome.Rojo, new HouseBurnsAbility(this));
            Roulette.Abilities.Connect(RouletteOutcome.Verde, new BankShieldAbility(this));
            Roulette.Abilities.Connect(RouletteOutcome.Negro, new HouseTaxAbility(this));
            Roulette.Abilities.Connect(RouletteOutcome.Dorado, new JackpotAbility(this));
            Roulette.Abilities.Connect(RouletteOutcome.Azul, new TableChangeAbility(this));
            Roulette.Abilities.Connect(RouletteOutcome.Morado, new DoubleOrNothingAbility(this));
            Roulette.StateChanged += s =>
            {
                if (s == RouletteState.Preparing) { Say("¡Hagan sus apuestas!"); G.Audio.Play("alert"); }
            };
            Roulette.Announced += o => { Say("¡No va más!"); G.Audio.Play("swap"); };
            Roulette.Revealed += OnRevealed;
            Roulette.AbilityStarted += (o, a) => G.Ui.FloatText(X, Cfg.BarY + 0.9f, Z, a.Title, TextStyle.Label);
            Roulette.Refused += () => Say("La banca está cerrada…");

            // 1) Ruleta: el Crupier se queda quieto mientras gira y mientras dure la parte "activa" de la habilidad
            AddPattern(new BossPattern
            {
                Id = "ruleta", Weight = 3, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => Roulette.IsIdle,
                Start = b => Roulette.TrySpin(),
                Update = (b, dt, pl, dist) =>
                {
                    if (pl != null) b.FaceTo(pl.X, pl.Z, 4f, dt);
                    return !Roulette.DealerBusy;
                },
            });

            // 2) Rastrillo: barrido en cono por delante (de cerca)
            AddPattern(new BossPattern
            {
                Id = "rastrillo", Weight = 2, Cooldown = 2.5f, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => pl != null && dist < 4.2f,
                Start = b =>
                {
                    var t = b.PickTarget(0f);
                    if (t != null) b.FaceTo(t.X, t.Z);
                    b.Telegraph(Shape.Cone(b.X, b.Z, b.Facing, 110f * Mathf.Deg2Rad, 3.6f), 0.6f, h =>
                    {
                        if (b.Dead) return;
                        BossHits.HitPlayersIn(G, h.Shape, 14, 12, b.X, b.Z);
                        G.Audio.Play("swing");
                    }, 0xffb040);
                },
                Update = (b, dt, pl, dist) => b.PatternT > 1.0f,
            });

            // 3) Abanico de cartas hacia su objetivo (reflejables)
            bool thrown = false;
            AddPattern(new BossPattern
            {
                Id = "cartas", Weight = 2, Cooldown = 3.2f, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => pl != null && dist < 16f,
                Start = b =>
                {
                    thrown = false;
                    var t = b.PickTarget(0f);
                    if (t != null) b.Telegraph(Shape.Line(b.X, b.Z, t.X, t.Z, 0.25f), 0.55f, null, 0xfff0d0);
                },
                Update = (b, dt, pl, dist) =>
                {
                    var t = b.Target ?? pl;
                    if (!thrown && t != null) b.FaceTo(t.X, t.Z, 8f, dt);
                    if (!thrown && b.PatternT >= 0.55f)
                    {
                        thrown = true;
                        if (t != null && !b.Dead) CardFan(this, t, b.Phase >= 2 ? 5 : 3, 9f, 24f, 0xf4f0e8);
                    }
                    return b.PatternT > 0.9f;
                },
            });
        }

        // Cartas en abanico (también las usa la ilusión, más flojas)
        public static void CardFan(Boss b, Player t, int n, float damage, float reflect, int color)
        {
            float a0 = Mathf.Atan2(t.X - b.X, t.Z - b.Z), spread = 12f * Mathf.Deg2Rad;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + (i - (n - 1) / 2f) * spread, sx = Mathf.Sin(a), sz = Mathf.Cos(a);
                b.Shoot(new Projectile
                {
                    PX = b.X + sx * 1.1f, PZ = b.Z + sz * 1.1f, Y = 1.3f, Vx = sx * 11f, Vz = sz * 11f,
                    Damage = damage, ProjRadius = 0.32f, Knock = 7, Life = 3f, Reflectable = true, ReflectDamage = reflect, Color = color,
                }, CardModel(b.G, color));
            }
            b.G.Audio.Play("throw");
        }

        static Mesh cardMesh;
        static Transform CardModel(Game g, int color)
        {
            if (!cardMesh)
            {
                var mb = new MeshBuilder();
                mb.Add(ProcMesh.Box(0.42f, 0.04f, 0.6f), Vector3.zero, Color.white);
                mb.Add(ProcMesh.Box(0.16f, 0.05f, 0.16f), Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 45f, 0), Vector3.one), MathUtil.Hex(0xc8202a));
                cardMesh = mb.ToMesh("carta");
            }
            var go = CharacterModel.MeshObject(g.FloorRoot, "Carta", cardMesh, Mats.Lit, Vector3.zero);
            if (color != 0xf4f0e8)
            {
                var r = go.GetComponent<Renderer>();
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(Mats.BaseColorId, MathUtil.Hex(color));
                r.SetPropertyBlock(mpb);
            }
            return go.transform;
        }

        void OnRevealed(RouletteOutcome o)
        {
            var seg = RCfg.Segments[Roulette.ResultIndex];
            var info = CrupierAbilityInfo.Of(o);
            G.Ui.Toast("🎡 " + info.Name + " — " + info.Title, 3.5f);
            G.Ui.FloatText(X, Cfg.BarY + 1.4f, Z, info.Name + "!", TextStyle.Big);
            var wp = Wheel.Root ? Wheel.Root.position : new Vector3(X, 4f, Z);
            G.Effects.Burst(wp.x, wp.y, wp.z, BurstOpts.Of(24, ColorHex(seg.Color), 6f, 4f, 0.16f));
            G.Audio.Play("chest");
            G.CameraCtl.AddShake(0.25f);
        }

        static int ColorHex(Color c)
        {
            return (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16) | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8) | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        }

        // ---------- Escudo (resultado verde) ----------
        public void RaiseShield(float hp, float seconds)
        {
            ShieldMax = ShieldHp = hp;
            ShieldT = seconds;
            if (Look != null) Look.ShowShield(true);
            G.Audio.Play("guard");
            Say("¡La banca se protege!");
        }

        public void DropShield(bool broken)
        {
            if (ShieldHp <= 0f && ShieldT <= 0f) return;
            ShieldHp = 0f; ShieldT = 0f;
            if (Look != null) Look.ShowShield(false);
            if (!broken || Dead) return;
            Stun = 1.6f;
            CancelPattern();
            G.Audio.Play("crash");
            G.CameraCtl.AddShake(0.4f);
            G.Effects.Burst(X, 1.4f, Z, BurstOpts.Of(18, 0x7dff9a, 5f, 4f, 0.14f));
            G.Ui.FloatText(X, Cfg.BarY + 0.6f, Z, "¡ESCUDO ROTO!", TextStyle.Big);
        }

        public bool Shielded { get { return ShieldHp > 0f; } }

        protected override Hit ModifyIncoming(Hit hit)
        {
            if (!Shielded) return hit;
            ShieldHp -= hit.Damage;
            hit.Blocked = true;
            hit.BlockText = "¡ESCUDO!";
            if (ShieldHp <= 0f) DropShield(true);
            return hit;
        }

        public override void Damage(float amount, bool env, string label)
        {
            if (env && Shielded) { ShieldHp -= amount; if (ShieldHp <= 0f) DropShield(true); return; }
            base.Damage(amount, env, label);
        }

        // ---------- Ciclo ----------
        protected override bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv)
        {
            if (IsAwake && !Dead) Roulette.Tick(dt);
            Casino.Tick(dt);
            Wheel.Update(dt, X, Z);
            if (ShieldT > 0f) { ShieldT -= dt; if (ShieldT <= 0f) DropShield(false); }
            return base.UpdateState(dt, player, dist, dx, dz, out mv);
        }

        protected override void OnPhase(int n)
        {
            if (n != 2) return;
            Cfg.Speed = 2.9f;
            Roulette.CooldownMult = RCfg.Phase2CooldownMult;
            Say("¡Subimos la apuesta!");
        }

        public override void OnAwake() { Say("Bienvenidos a mi mesa."); }

        // ---------- Modelo ----------
        protected override void BuildModel()
        {
            var cfg = RouletteConfig.Load();
            Figure = CrupierBody.Build(Model, cfg);
            bool asset = Figure != null || UseAnimatedModel(cfg.CrupierModel, cfg.CrupierHeight, 0, 0f);
            Look = new CrupierLook(Model, !asset);   // con modelo: solo la burbuja del escudo
        }

        protected override void Animate(float dt)
        {
            if (Look == null) return;
            float aL = Mathf.Sin(Anim * 2.2f) * 0.25f, aR = -aL;
            if (Is("ruleta"))
            {
                var s = Roulette.State;
                if (s == RouletteState.Preparing || s == RouletteState.Revealing) aL = aR = -2.6f;      // brazos en alto
                else if (s == RouletteState.Spinning) { aL = -2.6f; aR = -2.0f + Mathf.Sin(Anim * 14f) * 0.6f; }
                else aL = aR = -1.2f;
            }
            else if (Is("rastrillo")) aR = PatternT < 0.6f ? -1.6f * (PatternT / 0.6f) : -1.6f + 2.6f * Mathf.Clamp01((PatternT - 0.6f) / 0.2f);
            else if (Is("cartas")) aR = PatternT < 0.55f ? -2.2f : -0.4f;
            Look.Animate(dt, aL, aR, Shielded, ShieldHp / Mathf.Max(1f, ShieldMax));
            if (Figure != null) { Figure.Speed = MathUtil.Hypot(Vx, Vz); Figure.SpeedRef = Cfg.Speed; Figure.Acting = Current != null; }
        }

        public override void Die(string cause)
        {
            if (Dead) return;
            Roulette.Cancel(true);
            DropShield(false);
            Casino.ClearAll();
            base.Die(cause);
            if (G.BossFloor != null && G.BossFloor.Phase == "won") Prize = new CrupierPrize(G, Casino.Room.Center);
        }

        public override void Destroy()
        {
            if (Removed) return;
            Roulette.Cancel(true);
            Wheel.Destroy();
            Casino.Destroy();
            base.Destroy();
        }
    }

    // Nombre y rótulo de cada resultado (para el HUD y los avisos)
    public struct CrupierAbilityInfo
    {
        public string Name, Title;
        public static CrupierAbilityInfo Of(RouletteOutcome o)
        {
            switch (o)
            {
                case RouletteOutcome.Rojo: return new CrupierAbilityInfo { Name = "ROJO", Title = "La casa quema" };
                case RouletteOutcome.Verde: return new CrupierAbilityInfo { Name = "VERDE", Title = "La banca se protege" };
                case RouletteOutcome.Negro: return new CrupierAbilityInfo { Name = "NEGRO", Title = "Impuesto de la casa" };
                case RouletteOutcome.Dorado: return new CrupierAbilityInfo { Name = "DORADO", Title = "¡Jackpot!" };
                case RouletteOutcome.Azul: return new CrupierAbilityInfo { Name = "AZUL", Title = "Cambio de mesa" };
                default: return new CrupierAbilityInfo { Name = "MORADO", Title = "Doble o nada" };
            }
        }
    }

    // Placeholder del Crupier hecho por código (chaqué, chaleco rojo, pajarita, visera verde y rastrillo dorado).
    // Si se asigna RouletteConfig.CrupierModel, solo se añade la burbuja del escudo.
    public sealed class CrupierLook
    {
        readonly Transform armL, armR, shield;
        readonly Renderer shieldR;
        float t;

        public CrupierLook(Transform model, bool procedural)
        {
            Color suit = MathUtil.Hex(0x23222c), vest = MathUtil.Hex(0x8a1c24), shirt = MathUtil.Hex(0xf2eee4), skin = MathUtil.Hex(0xf0cfae);
            Color hair = MathUtil.Hex(0x15110e), visor = MathUtil.Hex(0x2f9a4a), gold = MathUtil.Hex(0xe8c050);
            if (procedural)
            {
                var b = new MeshBuilder();
                foreach (float s in new[] { -1f, 1f })
                {
                    b.Add(ProcMesh.Cylinder(0.15f, 0.13f, 0.85f, 7), new Vector3(s * 0.2f, 0.43f, 0), suit);
                    b.Add(ProcMesh.Box(0.22f, 0.1f, 0.34f), new Vector3(s * 0.2f, 0.05f, 0.06f), hair);
                }
                b.Add(ProcMesh.Cylinder(0.46f, 0.4f, 1.05f, 10), new Vector3(0, 1.35f, 0), suit);
                b.Add(ProcMesh.Box(0.5f, 0.5f, 0.42f), new Vector3(0, 0.95f, -0.12f), suit);                      // faldón del chaqué
                b.Add(ProcMesh.Box(0.5f, 0.7f, 0.08f), new Vector3(0, 1.4f, 0.4f), vest);
                b.Add(ProcMesh.Box(0.2f, 0.62f, 0.06f), new Vector3(0, 1.48f, 0.44f), shirt);
                b.Add(ProcMesh.Cone(0.09f, 0.18f, 4), Matrix4x4.TRS(new Vector3(-0.1f, 1.8f, 0.48f), Quaternion.Euler(0, 0, 90f), Vector3.one), vest);
                b.Add(ProcMesh.Cone(0.09f, 0.18f, 4), Matrix4x4.TRS(new Vector3(0.1f, 1.8f, 0.48f), Quaternion.Euler(0, 0, -90f), Vector3.one), vest);
                b.Add(ProcMesh.Sphere(0.36f, 10, 8), new Vector3(0, 2.2f, 0.05f), skin);
                b.Add(ProcMesh.Sphere(0.37f, 10, 6), Matrix4x4.TRS(new Vector3(0, 2.3f, -0.03f), Quaternion.identity, new Vector3(1f, 0.6f, 1f)), hair);
                b.Add(ProcMesh.Box(0.3f, 0.05f, 0.05f), new Vector3(0, 2.08f, 0.39f), hair);                     // bigote
                b.Add(ProcMesh.Cylinder(0.39f, 0.39f, 0.07f, 12), new Vector3(0, 2.45f, 0.02f), visor);
                b.Add(ProcMesh.Box(0.62f, 0.03f, 0.3f), Matrix4x4.TRS(new Vector3(0, 2.44f, 0.4f), Quaternion.Euler(-15f, 0, 0), Vector3.one), visor);
                CharacterModel.MeshObject(model, "Crupier", b.ToMesh("crupier"), Mats.Lit, Vector3.zero);
                var eyes = new MeshBuilder();
                foreach (float s in new[] { -1f, 1f }) eyes.Add(ProcMesh.Sphere(0.05f, 6, 4), new Vector3(s * 0.13f, 2.25f, 0.37f), MathUtil.Hex(0x111111));
                CharacterModel.MeshObject(model, "Ojos", eyes.ToMesh("ojosCrupier"), Mats.Unlit, Vector3.zero);
                armL = Arm(model, -0.58f, suit, shirt, null);
                armR = Arm(model, 0.58f, suit, shirt, gold);
            }
            shield = CharacterModel.MeshObject(model, "Escudo", new MeshBuilder().Add(ProcMesh.Sphere(1.45f, 16, 12), Vector3.zero, MathUtil.Hex(0x5dff8a)).ToMesh("escudoCrupier"), Mats.Additive, new Vector3(0, 1.3f, 0)).transform;
            shieldR = shield.GetComponent<Renderer>();
            shield.gameObject.SetActive(false);
        }

        static Transform Arm(Transform model, float x, Color sleeve, Color glove, Color? rake)
        {
            var g = new GameObject("Brazo").transform;
            g.SetParent(model, false);
            g.localPosition = new Vector3(x, 1.78f, 0);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.13f, 0.12f, 0.8f, 7), new Vector3(0, -0.4f, 0), sleeve);
            b.Add(ProcMesh.Sphere(0.14f, 7, 5), new Vector3(0, -0.85f, 0), glove);
            if (rake.HasValue)
            {
                // Rastrillo de crupier: vara larga con una pala al final
                b.Add(ProcMesh.Cylinder(0.035f, 0.035f, 1.6f, 5), Matrix4x4.TRS(new Vector3(0, -0.85f, 0.75f), Quaternion.Euler(90f, 0, 0), Vector3.one), rake.Value);
                b.Add(ProcMesh.Box(0.5f, 0.12f, 0.05f), new Vector3(0, -0.85f, 1.55f), rake.Value);
            }
            CharacterModel.MeshObject(g, "Manga", b.ToMesh("brazoCrupier"), Mats.Lit, Vector3.zero);
            return g;
        }

        public void ShowShield(bool on) { if (shield) shield.gameObject.SetActive(on); }

        public void Animate(float dt, float aL, float aR, bool shielded, float shieldK)
        {
            t += dt;
            if (armL) armL.localRotation = ProcMesh.EulerXYZ(aL, 0, 0);
            if (armR) armR.localRotation = ProcMesh.EulerXYZ(aR, 0, 0);
            if (shielded && shieldR)
            {
                shield.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t * 6f));
                Shape.SetOpacity(shieldR, 0.12f + 0.25f * Mathf.Clamp01(shieldK));
            }
        }
    }

    public static partial class BossRegistrations
    {
        static partial void Crupier()
        {
            Bosses.Register("crupier", new BossEntry
            {
                Name = "El Crupier", Title = "La banca siempre gana", Icon = "🎰",
                Hint = "Su ruleta decide la jugada. ROJO: las alfombras rojas queman (pisa la piedra). VERDE: escudo que se rompe a golpes. DORADO: fichas con premio para vosotros.",
                ArenaW = 21, ArenaH = 19, Fog = 0x0b0612,   // la sala del trono la monta CasinoRoom
                Spawn = (g, x, z) => new List<Boss> { new CrupierBoss(g, x, z) },
            });
        }
    }
}
