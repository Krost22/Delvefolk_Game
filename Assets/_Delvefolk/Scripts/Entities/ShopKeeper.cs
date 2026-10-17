using UnityEngine;

namespace Delvefolk
{
    // DUENDE TENDERO (ShopKeeper.js): lleva la tienda que aparece tras los pisos bonus. Mientras nadie le toque es un
    // personaje pacífico (no ataca, la asistencia de puntería le ignora). Si alguien le golpea (arma, pisotón,
    // habilidad, barril, compañero lanzado...), se enfada y se convierte en un JEFE con escopeta:
    //  - Guarda la distancia y apunta con un láser rojo antes de disparar (saltar en el último momento esquiva).
    //  - Dispara un cono de perdigones; tras 2 disparos (3 en la segunda fase) tiene que recargar: es tu momento.
    //  - Rueda para alejarse si te le echas encima. Por debajo de la mitad de vida apunta más rápido y abre más el cono.
    // Derrotado, suelta su Escopeta (no se rompe) y un buen montón de monedas.
    public static class ShopKeeperConfig
    {
        public const float BossHp = 320, HpPerFloor = 0.15f;   // vida del jefe (+15 % por piso)
        public const float Speed = 3.6f, KeepMin = 4, KeepMax = 7, Range = 9;
        public const float AimTime = 0.8f, AimTime2 = 0.5f;   // tiempo apuntando (fase 1 / fase 2)
        public const float Arc = 30, Arc2 = 44;               // grados del cono de perdigones
        public const float Damage = 16, Damage2 = 20, Knock = 14;
        public const int Shots = 2, Shots2 = 3;
        public const float Reload = 1.5f, Reload2 = 1.0f;
        public const float RollTrigger = 2.2f, RollCd = 3.0f, RollSpeed = 11;
        public static readonly string[] Lines =
        {
            "¡Mira qué género!", "Todo de primera mano... o de segunda.", "Ni se te ocurra tocarme.", "¿Vas a comprar o a mirar?",
            "Aquí no se fía, ¿eh?", "Esta espada es de verdad, no como tu escoba.",
        };
    }

    public enum KeeperMode { Move, Aim, Reload, Roll }

    public sealed class ShopKeeper : Enemy
    {
        public readonly ShopFloor Shop;
        public KeeperMode Mode = KeeperMode.Move;
        public float ModeT, AimCd = 1.2f, RollCdLeft;
        public int ShotsFired;
        public bool Phase2;
        float chatT;
        Vector2 rollDir;
        Transform gunArm, body3d;
        Renderer eyes;
        Transform laser;
        Renderer laserR;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public ShopKeeper(Game g, float x, float z, ShopFloor shop) : base(g, x, z, MakeCfg())
        {
            Shop = shop;
            Peaceful = true;
            NoSwap = true;
            SetState(EnemyState.Npc);
            chatT = 3f + g.FxRand.Next() * 3f;
            Facing = Mathf.PI - Mathf.PI / 4f;   // π/4 del web con el eje z invertido
            BuildLaser();
        }

        static EnemyCfg MakeCfg()
        {
            return new EnemyCfg
            {
                Name = "Duende Tendero", Hp = 9999, Speed = ShopKeeperConfig.Speed, Radius = 0.5f, Mass = 6,
                Damage = 0, AttackRange = 1, Windup = 0.5f, AttackTime = 0.3f, Recover = 0.5f, AttackCooldown = 1,
                HurtSound = "goblinHurt", DieSound = "ogreDie", BarY = 1.95f, HatY = 1.6f, HatScale = 1, StunMult = 0.35f,
                KnockDrag = 6, Color = 0x6aa040, Coins = new[] { 25, 30 },
            };
        }

