using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // ⛓️ GRUM Y GROM, LOS GEMELOS ENCADENADOS (TwinsBoss.js) — dos ogros hermanos unidos por una cadena.
    // Mecánicas cooperativas:
    //  - REPARTIRSE: cada jugador con un gemelo. Grum pega cuerpo a cuerpo; Grom dispara bolas devolvibles.
    //  - LA CADENA: con los gemelos a más de 6 unidades está TENSA (roja) y su línea daña a quien esté en el suelo
    //    sobre ella. Hay que saltarla. Con los gemelos juntos está floja (gris) y no daña.
    //  - ESCUDO FRATERNO: Grom canaliza 3 s brillando en azul y Grum queda protegido (-85 % de daño) 5 s.
    //    Si alguien golpea a Grom mientras canaliza, se interrumpe y queda aturdido 1 s.
    //  - BARRIDO DE CADENA: se separan a los lados de la arena y bajan juntos: la cadena tensa barre el suelo.
    //  - VÍNCULO DE VIDA: si cae uno, el otro se enfurece y tenéis 8 s para derrotarlo. Si no, el caído revive al 40 %.
    public abstract class TwinBoss : Boss
    {
        public const float ShieldMult = 0.15f;     // escudo: el gemelo protegido recibe el 15 % del daño (-85 %)
        public const float ShieldTime = 5f;        // segundos que dura el escudo tras la canalización
        public const float ChainLen = 6f;          // distancia a partir de la que la cadena está tensa
        public const float ChainMax = 11f;         // largo máximo: si se separan más, Grom vuelve hacia Grum
        public const float ChainReach = 0.55f;     // a menos de esto del segmento, el jugador queda enganchado
        public const float ChainHurt = 12f, ChainKnock = 9f, ChainCd = 0.8f;
        public const float ReviveTime = 8f;        // segundos para derrotar al superviviente tras caer el otro
        public const float ReviveHp = 0.4f;        // fracción de la vida máxima con la que vuelve
        public const float Fury = 1.4f;            // velocidad del superviviente enfurecido

        public sealed class ReviveCountdown { public float X, Z, T; public int Shown = -1; public TwinBoss Fallen; public Transform Ring; }

        public TwinBoss Twin;
        public bool ShieldOn, Channeling, Furious;
        public float ShieldT;
        public ReviveCountdown ReviveCd;
        public Vector2? ForcedMv;                 // lo fija Grum durante el barrido de cadena
        protected Transform bubble, orb;

        protected TwinBoss(Game g, float x, float z, EnemyCfg cfg) : base(g, x, z, cfg) { }

        protected abstract TwinBoss Reborn(float x, float z);

        public static void Link(TwinBoss a, TwinBoss b) { a.Twin = b; b.Twin = a; }

        // Velocidad deseada hacia un punto
        public static Vector2 Steer(Boss b, float tx, float tz, float sp)
        {
            float dx = tx - b.X, dz = tz - b.Z, d = MathUtil.Hypot(dx, dz);
            if (d < 0.3f) return Vector2.zero;
            return new Vector2(dx / d * sp, dz / d * sp);
        }

        // Jugador atacable más alejado del gemelo (o de él mismo si no hay gemelo vivo)
        public Player Farthest()
        {
            Boss r = Twin != null && !Twin.Dead ? (Boss)Twin : this;
            Player far = null; float best = -1f;
            foreach (var p in G.Players)
            {
                if (!p.Targetable) continue;
                float d = MathUtil.Hypot(p.X - r.X, p.Z - r.Z);
                if (d > best) { best = d; far = p; }
            }
            return far;
        }

        // Cada frame: escudo, burbuja, orbe de canalización y cuenta atrás de revivir
        void Tick(float dt)
        {
            if (ShieldT > 0f) { ShieldT -= dt; if (ShieldT <= 0f) ShieldOn = false; }
            if (bubble) Show(bubble, Twin != null && Twin.ShieldOn && !Dead);
            if (orb) Show(orb, Channeling && !Dead);
            var cd = ReviveCd;
            if (cd != null && !Dead)
            {
                cd.T -= dt;
                int n = Mathf.Max(0, Mathf.CeilToInt(cd.T));
                if (n != cd.Shown && n > 0) G.Ui.FloatText(cd.X, 2.8f, cd.Z, "Revive en " + n + " s", TextStyle.Big);
                cd.Shown = n;
                Shape.SetOpacity(cd.Ring.GetComponent<Renderer>(), 0.5f + 0.25f * Mathf.Sin(Anim * 8f));
                if (cd.T <= 0f) Revive();
            }
        }

        protected static void Show(Transform t, bool on) { if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

        public override void Update(float dt)
        {
            if (Removed) return;
            Tick(dt);
            BeforeUpdate(dt);
            base.Update(dt);
        }

        protected virtual void BeforeUpdate(float dt) { }

        // Al morir (antes de la base): el caído retiene la victoria mientras el superviviente pueda revivirlo
        void OnTwinDie()
        {
            ClearRevive();
            ShieldOn = false; ShieldT = 0f; Channeling = false;
            var tw = Twin;
            if (tw != null && tw.Dead && tw.HoldVictory) tw.HoldVictory = false;
            if (tw != null && !tw.Dead && !tw.Removed)
            {
                HoldVictory = true;
                tw.StartFury(this);
            }
            else HoldVictory = false;
        }

        // El superviviente se enfurece y empieza la cuenta atrás junto al caído
        void StartFury(TwinBoss fallen)
        {
            if (!Furious) { Furious = true; Cfg.Speed *= Fury; }
            ClearRevive();
            Say("¡HERMANO!");
            G.Audio.Play("bossRoar");
            G.CameraCtl.AddShake(0.4f);
            var ring = Shape.Ring(fallen.X, fallen.Z, 0.9f, 1.25f).BuildMesh(G.FloorRoot, 0xff2a2a, 0.7f, 0.07f);
            ReviveCd = new ReviveCountdown { X = fallen.X, Z = fallen.Z, T = ReviveTime, Fallen = fallen, Ring = ring };
        }

        public void ClearRevive()
        {
            var cd = ReviveCd;
            if (cd == null) return;
            ReviveCd = null;
            Shape.DestroyMeshObject(cd.Ring);
        }

        // Se acaba la cuenta atrás: nace de nuevo el caído, enlazado con el superviviente
        void Revive()
        {
            var cd = ReviveCd; var fallen = cd.Fallen;
            ClearRevive();
            var nb = fallen.Reborn(cd.X, cd.Z);
            Link(this, nb);
            nb.Hp = Mathf.Max(1, MathUtil.RoundJs(nb.MaxHp * ReviveHp));
            nb.Phase = nb.Hp / nb.MaxHp <= 0.5f ? 2 : 1;
            fallen.HoldVictory = false;
            if (G.BossFloor != null) G.BossFloor.AddBoss(nb);
            nb.Say("¡HE VUELTO!");
            G.Audio.Play("bossRoar");
            G.CameraCtl.AddShake(0.6f);
            G.Effects.Burst(cd.X, 1f, cd.Z, BurstOpts.Of(16, 0xff3020, 4f, 4f, 0.15f));
        }

        public override void Die(string cause)
        {
            if (Dead) return;
            OnTwinDie();
            base.Die(cause);
        }

        public override void Destroy()
        {
            ClearRevive();
            base.Destroy();
        }
    }

    // ---------------------------------------------------------------- GRUM (rojo, cuerpo a cuerpo)
    public sealed class GrumBoss : TwinBoss
    {
        Transform armR, body3d;
        Transform chain;
        readonly List<Transform> links = new List<Transform>();
        Renderer[] linkR;
        readonly Dictionary<Player, float> chainCd = new Dictionary<Player, float>();
        public bool ChainTense;
        public bool BubbleVisible { get { return bubble && bubble.gameObject.activeSelf; } }
        Telegraph hamTelegraph, carTelegraph;
        Vector2? chargeDir;
        readonly HashSet<Player> chargeHit = new HashSet<Player>();
        string sweepStep; float sweepT;
        float armX, leanX;

        public GrumBoss(Game g, float x, float z) : base(g, x, z, BossCfg(c =>
        {
            c.Name = "Grum"; c.Hp = 300; c.Speed = 2.6f; c.Radius = 1.0f; c.Mass = 9; c.BarY = 2.9f; c.Coins = new[] { 20, 25 };
        }))
        {
            AddPattern(new BossPattern
            {
                Id = "martillazo", Weight = 2, Cooldown = 3, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => dist < 5.5f,
                Start = b =>
                {
                    if (b.Target != null) b.FaceTo(b.Target.X, b.Target.Z);
                    float cx = b.X + Mathf.Sin(b.Facing) * 2.2f, cz = b.Z + Mathf.Cos(b.Facing) * 2.2f;
                    hamTelegraph = b.Telegraph(Shape.Circle(cx, cz, 2.2f), 0.8f, h =>
                    {
                        hamTelegraph = null;
                        BossHits.HitPlayersIn(G, h.Shape, 18, 12, b.X, b.Z, true);
                        if (b.Phase >= 2) b.Wave(new Wave { Speed = 7, MaxR = 10, Damage = 10 });
                    });
                },
                Update = (b, dt, pl, dist) => b.PatternT > 1.1f,
                End = b => { if (hamTelegraph != null) { hamTelegraph.Cancel(); hamTelegraph = null; } },
            });

            AddPattern(new BossPattern
            {
                Id = "carga", Weight = 2, Cooldown = 5, InitialCd = 2, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => dist > 3f && dist < 14f,
                Start = b =>
                {
                    var t = b.Target;
                    if (t == null) return;
                    float d = MathUtil.Hypot(t.X - b.X, t.Z - b.Z); if (d == 0f) d = 1f;
                    float ux = (t.X - b.X) / d, uz = (t.Z - b.Z) / d;
                    chargeDir = new Vector2(ux, uz);
                    b.Facing = Mathf.Atan2(ux, uz);
                    chargeHit.Clear();
                    carTelegraph = b.Telegraph(Shape.Line(b.X, b.Z, b.X + ux * 9f, b.Z + uz * 9f, 1.8f), 0.9f, null);
                },
                Update = (b, dt, pl, dist) =>
                {
                    if (chargeDir == null) return true;
                    float t = b.PatternT - 0.9f;          // tiempo de carga (tras el aviso de 0,9 s)
                    if (t < 0f) return false;
                    if (t < 0.6f)
                    {
                        b.Mv = chargeDir.Value * 13f;
                        BossHits.HitPlayersIn(G, Shape.Circle(b.X, b.Z, b.Radius + 0.8f), 18, 14, b.X, b.Z, true, chargeHit);
                        return false;
                    }
                    return t >= 0.9f;                     // pequeña pausa al acabar
                },
                End = b =>
                {
                    if (carTelegraph != null) { carTelegraph.Cancel(); carTelegraph = null; }
                    chargeDir = null;
                },
            });

            // Barrido de cadena (en el web: lados x 4 y x 28, bajando hacia +z; aquí z invertida)
            AddPattern(new BossPattern
            {
                Id = "barrido", Weight = 1, Cooldown = 14, InitialCd = 9, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => b.IsAwake && Twin != null && !Twin.Dead && Twin.IsAwake,
                Start = b =>
                {
                    Twin.CancelPattern();              // Grom deja lo que hacía
                    b.Say("¡SALTAD LA CADENA!");
                    sweepStep = "gather"; sweepT = 0f;
                },
                Update = (b, dt, pl, dist) =>
                {
                    var tw = Twin;
                    if (tw == null || tw.Dead) return true;
                    sweepT += dt;
                    if (sweepStep == "gather")
                    {
                        b.Mv = Steer(b, 4, -8, 5.2f);
                        tw.ForcedMv = Steer(tw, 28, -8, 5.2f);
                        bool near = MathUtil.Hypot(b.X - 4, b.Z + 8) < 0.6f && MathUtil.Hypot(tw.X - 28, tw.Z + 8) < 0.6f;
                        if (near || sweepT > 2.2f) { sweepStep = "sweep"; sweepT = 0f; }
                        return false;
                    }
                    // Bajan juntos manteniendo cada uno su lado: la cadena tensa barre la arena
                    b.Mv = new Vector2((4 - b.X) * 4f, -5.4f);
                    tw.ForcedMv = new Vector2((28 - tw.X) * 4f, -5.4f);
                    return sweepT >= 3.2f || b.Z <= -23.5f;
                },
                End = b =>
                {
                    if (Twin != null) Twin.ForcedMv = null;
                    sweepStep = null;
                },
            });
        }

        protected override TwinBoss Reborn(float x, float z) { return new GrumBoss(G, x, z); }

        protected override void BuildModel()
        {
            Color red = MathUtil.Hex(0xb3261e), dred = MathUtil.Hex(0x6e1410), stone = MathUtil.Hex(0x8a8478), bone = MathUtil.Hex(0xd8ccb0), wood = MathUtil.Hex(0x4a2e1a);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Icosahedron(0.9f, 1), Matrix4x4.TRS(new Vector3(0, 1.2f, 0), Quaternion.identity, new Vector3(1f, 1.1f, 0.9f)), red);
            b.Add(ProcMesh.Icosahedron(0.55f, 1), new Vector3(0, 1.0f, 0.45f), dred);
            b.Add(ProcMesh.Icosahedron(0.45f, 1), new Vector3(0, 2.25f, 0.1f), red);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Add(ProcMesh.Cone(0.12f, 0.5f, 5), Matrix4x4.TRS(new Vector3(s * 0.3f, 2.6f, 0.05f), ProcMesh.EulerXYZ(0, 0, -s * 0.5f), Vector3.one), bone);
                b.Add(ProcMesh.Box(0.35f, 0.7f, 0.4f), new Vector3(s * 0.4f, 0.35f, 0), dred);
            }
            body3d = new GameObject("Grum").transform;
            body3d.SetParent(Model, false);
            CharacterModel.MeshObject(body3d, "Cuerpo", b.ToMesh("grum"), Mats.Lit, Vector3.zero);
            var eyes = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f }) eyes.Add(ProcMesh.Sphere(0.06f, 5, 4), new Vector3(s * 0.17f, 2.3f, 0.45f), MathUtil.Hex(0xffe14a));
            CharacterModel.MeshObject(body3d, "Ojos", eyes.ToMesh("ojosGrum"), Mats.Unlit, Vector3.zero);
            // Martillo de piedra en el brazo derecho (pivota en el hombro; hacia +Z es el frente)
            armR = new GameObject("Brazo").transform;
            armR.SetParent(body3d, false);
            armR.localPosition = new Vector3(0.85f, 1.7f, 0);
            var ab = new MeshBuilder();
            ab.Add(ProcMesh.Box(0.3f, 0.3f, 1.1f), new Vector3(0, 0, 0.5f), red);
            ab.Add(ProcMesh.Box(0.14f, 0.14f, 1.2f), new Vector3(0, 0, 1.4f), wood);
            ab.Add(ProcMesh.Box(0.7f, 0.6f, 0.9f), new Vector3(0, 0, 2.0f), stone);
            CharacterModel.MeshObject(armR, "Martillo", ab.ToMesh("martilloGrum"), Mats.Lit, Vector3.zero);
            // Burbuja del escudo fraterno (visible solo cuando Grom lo tiene activo)
            bubble = CharacterModel.MeshObject(body3d, "Burbuja", new MeshBuilder().Add(ProcMesh.Sphere(1.7f, 14, 10), Vector3.zero, MathUtil.Hex(0x4aa8ff)).ToMesh("burbuja"), Mats.Transparent, new Vector3(0, 1.3f, 0)).transform;
            Shape.SetOpacity(bubble.GetComponent<Renderer>(), 0.28f);
            bubble.gameObject.SetActive(false);
        }

        protected override void Animate(float dt)
        {
            float t = Anim, arm = 0.2f, lean = 0f;
            if (Is("martillazo")) arm = PatternT < 0.6f ? -2.4f : 1.0f;   // sube el martillo y lo deja caer
            else if (Is("carga")) { arm = 0.6f; lean = PatternT > 0.9f ? 0.3f : 0f; }
            else if (Is("barrido")) arm = 0.5f + Mathf.Sin(t * 6f) * 0.1f;
            armX = MathUtil.Damp(armX, arm, 10f, dt);
            leanX = MathUtil.Damp(leanX, lean, 6f, dt);
            armR.localRotation = ProcMesh.EulerXYZ(armX, 0, 0);
            body3d.localRotation = ProcMesh.EulerXYZ(leanX, 0, 0);
            body3d.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(t * 4f)) * 0.05f, 0);
        }

        protected override void OnPhase(int n) { Say("¡BASTA DE JUGAR!"); }

        protected override void BeforeUpdate(float dt) { UpdateChain(dt); }

        // La cadena: se dibuja entre los dos gemelos y, si está tensa, daña a quien esté en el suelo sobre ella
        void UpdateChain(float dt)
        {
            var tw = Twin;
            bool linked = !Dead && !Removed && tw != null && !tw.Dead && !tw.Removed;
            if (!linked) { RemoveChain(); ChainTense = false; return; }
            if (!chain) MakeChain();
            float ax = X, az = Z, bx = tw.X, bz = tw.Z;
            float L = MathUtil.Hypot(bx - ax, bz - az); if (L == 0f) L = 0.001f;
            bool tense = L > ChainLen;
            ChainTense = tense;
            float ux = (bx - ax) / L, uz = (bz - az) / L;
            int N = links.Count;
            var col = tense ? MathUtil.Hex(0xff2a2a) : MathUtil.Hex(0x9a9a9a);
            for (int i = 0; i < N; i++)
            {
                float k = i / (float)(N - 1);
                // Tensa: recta a la altura del pecho. Floja: se comba hacia el suelo
                links[i].position = new Vector3(ax + (bx - ax) * k, 1.3f - (tense ? 0f : 0.7f * Mathf.Sin(Mathf.PI * k)), az + (bz - az) * k);
                links[i].rotation = Quaternion.Euler(0, (Mathf.Atan2(-uz, ux) + (i % 2 == 1 ? Mathf.PI / 2f : 0f)) * Mathf.Rad2Deg, 0);
                SetColor(linkR[i], col);
            }
            // Daño: solo con los dos en combate, la cadena tensa y el jugador en el suelo sobre ella
            bool armed = tense && IsAwake && tw.IsAwake;
            foreach (var p in G.Players.ToArray())
            {
                float prev; chainCd.TryGetValue(p, out prev);
                float left = prev - dt;
                chainCd[p] = Mathf.Max(0f, left);
                if (!armed || left > 0f || !p.Targetable || p.IsThrown || p.JumpY >= 0.5f) continue;
                if (Shape.SegDist(p.X, p.Z, ax, az, bx, bz) >= ChainReach) continue;
                // Empuje perpendicular a la cadena, hacia el lado en que está el jugador
                float px = -uz, pz = ux;
                float side = ((p.X - ax) * px + (p.Z - az) * pz) >= 0f ? 1f : -1f;
                if (p.Hurt(ChainHurt, p.X - px * side, p.Z - pz * side, ChainKnock))
                {
                    chainCd[p] = ChainCd;
                    G.Ui.FloatText(p.X, 2.6f, p.Z, "¡CADENA!", TextStyle.Label);
                }
            }
        }

        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Mats.BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }

        static Mesh linkMesh;
        void MakeChain()
        {
            if (!linkMesh) linkMesh = new MeshBuilder().Add(ProcMesh.Torus(0.2f, 0.05f, 6, 10), Vector3.zero, Color.white).ToMesh("eslabon");
            chain = new GameObject("Cadena").transform;
            chain.SetParent(G.FloorRoot, false);
            links.Clear();
            for (int i = 0; i < 12; i++) links.Add(CharacterModel.MeshObject(chain, "Eslabon", linkMesh, Mats.Unlit, Vector3.zero).transform);
            linkR = links.ConvertAll(l => l.GetComponent<Renderer>()).ToArray();
        }

        void RemoveChain()
        {
            if (!chain) return;
            Object.Destroy(chain.gameObject);
            chain = null;
            links.Clear();
        }

        // Escudo fraterno: si Grom lo tiene activo, Grum recibe el 15 % del golpe
        protected override Hit ModifyIncoming(Hit hit)
        {
            if (Twin != null && Twin.ShieldOn && !Dead) hit.Damage *= ShieldMult;
            return hit;
        }

        // Daño del entorno (paredes, bolos...) también se reduce con el escudo
        public override void Damage(float amount, bool env, string label)
        {
            if (env && Twin != null && Twin.ShieldOn) amount *= ShieldMult;
            base.Damage(amount, env, label);
        }

        public override void Destroy()
        {
            RemoveChain();
            base.Destroy();
        }
    }

    // ---------------------------------------------------------------- GROM (azul, a distancia)
    public sealed class GromBoss : TwinBoss
    {
        Transform staffArm, body3d;
        float? bolasDir;
        bool bolasDone;
        float staffX;

        public GromBoss(Game g, float x, float z) : base(g, x, z, BossCfg(c =>
        {
            c.Name = "Grom"; c.Hp = 220; c.Speed = 2.2f; c.Radius = 0.8f; c.Mass = 6; c.BarY = 2.5f; c.Coins = new[] { 20, 25 };
        }))
        {
            AddPattern(new BossPattern
            {
                Id = "bolas", Weight = 3, Cooldown = 3.5f, InitialCd = 1.5f, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => b.AlivePlayers().Count > 0,
                Start = b =>
                {
                    var far = Farthest();
                    bolasDone = false;
                    if (far == null) { bolasDir = null; return; }
                    bolasDir = Mathf.Atan2(far.X - b.X, far.Z - b.Z);
                    b.FaceTo(far.X, far.Z);
                    b.Telegraph(Shape.Line(b.X, b.Z, far.X, far.Z, 0.35f), 0.6f, null, 0x60a0ff);
                },
                Update = (b, dt, pl, dist) =>
                {
                    b.Mv = Vector2.zero;
                    if (bolasDir == null) return true;
                    if (!bolasDone && b.PatternT >= 0.6f) { bolasDone = true; FireBolas(); }
                    return b.PatternT > 1.0f;
                },
            });

            AddPattern(new BossPattern
            {
                Id = "escudo", Weight = 2, Cooldown = 10, InitialCd = 5, Phases = new[] { 1, 2 },
                Can = (b, pl, dist) => Twin != null && !Twin.Dead && !ShieldOn,
                Start = b => { Channeling = true; b.Say("¡ESCUDO FRATERNO!"); },
                Update = (b, dt, pl, dist) =>
                {
                    b.Mv = Vector2.zero;
                    if (Twin == null || Twin.Dead) return true;
                    if (b.PatternT >= 3f)
                    {
                        Channeling = false;
                        ShieldOn = true; ShieldT = ShieldTime;
                        G.Ui.FloatText(b.X, 2.6f, b.Z, "¡ESCUDO ACTIVO!", TextStyle.Label);
                        G.Audio.Play("clang");
                        return true;
                    }
                    return false;
                },
                End = b => { Channeling = false; },
            });
        }

        protected override TwinBoss Reborn(float x, float z) { return new GromBoss(G, x, z); }

        // Tres bolas (cinco en la fase 2) en abanico en la dirección fijada al empezar el patrón
        void FireBolas()
        {
            int n = Phase >= 2 ? 5 : 3; float step = 0.25f, bas = bolasDir.Value;
            for (int i = 0; i < n; i++)
            {
                float a = bas + (i - (n - 1) / 2f) * step;
                Shoot(new Projectile
                {
                    PX = X + Mathf.Sin(a) * 1.2f, Y = 1.4f, PZ = Z + Mathf.Cos(a) * 1.2f,
                    Vx = Mathf.Sin(a) * 8f, Vz = Mathf.Cos(a) * 8f,
                    Damage = 12, ProjRadius = 0.35f, Color = 0x60a0ff, Reflectable = true, Life = 4,
                });
            }
            G.Audio.Play("throw");
        }

        protected override void BuildModel()
        {
            Color blue = MathUtil.Hex(0x2f6fd0), dblue = MathUtil.Hex(0x183a7a), skin = MathUtil.Hex(0x3a7ad8), wood = MathUtil.Hex(0x4a2e1a), dark = MathUtil.Hex(0x102040);
            var b = new MeshBuilder();
            b.Add(ProcMesh.Cylinder(0.4f, 0.5f, 1.0f, 8), new Vector3(0, 0.75f, 0), blue);
            b.Add(ProcMesh.Icosahedron(0.34f, 1), new Vector3(0, 1.55f, 0.05f), skin);
            b.Add(ProcMesh.Cone(0.42f, 0.85f, 8), new Vector3(0, 1.85f, -0.02f), dblue);
            foreach (float s in new[] { -1f, 1f }) b.Add(ProcMesh.Box(0.28f, 0.6f, 0.32f), new Vector3(s * 0.2f, 0.3f, 0), dark);
            body3d = new GameObject("Grom").transform;
            body3d.SetParent(Model, false);
            CharacterModel.MeshObject(body3d, "Cuerpo", b.ToMesh("grom"), Mats.Lit, Vector3.zero);
            var eyes = new MeshBuilder();
            foreach (float s in new[] { -1f, 1f }) eyes.Add(ProcMesh.Sphere(0.05f, 5, 4), new Vector3(s * 0.12f, 1.5f, 0.3f), MathUtil.Hex(0xbfe0ff));
            CharacterModel.MeshObject(body3d, "Ojos", eyes.ToMesh("ojosGrom"), Mats.Unlit, Vector3.zero);
            // Bastón con llama azul en la mano derecha (se inclina al lanzar las bolas)
            staffArm = new GameObject("Baston").transform;
            staffArm.SetParent(body3d, false);
            staffArm.localPosition = new Vector3(0.5f, 1.0f, 0.1f);
            CharacterModel.MeshObject(staffArm, "Palo", new MeshBuilder().Add(ProcMesh.Cylinder(0.035f, 0.045f, 1.9f, 5), new Vector3(0, 0.6f, 0), wood).ToMesh("bastonGrom"), Mats.Lit, Vector3.zero);
            CharacterModel.MeshObject(staffArm, "Llama", new MeshBuilder().Add(ProcMesh.Icosahedron(0.2f), new Vector3(0, 1.7f, 0), MathUtil.Hex(0x60a0ff)).ToMesh("llamaGrom"), Mats.Unlit, Vector3.zero);
            // Orbe del escudo: solo visible mientras canaliza
            orb = CharacterModel.MeshObject(body3d, "Orbe", new MeshBuilder().Add(ProcMesh.Sphere(0.3f, 8, 6), Vector3.zero, MathUtil.Hex(0x40a0ff)).ToMesh("orbe"), Mats.Transparent, new Vector3(0, 1.2f, 0.5f)).transform;
            Shape.SetOpacity(orb.GetComponent<Renderer>(), 0.55f);
            orb.gameObject.SetActive(false);
        }

        protected override void Animate(float dt)
        {
            float staff = Is("bolas") && PatternT < 0.6f ? 0.7f : 0f;
            staffX = MathUtil.Damp(staffX, staff, 8f, dt);
            staffArm.localRotation = ProcMesh.EulerXYZ(staffX, 0, 0);
            if (orb.gameObject.activeSelf) orb.localScale = Vector3.one * (1f + Mathf.Sin(Anim * 12f) * 0.15f);
            body3d.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Anim * 4f)) * 0.04f, 0);
        }

        // Prefiere mantenerse a 6–8 unidades del jugador
        protected override Vector2 Idle(float dt, Player player, float dist, float dx, float dz)
        {
            var tw = Twin;
            if (tw != null && !tw.Dead && MathUtil.Hypot(tw.X - X, tw.Z - Z) > ChainMax)
            {
                var v = Steer(this, tw.X, tw.Z, Cfg.Speed * 1.2f);
                if (v != Vector2.zero) Facing = Mathf.Atan2(v.x, v.y);
                return v;
            }
            if (player == null) return Vector2.zero;
            float l0 = dist > 0f ? dist : 1f;
            float ux = dx / l0, uz = dz / l0;
            Facing = MathUtil.DampAngle(Facing, Mathf.Atan2(ux, uz), 5f, dt);
            float rad = 0f;                                   // + acercarse, - alejarse
            if (dist < 6f) rad = -1f; else if (dist > 8f) rad = 1f;
            float side = Mathf.Sin(Anim * 0.8f) > 0f ? 1f : -1f;
            float strafe = dist >= 6f && dist <= 8f ? 0.8f : 0.35f;
            float mx = ux * rad - uz * side * strafe, mz = uz * rad + ux * side * strafe;
            float l = MathUtil.Hypot(mx, mz); if (l == 0f) l = 1f;
            var dir = AvoidHazards(new Vector2(mx / l, mz / l));
            return new Vector2(dir.x * Cfg.Speed, dir.y * Cfg.Speed);
        }

        protected override bool UpdateState(float dt, Player player, float dist, float dx, float dz, out Vector2 mv)
        {
            // Grum lo lleva a su lado del barrido: no ataca mientras tanto
            if (ForcedMv != null && IsAwake && Controllable && State == EnemyState.Custom) { mv = ForcedMv.Value; return true; }
            return base.UpdateState(dt, player, dist, dx, dz, out mv);
        }

        public override void TakeHit(Hit hit)
        {
            bool was = Channeling && IsAwake && !Dead;
            base.TakeHit(hit);
            if (was && !Dead) Interrupt();
        }

        // Golpe a Grom mientras canaliza: se interrumpe y queda aturdido 1 s
        void Interrupt()
        {
            Channeling = false;
            CancelPattern();
            Stun = Mathf.Max(Stun, 1f);
            G.Ui.FloatText(X, 2.5f, Z, "¡INTERRUMPIDO!", TextStyle.Big);
            G.Audio.Play("clang");
        }

        protected override void OnPhase(int n) { Say("¡FUEGO PARA TODOS!"); }
    }

    public static partial class BossRegistrations
    {
        static partial void Twins()
        {
            Bosses.Register("twins", new BossEntry
            {
                Name = "Grum y Grom", Title = "Los Gemelos Encadenados", Icon = "⛓️",
                Hint = "Repartíos: uno con cada gemelo. Si cae uno, acabad con el otro antes de 8 s o revivirá. Saltad la cadena cuando esté roja.",
                ArenaW = 17, ArenaH = 15,
                Decorate = (put, W, H) => { put(1, 3, 'b'); put(W - 2, 3, 'b'); },
                Spawn = (g, x, z) =>
                {
                    var grum = new GrumBoss(g, x - 3, z); var grom = new GromBoss(g, x + 3, z);
                    TwinBoss.Link(grum, grom);
                    return new List<Boss> { grum, grom };
                },
            });
        }
    }
}
