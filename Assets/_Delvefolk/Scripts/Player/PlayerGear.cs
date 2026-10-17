using UnityEngine;

namespace Delvefolk
{
    public struct IncomingResult
    {
        public bool Blocked, ArmorHit, ArmorBroke;
        public float Amount;
    }

    // Equipo de JUEGO de un jugador (PlayerGear.js): armadura, objetos, mejoras y oro.
    // Las recompensas llegan en la fase 6; de momento solo el oro y los modificadores neutros.
    public sealed partial class PlayerGear
    {
        public readonly Player Owner;
        public int Gold;

        public PlayerGear(Player owner) { Owner = owner; }

        public float SpeedMult() { float v = 1f; SpeedMultImpl(ref v); return v; }
        public float StunMult() { float v = 1f; StunMultImpl(ref v); return v; }
        public float KnockTakenMult() { float v = 1f; KnockTakenMultImpl(ref v); return v; }
        public float DamageMult() { float v = 1f; DamageMultImpl(ref v); return v; }
        public float KnockDealtMult() { float v = 1f; KnockDealtMultImpl(ref v); return v; }
        public float Luck() { float v = 0f; LuckImpl(ref v); return v; }
        public bool Has(string id) { bool v = false; HasImpl(id, ref v); return v; }

        partial void SpeedMultImpl(ref float v);
        partial void StunMultImpl(ref float v);
        partial void KnockTakenMultImpl(ref float v);
        partial void DamageMultImpl(ref float v);
        partial void KnockDealtMultImpl(ref float v);
        partial void LuckImpl(ref float v);
        partial void HasImpl(string id, ref bool v);

        // Daño que llega al jugador tras escudo, ídolo, trampas y armadura: max(1, round(·))
        public IncomingResult ModifyIncoming(float amount, string kind)
        {
            var r = new IncomingResult { Amount = amount };
            bool handled = false;
            ModifyIncomingImpl(ref r, kind, ref handled);
            if (!r.Blocked) r.Amount = Mathf.Max(1f, Mathf.Round(r.Amount));
            return r;
        }

        partial void ModifyIncomingImpl(ref IncomingResult r, string kind, ref bool handled);

        // Devuelve true si algo de la UI debe refrescarse (mejoras que caducan)
        public bool Update(float dt) { bool changed = false; UpdateImpl(dt, ref changed); return changed; }
        partial void UpdateImpl(float dt, ref bool changed);

        // Lo que enseña el panel de EQUIPO: armadura (icono y fracción que le queda) y fichas de objetos y
        // mejoras (texto enriquecido). Sin armadura, armorIcon queda a null.
        public void HudInfo(out string armorIcon, out float armorK, out string chips)
        {
            armorIcon = null; armorK = 0f; chips = "";
            HudInfoImpl(ref armorIcon, ref armorK, ref chips);
        }

        partial void HudInfoImpl(ref string armorIcon, ref float armorK, ref string chips);

        // ¿Hay mejoras con cuenta atrás? (el panel las refresca una vez por segundo)
        public bool HasTimedBuffs { get { bool v = false; HasTimedBuffsImpl(ref v); return v; } }
        partial void HasTimedBuffsImpl(ref bool v);

        public void RefreshVisual(CharacterParts parts) { RefreshVisualImpl(parts); }
        partial void RefreshVisualImpl(CharacterParts parts);

        public GearState GetState() { var s = new GearState { gold = Gold }; GetStateImpl(s); return s; }
        partial void GetStateImpl(GearState s);

        public void SetState(GearState s)
        {
            if (s == null) return;
            Gold = s.gold;
            SetStateImpl(s);
        }
        partial void SetStateImpl(GearState s);
    }

    [System.Serializable]
    public sealed partial class GearState
    {
        public int gold;
    }
}