        protected override void BuildModel()
        {
            var skin = MathUtil.Hex(0x6aa040); var dark = MathUtil.Hex(0x3a2a1a);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.3f, 0.42f, 0.75f, 8), new Vector3(0, 0.6f, 0), MathUtil.Hex(0x5a3a5a));
            b.AddDouble(ProcMesh.Cylinder(0.33f, 0.44f, 0.55f, 8, 1, true, -Mathf.PI * 0.4f, Mathf.PI * 0.8f), Matrix4x4.Translate(new Vector3(0, 0.5f, 0)), MathUtil.Hex(0xd8c8a0));
            b.Add(ProcMesh.Box(0.18f, 0.12f, 0.04f), new Vector3(0.05f, 0.45f, 0.42f), MathUtil.Hex(0xb0a080));
            b.Add(ProcMesh.Icosahedron(0.33f, 1), Matrix4x4.TRS(new Vector3(0, 1.25f, 0), Quaternion.identity, new Vector3(1.05f, 0.95f, 1f)), skin);
            b.Add(ProcMesh.Cone(0.08f, 0.35f, 5), Matrix4x4.TRS(new Vector3(0, 1.2f, 0.42f), ProcMesh.EulerXYZ(Mathf.PI / 2, 0, 0), Vector3.one), skin);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Add(ProcMesh.Cone(0.1f, 0.45f, 4), Matrix4x4.TRS(new Vector3(s * 0.38f, 1.32f, 0), ProcMesh.EulerXYZ(0, 0, -s * 1.2f), Vector3.one), skin);
                b.Add(ProcMesh.Box(0.16f, 0.12f, 0.28f), new Vector3(s * 0.16f, 0.06f, 0.04f), dark);
            }
            b.Add(ProcMesh.Cylinder(0.17f, 0.21f, 0.24f, 8), Matrix4x4.TRS(new Vector3(0.03f, 1.6f, -0.02f), ProcMesh.EulerXYZ(0, 0, 0.12f), Vector3.one), MathUtil.Hex(0xb3261e));
            b.Add(ProcMesh.Cone(0.04f, 0.2f, 4), Matrix4x4.TRS(new Vector3(0.16f, 1.62f, 0), ProcMesh.EulerXYZ(0, 0, -0.8f), Vector3.one), dark);
            b.Add(ProcMesh.Box(0.32f, 0.05f, 0.05f), new Vector3(0, 1.12f, 0.36f), dark);
            body3d = new GameObject("Cuerpo").transform;
            body3d.SetParent(Model, false);
            CharacterModel.MeshObject(body3d, "Duende", b.ToMesh("duende"), Mats.Lit, Vector3.zero);
            var eb = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f }) eb.Add(ProcMesh.Sphere(0.055f, 5, 4), new Vector3(s * 0.12f, 1.33f, 0.3f), Color.white);
            eyes = CharacterModel.MeshObject(body3d, "Ojos", eb.ToMesh("ojosDuende"), Mats.Unlit, Vector3.zero).GetComponent<Renderer>();
            SetEyes(0xffe14a);
            // Escopeta en un brazo pivotante (de reposo: al hombro; apuntando: al frente)
            gunArm = new GameObject("BrazoEscopeta").transform;
            gunArm.SetParent(body3d, false);
            gunArm.localPosition = new Vector3(-0.32f, 0.9f, 0.05f);
            var gun = new ShotgunWeapon().BuildModel(gunArm);
            gun.transform.localScale = Vector3.one * 0.85f;
        }

        void SetEyes(int hex)
        {
            eyes.GetPropertyBlock(mpb);
            mpb.SetColor(Mats.BaseColorId, MathUtil.Hex(hex));
            eyes.SetPropertyBlock(mpb);
        }

        void BuildLaser()
        {
            var m = new MeshBuilder().Add(ProcMesh.Box(0.05f, 0.05f, 1f), Vector3.zero, Color.white).ToMesh("laser");
            var go = CharacterModel.MeshObject(G.FloorRoot, "LaserDuende", m, Mats.Transparent, Vector3.zero);
            laser = go.transform;
            laserR = go.GetComponent<Renderer>();
            go.SetActive(false);
        }

        public bool LaserVisible { get { return laser && laser.gameObject.activeSelf; } }
        void ShowLaser(bool on) { if (laser && laser.gameObject.activeSelf != on) laser.gameObject.SetActive(on); }

        public override float StompTop() { return 1.2f; }

        public override void TakeHit(Hit hit)
        {
            if (Peaceful) Enrage();
            base.TakeHit(hit);
        }

        public override void Damage(float amount, bool env, string label)
        {
            if (Peaceful && amount > 0f) Enrage();
            base.Damage(amount, env, label);
        }

        public void Enrage()
        {
            if (!Peaceful) return;
            Peaceful = false;
            SetState(EnemyState.Custom);
            Mode = KeeperMode.Move; ModeT = 0f; AimCd = 1.2f; ShotsFired = 0;
            MaxHp = Hp = MathUtil.RoundJs(ShopKeeperConfig.BossHp * (1f + ShopKeeperConfig.HpPerFloor * Mathf.Max(0, G.Floor - 1)));
            Body.Mass = 2.2f; Body.InvMass = 1f / 2.2f;
            SetEyes(0xff3020);
            G.Audio.Play("bossRoar");
            G.CameraCtl.AddShake(0.6f);
            G.Ui.FloatText(X, 2.6f, Z, "¡¿CÓMO TE ATREVES?!", TextStyle.Big);
            G.Ui.Banner("¡EL DUENDE SE HA ENFADADO!", "Derrótalo y su escopeta será tuya");
            G.Ui.SetBoss("😡 Duende Tendero", 1f);
            if (Shop != null) Shop.OnKeeperAngry();
        }

        public void Say(string text) { G.Ui.FloatText(X, 2.3f, Z, text, TextStyle.Small); }

        Vector2 Muzzle() { return new Vector2(X + Mathf.Sin(Facing) * 0.85f, Z + Mathf.Cos(Facing) * 0.85f); }

        // Estados propios: tranquilo (Npc) y jefe (Custom, con sus modos move / aim / reload / roll)
        protected override bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv)
        {
            mv = Vector2.zero;
            if (Peaceful)
            {
                // Mira al cliente más cercano y suelta alguna frase
                Player near = null; float nd = 7f;
                foreach (var p in G.Players) { if (p.Dead) continue; float d = MathUtil.Hypot(p.X - X, p.Z - Z); if (d < nd) { nd = d; near = p; } }
                if (near != null) Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(near.X - X, near.Z - Z), 4f, dt);
                chatT -= dt;
                if (chatT <= 0f && near != null)
                {
                    chatT = 6f + G.FxRand.Next() * 5f;
                    Say(ShopKeeperConfig.Lines[Mathf.FloorToInt(G.FxRand.Next() * ShopKeeperConfig.Lines.Length)]);
                }
                return true;
            }
            if (State != EnemyState.Custom) return false;
            if (!Controllable) { ShowLaser(false); if (Mode == KeeperMode.Aim) Mode = KeeperMode.Move; return false; }
            G.Ui.SetBoss(Phase2 ? "😡 Duende Tendero (¡furioso!)" : "😡 Duende Tendero", Hp / MaxHp);
            if (!Phase2 && Hp < MaxHp * 0.5f)
            {
                Phase2 = true;
                Say("¡AHORA SÍ QUE ME HABÉIS ENFADADO!");
                G.Audio.Play("bossRoar");
            }
            ModeT += dt;
            RollCdLeft = Mathf.Max(0f, RollCdLeft - dt);
            AimCd = Mathf.Max(0f, AimCd - dt);
            if (Mode == KeeperMode.Roll)
            {
                if (ModeT >= 0.35f) Mode = KeeperMode.Move;
                mv = rollDir * ShopKeeperConfig.RollSpeed;
                return true;
            }
            if (Mode == KeeperMode.Reload)
            {
                if (ModeT >= (Phase2 ? ShopKeeperConfig.Reload2 : ShopKeeperConfig.Reload)) { Mode = KeeperMode.Move; ModeT = 0f; ShotsFired = 0; AimCd = 0.3f; }
                return true;
            }
            if (player == null) { ShowLaser(false); return true; }
            float l0 = dist > 0f ? dist : 1f;
            float tx = dx / l0, tz = dz / l0;
            if (Mode == KeeperMode.Aim)
            {
                float aimTime = Phase2 ? ShopKeeperConfig.AimTime2 : ShopKeeperConfig.AimTime;
                if (ModeT < aimTime - 0.22f) Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(dx, dz), 8f, dt);
                UpdateLaser(aimTime);
                if (ModeT >= aimTime)
                {
                    Fire();
                    ShowLaser(false);
                    ShotsFired++;
                    ModeT = 0f;
                    if (ShotsFired >= (Phase2 ? ShopKeeperConfig.Shots2 : ShopKeeperConfig.Shots)) { Mode = KeeperMode.Reload; Say("*clic, clac*"); G.Audio.Play("reload"); }
                    else { Mode = KeeperMode.Move; AimCd = Phase2 ? 0.35f : 0.6f; }
                }
                return true;
            }
            // Moverse: guardar la distancia, rodar si se le echan encima, apuntar cuando pueda
            Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(dx, dz), 6f, dt);
            if (dist < ShopKeeperConfig.RollTrigger && RollCdLeft <= 0f)
            {
                float side = G.Rand.Next() < 0.5f ? 1f : -1f;
                float rx = -tx * 0.8f + -tz * side * 0.6f, rz = -tz * 0.8f + tx * side * 0.6f, rl = MathUtil.Hypot(rx, rz);
                if (rl == 0f) rl = 1f;
                rollDir = AvoidHazards(new Vector2(rx / rl, rz / rl));
                Mode = KeeperMode.Roll; ModeT = 0f; RollCdLeft = ShopKeeperConfig.RollCd;
                G.Audio.Play("swing", 0.5f);
                mv = rollDir * ShopKeeperConfig.RollSpeed;
                return true;
            }
            if (AimCd <= 0f && dist < ShopKeeperConfig.Range + 1f && G.Dungeon.LineClear(X, Z, player.X, player.Z))
            {
                Mode = KeeperMode.Aim; ModeT = 0f;
                G.Audio.Play("reload");
                return true;
            }
            float mx = 0f, mz = 0f;
            if (dist > ShopKeeperConfig.KeepMax) { mx = tx; mz = tz; }
            else if (dist < ShopKeeperConfig.KeepMin) { mx = -tx; mz = -tz; }
            float strafe = Mathf.Sin(Anim * 0.9f) > 0f ? 1f : -1f;
            mx += -tz * strafe * 0.6f; mz += tx * strafe * 0.6f;
            var dir = AvoidHazards(new Vector2(mx, mz));
            float l = MathUtil.Hypot(dir.x, dir.y); if (l == 0f) l = 1f;
            mv = new Vector2(dir.x / l * ShopKeeperConfig.Speed, dir.y / l * ShopKeeperConfig.Speed);
            return true;
        }

        // Láser de apuntado: hasta el primer muro; parpadea justo antes del disparo
        void UpdateLaser(float aimTime)
        {
            var m = Muzzle(); float f = Facing;
            float len = 0.3f;
            while (len < ShopKeeperConfig.Range)
            {
                float px = m.x + Mathf.Sin(f) * len, pz = m.y + Mathf.Cos(f) * len;
                if (G.Dungeon.IsWallLike(Grid.TileX(px), Grid.TileZ(pz))) break;
                len += 0.3f;
            }
            ShowLaser(ModeT < aimTime - 0.22f || Mathf.FloorToInt(ModeT * 30f) % 2 == 0);
            laser.localScale = new Vector3(1f, 1f, len);
            laser.position = new Vector3(m.x + Mathf.Sin(f) * len / 2f, 1.0f, m.y + Mathf.Cos(f) * len / 2f);
            laser.rotation = Quaternion.Euler(0, f * Mathf.Rad2Deg, 0);
            laserR.GetPropertyBlock(mpb);
            mpb.SetColor(Mats.BaseColorId, ModeT >= aimTime - 0.22f ? new Color(1, 1, 1, 0.7f) : new Color(1f, 0.125f, 0.125f, 0.7f));
            laserR.SetPropertyBlock(mpb);
        }

        bool InCone(float x, float z, float r, float f, float arc)
        {
            float ddx = x - X, ddz = z - Z, d = MathUtil.Hypot(ddx, ddz);
            if (d > ShopKeeperConfig.Range + r) return false;
            float ang = Mathf.Abs(MathUtil.AngleDiff(f, Mathf.Atan2(ddx, ddz)));
            return ang <= arc / 2f + Mathf.Atan2(r, Mathf.Max(d, 0.3f)) && G.Dungeon.LineClear(X, Z, x, z);
        }

        // Disparo: cono de perdigones. Saltar por encima lo esquiva; los muros lo bloquean.
        public void Fire()
        {
            var m = Muzzle(); float f = Facing;
            float arc = (Phase2 ? ShopKeeperConfig.Arc2 : ShopKeeperConfig.Arc) * Mathf.Deg2Rad;
            ShotgunFX.Blast(G, m.x, m.y, f, arc, ShopKeeperConfig.Range, 0xff9060);
            Knock(-Mathf.Sin(f) * 4f, -Mathf.Cos(f) * 4f);   // retroceso
            foreach (var p in G.Players.ToArray())
            {
                if (!p.Targetable || p.IsThrown || p.JumpY > JumpConfig.DodgeHeight || !InCone(p.X, p.Z, p.Radius, f, arc)) continue;
                float d = MathUtil.Hypot(p.X - X, p.Z - Z), k = 1f - Mathf.Min(1f, d / ShopKeeperConfig.Range) * 0.45f;
                if (p.Hurt(MathUtil.RoundJs((Phase2 ? ShopKeeperConfig.Damage2 : ShopKeeperConfig.Damage) * k), X, Z, ShopKeeperConfig.Knock * k))
                    G.Ui.FloatText(p.X, 2.6f, p.Z, "¡PUM!", TextStyle.Label);
            }
            foreach (var b in G.Barrels.ToArray())
                if (!b.Dead && !b.Falling && InCone(b.X, b.Z, b.Radius, f, arc)) b.TakeHit(Hit.Of(20, 10, 0, Mathf.Sin(f), Mathf.Cos(f)));
            foreach (var b in G.Breakables.ToArray())
                if (!b.Dead && InCone(b.X, b.Z, b.Radius, f, arc)) b.TakeHit(Hit.Of(20, 0, 0, 0, 0));
        }

        public override void Die(string cause)
        {
            ShowLaser(false);
            bool was = Dead;
            base.Die(cause);
            if (was) return;
            G.Ui.HideBoss();
            if (Shop != null) Shop.OnKeeperDefeated(this);
        }

        public override void Destroy()
        {
            if (laser) { Object.Destroy(laser.gameObject); laser = null; }
            base.Destroy();
        }

        protected override void Animate(float dt)
        {
            float t = Anim;
            bool moving = MathUtil.Hypot(Vx, Vz) > 0.4f;
            float gx = -1.9f, gy = 0.4f, bob = Mathf.Sin(t * 2f) * 0.03f;   // reposo: escopeta al hombro
            if (Peaceful) gx = -2.2f + Mathf.Sin(t * 1.5f) * 0.1f;
            else if (Mode == KeeperMode.Aim) { gx = 0f; gy = 0.05f; }
            else if (Mode == KeeperMode.Reload) { gx = 0.7f + Mathf.Sin(t * 14f) * 0.15f; gy = -0.3f; }
            else if (moving) { bob = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.08f; gx = -0.3f; gy = 0.1f; }
            armX = MathUtil.Damp(armX, gx, 14f, dt);
            armY = MathUtil.Damp(armY, gy, 14f, dt);
            gunArm.localRotation = ProcMesh.EulerXYZ(armX, armY, 0f);
            float q = SquashT = Mathf.Max(0f, SquashT - dt * 4f);
            body3d.localPosition = new Vector3(0, bob, 0);
            body3d.localScale = new Vector3(1f + q * 0.2f, 1f - q * 0.2f, 1f + q * 0.2f);
            ModelRotX = Mode == KeeperMode.Roll && !Peaceful ? ModeT / 0.35f * Mathf.PI * 2f : 0f;
            // Echando humo de rabia
            if (!Peaceful && G.FxRand.Next() < dt * 4f)
                G.Effects.Burst(X, 1.8f, Z, BurstOpts.Of(1, 0xdddddd, 0.5f, 2f, 0.18f).WithGravity(-1f).WithLife(0.8f));
        }

        float armX, armY;
    }
}
