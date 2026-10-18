namespace Delvefolk
{
    // Pisos bonus y recompensas dentro del juego (Game.js)
    public sealed partial class Game
    {
        public BonusFloorController Bonus { get; private set; }
        public BonusRewardService RewardService { get; private set; }

        partial void InitLaterSystems()
        {
            RewardService = new BonusRewardService(this);
            Bonus = new BonusFloorController(this);
            InitShopAndBosses();
        }

        partial void InitShopAndBosses();

        partial void OnRunStartMore()
        {
            RewardService.Reset();
            Bonus.ResetRun();
            OnRunStartLater();
        }

        partial void OnRunStartLater();

        partial void BonusPreUpdate(float dt) { Bonus.PreUpdate(dt); }
        partial void BonusOverlay(float dt) { Bonus.UpdateOverlay(dt); }

        // El minijuego del piso bonus y los pedestales de la tienda pueden quedarse con el botón
        partial void InteractionHooks(Player p, ref bool taken)
        {
            if (Bonus.HandleInteraction(p)) { taken = true; return; }
            if (BossFloor != null && BossFloor.Interact != null && BossFloor.Interact(p)) { taken = true; return; }
            ShopInteraction(p, ref taken);
            if (!taken && SlotInteraction(p)) taken = true;
        }

        partial void ShopInteraction(Player p, ref bool taken);

        // En un piso bonus caer no acaba la partida: el desafío decide (normalmente te elimina de la ronda)
        partial void OnPlayerDownInBonus(Player p, ref bool handled)
        {
            if (!InBonus) return;
            Bonus.OnPlayerDown(p);
            handled = true;
        }

        partial void AllowReviveImpl(ref bool v) { v = Bonus.AllowRevive(); }
        partial void AbilitiesBlockedImpl(Player p, ref bool v) { v = Bonus.AbilitiesBlockedFor(p); }
        partial void OnPlayerJoinedBonus(Player p) { Bonus.OnPlayerJoined(p); }
        partial void BonusPlayedCountImpl(ref int n) { n = Bonus.History.Count; }

        partial void AfterInteractionMore(float dt)
        {
            ShopUpdate(dt);
            BossSystemsUpdate(dt);
            if (InBonus)
            {
                Bonus.Update(dt);
                Bonus.UpdateResolving(dt);
            }
        }

        partial void ShopUpdate(float dt);
        partial void BossSystemsUpdate(float dt);

        // Al bajar: ¿toca jefe? (tras los pisos 2, 4, 6...) ¿y piso bonus?
        partial void DescendHooks(int from, int next, CarryState[] carry, ref bool handled)
        {
            BossDescend(from, next, carry, ref handled);
            if (handled) return;
            if (!InShop && Bonus.ShouldTrigger(from)) { Bonus.Enter(from, next, carry); handled = true; }
        }

        partial void BossDescend(int from, int next, CarryState[] carry, ref bool handled);

        // Al salir del bonus: la tienda (fase 7) o el piso siguiente
        public void AfterBonus(int next, CarryState[] carry)
        {
            bool handled = false;
            AfterBonusHooks(next, carry, ref handled);
            if (!handled) ProceedToFloor(next, carry);
        }

        partial void AfterBonusHooks(int next, CarryState[] carry, ref bool handled);
    }
}
