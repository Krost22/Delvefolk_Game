using System.Collections.Generic;

namespace Delvefolk
{
    // Pisos de jefe, avisos y proyectiles (Game.js)
    public sealed partial class Game
    {
        public Telegraphs Telegraphs { get; private set; }
        public Projectiles Projectiles { get; private set; }
        public BossFloor BossFloor;
        public int BossCount;                                  // jefes encontrados en esta partida
        public readonly HashSet<int> BossDone = new HashSet<int>();   // pisos tras los que ya hubo jefe
        public string LastBossId, ForceBoss;

        partial void InitShopAndBosses()
        {
            Telegraphs = new Telegraphs(this);
            Projectiles = new Projectiles(this);
        }

        partial void OnRunStartLater()
        {
            BossCount = 0;
            BossDone.Clear();
        }

        partial void BossSystemsUpdate(float dt)
        {
            Telegraphs.Update(dt);
            Projectiles.Update(dt);
            if (BossFloor != null) BossFloor.Update(dt);
        }

        partial void ClearBossFloor()
        {
            if (Telegraphs != null) Telegraphs.Clear();
            if (Projectiles != null) Projectiles.Clear();
            BossFloor = null;
        }

        // Proyectiles que un golpe puede devolver
        partial void AddReflectableTargetsImpl(List<IHittable> list)
        {
            foreach (var p in Projectiles.List) if (p.Reflectable && p.Hostile && !p.Dead) list.Add(p);
        }

        // ¿Hay pelea de jefe al bajar desde este piso?
        public bool BossDue(int from)
        {
            if (ForceBoss != null) return true;
            if (BossConfig.DemoFinalBoss != null) return Settings.bosses && from == LevelData.Levels.Length && !BossDone.Contains(from);
            return Settings.bosses && from % BossConfig.EveryFloors == 0 && !BossDone.Contains(from);
        }

        partial void BossDescend(int from, int next, CarryState[] carry, ref bool handled)
        {
            if (InShop || InBoss || !BossDue(from)) return;
            EnterBoss(from, next, carry, null);
            handled = true;
        }

        partial void BossRetry(CarryState[] carry, ref bool handled)
        {
            if (!InBoss) return;
            EnterBoss(Floor, Floor + 1, carry, LastBossId);
            handled = true;
        }

        // Piso de jefe. id: forzar un jefe concreto (reintentar, pruebas); si no, el siguiente de BossConfig.Order
        public void EnterBoss(int from, int next, CarryState[] carry, string id)
        {
            if (id == null)
            {
                id = ForceBoss ?? BossConfig.DemoFinalBoss ?? BossConfig.Order[BossCount % BossConfig.Order.Length];
                ForceBoss = null;
                BossCount++;
            }
            var entry = Bosses.Get(id) ?? Bosses.Get(BossConfig.Order[0]);
            BossDone.Add(from);
            LastBossId = entry.Id;
            State = GameState.Play;
            LoadLevel(Delvefolk.BossFloor.BuildLevel(entry), from, carry, new LoadOpts { Boss = true });
            new Delvefolk.BossFloor(this, entry, next, System.Math.Max(0, BossCount - 1));
            Ui.Fade(false);
        }
    }
}
