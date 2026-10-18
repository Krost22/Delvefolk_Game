using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Delvefolk
{
    // Estadísticas de la partida
    public sealed class RunStats
    {
        public int Kills, EnvKills, Abilities, Stomps, Coins, Broken, Fed, Bosses;
        public float EnvDamage, Time;
    }

    public struct LoadOpts
    {
        public bool Preview, Bonus, Shop, Boss;
    }

    // Escena, luces y carga de pisos
    public sealed partial class Game
    {
        public CameraRig CameraCtl;
        public Effects Effects;
        public Hud Ui;
        public Transform FloorRoot;
        public Dungeon Dungeon;
        public DungeonView View;
        public CirclePhysics Physics;
        public LevelDef Def;
        public float HpMult = 1f;
        public bool InBonus, InShop, InBoss;
        public float DeathTimer = -1f;
        public int RunId;
        public RunStats Stats = new RunStats();
        public readonly Dictionary<string, bool> SeenHints = new Dictionary<string, bool>();
        public readonly List<Player> Players = new List<Player>();
        public readonly List<StaticProp> Statics = new List<StaticProp>();
        public readonly PlayerLoadout[] Loadouts = new PlayerLoadout[ControlSchemes.MaxPlayers];
        Light moon;
        public readonly Light[] PlayerLights = new Light[ControlSchemes.MaxPlayers];
        // Dispositivo de cada hueco de jugador (se elige en la sala o al unirse a mitad de partida)
        public readonly DeviceBinding[] Bindings = { ControlSchemes.DefaultFor(0), ControlSchemes.DefaultFor(1), ControlSchemes.DefaultFor(2), ControlSchemes.DefaultFor(3) };

        // ¿Algún jugador presente (o de los primeros n huecos) usa este dispositivo?
        public bool DeviceTaken(DeviceBinding b, int except = -1, int slots = -1)
        {
            int n = slots >= 0 ? slots : Players.Count;
            for (int i = 0; i < n && i < Bindings.Length; i++) if (i != except && Bindings[i].Equals(b)) return true;
            return false;
        }

        // Cambia el dispositivo de un jugador (también el de su personaje si ya está en la partida)
        public void SetBinding(int i, DeviceBinding b)
        {
            Bindings[i] = b;
            if (i < Players.Count) Players[i].Controls.Bind(b);
            if (i < Players.Count) ApplyPlayerSetup();
        }

        public ControlScheme SchemeOf(int i) { return i < Players.Count ? Players[i].Controls.S : ControlSchemes.For(Bindings[i]); }

        public Player Player1 { get { return Players.Count > 0 ? Players[0] : null; } }
        public PlayerLoadout LoadoutFor(int i) { return Loadouts[i]; }

        partial void Init()
        {
            // Cámara isométrica: FOV 38, planos 0.5–140, fondo del color de la niebla
            var camGo = new GameObject("Camara");
            camGo.transform.SetParent(SceneRoot, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 140f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = MathUtil.Hex(0x050407);
            cam.cullingMask = ~(1 << CharacterPreview.Layer);   // la vista previa de la sala tiene su propia cámara
            camGo.AddComponent<AudioListener>();
            // Sonidos sintetizados al arrancar; el mezclador va junto al oyente
            AudioSynth.Prewarm();
            Audio.Attach(camGo);
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            data.renderShadows = false;
            CameraCtl = new CameraRig(cam);

            // Luz base tenue: la mazmorra es oscura, las antorchas y las lámparas de los jugadores hacen el resto
            Lighting.ApplyAmbient();
            var moonGo = new GameObject("Luna");
            moonGo.transform.SetParent(SceneRoot, false);
            moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = MathUtil.Hex(0x9fb0ff);
            moon.intensity = 0.25f * Lighting.DirScale;
            moon.shadows = LightShadows.None;
            // Posición web (−10, 20, 5) mirando al origen → en Unity desde (−10, 20, −5)
            moonGo.transform.rotation = Quaternion.LookRotation(new Vector3(10f, -20f, 5f));
            // Una luz por jugador (siempre existen las dos para no recompilar shaders)
            for (int i = 0; i < ControlSchemes.MaxPlayers; i++) PlayerLights[i] = Lighting.Point(SceneRoot, "Lampara" + (i + 1), 0xffd9a0, 11f);
            Lighting.SetFog(0x050407, 24f, 46f);

            Effects = new Effects(SceneRoot, FxRand);
            Ui = new Hud(this, SceneRoot);
            for (int i = 0; i < ControlSchemes.MaxPlayers; i++) Loadouts[i] = PlayerLoadout.Load(i);
            InitSystems();
            // Escena de fondo para la pantalla de título
            LoadFloor(1, null, true);
            OnTitleShown();
        }

        partial void InitSystems();
        partial void OnTitleShown();

        partial void OnDispose()
        {
            DisposeScreens();
            ClearFloor();
        }

        partial void DisposeScreens();

        partial void OnFade(bool on) { if (Ui != null) Ui.Fade(on); }
        partial void OnToast(string text, float seconds) { if (Ui != null) Ui.Toast(text, seconds); }
        partial void OnLateVisuals(float unscaledDt) { if (Ui != null) Ui.RealTimeUpdate(unscaledDt); }

        public void LoadFloor(int n, CarryState[] carry, bool preview = false)
        {
            LoadLevel(LevelData.For(n), n, carry, new LoadOpts { Preview = preview });
        }

        public void ClearFloor()
        {
            ClearElemental();
            ClearFloorExtra();
            foreach (var p in Players) p.Removed = true;
            Players.Clear();
            Statics.Clear();
            if (Effects != null) Effects.Clear();
            if (Ui != null) Ui.ClearFloats();
            if (FloorRoot) Object.Destroy(FloorRoot.gameObject);
            FloorRoot = null;
            View = null;
            Dungeon = null;
        }

        partial void ClearFloorExtra();

        // Busca un hueco de suelo libre cerca de (x, z) para colocar a un jugador
        public Vector2 FreeSpotNear(float x, float z)
        {
            Vector2 f, right;
            CameraCtl.GetBasis(out f, out right);
            var tries = new[]
            {
                new Vector2(right.x * 1.3f, right.y * 1.3f), new Vector2(-right.x * 1.3f, -right.y * 1.3f),
                new Vector2(1.3f, 0f), new Vector2(-1.3f, 0f), new Vector2(0f, -1.3f), new Vector2(0f, 1.3f),
            };
            foreach (var o in tries)
            {
                int tx = Grid.TileX(x + o.x), tz = Grid.TileZ(z + o.y);
                if (Dungeon.NavWalkable(tx, tz) && Dungeon.Spike[Dungeon.Idx(tx, tz)] == 0) return new Vector2(x + o.x, z + o.y);
            }
            return new Vector2(x, z);
        }

        // Carga cualquier nivel (piso normal, bonus, tienda o jefe)
        public void LoadLevel(LevelDef def, int n, CarryState[] carry, LoadOpts opts)
        {
            ClearFloor();
            Floor = n;
            InBonus = opts.Bonus;
            InShop = opts.Shop;
            InBoss = opts.Boss;
            Hitstop = 0f;
            DeathTimer = -1f;
            Def = def;
            FloorRoot = new GameObject("Piso" + n).transform;
            FloorRoot.SetParent(SceneRoot, false);
            Dungeon = new Dungeon(def);
            Physics = new CirclePhysics(Dungeon);
            View = new DungeonView(Dungeon, FloorRoot, n);
            Lighting.SetFog(def.Fog, 24f, 46f);
            CameraCtl.Cam.backgroundColor = MathUtil.Hex(def.Fog);
            LevelFogColor = def.Fog;
            HpMult = def.HpMult;
            foreach (var r in Dungeon.Rooms) r.Enemies.Clear();

            Spawn ps = default(Spawn);
            foreach (var s in Dungeon.Spawns) if (s.Ch == 'P') { ps = s; break; }
            int count = opts.Preview ? 1 : NumPlayers;
            for (int i = 0; i < count; i++)
            {
                var pos = i == 0 ? new Vector2(ps.X, ps.Z) : FreeSpotNear(ps.X, ps.Z);
                Players.Add(new Player(this, pos.x, pos.y, i));
            }
            foreach (var s in Dungeon.Spawns)
            {
                if (s.Ch == 'o') Statics.Add(new StaticProp(this, s.X, s.Z, true));
                else if (s.Ch == 'c') Statics.Add(new StaticProp(this, s.X, s.Z, false));
                else SpawnFromMap(s);
            }
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                ApplyCarry(p, carry != null && i < carry.Length ? carry[i] : null);
                p.Gear.RefreshVisual(p.Parts);
                Ui.SetGear(i, p.Gear);
                Ui.SetAbility(i, p.Ability);
                Ui.SetHealth(i, p.Hp, p.MaxHp, true);
                Ui.SetWeapon(i, p.Weapon);
                Ui.SetDowned(i, false);
                Ui.SetPrompt(i, null);
                Ui.SetPlayerBar(i, -1f, BarKind.None);
            }
            ApplyPlayerSetup();
            AfterLevelLoaded(opts);
            var c = CenterOf(Players);
            CameraCtl.SnapTo(c.x, c.y);
            View.PrimeLights(Players[0].X, Players[0].Z);
            View.UpdateOcclusion(10f, PlayerPoints(), CameraCtl.Yaw);
            Ui.SetFloor(InBonus ? "BONUS" : InShop ? "TIENDA" : InBoss ? "JEFE" : n.ToString());
            if (InBonus) Ui.ClearToast();
            if (!opts.Preview && !InBonus)
            {
                var parts = def.Name.Split('—');
                Ui.Banner(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
                // La sala inicial cuenta como visitada: su pista sale como aviso
                var r0 = Dungeon.RoomAt(Players[0].X, Players[0].Z);
                if (r0 != null) { r0.Entered = true; if (!string.IsNullOrEmpty(r0.Hint)) Ui.Toast(r0.Hint, 6f); }
            }
            // En los pisos bonus las salas no tienen anuncios propios: el desafío presenta sus reglas
            if (InBonus) foreach (var r in Dungeon.Rooms) r.Entered = true;
        }

        partial void SpawnFromMap(Spawn s);
        partial void ApplyCarryImpl(Player p, CarryState c);
        partial void AfterLevelLoaded(LoadOpts opts);

        void ApplyCarry(Player p, CarryState c) { if (c != null) ApplyCarryImpl(p, c); }

        // Ajustes que dependen de cuántos jugadores hay
        public void ApplyPlayerSetup()
        {
            bool solo = Players.Count == 1;
            // En solitario con el teclado izquierdo, el J1 también se mueve con las flechas
            if (Players.Count > 0) Players[0].Controls.Alt = solo && Bindings[0].Kind == DeviceKind.KeyboardA ? ControlSchemes.KeyboardB : null;
            for (int i = 1; i < ControlSchemes.MaxPlayers; i++) Ui.ShowPlayer(i, i < Players.Count);
            for (int i = 0; i < Players.Count; i++) Ui.SetPanelKeys(i, ControlSchemes.Help(Players[i].Controls.S));
            Ui.SetJoinHint(Players.Count < ControlSchemes.MaxPlayers && State != GameState.Title);
            for (int i = 0; i < ControlSchemes.MaxPlayers; i++)
            {
                PlayerLights[i].intensity = i < Players.Count ? (solo ? 1.3f : 1.0f) * Lighting.PointScale : 0f;
                PlayerLights[i].enabled = i < Players.Count;
            }
        }

        public static Vector2 CenterOf(List<Player> list)
        {
            if (list.Count == 0) return Vector2.zero;
            float x = 0, z = 0;
            foreach (var p in list) { x += p.X; z += p.Z; }
            return new Vector2(x / list.Count, z / list.Count);
        }

        readonly List<Vector2> pointsBuf = new List<Vector2>();
        public List<Vector2> PlayerPoints()
        {
            pointsBuf.Clear();
            foreach (var p in Players) pointsBuf.Add(new Vector2(p.X, p.Z));
            return pointsBuf;
        }
    }

    // Lo que se lleva cada jugador al cambiar de piso: el objeto de la mano (con su desgaste), la vida y el equipo
    public sealed class CarryState
    {
        public ItemState Item;
        public float Hp;
        public GearState Gear;
    }
}
