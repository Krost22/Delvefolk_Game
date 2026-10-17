using System.Collections.Generic;

namespace Delvefolk
{
    // Configuración de los pisos bonus (BonusConfig.js): cuándo aparecen, qué desafíos salen y sus reglas.
    // Todo lo que afecta al equilibrio está aquí para poder ajustarlo sin tocar la lógica (y las pruebas pueden cambiarlo).
    public static class BonusConfig
    {
        // Aparición. bonusMode (ajuste del anfitrión): "normal" | "always" | "never"
        public static readonly int[] AfterFloors = { 1 };   // tras estos pisos SIEMPRE hay bonus (en modo normal), una vez por partida
        public static float Chance = 0.35f;                 // tras cualquier otro piso, probabilidad de bonus
        public static int MinFloor = 1;                     // no antes de terminar este piso
        public static bool NoBackToBack = true;             // nunca dos desafíos iguales seguidos

        public static float IntroTimeout = 20f;             // s: la introducción empieza sola si nadie confirma
        public static float ResultTimeout = 25f;            // s: la pantalla de resultados avanza sola
        public static float ReviveHp = 30f;                 // vida con la que se levanta quien cayó en el bonus

        // Peso de cada desafío en el sorteo (0 = desactivado)
        public static readonly List<KeyValuePair<string, float>> Weights = new List<KeyValuePair<string, float>>
        {
            new KeyValuePair<string, float>("minefield", 3), new KeyValuePair<string, float>("loot", 2), new KeyValuePair<string, float>("arena", 2),
            new KeyValuePair<string, float>("bridge", 2), new KeyValuePair<string, float>("feast", 2), new KeyValuePair<string, float>("breath", 2),
        };

        public static class Minefield
        {
            public static int Cols = 8, Rows = 6;
            public static float Mines = 8, MinesPerFloor = 0.5f;
            public static int MaxMines = 13;                       // dificultad: más minas en pisos profundos
            public static float TimeLimit = 150f;
            public static int Strikes = 3;                         // oportunidades (errores) permitidos
            public static string ErrorScope = "team";              // "team": compartidos · "player": cada uno los suyos
            public static string StepPenalty = "eliminate";        // pisar una mina: "eliminate" o "damage"
            public static float ProbeDamage = 18f, StepDamage = 30f;
            public static float BlastRadius = 3.0f;                // cubre al que sondea desde cualquier losa vecina (también en diagonal)
            public static float TargetRatio = 1.0f;                // fracción de casillas seguras a revelar para ganar
            public static bool RequireSolvable = true;             // tablero resoluble por deducción, sin adivinar
            public static int MaxTries = 300;
            public static int Hint = 1, Shield = 1, Gold = 2;      // losas especiales
            public static int RewardRolls = 1, PerfectBonusRolls = 1;   // tirada extra si no se gastó ninguna oportunidad
        }

        public static class Loot
        {
            public static float TimeLimit = 60f;
            public static int Chests = 6, MaxOpensPerPlayer = 2;
            public static float OpenTime = 0.9f, ExamineTime = 2.2f, HintAccuracy = 0.8f;
            // Pesos del contenido de los cofres normales
            public static readonly List<KeyValuePair<string, float>> Contents = new List<KeyValuePair<string, float>>
            {
                new KeyValuePair<string, float>("reward", 5), new KeyValuePair<string, float>("shared", 2), new KeyValuePair<string, float>("trap", 2),
                new KeyValuePair<string, float>("curse", 2), new KeyValuePair<string, float>("monsters", 2),
            };
            public static float GoldenRisk = 0.5f;   // el cofre dorado: 2 recompensas raras... o una explosión con premio
        }

        public static class Arena
        {
            public static float Duration = 45f, WaveEvery = 7f, ShrinkEvery = 9f, DangerDps = 12f;
            public static int WaveSize = 3, WaveGrowth = 1;
            public static int KillGoal = 8;                                  // objetivo de equipo: bonus extra
            public static readonly float[][] RewardTiers = { new[] { 45f, 2f }, new[] { 25f, 1f } };   // [segundos sobrevividos, tiradas]
        }

        public static class Bridge
        {
            public static float TimeLimit = 70f;
            public static int Cols = 7;
            public static readonly float[] FragileByDistance = { 0.45f, 0.3f, 0.2f, 0.12f };   // probabilidad de losa frágil según distancia a la columna central
            public static int UsesCenter = 1, UsesOther = 2;   // jugadores distintos que aguanta una losa segura antes de caer
            public static float FallDamage = 15f, CollapseDelay = 0.45f, WornDelay = 0.8f;
            public static int FirstBonusRolls = 1;
            public static float FastBonusTime = 25f;
        }

        public static class Feast
        {
            public static float TimeLimit = 45f;
            public static int Dishes = 8, MaxBitesPerPlayer = 3;
            public static float EatTime = 1.0f, SniffTime = 1.6f, SniffAccuracy = 0.85f;
            public static readonly List<KeyValuePair<string, float>> Contents = new List<KeyValuePair<string, float>>
            {
                new KeyValuePair<string, float>("buff", 4), new KeyValuePair<string, float>("mimic", 2), new KeyValuePair<string, float>("clue", 1),
            };
            public static int Forbidden = 1;   // "festín prohibido": gran premio para quien lo come, castigo para el resto
        }

        public static class Breath
        {
            public static float Duration = 40f, PhaseEvery = 7f, WarnTime = 3f, DangerDps = 14f;
            public static readonly int[] Zones = { 3, 3, 2, 2, 1, 1 };
            public static float RollerEvery = 6f, RollerSpeed = 8f, RollerDamage = 15f;
            public static int RewardRolls = 1, LastStandingRolls = 1;
        }
    }
}
