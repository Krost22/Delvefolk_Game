using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // El frame de la simulación, en el orden fijo de Game.frame del original
    public sealed partial class Game
    {
        readonly List<Vector2Int> navSources = new List<Vector2Int>();
        readonly List<Player> focusBuf = new List<Player>();

        // Empezar partida (desde la sala de preparación, o directamente en las pruebas)
        public void Start(int numPlayers)
        {
            NumPlayers = numPlayers;
            RunId++;
            Stats = new RunStats();
            SeenHints.Clear();
            OnRunStart();
            Input.Clear();
            State = GameState.Play;
            Ui.ShowHeads();
            LoadFloor(1, null);
            AnnounceSolo();
        }

        partial void OnRunStart();

        partial void FrameStates(float dt, ref bool handled)
        {
            // Tirada de la tragaperras en pantalla: la simulación espera (también en pausa avanza la animación)
            if (slotUi != null && slotUi.Active)
            {
                slotUi.Update(dt);
                Effects.Update(dt);
                Ui.Update(dt);
                Input.EndFrame();
                handled = true;
                return;
            }
            if (State == GameState.Title || State == GameState.Lobby)
            {
                if (State == GameState.Title)
                {
                    // Abrir la sala acaba el frame (como el original)
                    bool opened = false;
                    TitleInput(ref opened);
                    if (opened) { Input.EndFrame(); handled = true; return; }
                }
                else
                {
                    LobbyUpdate(dt);
                    if (State != GameState.Lobby) { Input.EndFrame(); handled = true; return; }   // la partida acaba de empezar
                }
                // Vista previa: la cámara pasea lentamente sobre la mazmorra (un punto 6 u al norte del jugador)
                Time += dt;
                var p = Players[0];
                float nx = p.X, nz = p.Z + 6f;
                View.Update(dt, nx, nz);
                Dungeon.Update(dt);
                CameraCtl.Update(dt, p.X + Mathf.Sin(Time * 0.2f) * 4f, nz - Mathf.Cos(Time * 0.2f) * 3f, 0f, 0f);
                pointsBuf.Clear(); pointsBuf.Add(new Vector2(nx, nz));
                View.UpdateOcclusion(dt, pointsBuf, CameraCtl.Yaw);
                Input.EndFrame();
                handled = true;
                return;
            }
            if (State == GameState.Bonus)
            {
                BonusOverlay(dt);
                Effects.Update(dt);
                Ui.Update(dt);
                Input.EndFrame();
                handled = true;
            }
        }

        partial void TitleInput(ref bool handled);
        partial void LobbyUpdate(float dt);
        partial void BonusOverlay(float dt);

        List<Player> Focus()
        {
            focusBuf.Clear();
            foreach (var p in Players) if (!p.Dead) focusBuf.Add(p);
            if (focusBuf.Count == 0) focusBuf.AddRange(Players);
            return focusBuf;
        }

        partial void HitstopFrame(float dt)
        {
            var c = CenterOf(Focus());
            CameraCtl.Update(dt, c.x, c.y, 0f, 0f);
            Ui.Update(dt);
        }

        partial void Simulate(float dt)
        {
            if (State == GameState.Play) Stats.Time += dt;
            BeforePlayers(dt);
            foreach (var p in Players) p.Update(dt);

            navSources.Clear();
            foreach (var p in Players) if (p.Targetable) navSources.Add(new Vector2Int(Grid.TileX(p.X), Grid.TileZ(p.Z)));
            if (navSources.Count > 0) Dungeon.UpdateNav(navSources);
            UpdateElemental(dt);
            UpdateWorldEntities(dt);

            Physics.Step(dt);

            // Sincronizar visuales tras la física (los jugadores en brazos o agarrados se fijan a quien los lleva)
            foreach (var p in Players)
            {
                if (p.Removed) continue;
                PinPlayer(p);
                p.SyncRoot();
            }
            SyncEntities();

            if (State == GameState.Play)
            {
                bool anyAlive = false;
                foreach (var p in Players) if (!p.Dead) { anyAlive = true; break; }
                if (anyAlive && !InBonus && !InShop && !InBoss) UpdateRooms();
                UpdateInteraction(dt);
            }
            AfterInteraction(dt);
            Purge();
            UpdateDeathTimer(dt);

            var focus = Focus();
            var center = CenterOf(focus);
            Dungeon.Update(dt);
            View.Update(dt, center.x, center.y);
            View.UpdateOcclusion(dt, PlayerPoints(), CameraCtl.Yaw);
            Effects.Update(dt);
            // Cámara: centro de los jugadores, se aleja si se separan
            float spread = 0f, vx = 0f, vz = 0f;
            foreach (var p in focus)
            {
                spread = Mathf.Max(spread, MathUtil.Hypot(p.X - center.x, p.Z - center.y) * 2f);
                vx += p.Vx / focus.Count; vz += p.Vz / focus.Count;
            }
            CameraCtl.Update(dt, center.x, center.y, vx, vz, spread);
            UpdateFog();
            for (int i = 0; i < Players.Count && i < PlayerLights.Length; i++) PlayerLights[i].transform.position = new Vector3(Players[i].X, 2.6f, Players[i].Z);
            Ui.Update(dt);
        }

        partial void BeforePlayers(float dt);
        partial void UpdateWorldEntities(float dt);
        partial void PinPlayerImpl(Player p);
        partial void SyncEntities();
        partial void UpdateRooms();
        partial void UpdateInteraction(float dt);
        partial void AfterInteraction(float dt);
        partial void Purge();

        void PinPlayer(Player p) { PinPlayerImpl(p); }

        // Todos derribados: fin de la partida tras un momento
        void UpdateDeathTimer(float dt)
        {
            if (DeathTimer < 0f || InBonus) return;
            DeathTimer -= dt;
            bool anyAlive = false;
            foreach (var p in Players) if (!p.Dead) { anyAlive = true; break; }
            if (anyAlive) DeathTimer = -1f;
            else if (DeathTimer <= 0f)
            {
                DeathTimer = -1f;
                State = GameState.Dead;
                OnGameOver();
            }
        }

        partial void OnGameOver();

        // ---------- Eventos ----------
        // opts.Dot: daño continuo (gas, lava): solo número y sonido, sin congelar ni sacudir cada medio segundo
        public void OnPlayerHurt(Player p, float amount, HurtOpts opts)
        {
            Ui.SetHealth(p.Index, p.Hp, p.MaxHp);
            Ui.FloatText(p.X, 2.0f, p.Z, "-" + Mathf.RoundToInt(amount), TextStyle.Hurt);
            Audio.Play("hurt");
            if (opts.Dot) return;
            Ui.DamageFlash();
            CameraCtl.AddShake(0.38f);
            RequestHitstop(0.05f);
            Effects.Burst(p.X, 1f, p.Z, BurstOpts.Of(8, 0xff4040, 4f, 4f, 0.12f));
        }

        public void OnPlayerDeath(Player p)
        {
            Audio.Play("death");
            Ui.SetPrompt(p.Index, null);
            Ui.SetDowned(p.Index, true);
            // En un piso bonus caer no acaba la partida: el desafío decide (normalmente te elimina de la ronda)
            bool handled = false;
            OnPlayerDownInBonus(p, ref handled);
            if (handled) return;
            Player mate = null;
            foreach (var q in Players) if (!q.Dead) { mate = q; break; }
            if (mate != null)
            {
                Ui.Toast(p.Look.Name + " ha caído. " + mate.Look.Name + ": ponte a su lado y mantén " + mate.Controls.S.InteractKey + " para reanimarle.", 5f);
                return;
            }
            DeathTimer = 1.3f;
        }

        partial void OnPlayerDownInBonus(Player p, ref bool handled);
    }
}
