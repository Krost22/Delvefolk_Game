using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public sealed class PlayerLook
    {
        public int Tunic, Ring;
        public string Hat, Name;
        public PlayerLook(int tunic, int ring, string hat, string name) { Tunic = tunic; Ring = ring; Hat = hat; Name = name; }
    }

    public struct HurtOpts
    {
        public string Kind;      // "trap", ...
        public bool Raw;         // sin armadura ni escudo
        public bool Pierce;      // ignora la invulnerabilidad breve
        public bool Stomp;       // pisotón (si muere, queda aplastado)
        public bool Dot;         // daño continuo (gas, lava): sin congelar ni sacudir
    }

    // Un don nadie con una olla (o un embudo) por casco. Puede haber dos en cooperativo local.
    // Solo lleva UNA cosa en la mano: un arma, comida... o a su compañero.
    public sealed partial class Player : Entity
    {
        public static readonly PlayerLook[] Looks =
        {
            new PlayerLook(0x5b7f3c, 0x7dd65b, "pot", "J1"),
            new PlayerLook(0x3c6a9f, 0x5bb0ff, "funnel", "J2"),
            new PlayerLook(0xd0702a, 0xffa94a, "helmet", "J3"),
            new PlayerLook(0x6a3a7a, 0xc77dff, "hood", "J4"),
        };

        public readonly int Index;
        public readonly PlayerLook Look;
        public readonly PlayerControls Controls;
        public readonly PlayerLoadout Loadout;
        public readonly PlayerGear Gear;
        public readonly AbilityController Ability;
        public CharacterParts Parts;
        Transform colorRing;

        public float MaxHp = Cfg.PlayerMaxHp;
        public float Hp;
        public float Speed = 6.4f;
        public float Vx, Vz;
        public float Facing;
        public float Iframes;
        public float Cooldown, Buffer;
        public float WalkT, Squash, DeadT, StepAcc, ReviveT, FeedT, FxT;
        // Estados temporales (todos en tiempo de juego)
        public float StunT;          // aturdido: sin control
        public float StunGuard;      // inmunidad a un nuevo aturdimiento justo después de otro
        public float ConfusedT;      // desorientado: controles invertidos
        public float HasteT, HasteMult = 1f, SlowT, SlowMult = 1f;
        public float PopT;           // saltito al ser levantado por un terremoto
        public float JumpY, JumpV, JumpBuffer;
        public float Stamina = JumpConfig.Stamina;   // energía para saltar y pisotear
        public float StaminaT;                       // tiempo desde el último gasto (para empezar a recuperarla)
        public bool Exhausted;                       // gastó más de la que tenía: al aterrizar cae rendido
        public bool FlatDeath;                       // derribado por un pisotón: queda aplastado
        public readonly Dictionary<object, float> StompCd = new Dictionary<object, float>();
        public float Struggle;
        public bool Spectator;
        // Cooperativo y agarres (fase 3)
        public Player Carrying, CarriedBy;
        public object GrabbedBy;
        public float GrabT, CarriedT;

        public Weapon Weapon { get; private set; }
        GameObject weaponModel;

        public Player(Game g, float x, float z, int index)
            : base(g, x, z, new Body { Radius = 0.42f, Mass = 1f, Kind = BodyKind.Player, KnockDrag = 7f, Restitution = 0.15f }, "Jugador" + (index + 1))
        {
            Index = index;
            Look = Looks[index];
            Controls = new PlayerControls(g.Input, g.Bindings[index]);
            Body.PitSolid = () => true;   // el jugador nunca se cae al foso por accidente
            Body.OnBodyImpact = OnBodyImpact;
            Body.OnWallImpact = OnWallImpact;
            Hp = MaxHp;
            Vector2 f, r;
            g.CameraCtl.GetBasis(out f, out r);
            Facing = Mathf.Atan2(f.x, f.y);
            Loadout = g.LoadoutFor(index);
            Gear = new PlayerGear(this);
            Ability = new AbilityController(this, Loadout.Ability);
            BuildModel();
            SetWeapon(new FistsWeapon());
        }

        void BuildModel()
        {
            // El muñeco se monta con las piezas cosméticas elegidas (mismo código que la vista previa)
            Parts = CharacterModel.Build(Model, Loadout.Cosmetics);
            RegisterFlash(Parts.Renderers);
            // Anillo de color del jugador (para distinguirse en cooperativo)
            var ring = new MeshBuilder().Add(ProcMesh.Ring(0.55f, 0.68f, 24).RotateX(-Mathf.PI / 2f), Vector3.zero, MathUtil.Hex(Look.Ring, 0.75f)).ToMesh("anillo");
            colorRing = CharacterModel.MeshObject(Root, "AnilloColor", ring, Mats.Transparent, new Vector3(0, 0.03f, 0)).transform;
        }

        public void SetWeapon(Weapon w)
        {
            // Lo que deja de llevar en la mano suelta lo que tuviera dentro (la aspiradora)
            var old = Weapon as ElementalWeapon;
            if (old != null && old != w) old.OnUnequip(this, G);
            if (weaponModel) Object.Destroy(weaponModel);
            Weapon = w;
            weaponModel = w.BuildDisplayModel(Parts.Holder);
            OnWeaponChanged();
            Cooldown = Mathf.Min(Cooldown, 0.15f);
            G.Ui.SetWeapon(Index, w);
        }

        partial void OnWeaponChanged();

        // Se rompió en plena pelea: el modelo pasa a la versión rota
        public void RefreshWeaponModel()
        {
            if (weaponModel) Object.Destroy(weaponModel);
            weaponModel = Weapon.BuildDisplayModel(Parts.Holder);
            G.Ui.SetWeapon(Index, Weapon);
        }

        // ¿Puede ser objetivo de enemigos / trampas?
        public bool Targetable { get { return !Dead && CarriedBy == null && GrabbedBy == null && !Spectator; } }
        public bool Airborne { get { return JumpY > 0f; } }

        // ---------- Estados temporales ----------
        void UpdateStatus(float dt)
        {
            Ability.Update(dt);
            if ((Dead || CarriedBy != null || GrabbedBy != null || IsThrown) && Ability.Active) Ability.Finish();
            UpdateStompCooldowns(dt);
            StaminaT += dt;
            // Agarrado o lanzado en pleno salto: no llega a "aterrizar", así que se olvida del agotamiento
            if (Exhausted && (CarriedBy != null || GrabbedBy != null || IsThrown)) { Exhausted = false; Stamina = Mathf.Max(0f, Stamina); }
            if (JumpY == 0f && !Exhausted && StunT <= 0f && StaminaT >= JumpConfig.RegenDelay && Stamina < JumpConfig.Stamina)
                Stamina = Mathf.Min(JumpConfig.Stamina, Stamina + JumpConfig.Regen * dt);
            if (Gear.Update(dt)) G.Ui.SetGear(Index, Gear);
            TickElemental(dt);
            if (StunT > 0f)
            {
                StunT = Mathf.Max(0f, StunT - dt);
                if (StunT == 0f) StunGuard = AbilityRules.PlayerStunGuard;
            }
            else StunGuard = Mathf.Max(0f, StunGuard - dt);
            ConfusedT = Mathf.Max(0f, ConfusedT - dt);
            if (HasteT > 0f) { HasteT -= dt; if (HasteT <= 0f) HasteMult = 1f; }
            if (SlowT > 0f) { SlowT -= dt; if (SlowT <= 0f) SlowMult = 1f; }
            PopT = Mathf.Max(0f, PopT - dt);
            bool starsOn = (StunT > 0f || ConfusedT > 0f) && !Dead;
            if (Parts.Stars.gameObject.activeSelf != starsOn) Parts.Stars.gameObject.SetActive(starsOn);
            if (starsOn)
            {
                Parts.Stars.Rotate(0f, dt * (ConfusedT > 0f ? -8f : 6f) * Mathf.Rad2Deg, 0f, Space.Self);
                SetStarsColor(ConfusedT > 0f ? 0xc080ff : 0xffe14a);
            }
            // Efecto cosmético discreto (solo visual)
            var fx = Parts.Fx;
            if (fx != null && !Dead && Model.gameObject.activeSelf)
            {
                FxT -= dt;
                if (FxT <= 0f)
                {
                    FxT = fx.Every;
                    G.Effects.Burst(X + (G.FxRand.Next() - 0.5f) * 0.6f, fx.Y, Z + (G.FxRand.Next() - 0.5f) * 0.6f,
                        new BurstOpts { Count = 1, Color = fx.Color, Speed = 0.6f, Up = fx.Up, HasUp = true, Size = fx.Size, Gravity = 2f, HasGravity = true, Life = 0.9f });
                }
            }
            UpdateCompass(dt);
        }

        int starsColor = -1;
        static readonly MaterialPropertyBlock starMpb = new MaterialPropertyBlock();
        void SetStarsColor(int hex)
        {
            if (starsColor == hex) return;
            starsColor = hex;
            var c = MathUtil.Hex(hex).linear;
            starMpb.SetVector(Mats.BaseColorId, new Vector4(c.r, c.g, c.b, 1f));
            Parts.StarsRenderer.SetPropertyBlock(starMpb);
        }

        readonly List<object> stompKeys = new List<object>();
        void UpdateStompCooldowns(float dt)
        {
            if (StompCd.Count == 0) return;
            stompKeys.Clear();
            stompKeys.AddRange(StompCd.Keys);
            foreach (var k in stompKeys)
            {
                float t = StompCd[k] - dt;
                if (t <= 0f) StompCd.Remove(k); else StompCd[k] = t;
            }
        }

        partial void UpdateCompass(float dt);

        // ---------- Salto ----------
        public bool CanJump()
        {
            if (!(JumpY == 0f && Carrying == null && !IsEating && StunT <= 0f && !Ability.LocksMovement && G.State == GameState.Play)) return false;
            if (Stamina <= 0f)
            {
                // Sin aliento: no hay salto hasta recuperar algo de energía
                if (StaminaT > 0.5f)
                {
                    G.Ui.FloatText(X, 2.6f, Z, "Sin aliento...", TextStyle.Small);
                    G.Audio.Play("denied");
                    StaminaT = 0f;
                }
                return false;
            }
            return true;
        }

        // Gasta energía; si se queda en negativo, al aterrizar cae rendido
        public void SpendStamina(float n)
        {
            Stamina -= n;
            StaminaT = 0f;
            if (Stamina < 0f) Exhausted = true;
        }

        public void StartJump()
        {
            SpendStamina(JumpConfig.JumpCost);
            JumpV = JumpConfig.Speed;
            JumpY = 0.001f;
            Squash = 0.6f;
            G.Audio.Play("jump");
            G.Effects.Burst(X, 0.1f, Z, BurstOpts.Of(5, 0x8a7b68, 2f, 1.5f, 0.1f));
        }

        public void EndJump()
        {
            JumpY = 0f; JumpV = 0f; JumpBuffer = 0f;
            Body.Airborne = false;
        }

        void UpdateJump(float dt)
        {
            if (JumpY <= 0f && JumpV <= 0f) { Body.Airborne = false; return; }
            JumpV -= JumpConfig.Gravity * dt;
            JumpY += JumpV * dt;
            if (JumpV < 0f && !Spectator) CheckStomp();
            if (JumpY <= 0f)
            {
                // Aterrizaje
                EndJump();
                Squash = 0.8f;
                if (Exhausted) Collapse();
                G.Effects.Burst(X, 0.1f, Z, BurstOpts.Of(6, 0x8a7b68, 2.5f, 1.5f, 0.12f));
                G.Audio.Play("step");
                return;
            }
            Body.Airborne = JumpY > JumpConfig.PassHeight;
        }

        // Al caer sobre un enemigo, un compañero o un barril: golpe, aturdimiento y rebote
        partial void CheckStompTargets(ref bool done);
        void CheckStomp()
        {
            bool done = false;
            CheckStompTargets(ref done);
            if (done) return;
            // Caer encima de un compañero: le duele y le aturde (y uno rebota igual)
            foreach (var q in G.Players)
            {
                if (q == this || q.Dead || q.Removed || q.Spectator || q.CarriedBy != null || q.GrabbedBy != null || q.JumpY > 0.3f || StompCd.ContainsKey(q)) continue;
                if (MathUtil.Hypot(q.X - X, q.Z - Z) > q.Radius + 0.35f) continue;
                const float top = 1.35f;
                if (JumpY > top + 0.2f || JumpY < top - 0.8f) continue;
                StompMate(q, top);
                return;
            }
            CheckStompBarrels(ref done);
        }

        partial void CheckStompBarrels(ref bool done);

        void StompMate(Player t, float top)
        {
            // Pisotón a un compañero: daño y aturdimiento (respeta su inmunidad tras otro aturdimiento)
            t.Iframes = 0f;
            t.Hurt(JumpConfig.MateStompDamage, X, Z, 3f, new HurtOpts { Stomp = true });
            if (!t.Dead) t.ApplyStun(JumpConfig.MateStompStun);
            t.Squash = 1f;
            G.Ui.FloatText(X, top + 1.6f, Z, "¡Perdón!", TextStyle.Small);
            AfterStomp(t, t.X, t.Z, top, false);
        }

        // Rebote y efectos comunes a todo pisotón
        public void AfterStomp(object target, float tx, float tz, float top, bool bouncy)
        {
            StompCd[target] = 0.35f;
            JumpY = Mathf.Max(JumpY, top);
            SpendStamina(JumpConfig.StompCost);
            // Agotado: ya no hay fuerzas para rebotar
            JumpV = Exhausted ? JumpConfig.ExhaustBounce : bouncy ? JumpConfig.BouncyBounce : JumpConfig.Bounce;
            Squash = 1f;
            G.Stats.Stomps++;
            G.Audio.Play("stomp");
            G.RequestHitstop(0.05f);
            G.CameraCtl.AddShake(0.2f);
            G.Effects.Ring(tx, tz, new RingOpts { Color = 0xffe0a0, Radius = 1.2f, Life = 0.2f, Y = top, R0 = 0.3f, Opacity = 0.8f });
            G.Effects.Burst(tx, top, tz, BurstOpts.Of(10, 0xfff4c0, 4f, 3f, 0.1f));
            G.Ui.FloatText(tx, top + 1.2f, tz, bouncy ? "¡BOING!" : "¡CHAF!", TextStyle.Label);
        }

        // Se queda sin energía: cae rendido al suelo y aturdido un momento
        public void Collapse()
        {
            Exhausted = false;
            Stamina = 0f;
            StaminaT = 0f;
            StunGuard = 0f;
            ApplyStun(JumpConfig.ExhaustStun, true);
            Squash = 1f;
            G.Audio.Play("crash");
            G.CameraCtl.AddShake(0.15f);
            G.Effects.Burst(X, 0.1f, Z, BurstOpts.Of(10, 0x8a7b68, 3f, 2f, 0.14f));
            G.Ui.FloatText(X, 2.6f, Z, "¡Agotado!", TextStyle.Label);
        }

        // Aturdimiento con tope de duración e inmunidad posterior: nadie queda aturdido indefinidamente.
        // force: lo provoca el propio jugador (chocar en plena carga, agotarse) e ignora la inmunidad.
        public bool ApplyStun(float t, bool force = false)
        {
            if (Dead || CarriedBy != null) return false;
            if (Ability.Unstoppable) return false;
            if (!force && (StunGuard > 0f || StunT > 0f)) return false;
            float dur = Mathf.Min(AbilityRules.PlayerStunCap, t * Gear.StunMult());
            StunT = Mathf.Max(StunT, dur);
            CancelEating();
            CancelAttack();
            return true;
        }

        public void AddHaste(float mult, float t) { HasteMult = Mathf.Max(HasteMult, mult); HasteT = Mathf.Max(HasteT, t); }
        public void AddSlow(float mult, float t) { SlowMult = Mathf.Min(SlowMult, mult); SlowT = Mathf.Max(SlowT, t); }

        // ---------- Bucle del jugador ----------
        public void Update(float dt)
        {
            UpdateFlash(dt);
            Iframes = Mathf.Max(0f, Iframes - dt);
            if (Body.IgnoreT > 0f) { Body.IgnoreT -= dt; if (Body.IgnoreT <= 0f) Body.Ignore = null; }
            UpdateStatus(dt);
            if (Dead) { UpdateDeath(dt); return; }
            if (CarriedBy != null) { UpdateCarried(dt); return; }
            if (GrabbedBy != null) { UpdateGrabbed(dt); return; }
            if (IsThrown) { UpdateThrown(dt); return; }
            var c = Controls;

            // ---- Salto (con un pequeño margen para pulsar un pelín antes de aterrizar) ----
            if (c.ConsumeJump()) JumpBuffer = 0.15f;
            JumpBuffer = Mathf.Max(0f, JumpBuffer - dt);
            if (JumpBuffer > 0f && CanJump()) { JumpBuffer = 0f; StartJump(); }
            UpdateJump(dt);

            // ---- Habilidad especial ----
            if (c.ConsumeAbility()) Ability.TryActivate();
            if (Ability.LocksMovement)
            {
                // La habilidad (carga, terremoto) controla el movimiento mientras dura
                if (Ability.Id != "charge") { Vx *= 0.8f; Vz *= 0.8f; Body.Mvx = Vx; Body.Mvz = Vz; }
                Animate(dt, MathUtil.Hypot(Vx, Vz) / Speed);
                return;
            }
            // ---- Aturdido: sin control ----
            if (StunT > 0f)
            {
                Vx = MathUtil.Damp(Vx, 0f, 10f, dt); Vz = MathUtil.Damp(Vz, 0f, 10f, dt);
                Body.Mvx = Vx; Body.Mvz = Vz;
                CancelAttack(); Buffer = 0f;
                Animate(dt, 0f);
                return;
            }

            // ---- Movimiento relativo a la cámara ----
            var ax = c.Axis();
            if (ConfusedT > 0f) { ax.x = -ax.x; ax.y = -ax.y; }   // desorientado: todo al revés
            Vector2 forward, right;
            G.CameraCtl.GetBasis(out forward, out right);
            float dx = forward.x * ax.y + right.x * ax.x;
            float dz = forward.y * ax.y + right.y * ax.x;
            float dl = MathUtil.Hypot(dx, dz);
            if (dl > 0f) { dx /= dl; dz /= dl; }          // diagonal normalizada
            float mag = Mathf.Min(1f, ax.magnitude);      // magnitud analógica del stick
            float mult = MoveMultiplier();
            mult *= Gear.SpeedMult() * HasteMult * SlowMult;
            float tvx = dx * Speed * mult * (dl > 0f ? mag : 1f), tvz = dz * Speed * mult * (dl > 0f ? mag : 1f);
            // Correa invisible en cooperativo: no alejarse demasiado del compañero (la cámara los enmarca a los dos)
            Player mate = null;
            foreach (var p in G.Players) if (p != this && !p.Dead) { mate = p; break; }
            if (mate != null)
            {
                float ox = X - mate.X, oz = Z - mate.Z, od = MathUtil.Hypot(ox, oz);
                const float Leash = 17f;
                if (od > Leash)
                {
                    float nx = ox / od, nz = oz / od, outv = tvx * nx + tvz * nz;
                    if (outv > 0f) { tvx -= nx * outv; tvz -= nz * outv; }
                }
            }
            float rate = (dl > 0f ? 70f : 55f) * dt;      // aceleración rápida y frenado natural
            float ddx = tvx - Vx, ddz = tvz - Vz;
            float dd = MathUtil.Hypot(ddx, ddz);
            if (dd > rate) { ddx *= rate / dd; ddz *= rate / dd; }
            Vx += ddx; Vz += ddz;
            Body.Mvx = Vx; Body.Mvz = Vz;

            // ---- Orientación suave ----
            if (dl > 0f && AttackAllowsTurning()) Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(dx, dz), 15f, dt);

            // ---- Acción principal: atacar / comer / lanzar ----
            if (c.AttackPressed()) Buffer = 0.22f;
            Buffer = Mathf.Max(0f, Buffer - dt);
            Cooldown = Mathf.Max(0f, Cooldown - dt);
            float? inputAngle = dl > 0f ? Mathf.Atan2(dx, dz) : (float?)null;
            ActionButton(dt, inputAngle);

            Animate(dt, MathUtil.Hypot(Vx, Vz) / Speed);
        }

        // ---------- Ganchos de las fases siguientes (combate, comida, cooperativo) ----------
        public bool IsEating { get { bool v = false; IsEatingImpl(ref v); return v; } }
        public bool IsAttacking { get { bool v = false; IsAttackingImpl(ref v); return v; } }
        public bool IsThrown { get { bool v = false; IsThrownImpl(ref v); return v; } }
        partial void IsEatingImpl(ref bool v);
        partial void IsAttackingImpl(ref bool v);
        partial void IsThrownImpl(ref bool v);

        float MoveMultiplier() { float m = 1f; MoveMultiplierImpl(ref m); return m; }
        partial void MoveMultiplierImpl(ref float m);
        bool AttackAllowsTurning() { bool v = true; AttackAllowsTurningImpl(ref v); return v; }
        partial void AttackAllowsTurningImpl(ref bool v);

        public void CancelEating() { CancelEatingImpl(); }
        partial void CancelEatingImpl();
        public void CancelAttack() { CancelAttackImpl(); }
        partial void CancelAttackImpl();

        partial void ActionButton(float dt, float? inputAngle);
        partial void UpdateCarried(float dt);
        partial void UpdateGrabbed(float dt);
        partial void UpdateThrown(float dt);
        partial void OnBodyImpactImpl(Body other, float speed, float nx, float nz);
        partial void OnWallImpactImpl(float speed);
        partial void ArmPose(ref Pose p, ref bool handled);

        void OnBodyImpact(Body other, float speed, float nx, float nz) { OnBodyImpactImpl(other, speed, nx, nz); }
        void OnWallImpact(float speed, float nx, float nz) { OnWallImpactImpl(speed); }

        // ---------- Animación ----------
        void Animate(float dt, float moveRatio)
        {
            // Caminar: balanceo y pasos
            if (moveRatio > 0.05f)
            {
                WalkT += dt * 13f * Mathf.Min(1f, moveRatio + 0.2f);
                StepAcc += dt * moveRatio;
                if (StepAcc > 0.24f) { StepAcc = 0f; G.Audio.Play("step"); }
            }
            else WalkT = MathUtil.Damp(WalkT, Mathf.Round(WalkT / Mathf.PI) * Mathf.PI, 10f, dt);
            SetRootY(JumpY);
            float sh = Body.Radius * 1.15f * (1f - Mathf.Min(0.5f, JumpY * 0.3f));
            Shadow.localScale = new Vector3(sh, 1f, sh);
            float s = Mathf.Sin(WalkT);
            Model.localPosition = new Vector3(0f, Mathf.Abs(s) * 0.09f * Mathf.Min(1f, moveRatio * 2f) + Mathf.Sin(Mathf.Min(1f, PopT / 0.55f) * Mathf.PI) * 0.9f, 0f);
            if (Parts.Cape) Parts.Cape.localRotation = Quaternion.Euler((Mathf.Min(0.9f, moveRatio * 0.7f) + Mathf.Sin(WalkT * 2f) * 0.05f) * Mathf.Rad2Deg, 0, 0);
            float rotX = 0f, rotZ = s * 0.07f * moveRatio;
            float flz = s * 0.2f * moveRatio, frz = -s * 0.2f * moveRatio;
            if (JumpY > 0f) { flz = 0.12f; frz = -0.12f; rotX = -0.12f; }   // piernas recogidas
            Model.localRotation = ProcMesh.EulerXYZ(rotX, 0f, rotZ);
            SetFeet(flz, frz);
            Root.rotation = Quaternion.Euler(0f, Facing * Mathf.Rad2Deg, 0f);
            // Estiramiento al golpear
            Squash = Mathf.Max(0f, Squash - dt * 5f);
            float q = Squash;
            Model.localScale = new Vector3(1f + q * 0.15f, 1f - q * 0.12f, 1f + q * 0.15f);
            // Pose del brazo
            Pose p = default(Pose);
            bool handled = false;
            ArmPose(ref p, ref handled);
            if (!handled)
            {
                Parts.LHand.localPosition = new Vector3(0.42f, 0.8f, 0.1f);
                p = Weapon.IdlePose();
                p.Yaw += Mathf.Sin(WalkT) * 0.12f;
            }
            Parts.ArmPivot.localRotation = ProcMesh.EulerYXZ(p.Pitch, p.Yaw, p.Roll);
            Parts.Holder.localPosition = new Vector3(0f, 0f, 0.45f + p.Reach);
            // Parpadeo de invulnerabilidad
            bool vis = Iframes <= 0f || Mathf.FloorToInt(Iframes * 14f) % 2 == 0;
            if (Model.gameObject.activeSelf != vis) Model.gameObject.SetActive(vis);
        }

        public void SetFeet(float lz, float rz)
        {
            var l = Parts.FootL.localPosition; l.z = lz; Parts.FootL.localPosition = l;
            var r = Parts.FootR.localPosition; r.z = rz; Parts.FootR.localPosition = r;
        }

        // Altura de la raíz; la sombra y el anillo de color se quedan en el suelo
        public void SetRootY(float y)
        {
            var p = Root.position; p.y = y; Root.position = p;
            Shadow.localPosition = new Vector3(0f, 0.02f - y, 0f);
            colorRing.localPosition = new Vector3(0f, 0.03f - y, 0f);
        }

        // ---------- Vida ----------
        public bool Hurt(float amount, float sx, float sz, float knock = 9f, HurtOpts opts = default(HurtOpts))
        {
            if (Dead || CarriedBy != null || Spectator) return false;
            if (Iframes > 0f && !opts.Pierce) return false;
            if (G.SoloHard && !opts.Raw) amount *= SoloConfig.DamageTakenMult;   // solo: todo pega más fuerte
            float dx = X - sx, dz = Z - sz;
            float l = MathUtil.Hypot(dx, dz); if (l == 0f) l = 1f;
            if (!opts.Raw)
            {
                var r = Gear.ModifyIncoming(amount * Ability.DamageTakenMult, opts.Kind);
                if (r.Blocked)
                {
                    // Escudo de emergencia: el impacto entero se anula
                    Iframes = 0.6f;
                    G.Audio.Play("clang");
                    G.Effects.Ring(X, Z, new RingOpts { Color = 0x7dd6ff, Radius = 1.4f, Life = 0.3f, Y = 1f, R0 = 0.3f, Opacity = 0.8f });
                    G.Ui.FloatText(X, 2.7f, Z, "¡BLOQUEADO!", TextStyle.Label);
                    G.Ui.SetGear(Index, Gear);
                    return false;
                }
                amount = r.Amount;
                if (r.ArmorHit)
                {
                    G.Ui.SetGear(Index, Gear);
                    if (r.ArmorBroke)
                    {
                        Gear.RefreshVisual(Parts);
                        G.Audio.Play("break");
                        G.Ui.FloatText(X, 2.9f, Z, "¡Se rompe la armadura!", TextStyle.Big);
                    }
                }
                knock *= Gear.KnockTakenMult();
            }
            if (Ability.KnockImmune) knock = 0f;
            Hp = Mathf.Max(0f, Hp - amount);
            if (Hp <= 0f && opts.Stomp) FlatDeath = true;
            Iframes = Mathf.Max(Iframes, 0.9f);
            if (knock != 0f) Knock(dx / l * knock, dz / l * knock);
            Flash(0.25f, 0xff2020);
            if (FeedT > 0f) { FeedT = 0f; G.Ui.SetPlayerBar(Index, -1f, BarKind.None); }
            if (!Ability.Unstoppable)
            {
                CancelAttack();
                CancelEating();   // un golpe interrumpe la comida
            }
            G.OnPlayerHurt(this, amount, opts);
            if (Hp <= 0f) Die();
            return true;
        }

        public void Heal(float n)
        {
            Hp = Mathf.Min(MaxHp, Hp + n);
            G.Ui.SetHealth(Index, Hp, MaxHp);
        }

        public void Die()
        {
            OnDieImpl();
            Dead = true;
            CancelAttack();
            StunT = 0f; ConfusedT = 0f;
            Ability.Finish();
            EndJump();
            Exhausted = false;
            ReviveT = 0f;
            DeadT = 0f;
            Body.Mvx = Body.Mvz = 0f;
            Body.Ghost = true;   // los demás le pasan por encima
            Vx = Vz = 0f;
            Model.gameObject.SetActive(true);
            SetRootY(0f);
            G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
            G.OnPlayerDeath(this);
        }

        partial void OnDieImpl();

        public void Revive(float hp)
        {
            Dead = false;
            FlatDeath = false;
            Model.localScale = Vector3.one;
            Hp = hp;
            Body.Ghost = false;
            Iframes = 1.5f;
            Model.localRotation = Quaternion.identity;
            Model.localPosition = Vector3.zero;
            ReviveT = 0f;
            G.Ui.SetHealth(Index, Hp, MaxHp);
            G.Ui.SetPlayerBar(Index, -1f, BarKind.None);
            // Corrección pedida por el usuario: el original dejaba el panel en "DERRIBADO" hasta el siguiente piso
            G.Ui.SetDowned(Index, false);
            G.Audio.Play("heal");
            G.Ui.FloatText(X, 2.6f, Z, "¡De vuelta!", TextStyle.Heal);
        }

        void UpdateDeath(float dt)
        {
            Body.Mvx = Body.Mvz = 0f;
            DeadT += dt;
            if (FlatDeath)
            {
                // Aplastado como una tortita (se recupera al reanimarle)
                float kf = Mathf.Min(1f, DeadT / 0.08f);
                Model.localRotation = Quaternion.identity;
                Model.localPosition = Vector3.zero;
                Model.localScale = new Vector3(1f + 0.6f * kf, Mathf.Max(0.1f, 1f - 0.9f * kf), 1f + 0.6f * kf);
                return;
            }
            float k = Mathf.Min(1f, DeadT / 0.5f);
            Model.localRotation = ProcMesh.EulerXYZ(-MathUtil.EaseOutCubic(k) * Mathf.PI / 2f, 0f, 0f);
            Model.localPosition = new Vector3(0f, 0.3f * k, 0f);
        }
    }
}
