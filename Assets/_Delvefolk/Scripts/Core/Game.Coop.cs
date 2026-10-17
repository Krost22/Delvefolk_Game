using UnityEngine;

namespace Delvefolk
{
    // Cooperativo local: el J2 se une a mitad de partida, dar de comer y reanimar al compañero (Game.js)
    public sealed partial class Game
    {
        public MatchSettings Settings = MatchSettings.Load();

        partial void BeforePlayers(float dt)
        {
            // Unirse a mitad de partida
            if (State == GameState.Play && Players.Count == 1 && Input.ConsumeAny(ControlSchemes.All[1].Attack)) JoinPlayer2();
            BonusPreUpdate(dt);
        }

        partial void BonusPreUpdate(float dt);

        // El J2 se une a mitad de partida pulsando su botón de ataque
        public void JoinPlayer2()
        {
            if (Players.Count != 1 || State != GameState.Play) return;
            NumPlayers = 2;
            var p1 = Players[0];
            // Habilidades exclusivas: si J2 trae la misma que J1, se le asigna la siguiente libre
            if (Settings.ExclusiveAbilities && Loadouts[1].Ability == p1.Ability.Id)
            {
                foreach (var id in AbilityController.Order) if (id != p1.Ability.Id) { Loadouts[1].SetAbility(id); break; }
            }
            var pos = FreeSpotNear(p1.X, p1.Z);
            var p2 = new Player(this, pos.x, pos.y, 1);
            Players.Add(p2);
            if (!InBonus && FloorStart != null)
            {
                System.Array.Resize(ref FloorStart, FloorStart.Length + 1);
                FloorStart[FloorStart.Length - 1] = new CarryState { Item = new FistsWeapon().GetState(), Hp = Cfg.PlayerMaxHp, Gear = null };
            }
            Ui.SetHealth(1, p2.Hp, p2.MaxHp, true);
            Ui.SetGear(1, p2.Gear);
            Ui.SetAbility(1, p2.Ability);
            OnPlayerJoinedBonus(p2);
            ApplyPlayerSetup();
            Audio.Play("join");
            Effects.Ring(p2.X, p2.Z, RingOpts.Of(0x5bb0ff, 2f, 0.5f));
            Ui.FloatText(p2.X, 2.8f, p2.Z, "¡J2 se une!", TextStyle.Big);
            Ui.Toast("J2: FLECHAS para moverte, ENTER para atacar, SHIFT derecho para recoger o agarrar a tu compañero.", 5f);
        }

        partial void OnPlayerJoinedBonus(Player p);

        // En los pisos bonus el desafío decide si se puede reanimar
        public bool AllowRevive() { bool v = true; AllowReviveImpl(ref v); return v; }
        partial void AllowReviveImpl(ref bool v);

        // Prioridades 6 y 7 del §6.17: dar de comer y reanimar al compañero (mantener interactuar)
        partial void CoopInteraction(Player p, Player mate, float dt, ref bool taken)
        {
            var c = p.Controls;
            // Si está derribado, la comida lo levanta antes y con más vida que reanimarle a pelo
            bool canFeed = p.Weapon.IsFood && mate != null && !mate.Removed && mate.CarriedBy == null && mate.GrabbedBy == null && !mate.Spectator &&
                MathUtil.Hypot(mate.X - p.X, mate.Z - p.Z) < FeedConfig.Range && (mate.Dead ? AllowRevive() : mate.Hp < mate.MaxHp);
            if (canFeed)
            {
                float need = mate.Dead ? FeedConfig.ReviveTime : FeedConfig.Time;
                Ui.SetPrompt(p.Index, "Mantén {E}: dar de comer a " + mate.Look.Name + (mate.Dead ? " (¡arriba!)" : ""));
                if (c.InteractDown())
                {
                    p.FeedT += dt;
                    Ui.SetPlayerBar(p.Index, p.FeedT / need, BarKind.Eat);
                    if (p.FeedT >= need) FeedMate(p, mate);
                }
                else if (p.FeedT > 0f)
                {
                    p.FeedT = 0f;
                    Ui.SetPlayerBar(p.Index, -1f, BarKind.None);
                }
                c.ConsumeInteract();
                taken = true;
                return;
            }
            if (p.FeedT > 0f)
            {
                p.FeedT = 0f;
                Ui.SetPlayerBar(p.Index, -1f, BarKind.None);
            }
            // Reanimar al compañero derribado: mantener pulsado
            if (mate != null && mate.Dead && AllowRevive() && MathUtil.Hypot(mate.X - p.X, mate.Z - p.Z) < 1.9f)
            {
                Ui.SetPrompt(p.Index, "Mantén {E} para reanimar a " + mate.Look.Name);
                if (c.InteractDown())
                {
                    mate.ReviveT += dt;
                    Ui.SetPlayerBar(mate.Index, mate.ReviveT / 2f, BarKind.Revive);
                    if (mate.ReviveT >= 2f) mate.Revive(40f);
                }
                else if (mate.ReviveT > 0f)
                {
                    mate.ReviveT = 0f;
                    Ui.SetPlayerBar(mate.Index, -1f, BarKind.None);
                }
                c.ConsumeInteract();
                taken = true;
            }
        }

        // El jugador p le da su comida al compañero (validado en CoopInteraction: comida en mano y al lado)
        public bool FeedMate(Player p, Player mate)
        {
            var food = p.Weapon;
            if (!food.IsFood || mate.Removed) return false;
            p.FeedT = 0f;
            p.CancelEating();
            Ui.SetPlayerBar(p.Index, -1f, BarKind.None);
            if (mate.Dead)
            {
                mate.Revive(Mathf.Min(mate.MaxHp, FeedConfig.ReviveHp + food.Heal));
                Ui.FloatText(mate.X, 3.0f, mate.Z, "¡Resucitado a bocados!", TextStyle.Heal);
                food.OnEaten(mate, this);
            }
            else
            {
                mate.Heal(food.Heal);
                // La comida con efecto propio (el queso podrido) pone su texto
                if (!food.OnEaten(mate, this)) Ui.FloatText(mate.X, 2.6f, mate.Z, "+" + food.Heal + " ¡Ñam! Gracias", TextStyle.Heal);
            }
            mate.Squash = 1f;
            Audio.Play("eat");
            Audio.Play("heal");
            Effects.Burst(mate.X, 1.3f, mate.Z, BurstOpts.Of(12, 0x7dff8a, 2f, 5f, 0.1f));
            p.SetWeapon(WeaponRegistry.Create("fists"));
            Stats.Fed++;
            return true;
        }
    }
}
