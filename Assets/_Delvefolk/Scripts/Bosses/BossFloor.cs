using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // PISO DE JEFE (BossFloor.js): aparece al bajar desde los pisos 2, 4, 6... (BossConfig.EveryFloors).
    // Arena propia (definida por cada jefe), presentación con su nombre, barra de vida de jefe (suma de todos
    // los jefes del combate) y, al vencer, se abre el rastrillo de las escaleras y cada jugador recibe botín.
    // Perder aquí funciona como en un piso normal (si caéis los dos, "Reintentar" repite el combate).
    public sealed class BossFloor
    {
        readonly Game G;
        public readonly BossEntry Entry;
        public readonly int Next, Encounter;
        public string Phase = "intro";
        public float T;
        public readonly List<Boss> BossList = new List<Boss>();
        public readonly int ArenaW, ArenaH, Cx;

        // Mapa de la arena: rastrillo '1' delante de las escaleras 'X' (arriba) y entrada 'P' (abajo)
        public static LevelDef BuildLevel(BossEntry entry)
        {
            int W = entry.ArenaW, H = entry.ArenaH, cx = W / 2;
            var rows = BonusChallenge.Frame(W, H);
            System.Action<int, int, char> put = (x, z, ch) => BonusChallenge.Put(rows, x, z, ch);
            // Fila 1: pasillito con la salida; fila 2: muro con el rastrillo en el centro
            for (int x = 1; x < W - 1; x++) { put(x, 1, Mathf.Abs(x - cx) <= 1 ? '.' : '#'); put(x, 2, '#'); }
            put(cx, 1, 'X'); put(cx, 2, '1');
            put(0, 5, 't'); put(W - 1, 5, 't'); put(0, H - 4, 't'); put(W - 1, H - 4, 't'); put(cx - 3, 2, 't'); put(cx + 3, 2, 't');
            put(cx, H - 3, 'P'); put(cx, H - 1, 'N');
            if (entry.Decorate != null) entry.Decorate(put, W, H);
            return new LevelDef
            {
                Name = entry.Icon + " " + entry.Name + " — " + (entry.Title ?? ""), Fog = entry.Fog, Map = rows,
                Rooms = new[] { new RoomDef("J", entry.Name, new[] { 1, 3, W - 2, H - 2 }, entry.Hint ?? "") },
            };
        }

        public BossFloor(Game g, BossEntry entry, int nextFloor, int encounter)
        {
            G = g;
            g.BossFloor = this;            // los jefes leen el número de encuentro al crearse
            Entry = entry;
            Next = nextFloor;
            Encounter = encounter;
            ArenaW = entry.ArenaW; ArenaH = entry.ArenaH; Cx = ArenaW / 2;
            var spawned = entry.Spawn(g, Grid.X(Cx), Grid.Z(5)) ?? new List<Boss>();
            foreach (var b in spawned) AddBoss(b);
            g.Audio.Play("bossRoar");
            g.CameraCtl.AddShake(0.4f);
        }

        // Añade un jefe al combate (también vale a mitad: refuerzos, un gemelo que revive...)
        public void AddBoss(Boss b)
        {
            if (!BossList.Contains(b)) BossList.Add(b);
            if (!G.Enemies.Contains(b)) G.Enemies.Add(b);
            if (Phase == "fight") { b.IsAwake = true; b.OnAwake(); }
        }

        public float TotalHp { get { float s = 0f; foreach (var b in BossList) if (!b.Dead) s += Mathf.Max(0f, b.Hp); return s; } }
        public float TotalMax { get { float s = 0f; foreach (var b in BossList) s += b.MaxHp; return s > 0f ? s : 1f; } }

        public void Update(float dt)
        {
            T += dt;
            if (Phase == "intro" && T >= BossConfig.IntroTime)
            {
                Phase = "fight";
                foreach (var b in BossList.ToArray()) { b.IsAwake = true; b.OnAwake(); }
                G.Audio.Play("bossRoar");
            }
            if (Phase != "won")
            {
                var top = BossList.Find(b => !b.Dead) ?? (BossList.Count > 0 ? BossList[0] : null);
                string phaseTxt = top != null && top.Phase > 1 ? " · fase " + top.Phase : "";
                G.Ui.SetBoss(Entry.Icon + " " + Entry.Name + phaseTxt, TotalHp / TotalMax);
            }
        }

        // Lo llama Boss.Die. La victoria llega cuando todos han caído y ninguno la retiene (HoldVictory)
        public void OnBossDied(Boss b) { CheckVictory(); }

        public void CheckVictory()
        {
            if (Phase == "won") return;
            if (BossList.Exists(b => !b.Dead || b.HoldVictory)) return;
            Win();
        }

        void Win()
        {
            Phase = "won";
            G.Ui.HideBoss();
            G.Telegraphs.Clear();
            G.Projectiles.Clear();
            var gate = G.Dungeon.OpenGate("1");
            if (gate != null) G.View.OnGateOpened(gate.Ch);
            foreach (var e in G.Enemies.ToArray())
            {
                if (e.Dead || BossList.Contains(e as Boss)) continue;
                G.Effects.Burst(e.X, 0.8f, e.Z, BurstOpts.Of(8, 0xd8d0ff, 3f, 3f, 0.14f));
                e.Die("vanish");
            }
            G.Audio.Play("win");
            G.Audio.Play("gate");
            G.CameraCtl.AddShake(0.6f);
            G.Ui.Banner("¡JEFE DERROTADO!", Entry.Name);
            G.Ui.Toast("🏆 El rastrillo se ha abierto: bajad por las escaleras.", 5f);
            G.Stats.Bosses++;
            // Botín raro para cada jugador (también para el que esté derribado: se levanta)
            foreach (var p in G.Players)
            {
                if (p.Dead) p.Revive(40);
                for (int k = 0; k < BossConfig.RewardRolls; k++)
                    G.RewardService.ClaimReward(p, Rewards.Roll(G.Rand, d => d.Category != "Maldición" && (d.Rare || d.Kind != "instant"), p.Gear.Luck()),
                        "boss:" + Entry.Id + ":" + G.Floor + ":" + k);
            }
        }
    }
}
