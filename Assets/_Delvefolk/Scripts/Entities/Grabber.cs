using UnityEngine;

namespace Delvefolk
{
    // ABRAZÓN: un bruto lento y pesado que, en vez de pegar, te agarra (Grabber.js).
    //  - Avisa levantando los brazos (brillo morado) y se abalanza. Saltar a tiempo esquiva el agarre.
    //  - Con alguien en brazos lo estruja (daño periódico), camina despacio hacia el otro jugador y,
    //    si nadie lo impide, acaba lanzándolo por los aires (el lanzado golpea a lo que pille).
    //  - Para soltarse: machacar atacar / interactuar / saltar. Los guantes de fuerza ayudan.
    //  - Para rescatar: pegarle fuerte, aturdirlo, saltarle encima o empujarlo lejos.
    //  - "Último recurso" activo: no consigue agarrarte.
    // Modelo: el árbol gigante del paquete de monstruos (sus brazos largos dan el abrazo).
    public sealed class Grabber : Enemy
    {
        public Player Held;
        float squeezeT;
        Transform body3d;
        Vector3 bodyScale = Vector3.one;
        Transform[] arms, legs;
        readonly float[] armRotX = new float[2], armRotZ = new float[2];

        public Grabber(Game g, float x, float z, EnemyOpts opts) : base(g, x, z, MakeCfg(opts)) { }

        static EnemyCfg MakeCfg(EnemyOpts opts)
        {
            var c = new EnemyCfg
            {
                Name = "Abrazón", Hp = 80, Speed = 2.4f, Radius = 0.72f, Mass = 3.0f,
                Damage = 0, AttackRange = 1.15f, Windup = 0.75f, AttackTime = 0.4f, Recover = 1.0f, AttackCooldown = 2.4f,
                HurtSound = "ogreHurt", DieSound = "ogreDie", BarY = 2.35f, HatY = 2.05f, HatScale = 1.3f, StunMult = 0.6f,
                KnockDrag = 6.5f, Color = 0x8a7aa8, WindupTrack = 3, Coins = new[] { 5, 7 },
                GrabSpeed = 7, HoldTime = 3.5f, SqueezeEvery = 1.0f, SqueezeDamage = 6, ThrowForce = 20, ThrowDamage = 6,
                EscapePresses = 8, EscapeDecay = 1.5f, ReleaseDamage = 8, HoldMoveSpeed = 1.1f, EscapeStun = 1.3f,
            };
            if (opts != null && opts.Cfg != null) opts.Cfg(c);
            return c;
        }

