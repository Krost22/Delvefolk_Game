using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Contenido del piso, salas, interacción y progresión (Game.js)
    public sealed partial class Game
    {
        public CombatSystem Combat;
        public readonly List<Enemy> Enemies = new List<Enemy>();
        public readonly List<Barrel> Barrels = new List<Barrel>();
        public readonly List<SpikeTrap> Traps = new List<SpikeTrap>();
        public readonly List<ItemPickup> Pickups = new List<ItemPickup>();
        public readonly List<Breakable> Breakables = new List<Breakable>();
        public readonly List<Coin> Coins = new List<Coin>();
        public CarryState[] FloorStart;
        public bool ShownWin;
        CarryState[] pendingCarry;
        public bool InBonusPending;

        partial void InitSystems()
        {
            Combat = new CombatSystem(this);
            InitMoreSystems();
        }

        partial void InitMoreSystems();

        // Proyectiles devolvibles: los añade el sistema de jefes (fase 8)
        public void AddReflectableTargets(List<IHittable> list) { AddReflectableTargetsImpl(list); }
        partial void AddReflectableTargetsImpl(List<IHittable> list);

        partial void ClearFloorExtra()
        {
            foreach (var e in Enemies) e.Destroy();
            foreach (var b in Barrels) b.Destroy();
            foreach (var b in Breakables) b.Destroy();
            foreach (var p in Pickups) p.Destroy();
            foreach (var c in Coins) c.Remove();
            Enemies.Clear(); Barrels.Clear(); Traps.Clear(); Pickups.Clear(); Breakables.Clear(); Coins.Clear();
            ClearFloorMore();
        }

        partial void ClearFloorMore();

        partial void SpawnFromMap(Spawn s)
        {
            char ch = s.Ch;
            if (ch == 'S' || ch == 'G' || ch == 'L' || ch == 'A') SpawnEnemy(ch, s.X, s.Z);
            else if (ch == 'b' || ch == 'e') Barrels.Add(new Barrel(this, s.X, s.Z, ch == 'e'));
            else if (ch == '^') Traps.Add(new SpikeTrap(this, s.X, s.Z));
            else if (Breakable.ByChar.ContainsKey(ch)) Breakables.Add(new Breakable(this, s.X, s.Z, Breakable.ByChar[ch], s.Tx, s.Tz));
            else if (WeaponRegistry.ByMapChar.ContainsKey(ch)) SpawnItemPickup(WeaponRegistry.Create(WeaponRegistry.ByMapChar[ch]), s.X, s.Z);
        }

        public Enemy SpawnEnemy(char ch, float x, float z, EnemyOpts opts = null)
        {
            var e = EnemyFactory.Create(this, ch, x, z, opts);
            if (e == null) return null;
            if (HpMult != 1f) { e.MaxHp = Mathf.Round(e.MaxHp * HpMult); e.Hp = e.MaxHp; }
            Enemies.Add(e);
            e.Room = Dungeon.RoomAt(x, z);
            if (e.Room != null) e.Room.Enemies.Add(e);
            return e;
        }

        public ItemPickup SpawnItemPickup(Weapon item, float x, float z, Vector2? tossFrom = null)
        {
            var p = new ItemPickup(this, item, x, z, tossFrom);
            Pickups.Add(p);
            return p;
        }

        public void RemovePickup(ItemPickup p)
        {
            p.Destroy();
            Pickups.Remove(p);
        }

        // Probabilidad de comida en un barril (el amuleto de fortuna la mejora)
        public float FoodChance()
        {
            bool lucky = false;
            foreach (var p in Players) if (p.Gear.Luck() > 0f) lucky = true;
            return 0.3f + (lucky ? 0.15f : 0f);
        }

        public void SpawnFood(float x, float z)
        {
            SpawnItemPickup(WeaponRegistry.Create(Rand.Next() < 0.6f ? "chicken" : "cheese"), x, z);
        }

        // Monedas que saltan desde (x, z): un rango [mín, máx]; el amuleto de fortuna da más
        public void SpawnCoins(float x, float z, int[] range)
        {
            int n = range[0] + (int)Mathf.Floor(Rand.Next() * (range[1] - range[0] + 1));
            SpawnCoins(x, z, n);
        }

        public void SpawnCoins(float x, float z, int n)
        {
            bool lucky = false;
            foreach (var p in Players) if (p.Gear.Luck() > 0f) lucky = true;
            if (lucky) n = Mathf.RoundToInt(n * (1f + CoinConfig.LuckBonus));
            for (int i = 0; i < n; i++)
            {
                float a = Rand.Next() * Mathf.PI * 2f, sp = 1.5f + Rand.Next() * 2.5f;
                Coins.Add(new Coin(this, x, z, Mathf.Cos(a) * sp, Mathf.Sin(a) * sp));
            }
        }

        // ---------- Eventos ----------
        public void OnBreakableBroken(Breakable b, List<string> drops)
        {
            Stats.Broken++;
            if (drops.Count > 0 && !SeenHints.ContainsKey("breakable"))
            {
                SeenHints["breakable"] = true;
                Ui.Toast("¡Los muebles se rompen! Estanterías, armeros y mesas suelen esconder armas.", 4.5f);
            }
        }

        public void OnEnemyKilled(Enemy e, string cause)
        {
            Stats.Kills++;
            Audio.Play(e.Cfg.DieSound);
            CameraCtl.AddShake(0.2f);
            if (cause == "stomp") Ui.FloatText(e.X, 1.6f, e.Z, "¡PLAF!", TextStyle.Big);
            if (cause != "pit") SpawnCoins(e.X, e.Z, e.Cfg.Coins ?? CoinConfig.EnemyDefault);
            OnEnemyKilledExtra(e, cause);
            if (cause == "env" || cause == "pit")
            {
                Stats.EnvKills++;
                Ui.FloatText(e.X, 3.0f, e.Z, "¡KO AMBIENTAL!", TextStyle.Big);
            }
        }

        partial void OnEnemyKilledExtra(Enemy e, string cause);

        // ---------- Simulación de lo que hay en el piso ----------
        partial void UpdateWorldEntities(float dt)
        {
            for (int i = 0; i < Enemies.Count; i++) Enemies[i].Update(dt);
            for (int i = 0; i < Barrels.Count; i++) Barrels[i].Update(dt);
            for (int i = 0; i < Breakables.Count; i++) Breakables[i].Update(dt);
            for (int i = 0; i < Traps.Count; i++) Traps[i].Update(dt);
        }

        partial void PinPlayerImpl(Player p)
        {
            if (p.CarriedBy != null) { p.Body.X = p.CarriedBy.X; p.Body.Z = p.CarriedBy.Z; }
            var gb = p.GrabbedBy as IGrabHolder;
            if (gb != null) { var h = gb.HoldPos(); p.Body.X = h.x; p.Body.Z = h.z; }
        }

        partial void SyncEntities()
        {
            foreach (var e in Enemies) if (!e.Removed && e.Body.Enabled) e.SyncRoot();
            foreach (var b in Barrels) if (!b.Removed && b.Body.Enabled) b.SyncRoot();
        }

        partial void AfterInteraction(float dt)
        {
            for (int i = 0; i < Pickups.Count; i++) Pickups[i].Update(dt);
            for (int i = 0; i < Coins.Count; i++) Coins[i].Update(dt);
            Coins.RemoveAll(c => c.Collected);
            AfterInteractionMore(dt);
        }

        partial void AfterInteractionMore(float dt);

        partial void Purge()
        {
            Enemies.RemoveAll(e => e.Removed);
            Barrels.RemoveAll(b => b.Removed);
            Breakables.RemoveAll(b => b.Dead);
        }

        // ---------- Salas y rastrillos ----------
        partial void UpdateRooms()
        {
            foreach (var p in Players)
            {
                if (p.Dead) continue;
                var room = Dungeon.RoomAt(p.X, p.Z);
                if (room != null && !room.Entered)
                {
                    room.Entered = true;
                    Ui.Banner(room.Name);
                    if (!string.IsNullOrEmpty(room.Hint)) Ui.Toast(room.Hint, 5.5f);
                    for (int i = 0; i < room.Enemies.Count; i++)
                    {
                        var e = room.Enemies[i];
                        if (e.WakeDelay < 0f) e.WakeDelay = 0.25f + i * 0.16f;
                    }
                }
            }
            foreach (var r in Dungeon.Rooms)
            {
                if (r.Cleared || !r.Entered || r.Enemies.Count == 0) continue;
                bool all = true;
                foreach (var e in r.Enemies) if (!e.Dead) { all = false; break; }
                if (!all) continue;
                r.Cleared = true;
                if (!string.IsNullOrEmpty(r.Opens))
                {
                    var g = Dungeon.OpenGate(r.Opens);
                    if (g != null)
                    {
                        View.OnGateOpened(g.Ch);
                        Audio.Play("gate");
                        CameraCtl.AddShake(0.25f);
                        Ui.Toast("¡Sala despejada! Un rastrillo se abre con un chirrido...", 3.5f);
                    }
                }
                else Ui.Toast("Sala despejada.", 2f);
            }
        }

        // ---------- Interacción de cada jugador (prioridades del §6.17) ----------
        partial void UpdateInteraction(float dt)
        {
            foreach (var pk in Pickups) pk.SetHighlight(false);
            foreach (var p in Players)
            {
                var c = p.Controls;
                Player mate = null;
                foreach (var q in Players) if (q != p) { mate = q; break; }
                // Agarrado (por un compañero o un enemigo): aviso de forcejeo
                if (!p.Dead && (p.CarriedBy != null || p.GrabbedBy != null)) { Ui.SetPrompt(p.Index, "¡Machaca {A} {E} para soltarte!"); continue; }
                if (p.Dead || p.IsThrown || p.Spectator) { Ui.SetPrompt(p.Index, null); continue; }
                bool taken = false;
                // El minijuego del piso bonus y los pedestales de la tienda pueden quedarse con el botón
                InteractionHooks(p, ref taken);
                if (taken) continue;
                if (p.Carrying != null)
                {
                    Ui.SetPrompt(p.Index, "{A} Lanzar · {E} Soltar");
                    if (c.ConsumeInteract()) p.PutDown();
                    continue;
                }
                CoopInteraction(p, mate, dt, ref taken);
                if (taken) continue;
                // Objetivo más cercano: objeto del suelo o compañero
                ItemPickup best = null;
                Player bestMate = null;
                float bestD = 1.8f;
                foreach (var pk in Pickups)
                {
                    if (pk.Tossing || pk.Magnet != null) continue;
                    float d = MathUtil.Hypot(pk.X - p.X, pk.Z - p.Z);
                    if (d < bestD) { bestD = d; best = pk; }
                }
                if (mate != null && !mate.Dead && mate.CarriedBy == null && mate.Carrying == null && !mate.IsThrown && !mate.Spectator && mate.GrabbedBy == null && !mate.Airborne && !p.Airborne)
                {
                    float d = MathUtil.Hypot(mate.X - p.X, mate.Z - p.Z);
                    if (d < Mathf.Min(bestD, 1.7f)) { bestD = d; bestMate = mate; best = null; }
                }
                bool nearExit = Dungeon.HasExit && MathUtil.Hypot(Dungeon.ExitX - p.X, Dungeon.ExitZ - p.Z) < 2.2f;
                if (best != null)
                {
                    best.SetHighlight(true);
                    Ui.SetPrompt(p.Index, best.Label);
                    if (c.ConsumeInteract())
                    {
                        EquipFromPickup(p, best);
                        string key = "use" + p.Index;
                        if (!SeenHints.ContainsKey(key))
                        {
                            SeenHints[key] = true;
                            Ui.Toast(p.Weapon.IsFood
                                ? p.Look.Name + ": pulsa " + c.S.AttackKey + " para comer (te moverás despacio y si te pegan, se te cae el bocado)" +
                                  (Players.Count > 1 ? ", o mantén " + c.S.InteractKey + " junto a tu compañero para darle de comer." : ".")
                                : p.Look.Name + ": " + c.S.AttackKey + " para atacar. Ojo: las armas se gastan y acaban rompiéndose.", 4.5f);
                        }
                    }
                }
                else if (bestMate != null)
                {
                    Ui.SetPrompt(p.Index, "Agarrar a " + bestMate.Look.Name);
                    if (c.ConsumeInteract())
                    {
                        p.Grab(bestMate);
                        if (!SeenHints.ContainsKey("throw"))
                        {
                            SeenHints["throw"] = true;
                            Ui.Toast(p.Look.Name + " lleva a " + bestMate.Look.Name + " en brazos: " + c.S.AttackKey +
                                " para lanzarlo contra los monstruos (a los dos les duele).", 5f);
                        }
                    }
                }
                else if (nearExit)
                {
                    Ui.SetPrompt(p.Index, "Descender");
                    if (c.ConsumeInteract()) Descend();
                }
                else Ui.SetPrompt(p.Index, null);
            }
        }

        partial void InteractionHooks(Player p, ref bool taken);
        partial void CoopInteraction(Player p, Player mate, float dt, ref bool taken);

        // ---------- Progresión ----------
        // Lo que se lleva cada jugador al cambiar de piso: el objeto de la mano (con su desgaste), la vida y el equipo.
        // Quien esté derribado se levanta en el piso siguiente con 40 de vida.
        public CarryState[] CollectCarry()
        {
            var arr = new CarryState[Players.Count];
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                arr[i] = new CarryState
                {
                    Item = p.Carrying != null ? new ItemState("fists") : p.Weapon.GetState(),
                    Hp = p.Dead ? 40f : p.Hp,
                    Gear = p.Gear.GetState(),
                };
            }
            return arr;
        }

        partial void ApplyCarryImpl(Player p, CarryState c)
        {
            p.SetWeapon(WeaponRegistry.FromState(c.Item));
            p.Hp = c.Hp;
            if (c.Gear != null) p.Gear.SetState(c.Gear);
        }

        partial void AfterLevelLoaded(LoadOpts opts)
        {
            // Lo que se lleva al empezar el piso (para reintentar): incluye el equipo, así reintentar no duplica recompensas
            if (!InBonus)
            {
                FloorStart = new CarryState[Players.Count];
                for (int i = 0; i < Players.Count; i++)
                    FloorStart[i] = new CarryState { Item = Players[i].Weapon.GetState(), Hp = Cfg.PlayerMaxHp, Gear = Players[i].Gear.GetState() };
            }
            AfterLevelLoadedMore(opts);
        }

        partial void AfterLevelLoadedMore(LoadOpts opts);

        public void Descend()
        {
            if (State != GameState.Play) return;
            State = GameState.Transition;
            Audio.Play("descend");
            for (int i = 0; i < Players.Count; i++) Ui.SetPrompt(i, null);
            var carry = CollectCarry();
            int from = Floor, next = from + 1;
            Transition(() =>
            {
                bool handled = false;
                // ¿Toca jefe? (tras los pisos 2, 4, 6...) ¿Y piso bonus?
                DescendHooks(from, next, carry, ref handled);
                if (!handled) ProceedToFloor(next, carry);
            });
        }

        partial void DescendHooks(int from, int next, CarryState[] carry, ref bool handled);

        // Llega al piso "next" (tras el fundido): o la pantalla de victoria o el piso en sí
        public void ProceedToFloor(int next, CarryState[] carry)
        {
            if (next == LevelData.Levels.Length + 1 && !ShownWin)
            {
                ShownWin = true;
                pendingCarry = carry;
                int gold = 0;
                foreach (var c in carry) if (c.Gear != null) gold += c.Gear.gold;
                State = GameState.Win;
                InBonus = false;
                Ui.ShowWin(Stats, gold, BonusPlayedCount());
                return;
            }
            State = GameState.Play;
            LoadFloor(next, carry);
            Ui.Fade(false);
        }

        int BonusPlayedCount() { int n = 0; BonusPlayedCountImpl(ref n); return n; }
        partial void BonusPlayedCountImpl(ref int n);

        public void ContinueDeeper()
        {
            Ui.HideScreens();
            State = GameState.Play;
            LoadFloor(Floor + 1, pendingCarry);
            Ui.Fade(false);
        }

        partial void OnGameOver()
        {
            Ui.ShowDeath((Players.Count > 1 ? "Caísteis" : "Caíste") + " en el piso " + Floor + ". La mazmorra se ríe de " + (Players.Count > 1 ? "vosotros." : "ti."));
        }

        public void Retry()
        {
            Ui.HideScreens();
            NumPlayers = FloorStart.Length;
            State = GameState.Play;
            var carry = new CarryState[FloorStart.Length];
            for (int i = 0; i < carry.Length; i++) carry[i] = new CarryState { Item = FloorStart[i].Item, Hp = Cfg.PlayerMaxHp, Gear = FloorStart[i].Gear };
            bool handled = false;
            RetryHooks(carry, ref handled);
            if (!handled) LoadFloor(Floor, carry);
        }

        partial void RetryHooks(CarryState[] carry, ref bool handled);

        static readonly string[] RetryKeys = { "KeyR", "GP0:A", "GP1:A" };
        static readonly string[] ContinueKeys = { "Space", "Enter", "NumpadEnter", "GP0:A", "GP1:A" };

        // Pantallas de derrota y victoria (reintentar / seguir)
        partial void ScreenInput()
        {
            if (State == GameState.Dead && Input.ConsumeAny(RetryKeys)) Retry();
            else if (State == GameState.Win && Input.ConsumeAny(ContinueKeys)) ContinueDeeper();
        }
    }
}
