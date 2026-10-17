namespace Delvefolk
{
    // Salto: solo visual en altura (la física sigue siendo 2D). En el aire se pasa por encima de enemigos,
    // barriles y compañeros (no de columnas ni muebles), se esquivan ataques y púas, y al caer encima de
    // un enemigo se le aplasta y se rebota. (DF.JUMP)
    public static class JumpConfig
    {
        public const float Speed = 10f, Gravity = 30f;          // altura máxima ≈ 1.67
        public const float PassHeight = 0.45f;                  // a partir de aquí no choca con cuerpos que se mueven
        public const float DodgeHeight = 0.85f;                 // a partir de aquí los ataques cuerpo a cuerpo y los agarres no le alcanzan
        public const float StompDamage = 10f, StompStun = 0.9f, Bounce = 8f, BouncyBounce = 10.5f;
        // Energía (aguante): saltar y pisotear la gastan. Si baja de 0, al caer se queda agotado y aturdido.
        public const float Stamina = 100f, JumpCost = 30f, StompCost = 25f;
        public const float Regen = 35f, RegenDelay = 0.7f;      // por segundo, a partir de este tiempo en el suelo sin saltar
        public const float ExhaustStun = 1.3f, ExhaustBounce = 3f;
        public const float MateStompDamage = 10f, MateStompStun = 0.8f;   // caer encima de un compañero
    }

    // Forcejeo para soltarse: cada pulsación de atacar, interactuar o saltar suma; el progreso se pierde poco a poco
    public static class StruggleConfig
    {
        public const float MateNeed = 3f, MateDecay = 1.2f, Gloves = 1.5f;
    }

    // Dar de comer al compañero: tiempo manteniendo interactuar, alcance y vida base al levantarlo con comida
    public static class FeedConfig
    {
        public const float Time = 0.8f, ReviveTime = 1.0f, Range = 1.9f, ReviveHp = 40f;
    }

    // Reglas globales de aturdimiento (DF.ABILITY_RULES)
    public static class AbilityRules
    {
        public const float EnemyStunCap = 1.6f;      // aturdimiento máximo a enemigos por habilidad
        public const float PlayerStunCap = 1.2f;     // a jugadores
        public const float PlayerStunGuard = 1.0f;   // inmunidad del jugador tras un aturdimiento
    }
}