        protected override void BuildModel()
        {
            var c = Cfg;
            var art = Art.Catalog;
            if (art != null && UseAnimatedModel(art.GrabberModel, 2.5f, c.Color, 0.45f))
            {
                body3d = Model.GetChild(Model.childCount - 1);
                bodyScale = body3d.localScale;
                Animator.Move = "Walk Forward In Place";
                Animator.Windup = "Cast Spell";
                Animator.Attack = "Clap Attack";
                Animator.Hold = "Clap Attack";
                Animator.Stunned = "Take Damage";
                Animator.MoveSpeedRef = 2.4f;
                return;
            }
            Color skin = MathUtil.Hex(c.Color), belly = MathUtil.Hex(0xb8a8c8), dark = MathUtil.Hex(0x3a2a4a), cloth = MathUtil.Hex(0x6a4426);
            body3d = new GameObject("Cuerpo").transform;
            body3d.SetParent(Model, false);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Icosahedron(0.75f, 1), Matrix4x4.TRS(new Vector3(0, 1.25f, 0), Quaternion.identity, new Vector3(1.15f, 1.0f, 0.9f)), skin);
            b.Add(ProcMesh.Icosahedron(0.5f, 1), Matrix4x4.TRS(new Vector3(0, 1.1f, 0.42f), Quaternion.identity, new Vector3(1, 0.9f, 0.6f)), belly);
            b.Add(ProcMesh.Icosahedron(0.34f, 1), new Vector3(0, 2.05f, 0.22f), skin);
            b.Add(ProcMesh.Box(0.5f, 0.1f, 0.15f), Matrix4x4.TRS(new Vector3(0, 2.15f, 0.48f), ProcMesh.EulerXYZ(0.3f, 0, 0), Vector3.one), dark);
            foreach (var s in new[] { -1f, 1f }) b.Add(ProcMesh.Sphere(0.06f, 5, 4), new Vector3(s * 0.13f, 2.07f, 0.52f), MathUtil.Hex(0xffe14a));
            b.Add(ProcMesh.Cone(0.04f, 0.12f, 4), Matrix4x4.TRS(new Vector3(0.1f, 1.86f, 0.5f), ProcMesh.EulerXYZ(Mathf.PI, 0, 0), Vector3.one), MathUtil.Hex(0xf0e6c8));
            b.Add(ProcMesh.Cylinder(0.62f, 0.7f, 0.35f, 8), new Vector3(0, 0.62f, 0), cloth);
            CharacterModel.MeshObject(body3d, "Torso", b.ToMesh("abrazon"), Mats.Lit, Vector3.zero);
            var legMesh = new MeshBuilder().Add(ProcMesh.Cylinder(0.17f, 0.2f, 0.55f, 6), Vector3.zero, skin).ToMesh("piernaAbrazon");
            legs = new Transform[2];
            arms = new Transform[2];
            var armB = new MeshBuilder();
            armB.Add(ProcMesh.Cylinder(0.13f, 0.17f, 1.05f, 6), new Vector3(0, -0.52f, 0), skin);
            armB.Add(ProcMesh.Icosahedron(0.24f), new Vector3(0, -1.08f, 0), skin);
            var armMesh = armB.ToMesh("brazoAbrazon");
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                legs[i] = CharacterModel.MeshObject(body3d, "Pierna", legMesh, Mats.Lit, new Vector3(s * 0.32f, 0.28f, 0)).transform;
                var pivot = new GameObject("Brazo").transform;
                pivot.SetParent(body3d, false);
                pivot.localPosition = new Vector3(s * 0.82f, 1.6f, 0.05f);
                CharacterModel.MeshObject(pivot, "Malla", armMesh, Mats.Lit, Vector3.zero);
                arms[i] = pivot;
            }
        }

        // Donde queda sujeto el jugador: delante, a la altura del pecho (nunca dentro de un muro)
        public Vector3 HoldPos()
        {
            float f = Facing;
            float x = X + Mathf.Sin(f) * 0.95f, z = Z + Mathf.Cos(f) * 0.95f;
            if (G.Dungeon.IsWallLike(Grid.TileX(x), Grid.TileZ(z))) { x = X; z = Z; }
            return new Vector3(x, 1.05f, z);
        }

        protected override void OnWindupStart() { G.Audio.Play("ogreGrowl"); }
        protected override void OnAttackStart() { G.Audio.Play("swing", 0.6f); }

        // Se abalanza con los brazos abiertos y agarra al primer jugador que tenga delante
        protected override bool UpdateAttack(float dt, float dist, out Vector2 mv)
        {
            mv = Vector2.zero;
            foreach (var p in G.Players)
            {
                if (!p.Targetable || p.IsThrown || p.JumpY > JumpConfig.DodgeHeight) continue;
                float dx = p.X - X, dz = p.Z - Z, d = MathUtil.Hypot(dx, dz);
                if (d > Cfg.AttackRange + Radius + p.Radius + 0.25f) continue;
                if (Mathf.Abs(MathUtil.AngleDiff(Facing, Mathf.Atan2(dx, dz))) > 1.1f) continue;
                if (p.Ability.Unstoppable)
                {
                    // Último recurso: rebota contra el caparazón
                    SetState(EnemyState.Recover);
                    StunEnemy(0.8f);
                    G.Ui.FloatText(p.X, 2.6f, p.Z, "¡No puedes conmigo!", TextStyle.Small);
                    G.Audio.Play("clang");
                    return true;
                }
                Grab(p);
                return true;
            }
            if (StateT < 0.28f) { mv = new Vector2(Mathf.Sin(Facing) * Cfg.GrabSpeed, Mathf.Cos(Facing) * Cfg.GrabSpeed); return true; }
            return false;
        }

        // Aturdimiento de habilidad con tope (stunEnemy de Abilities.js)
        public void StunEnemy(float t) { Stun = Mathf.Min(AbilityRules.EnemyStunCap, Mathf.Max(Stun, t * Cfg.StunMult)); }

        void Grab(Player p)
        {
            Held = p;
            squeezeT = 0.7f;
            p.OnGrabbedBy(this);
            SetState(EnemyState.Hold);
            G.Audio.Play("grab");
            G.CameraCtl.AddShake(0.3f);
            G.Ui.FloatText(X, 3.0f, Z, "¡TE PILLÉ!", TextStyle.Big);
            if (!G.SeenHints.ContainsKey("grabber"))
            {
                G.SeenHints["grabber"] = true;
                Player mate = null;
                foreach (var q in G.Players) if (q != p) { mate = q; break; }
                G.Ui.Toast("¡Un Abrazón ha atrapado a " + p.Look.Name + "! Machaca botones para soltarte" +
                    (mate != null ? "... o que " + mate.Look.Name + " le pegue o le salte encima." : "."), 5.5f);
            }
        }

        // Estado propio 'hold' (antes que los demás estados)
        protected override bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv)
        {
            mv = Vector2.zero;
            if (State != EnemyState.Hold) return false;
            var p = Held;
            if (p == null || p.Dead || p.Removed || p.GrabbedBy != this) { ReleaseHeld("lost"); return true; }
            // Aturdido o empujado con fuerza: se le escapa de las manos
            if (!Controllable) { ReleaseHeld("stun"); return false; }
            squeezeT -= dt;
            if (squeezeT <= 0f)
            {
                squeezeT = Cfg.SqueezeEvery;
                p.Iframes = 0f;
                if (p.Hurt(Cfg.SqueezeDamage, X, Z, 0f, new HurtOpts { Pierce = true }))
                {
                    G.Audio.Play("squeeze");
                    G.Ui.FloatText(p.X, 3.2f, p.Z, "¡Ñññ!", TextStyle.Small);
                }
                if (Held != p) return true;   // le ha derribado estrujándolo
            }
            if (StateT >= Cfg.HoldTime) { ThrowHeld(); return true; }
            // Camina despacio hacia el otro jugador (amenazante), o se queda quieto
            if (player != null)
            {
                var dir = AvoidHazards(ChaseDirection(dist, dx, dz, dt));
                if (dir.x != 0f || dir.y != 0f) Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(dir.x, dir.y), 3f, dt);
                mv = new Vector2(dir.x * Cfg.HoldMoveSpeed, dir.y * Cfg.HoldMoveSpeed);
            }
            return true;
        }

        // Suelta al jugador. why: 'escape' (forcejeo), 'hit', 'stun', 'dead', 'lost', 'throw'
        public void ReleaseHeld(string why)
        {
            var p = Held;
            if (p == null) return;
            Held = null;
            float f = Facing;
            var h = HoldPos();
            if (p.GrabbedBy == this)
            {
                p.Body.X = h.x; p.Body.Z = h.z;
                p.Body.Ignore = Body; p.Body.IgnoreT = 0.4f;
                p.FreeFromGrab();
                p.SyncRoot();
            }
            if (why == "escape")
            {
                p.Knock(Mathf.Sin(f) * 8f, Mathf.Cos(f) * 8f);
                p.PopT = 0.5f;
                StunEnemy(Cfg.EscapeStun);
                G.Audio.Play("breakFree");
                G.Ui.FloatText(X, 2.9f, Z, "¡Se le escapa!", TextStyle.Label);
            }
            else if (why == "hit" || why == "stun")
            {
                p.Knock(Mathf.Sin(f) * 5f, Mathf.Cos(f) * 5f);
                G.Ui.FloatText(p.X, 2.6f, p.Z, "¡Libre!", TextStyle.Heal);
            }
            if (State == EnemyState.Hold && !Dead) SetState(EnemyState.Recover);
        }

        // Se cansó de estrujar: lo lanza por los aires (y el lanzado golpea a lo que pille)
        void ThrowHeld()
        {
            var p = Held;
            float f = Facing;
            ReleaseHeld("throw");
            if (p == null || p.Dead) return;
            p.Hurt(Cfg.ThrowDamage, X, Z, 0f, new HurtOpts { Pierce = true });
            if (p.Dead) return;
            p.Iframes = 0f;
            p.Body.Kx = Mathf.Sin(f) * Cfg.ThrowForce;
            p.Body.Kz = Mathf.Cos(f) * Cfg.ThrowForce;
            p.StartThrown(null);
            G.Audio.Play("throw");
            G.Ui.FloatText(X, 3.0f, Z, "¡FUERAAA!", TextStyle.Big);
        }

        // Cualquier golpe directo de un jugador le hace soltar a su presa
        public override void TakeHit(Hit hit)
        {
            base.TakeHit(hit);
            if (Held != null && !Dead) ReleaseHeld("hit");
        }

        // El daño del entorno (explosiones, púas, barrilazos) solo si es fuerte
        public override void Damage(float amount, bool env, string label)
        {
            base.Damage(amount, env, label);
            if (Held != null && !Dead && Mathf.Round(amount) >= Cfg.ReleaseDamage) ReleaseHeld("hit");
        }

        public override void Die(string cause)
        {
            if (Held != null) ReleaseHeld("dead");
            base.Die(cause);
        }

        public override void Destroy()
        {
            if (Held != null) ReleaseHeld("lost");
            base.Destroy();
        }

        protected override void Animate(float dt)
        {
            float t = Anim;
            bool moving = MathUtil.Hypot(Vx, Vz) > 0.3f;
            float armX = 0.15f, armZ = 0.12f, sy = 1f;
            if (State == EnemyState.Windup)
            {
                float k = Mathf.Min(1f, StateT / Cfg.Windup);
                armX = -2.5f * k; armZ = 0.7f * k;
                sy = 1f + 0.08f * k;
                SetGlow(0.35f * k, 0.1f * k, 0.45f * k);
            }
            else if (State == EnemyState.Attack) { armX = -1.5f; armZ = 0.15f; }
            else if (State == EnemyState.Hold)
            {
                armX = -1.25f + Mathf.Sin(t * 10f) * 0.08f; armZ = -0.35f;   // abrazo apretado
                sy = 1f + Mathf.Sin(t * 10f) * 0.03f;
            }
            else if (Stun > 0.3f) { armX = 0.4f; armZ = 0.5f + Mathf.Sin(t * 6f) * 0.2f; }
            else if (moving) armX = 0.15f + Mathf.Sin(t * 6f) * 0.3f;
            if (State != EnemyState.Windup) SetGlow(0, 0, 0);
            if (arms != null)
                for (int i = 0; i < 2; i++)
                {
                    float s = i == 1 ? 1f : -1f;
                    armRotX[i] = MathUtil.Damp(armRotX[i], armX + (moving && State == EnemyState.Chase ? s * Mathf.Sin(t * 6f) * 0.25f : 0f), 12f, dt);
                    armRotZ[i] = MathUtil.Damp(armRotZ[i], -s * armZ, 12f, dt);
                    arms[i].localRotation = ProcMesh.EulerXYZ(armRotX[i], 0, armRotZ[i]);
                }
            if (legs != null)
                for (int i = 0; i < 2; i++)
                {
                    var lp = legs[i].localPosition;
                    lp.z = moving ? Mathf.Sin(t * 6f + i * Mathf.PI) * 0.15f : 0f;
                    legs[i].localPosition = lp;
                }
            SquashT = Mathf.Max(0f, SquashT - dt * 4f);
            float q = SquashT;
            body3d.localScale = Vector3.Scale(bodyScale, new Vector3(1f + q * 0.2f, sy - q * 0.25f, 1f + q * 0.2f));
            body3d.localRotation = Quaternion.Euler(0, 0, (moving ? Mathf.Sin(t * 6f) * 0.06f : 0f) * Mathf.Rad2Deg);
        }
    }
}
