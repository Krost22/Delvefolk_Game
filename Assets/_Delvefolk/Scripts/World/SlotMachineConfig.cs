using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Configuración de la tragaperras de los pisos de exploración (Resources/SlotMachine). Independiente de la
    // ruleta del Crupier. Si falta el asset, se usan los valores por defecto de código.
    [CreateAssetMenu(fileName = "SlotMachine", menuName = "Delvefolk/Tragaperras de pisos")]
    public sealed class SlotMachineConfig : ScriptableObject
    {
        [Header("Aparición")]
        [Range(0f, 1f)] public float SpawnChance = 0.75f;
        public int MinFloor = 1, MaxFloor = 99;
        public bool ExcludeFirstFloor = true;
        [Min(0)] public int MaxPerFloor = 1;
        [Tooltip("Obligatoria: la salida no deja bajar hasta haberla girado")]
        public bool Mandatory;

        [Header("Giro")]
        public float ReelSpin = 1.1f;        // lo que gira el primer rodillo
        public float ReelStagger = 0.45f;    // cada rodillo para este tiempo después del anterior
        public float RevealPause = 1.2f;     // pausa con el resultado antes de aplicarlo

        [Header("Volver a girar (solo si ya hay monedas en el juego; desactivado por defecto)")]
        public bool RespinEnabled;
        public int RespinCost = 25;

        [Header("Rodillos")]
        public List<SlotOption> Enemies = new List<SlotOption>();
        public List<SlotOption> Abilities = new List<SlotOption>();
        public List<SlotOption> Affinities = new List<SlotOption>();
        [Range(0f, 1f)] public float AffinityWeaponChance = 1f;   // arma de la afinidad junto a la máquina

        [Header("Depuración (forzar resultados por id; vacío = aleatorio)")]
        public bool DebugForce;
        public string ForceEnemies = "", ForceAbility = "", ForceAffinity = "";

        static SlotMachineConfig cached;
        static bool loaded;
        // Las pruebas usan su propia copia (nunca tocan el asset)
        public static SlotMachineConfig Override;
        public static SlotMachineConfig Load()
        {
            if (Override) return Override;
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<SlotMachineConfig>("SlotMachine");
                if (!cached) { cached = CreateInstance<SlotMachineConfig>(); cached.FillDefaults(); }
            }
            return cached;
        }

        public void FillDefaults()
        {
            Enemies = new List<SlotOption>
            {
                new SlotOption { Id = "slime", Label = "Recompensa: Slimes", Icon = "🟢", Weight = 3, Explain = "Los slimes sueltan +2 monedas." },
                new SlotOption { Id = "goblin", Label = "Recompensa: Goblins", Icon = "👺", Weight = 3, Explain = "Los goblins sueltan +2 monedas." },
                new SlotOption { Id = "grabber", Label = "Recompensa: Abrazones", Icon = "🤗", Weight = 1, Explain = "Los abrazones sueltan +5 monedas." },
                new SlotOption { Id = "all", Label = "Recompensa: Todos", Icon = "💰", Weight = 1, Explain = "Todos los enemigos sueltan +1 moneda." },
            };
            Abilities = new List<SlotOption>
            {
                new SlotOption { Id = "buff_speed", Label = "Patas ligeras", Icon = "💨", Weight = 3, Explain = "+15 % velocidad un buen rato." },
                new SlotOption { Id = "buff_cook", Label = "Golpe fuerte", Icon = "💪", Weight = 3, Explain = "+20 % daño con objetos un buen rato." },
                new SlotOption { Id = "buff_headstrong", Label = "Cabeza dura", Icon = "🤕", Weight = 2, Explain = "Te recuperas antes de los aturdimientos." },
                new SlotOption { Id = "item_shield", Label = "Escudo", Icon = "🛡️", Weight = 2, Explain = "Bloquea por completo un impacto." },
                new SlotOption { Id = "heal_kill", Label = "Curación por eliminación", Icon = "❤️", Weight = 2, Explain = "Cada enemigo derrotado en este piso cura 3 de vida." },
            };
            Affinities = new List<SlotOption>
            {
                new SlotOption { Id = "Water", Label = "Agua", Icon = "🌊", Weight = 1 },
                new SlotOption { Id = "Fire", Label = "Fuego", Icon = "🔥", Weight = 1 },
                new SlotOption { Id = "Plant", Label = "Planta", Icon = "🌿", Weight = 1 },
                new SlotOption { Id = "Poison", Label = "Veneno", Icon = "🍄", Weight = 1 },
                new SlotOption { Id = "Future", Label = "Futurista", Icon = "⚡", Weight = 1 },
            };
        }
    }
}
