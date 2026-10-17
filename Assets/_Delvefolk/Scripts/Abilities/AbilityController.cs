using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Activación en curso de una habilidad: cada objetivo se ve afectado una sola vez por activación (HitSet)
    public sealed class AbilityAct
    {
        public float T;
        public int Serial;
        public bool Cancelled, Crashed;
        public readonly HashSet<object> HitSet = new HashSet<object>();
        public List<Barrel> Barrels;   // Imán de Chatarra: barriles atraídos
    }

    // Contexto que reciben las funciones de una habilidad (ctx del original: game, player, act, p)
    public struct AbilityCtx
    {
        public Game G;
        public Player P;
        public AbilityAct Act;
        public AbilityDef Def;
        public float this[string key] { get { return Def.P[key]; } }
    }

    // Activación y enfriamiento de la habilidad especial de un jugador (AbilityController.js).
    // Reglas: una sola habilidad equipada; no se puede activar si ya está activa o recargando, ni derribado,
    // en brazos, agarrado, volando, aturdido o fuera de juego. El enfriamiento empieza al activarla.
    public sealed class AbilityController
    {
        public readonly Player Player;
        public AbilityDef Def { get; private set; }
        public float Cd;
        public int Serial;         // número de activaciones (útil para pruebas y estadísticas)
        float deniedT;
        public AbilityAct Act { get; private set; }

        public AbilityController(Player player, string id)
        {
            Player = player;
            SetAbility(id);
        }

        public void SetAbility(string id)
        {
            if (Act != null) Finish();
            Def = Abilities.Get(id) ?? Abilities.Get(Abilities.DefaultFor(Player != null ? Player.Index : 0));
            Cd = 0f;
            Act = null;
        }

        public string Id { get { return Def.Id; } }
        public bool Active { get { return Act != null; } }
        public bool Ready { get { return Cd <= 0f && Act == null; } }
        // Fracción del enfriamiento que queda (1 recién usada, 0 lista): relleno circular del panel
        public float CooldownK { get { return Def.Cooldown > 0f ? Mathf.Clamp01(Cd / Def.Cooldown) : 0f; } }
        public bool LocksMovement { get { return Act != null && Def.LocksMovement; } }
        // Banderas defensivas de la habilidad activa (Último Recurso)
        public float DamageTakenMult { get { return Act != null ? Def.DamageTakenMult : 1f; } }
        public bool KnockImmune { get { return Act != null && Def.KnockImmune; } }
        public bool Unstoppable { get { return Act != null && Def.Unstoppable; } }

        AbilityCtx Ctx() { return new AbilityCtx { G = Player.G, P = Player, Act = Act, Def = Def }; }

        public string BlockReason()
        {
            var pl = Player;
            var g = pl.G;
            if (g.State != GameState.Play) return "fuera de juego";
            if (pl.Dead) return "derribado";
            if (pl.CarriedBy != null) return "en brazos";
            if (pl.GrabbedBy != null) return "agarrado";
            if (pl.IsThrown) return "volando";
            if (pl.StunT > 0f) return "aturdido";
            if (g.AbilitiesBlockedFor(pl)) return "eliminado del desafío";
            if (Act != null) return "ya activa";
            if (Cd > 0f) return "recargando";
            return null;
        }

        public bool TryActivate()
        {
            var pl = Player;
            var g = pl.G;
            string why = BlockReason();
            if (why != null)
            {
                if (deniedT <= 0f && (why == "recargando" || why == "aturdido"))
                {
                    deniedT = 0.6f;
                    g.Ui.FloatText(pl.X, 2.6f, pl.Z, why == "recargando" ? "Recargando… " + Mathf.CeilToInt(Cd) + " s" : "¡Estoy mareado!", TextStyle.Small);
                    g.Audio.Play("denied");
                }
                return false;
            }
            pl.CancelEating();
            pl.Attack = null;
            Act = new AbilityAct { Serial = Serial + 1 };
            bool ok = Def.Activate == null || Def.Activate(Ctx());
            if (!ok)
            {
                // No había objetivo válido: no se gasta el enfriamiento (solo un pequeño respiro anti-spam)
                Act = null;
                Cd = 0.4f;
                g.Audio.Play("denied");
                return false;
            }
            Serial++;
            Cd = Def.Cooldown;
            if (g.Stats != null) g.Stats.Abilities++;
            if (Def.Duration <= 0f) Act = null;
            g.Ui.SetAbility(pl.Index, this);
            return true;
        }

        public void Update(float dt)
        {
            deniedT = Mathf.Max(0f, deniedT - dt);
            if (Cd > 0f)
            {
                Cd = Mathf.Max(0f, Cd - dt);
                if (Cd == 0f && Act == null)
                {
                    var pl = Player;
                    if (!pl.Dead && pl.G.State == GameState.Play)
                    {
                        pl.G.Audio.Play("abilityReady");
                        pl.G.Effects.Ring(pl.X, pl.Z, RingOpts.Of(Def.Color, 1.1f, 0.3f, 0.1f));
                    }
                }
            }
            if (Act != null)
            {
                Act.T += dt;
                if (Def.Tick != null) Def.Tick(Ctx(), dt);
                if (Act != null && (Act.T >= Def.Duration || Act.Cancelled)) Finish();
            }
        }

        // Termina la habilidad activa (también al ser derribado o agarrado)
        public void Finish()
        {
            if (Act == null) return;
            if (Def.End != null) Def.End(Ctx());
            Act = null;
        }
    }
}
