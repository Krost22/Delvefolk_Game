namespace Delvefolk
{
    // Activación y enfriamiento de la habilidad especial de un jugador (AbilityController.js).
    // Las 7 habilidades llegan en la fase 5; mientras tanto el jugador no tiene ninguna activa.
    public sealed partial class AbilityController
    {
        public readonly Player Player;
        public string Id = "quake";
        public float Cd;
        public int Serial;

        public AbilityController(Player player, string id)
        {
            Player = player;
            Id = Abilities.Get(id) != null ? id : Abilities.DefaultFor(player != null ? player.Index : 0);
        }

        public AbilityDef Def { get { return Abilities.Get(Id); } }
        public bool Active { get { return IsActive(); } }
        public bool Ready { get { return Cd <= 0f && !Active; } }
        // Fracción del enfriamiento que queda (1 recién usada, 0 lista): relleno circular del panel
        public float CooldownK { get { var d = Def; return d == null || d.Cooldown <= 0f ? 0f : UnityEngine.Mathf.Clamp01(Cd / d.Cooldown); } }
        // Banderas de la habilidad activa (Terremoto bloquea el movimiento; Último Recurso protege)
        public bool LocksMovement { get { bool v = false; LocksMovementImpl(ref v); return v; } }
        public float DamageTakenMult { get { float v = 1f; DamageTakenMultImpl(ref v); return v; } }
        public bool KnockImmune { get { bool v = false; KnockImmuneImpl(ref v); return v; } }
        public bool Unstoppable { get { bool v = false; UnstoppableImpl(ref v); return v; } }

        bool IsActive() { bool v = false; IsActiveImpl(ref v); return v; }

        partial void IsActiveImpl(ref bool v);
        partial void LocksMovementImpl(ref bool v);
        partial void DamageTakenMultImpl(ref float v);
        partial void KnockImmuneImpl(ref bool v);
        partial void UnstoppableImpl(ref bool v);

        public bool TryActivate() { bool ok = false; TryActivateImpl(ref ok); return ok; }
        partial void TryActivateImpl(ref bool ok);

        public void Update(float dt) { UpdateImpl(dt); }
        partial void UpdateImpl(float dt);

        // Termina la habilidad activa (también al ser derribado o agarrado)
        public void Finish() { FinishImpl(); }
        partial void FinishImpl();
    }
}
