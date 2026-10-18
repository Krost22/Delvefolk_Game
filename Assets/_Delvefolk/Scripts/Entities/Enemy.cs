using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public enum EnemyState { Sleep, Chase, Windup, Attack, Recover, Hold, Npc, Custom, Falling, Dead }

    // Configuración de un tipo de enemigo (los valores por defecto son los de Enemy.js)
    public sealed class EnemyCfg
    {
        public string Name = "Enemigo";
        public float Hp = 30, Speed = 2, Radius = 0.5f, Mass = 1, Damage = 8, AttackRange = 1;
        public float Windup = 0.5f, AttackTime = 0.4f, Recover = 0.5f, AttackCooldown = 1;
        public float Strafe, StunMult = 1, WindupTrack = 4, KnockDrag = 4.2f, PlayerKnock = 9;
        public string HurtSound = "slimeHurt", DieSound = "slimeDie";
        public float BarY = 1.6f, HatY = 1, HatScale = 1, Scale = 1, Lunge;
        public int Color = 0xcc3333;
        public bool Bouncy, Crown, Splits;
        public int[] Coins;
        // Abrazón
        public float GrabSpeed = 7, HoldTime = 3.5f, SqueezeEvery = 1, SqueezeDamage = 6, ThrowForce = 20, ThrowDamage = 6;
        public float EscapePresses = 8, EscapeDecay = 1.5f, ReleaseDamage = 8, HoldMoveSpeed = 1.1f, EscapeStun = 1.3f;

        public EnemyCfg Clone() { return (EnemyCfg)MemberwiseClone(); }
    }

    // Enemigo base (Enemy.js): máquina de estados sencilla + reacción física a golpes y al entorno.
    // sleep → chase → windup → attack → recover (y falling / dead, más estados propios por tipo)
    public abstract class Enemy : Entity, IHittable
    {
        public readonly EnemyCfg Cfg;
        public string Name;
        public float MaxHp, Hp;
        public EnemyState State = EnemyState.Sleep;
        public float StateT;
        public float Facing;
        public float Stun;
        public float AttackCd;
        public float Vx, Vz;
        public Room Room;
        public float WakeDelay = -1f;      // despertar programado al entrar el jugador en la sala (−1: ninguno)
        public float Anim;
        public float ImpactCd;
        public float SquashT, Hop, PopT;
        public bool Peaceful;              // el Duende Tendero mientras no se le enfade
        public bool NoSwap;                // jefes y Duende: el Intercambio Caótico no los mueve
        public bool Flattened;
        public bool LastHitStomp;
        // Armas elementales: ralentización/raíz (multiplica el movimiento) y "dentro de la aspiradora" (sin IA)
        public float StatusMoveMult = 1f;
        public bool Captured;
        public Player Target;
        public readonly HashSet<Player> AttackHit = new HashSet<Player>();
        protected float retarget;
        float stuckT, jitter, lastX, lastZ;
        protected float strafeSign;
        float deathT;
        float fallVx, fallVz;
        float hatT;
        Transform hpBar, hpFill, stars, bucketHat;
        protected EnemyAnimator Animator;
        static Mesh barBg, barFg, starsMesh, hatMesh;

        protected Enemy(Game g, float x, float z, EnemyCfg cfg)
            : base(g, x, z, new Body { Radius = cfg.Radius, Mass = cfg.Mass, Kind = BodyKind.Enemy, KnockDrag = cfg.KnockDrag, Restitution = 0.5f }, cfg.Name)
        {
            Cfg = cfg;
            Name = cfg.Name;
            MaxHp = cfg.Hp; Hp = cfg.Hp;
            Facing = g.Rand.Next() * Mathf.PI * 2f;
            AttackCd = 0.5f + g.Rand.Next() * 0.5f;
            lastX = x; lastZ = z;
            strafeSign = g.Rand.Next() < 0.5f ? -1f : 1f;
            Body.PitSolid = () => !(KnockSpeed > 4.5f || Dead);
            Body.OnWallImpact = OnWallImpact;
            Body.OnBodyImpact = OnBodyImpact;
            BuildModel();
            RegisterFlash(Model.gameObject);
            BuildHpBar();
        }

        protected abstract void BuildModel();

        // ---------- Modelo animado del paquete de monstruos ----------
        // Devuelve true si lo pudo poner (si no, cada tipo construye su modelo con primitivas)
        protected bool UseAnimatedModel(GameObject prefab, float height, int recolor, float recolorAmount)
        {
            if (!Art.Has(prefab)) return false;
            var go = Art.Spawn(prefab, Model);
            if (!go) return false;
            var b = Art.LocalBounds(prefab);
            float s = height / Mathf.Max(0.05f, b.size.y);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(0, -b.min.y * s, 0);
            Animator = new EnemyAnimator(go);
            if (recolorAmount > 0f) Animator.Recolor(MathUtil.Hex(recolor), recolorAmount);
            return true;
        }

        void BuildHpBar()
        {
            if (!barBg)
            {
                barBg = new MeshBuilder().Add(ProcMesh.Plane(1.0f, 0.13f), Vector3.zero, MathUtil.Hex(0x220000)).ToMesh("barraFondo");
                // El relleno nace en el borde izquierdo para escalarlo desde ahí
                barFg = new MeshBuilder().Add(ProcMesh.Plane(0.96f, 0.09f).Translate(0.48f, 0, 0), Vector3.zero, MathUtil.Hex(0xe23b3b)).ToMesh("barraVida");
            }
            hpBar = new GameObject("BarraVida").transform;
            hpBar.SetParent(Root, false);
            hpBar.localPosition = new Vector3(0, Cfg.BarY, 0);
            var bg = CharacterModel.MeshObject(hpBar, "Fondo", barBg, Mats.Overlay, Vector3.zero);
            var fg = CharacterModel.MeshObject(hpBar, "Relleno", barFg, Mats.Overlay, new Vector3(-0.48f, 0, -0.001f));
            bg.GetComponent<MeshRenderer>().sortingOrder = 10;
            fg.GetComponent<MeshRenderer>().sortingOrder = 11;
            hpFill = fg.transform;
            hpBar.gameObject.SetActive(false);
        }

        public bool HpBarVisible { get { return hpBar && hpBar.gameObject.activeSelf; } }

        public void Wake(bool alert = true)
        {
            if (State != EnemyState.Sleep || Dead) return;
            SetState(EnemyState.Chase);
            AttackCd = 0.6f + G.Rand.Next() * 0.6f;
            if (alert)
            {
                G.Ui.FloatText(X, 2.2f, Z, "!", TextStyle.Alert);
                G.Audio.Play("alert");
                Hop = 1f;
            }
        }

        public void SetState(EnemyState s) { State = s; StateT = 0f; OnStateChanged(s); }
        protected virtual void OnStateChanged(EnemyState s) { }

        public bool Controllable { get { return !Dead && State != EnemyState.Falling && Stun <= 0f && KnockSpeed < 3.5f; } }

        public virtual void Update(float dt)
        {
            if (Removed || Captured) return;
            UpdateFlash(dt);
            StateT += dt;
            Anim += dt;
            ImpactCd = Mathf.Max(0f, ImpactCd - dt);
            if (State == EnemyState.Dead) { UpdateDeath(dt); return; }
            if (State == EnemyState.Falling) { UpdateFall(dt); return; }

            // ¿Ha salido volando por encima del foso?
            if (G.Dungeon.IsPitAt(X, Z)) { StartFall(); return; }

            var player = PickTarget(dt);   // el jugador más cercano (puede no haber ninguno)
            float dx = player != null ? player.X - X : 0f, dz = player != null ? player.Z - Z : 0f;
            float dist = player != null ? MathUtil.Hypot(dx, dz) : float.PositiveInfinity;
            Stun = Mathf.Max(0f, Stun - dt);
            AttackCd = Mathf.Max(0f, AttackCd - dt);

            float mvx = 0f, mvz = 0f;
            // Estados propios de un tipo (p. ej. 'hold' del Abrazón): devuelven la velocidad deseada
            Vector2 custom;
            if (UpdateState(dt, player, dist, dx, dz, out custom)) { mvx = custom.x; mvz = custom.y; }
            else if (!Controllable)
            {
                // Aturdido o volando: sin control
                if (State == EnemyState.Windup || State == EnemyState.Attack) SetState(EnemyState.Chase);
            }
            else if (State == EnemyState.Sleep)
            {
                if (WakeDelay >= 0f) { WakeDelay -= dt; if (WakeDelay <= 0f) { WakeDelay = -1f; Wake(); } }
                else if (player != null && dist < 4.5f && G.Dungeon.LineClear(X, Z, player.X, player.Z)) Wake();
            }
            else if (player == null)
            {
                // Nadie a quien pegar: celebran
            }
            else if (State == EnemyState.Chase)
            {
                float reach = Cfg.AttackRange + Radius + player.Radius;
                if (dist < reach && AttackCd <= 0f && G.Dungeon.LineClear(X, Z, player.X, player.Z))
                {
                    SetState(EnemyState.Windup);
                    Facing = Mathf.Atan2(dx, dz);
                    OnWindupStart();
                }
                else
                {
                    var dir = AvoidHazards(ChaseDirection(dist, dx, dz, dt));
                    float sp = Cfg.Speed * (dist < reach ? 0.35f : 1f);
                    mvx = dir.x * sp; mvz = dir.y * sp;
                    if (MathUtil.Hypot(mvx, mvz) > 0.1f) Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(mvx, mvz), 10f, dt);
                }
            }
            else if (State == EnemyState.Windup)
            {
                Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(dx, dz), Cfg.WindupTrack, dt);
                if (StateT >= Cfg.Windup) { SetState(EnemyState.Attack); AttackHit.Clear(); OnAttackStart(); }
            }
            else if (State == EnemyState.Attack)
            {
                Vector2 r;
                if (UpdateAttack(dt, dist, out r)) { mvx = r.x; mvz = r.y; }
                if (State == EnemyState.Attack && StateT >= Cfg.AttackTime) SetState(EnemyState.Recover);
            }
            else if (State == EnemyState.Recover)
            {
                if (StateT >= Cfg.Recover) { SetState(EnemyState.Chase); AttackCd = Cfg.AttackCooldown * (0.7f + G.Rand.Next() * 0.6f); }
            }

            // Aceleración suave hacia la velocidad deseada
            Vx = MathUtil.Damp(Vx, mvx, 12f, dt);
            Vz = MathUtil.Damp(Vz, mvz, 12f, dt);
            Body.Mvx = Vx * StatusMoveMult; Body.Mvz = Vz * StatusMoveMult;

            UpdateStars(dt);
            Animate(dt);
            if (Animator != null) Animator.Update(this, dt);
            SyncRoot();
            // Al salir disparado se eleva un poco: vuelo exagerado
            float lift = Mathf.Clamp((KnockSpeed - 8f) * 0.07f, 0f, 0.8f);
            // Levantado por un terremoto: saltito
            if (PopT > 0f) { PopT = Mathf.Max(0f, PopT - dt); lift = Mathf.Max(lift, Mathf.Sin(PopT / 0.55f * Mathf.PI) * 1.1f); }
            var rp = Root.position;
            rp.y = MathUtil.Damp(rp.y, lift, 14f, dt);
            Root.position = rp;
            Root.rotation = Quaternion.identity;
            SetModelYaw(Facing);
            // Barra de vida mirando a la cámara
            if (hpBar.gameObject.activeSelf)
            {
                hpBar.rotation = G.CameraCtl.Cam.transform.rotation;
                float k = Mathf.Max(0f, Hp / MaxHp);
                hpFill.localScale = new Vector3(Mathf.Max(0.001f, k), 1f, 1f);
            }
        }

        // La rotación del modelo combina el rumbo con las inclinaciones de cada tipo (giros al volar...)
        protected float ModelRotX, ModelRotZ;
        protected void SetModelYaw(float yaw)
        {
            // model.rotation = (x, facing, z) con el orden XYZ de three.js
            Model.localRotation = ProcMesh.EulerXYZ(ModelRotX, yaw, ModelRotZ);
        }

        // Estados propios de un tipo: true si lo ha gestionado (con la velocidad deseada en mv)
        protected virtual bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv) { mv = Vector2.zero; return false; }
        protected virtual void OnWindupStart() { }
        protected virtual void OnAttackStart() { }
        protected virtual bool UpdateAttack(float dt, float dist, out Vector2 mv) { mv = Vector2.zero; return false; }
        protected virtual void Animate(float dt) { }

        // Persecución: directa si hay camino libre, si no sigue el campo de navegación
        public Vector2 ChaseDirection(float dist, float dx, float dz, float dt)
        {
            var d = G.Dungeon;
            var p = Target;
            float dirX, dirZ;
            float dd = dist == 0f ? 1f : dist;
            if (dist < 14f && d.WalkClear(X, Z, p.X, p.Z, Radius * 0.9f)) { dirX = dx / dd; dirZ = dz / dd; }
            else if (!d.NavDirection(X, Z, out dirX, out dirZ)) { dirX = dx / dd; dirZ = dz / dd; }
            // Rodeo lateral (los goblins son más escurridizos)
            if (Cfg.Strafe > 0f && dist < 4.5f)
            {
                if (G.Rand.Next() < dt * 0.6f) strafeSign *= -1f;
                float nx = dirX + -dirZ * strafeSign * Cfg.Strafe, nz = dirZ + dirX * strafeSign * Cfg.Strafe;
                dirX = nx; dirZ = nz;
            }
            // Antiatasco: si no avanza, añade un empujón lateral temporal
            stuckT += dt;
            if (stuckT > 0.8f)
            {
                float moved = MathUtil.Hypot(X - lastX, Z - lastZ);
                if (moved < 0.4f && dist > 2f) jitter = 0.7f;
                lastX = X; lastZ = Z; stuckT = 0f;
            }
            if (jitter > 0f)
            {
                jitter -= dt;
                float nx = dirX - dirZ * strafeSign * 1.2f, nz = dirZ + dirX * strafeSign * 1.2f;
                dirX = nx; dirZ = nz;
            }
            float l = MathUtil.Hypot(dirX, dirZ);
            if (l == 0f) l = 1f;
            return new Vector2(dirX / l, dirZ / l);
        }

        // Por su propio pie no pisan púas ni fosos: prueban direcciones alternativas o se quedan quietos
        bool HazardAt(float x, float z)
        {
            var d = G.Dungeon;
            int tx = Grid.TileX(x), tz = Grid.TileZ(z);
            if (!d.InBounds(tx, tz)) return false;
            int i = d.Idx(tx, tz);
            return d.Spike[i] != 0 || d.Type[i] == TileType.Pit;
        }

        static readonly float[] AvoidAngles = { 0f, 0.6f, -0.6f, 1.2f, -1.2f, 1.7f, -1.7f };

        public Vector2 AvoidHazards(Vector2 dir)
        {
            if (dir.x == 0f && dir.y == 0f) return dir;
            if (HazardAt(X, Z)) return dir;   // ya está encima: que salga
            float look = Radius + 0.45f;
            foreach (var a in AvoidAngles)
            {
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float rx = dir.x * c + dir.y * s, rz = -dir.x * s + dir.y * c;
                if (!HazardAt(X + rx * look, Z + rz * look)) return new Vector2(rx, rz);
            }
            return Vector2.zero;
        }

        float AggroDist(Player p)
        {
            return MathUtil.Hypot(p.X - X, p.Z - Z) - (p.Gear.Has("item_compass") ? GearConfig.CompassAggroRange : 0f);
        }

        // Objetivo: el jugador más cercano, con algo de "memoria" para no cambiar a cada momento
        public virtual Player PickTarget(float dt)
        {
            retarget -= dt;
            if (retarget <= 0f || Target == null || !Target.Targetable)
            {
                retarget = 0.5f;
                Player best = null;
                foreach (var p in G.Players)
                    if (p.Targetable && (best == null || AggroDist(p) < AggroDist(best))) best = p;
                if (Target != null && Target.Targetable && best != null && AggroDist(Target) < AggroDist(best) + 2f) best = Target;
                Target = best;
            }
            return Target;
        }

        // Altura a la que un jugador que cae encima le aplasta (los muy altos, a la altura de los hombros)
        public virtual float StompTop() { return Mathf.Min(1.45f, Cfg.BarY - 0.45f); }

        // Comprobación de golpe: cada jugador en alcance recibe como mucho un golpe por ataque
        public void TryHitPlayer(float extraReach = 0f)
        {
            foreach (var p in G.Players)
            {
                if (!p.Targetable || AttackHit.Contains(p)) continue;
                if (p.JumpY > JumpConfig.DodgeHeight) continue;   // saltando por encima del ataque
                float dx = p.X - X, dz = p.Z - Z;
                float dist = MathUtil.Hypot(dx, dz);
                if (dist > Cfg.AttackRange + Radius + p.Radius + extraReach) continue;
                float ang = Mathf.Abs(MathUtil.AngleDiff(Facing, Mathf.Atan2(dx, dz)));
                if (ang > 1.3f) continue;
                AttackHit.Add(p);
                p.Hurt(Cfg.Damage, X, Z, Cfg.PlayerKnock);
            }
        }

        // ---------- Golpes del jugador ----------
        public virtual void TakeHit(Hit hit)
        {
            if (Dead || State == EnemyState.Falling) return;
            Wake(false);
            if (State == EnemyState.Sleep) SetState(EnemyState.Chase);
            // Cualquier golpe interrumpe la preparación de un ataque
            if (State == EnemyState.Windup || State == EnemyState.Attack)
            {
                SetState(EnemyState.Chase);
                AttackCd = Mathf.Max(AttackCd, 0.4f);
                if (hit.Stun > 0.5f) G.Ui.FloatText(X, 2.4f, Z, "¡INTERRUMPIDO!", TextStyle.Small);
            }
            Stun = Mathf.Max(Stun, hit.Stun * Cfg.StunMult);
            Knock(hit.DirX * hit.Knock / Body.Mass, hit.DirZ * hit.Knock / Body.Mass);
            LastHitStomp = hit.Stomp;
            Damage(hit.Damage, false, null);
            LastHitStomp = false;
            SquashT = 1f;
            if (Animator != null && !Dead) Animator.Hurt();
        }

        // Daño genérico. env = daño del entorno (se cuenta y se muestra distinto)
        public virtual void Damage(float amount, bool env, string label)
        {
            if (Dead || amount <= 0f) return;
            amount = Mathf.Round(amount);
            Hp -= amount;
            hpBar.gameObject.SetActive(true);
            Flash(0.13f, env ? 0xff8a20 : 0xffffff);
            G.Ui.FloatText(X, 1.8f + G.FxRand.Next() * 0.4f, Z, ((int)amount).ToString(), env ? TextStyle.Env : TextStyle.Dmg);
            if (label != null) G.Ui.FloatText(X, 2.6f, Z, label, TextStyle.Label);
            if (env) G.Stats.EnvDamage += amount;
            G.Audio.Play(Cfg.HurtSound);
            if (Hp <= 0f) Die(env ? "env" : "hit");
        }

        public void EnvDamage(float amount, string label)
        {
            Wake(false);
            if (State == EnemyState.Sleep) SetState(EnemyState.Chase);
            Damage(amount, true, label);
        }

        protected virtual void OnWallImpact(float speed, float nx, float nz)
        {
            if (Dead || speed < 6.5f || ImpactCd > 0f) return;
            ImpactCd = 0.15f;
            float dmg = Mathf.Min(35f, (speed - 4.5f) * 1.7f);
            G.Audio.Play("crash");
            G.CameraCtl.AddShake(Mathf.Min(0.4f, speed * 0.02f));
            G.Effects.Burst(X - nx * Radius, 0.6f, Z - nz * Radius, BurstOpts.Of(12, 0x9a8c7a, 4f, 4f, 0.18f).Dir(nx, nz));
            G.RequestHitstop(0.05f);
            SquashT = 1f;
            Stun = Mathf.Max(Stun, 0.5f);
            EnvDamage(dmg, "¡PLAF!");
        }

        protected virtual void OnBodyImpact(Body other, float speed, float nx, float nz)
        {
            if (Dead || ImpactCd > 0f) return;
            if (other.IsStatic) { OnWallImpact(speed, nx, nz); return; }
            var o = other.Owner as Enemy;
            if (o != null && speed > 7f)
            {
                // Bolos: el que vuela y el que recibe se hacen daño
                ImpactCd = 0.15f;
                EnvDamage(Mathf.Min(25f, (speed - 5f) * 1.3f), KnockSpeed > o.KnockSpeed ? null : "¡BOLOS!");
                Stun = Mathf.Max(Stun, 0.4f);
                G.Audio.Play("hit");
                G.CameraCtl.AddShake(0.15f);
            }
        }

        public void SetBucketHat(float dur)
        {
            if (!bucketHat)
            {
                if (!hatMesh) hatMesh = new MeshBuilder().Add(ProcMesh.Cylinder(0.38f, 0.48f, 0.55f, 9), Vector3.zero, MathUtil.Hex(0x8c9aa6)).ToMesh("cuboCabeza");
                var go = CharacterModel.MeshObject(Model, "Cubo", hatMesh, Mats.Lit, new Vector3(0, Cfg.HatY, 0));
                go.transform.localScale = Vector3.one * Cfg.HatScale;
                bucketHat = go.transform;
            }
            bucketHat.gameObject.SetActive(true);
            hatT = dur;
        }

        public bool HasBucketHat { get { return bucketHat && bucketHat.gameObject.activeSelf; } }

        void UpdateStars(float dt)
        {
            bool stunned = Stun > 0.3f;
            if (stunned && !stars)
            {
                if (!starsMesh)
                {
                    var sb = new MeshBuilder();
                    for (int i = 0; i < 3; i++) sb.Add(ProcMesh.Octahedron(0.1f), new Vector3(Mathf.Cos(i * 2.1f) * 0.45f, 0, Mathf.Sin(i * 2.1f) * 0.45f), MathUtil.Hex(0xffe14a));
                    starsMesh = sb.ToMesh("estrellasEnemigo");
                }
                stars = CharacterModel.MeshObject(Root, "Estrellas", starsMesh, Mats.Unlit, new Vector3(0, Cfg.BarY - 0.15f, 0)).transform;
            }
            if (stars)
            {
                if (stars.gameObject.activeSelf != stunned) stars.gameObject.SetActive(stunned);
                stars.Rotate(0, dt * 6f * Mathf.Rad2Deg, 0, Space.Self);
            }
            if (bucketHat && bucketHat.gameObject.activeSelf)
            {
                hatT -= dt;
                bucketHat.Rotate(0, dt * 2f * Mathf.Rad2Deg, 0, Space.Self);
                bucketHat.localRotation = Quaternion.Euler(0, bucketHat.localEulerAngles.y, 0.3f * Mathf.Rad2Deg);
                if (hatT <= 0f || Stun <= 0f)
                {
                    bucketHat.gameObject.SetActive(false);
                    G.Effects.Burst(X, 1.2f, Z, BurstOpts.Of(6, 0x8c9aa6, 3f, 5f, 0.12f));
                }
            }
        }

        public void StartFall()
        {
            SetState(EnemyState.Falling);
            Body.Enabled = false;
            hpBar.gameObject.SetActive(false);
            if (stars) stars.gameObject.SetActive(false);
            Shadow.gameObject.SetActive(false);
            fallVx = Body.Kx * 0.3f; fallVz = Body.Kz * 0.3f;
            G.Audio.Play("fall");
            G.Ui.FloatText(X, 1.5f, Z, "¡AL FOSO!", TextStyle.Label);
        }

        void UpdateFall(float dt)
        {
            var p = Root.position;
            p.y -= dt * (2f + StateT * 14f);
            p.x += fallVx * dt; p.z += fallVz * dt;
            Root.position = p;
            ModelRotX += dt * 8f;
            SetModelYaw(Facing);
            float s = Mathf.Max(0.05f, 1f - StateT * 0.9f);
            Model.localScale = Vector3.one * s;
            if (StateT > 0.9f)
            {
                Hp = 0f;
                G.Stats.EnvDamage += MaxHp;
                Die("pit");
            }
        }

        public virtual void Die(string cause)
        {
            if (Dead) return;
            if (cause == "hit" && LastHitStomp) { cause = "stomp"; Flattened = true; }
            Dead = true;
            hpBar.gameObject.SetActive(false);
            if (stars) stars.gameObject.SetActive(false);
            Body.Enabled = false;
            if (cause != "pit") SetState(EnemyState.Dead);
            else State = EnemyState.Dead;
            deathT = 0f;
            if (Animator != null) Animator.Die();
            G.OnEnemyKilled(this, cause);
            if (cause == "pit") Destroy();
        }

        void UpdateDeath(float dt)
        {
            deathT += dt;
            if (Flattened)
            {
                // Aplastado de un pisotón: queda plano en el suelo un rato y luego desaparece
                float k = Mathf.Min(1f, deathT / 0.08f);
                float fade = Mathf.Max(0f, (deathT - 1.4f) / 0.4f);
                var rp = Root.position; rp.y = 0f; Root.position = rp;
                Model.localPosition = Vector3.zero;
                ModelRotX = 0f;
                SetModelYaw(Facing);
                Model.localScale = new Vector3((1f + 0.8f * k) * (1f - fade), Mathf.Max(0.06f, 1f - 0.94f * k), (1f + 0.8f * k) * (1f - fade));
                if (deathT > 1.8f) Destroy();
                return;
            }
            float kk = Mathf.Min(1f, deathT / 0.35f);
            Model.localScale = new Vector3(1f + kk * 0.6f, Mathf.Max(0.05f, 1f - kk), 1f + kk * 0.6f);
            float sh = Radius * 1.15f * (1f - kk);
            Shadow.localScale = new Vector3(sh, 1f, sh);
            if (deathT > 0.4f) Destroy();
        }

        // Brillo de aviso (windup): solo si no hay un destello de golpe en curso
        protected void SetGlow(float r, float gg, float b) { if (!Flashing) SetFlashColor(new Color(r, gg, b)); }
    }

    // La brújula maldita llega con el equipo (fase 6): de momento solo su alcance
}
