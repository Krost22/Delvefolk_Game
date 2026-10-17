using UnityEngine;

namespace Delvefolk
{
    // Tienda del Duende Tendero y munición de la escopeta (Game.js)
    public sealed partial class Game
    {
        public ShopFloor Shop { get; private set; }

        partial void ShopInteraction(Player p, ref bool taken)
        {
            if (Shop != null && Shop.HandleInteraction(p)) taken = true;
        }

        partial void ShopUpdate(float dt) { if (Shop != null) Shop.Update(dt); }

        partial void ClearFloorMore()
        {
            Shop = null;
            Ui.HideBoss();
            ClearBossFloor();
        }

        partial void ClearBossFloor();

        // Tras un piso bonus: la tienda (si está activada) antes de seguir bajando
        partial void AfterBonusHooks(int next, CarryState[] carry, ref bool handled)
        {
            if (!ShopConfig.AfterBonus) return;
            EnterShop(next, carry);
            handled = true;
        }

        // Tienda del Duende Tendero (tras un piso bonus). Al bajar por sus escaleras se llega a nextFloor.
        public void EnterShop(int nextFloor, CarryState[] carry)
        {
            State = GameState.Play;
            LoadLevel(ShopFloor.BuildLevel(), nextFloor - 1, carry, new LoadOpts { Shop = true });
            Shop = new ShopFloor(this, nextFloor);
            Ui.Fade(false);
        }

        // Escopeta: cada enemigo derrotado da una bala a quien la tenga en la mano
        partial void OnEnemyKilledExtra(Enemy e, string cause)
        {
            foreach (var p in Players)
            {
                var sg = p.Weapon as ShotgunWeapon;
                if (p.Dead || sg == null) continue;
                if (sg.AddAmmo(1) > 0) { Ui.SetWeapon(p.Index, sg); Ui.FloatText(p.X, 2.7f, p.Z, "+1 bala", TextStyle.Pickup); }
            }
        }

        // Reintentar en la tienda la recarga; en un piso de jefe repite el jefe
        partial void RetryHooks(CarryState[] carry, ref bool handled)
        {
            if (InShop) { EnterShop(Floor + 1, carry); handled = true; return; }
            BossRetry(carry, ref handled);
        }

        partial void BossRetry(CarryState[] carry, ref bool handled);
    }
}
