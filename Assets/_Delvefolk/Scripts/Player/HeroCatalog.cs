using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public enum HeroHand { Right, Left }

    // Ajuste del arma en la mano de un personaje concreto (se suma al agarre general del arma)
    [System.Serializable]
    public sealed class HeroWeaponOffset
    {
        public string WeaponId;
        public Vector3 Offset;
        public Vector3 Euler;
        public float Scale = 1f;
    }

    // Un personaje personalizado (modelo con esqueleto propio). Los huesos se buscan por nombre una sola vez al
    // montarlo; si alguno no existe se avisa por consola y el personaje se dibuja sin brazo animado.
    [System.Serializable]
    public sealed class HeroEntry
    {
        public string Id;
        public string Name;
        public string Icon = "🧝";
        [Tooltip("FBX con esqueleto (el *_biped de Meshy)")]
        public GameObject Prefab;
        [Tooltip("Habilidad del personaje (id de Abilities): es la que se muestra y se usa en la partida")]
        public string Ability = "quake";
        [Tooltip("Animación de correr (en bucle)")]
        public AnimationClip Run;
        [Tooltip("Animación de reposo (en bucle). Vacía: se usa el fotograma más neutro del correr")]
        public AnimationClip Idle;
        [Tooltip("Velocidad del jugador a la que la animación de correr va a 1×")]
        public float RunSpeedRef = 6.4f;
        [Tooltip("Ajuste fino sobre la altura común del catálogo (1 = exactamente TargetHeight)")]
        public float Scale = 1f;
        [Tooltip("Giro del modelo (grados) para que mire hacia +Z")]
        public float Yaw;

        [Header("Mano del arma")]
        public HeroHand Hand = HeroHand.Right;
        public string UpperArmBone = "mixamorig:RightArm";
        public string ForeArmBone = "mixamorig:RightForeArm";
        public string HandBone = "mixamorig:RightHand";
        [Tooltip("Posición del socket respecto a la mano (x derecha, y arriba, z hacia donde apunta el arma)")]
        public Vector3 SocketOffset = new Vector3(0f, 0f, 0.04f);
        public Vector3 SocketEuler;
        public float SocketScale = 1f;
        [Tooltip("Ajustes por arma para este personaje")]
        public List<HeroWeaponOffset> WeaponOffsets = new List<HeroWeaponOffset>();

        public HeroWeaponOffset OffsetFor(string weaponId)
        {
            foreach (var o in WeaponOffsets) if (o != null && o.WeaponId == weaponId) return o;
            return null;
        }
    }

    // Personajes seleccionables en la sala (Resources/Heroes). Lo crea Delvefolk/Preparar personajes de Meshy.
    // "classic" (el aventurero hecho por código) siempre existe aunque no esté en la lista.
    [CreateAssetMenu(fileName = "Heroes", menuName = "Delvefolk/Personajes")]
    public sealed class HeroCatalog : ScriptableObject
    {
        public const string Classic = "classic";
        [Tooltip("Altura en partida de TODOS los personajes (unidades del juego; casilla = 2, muro = 3,2). Cada modelo se " +
                 "normaliza a esta altura: los FBX de Meshy vienen en metros reales (1,6–1,9) y se veían pequeños y desiguales")]
        public float TargetHeight = 2.3f;
        public List<HeroEntry> Heroes = new List<HeroEntry>();

        public static float Height { get { var c = Load(); return c != null ? c.TargetHeight : 1.85f; } }

        // Las pruebas del juego original usan el aventurero clásico (la sala, en cambio, solo ofrece personajes)
        public static bool ClassicAllowed;

        // Personaje por defecto de cada hueco (uno distinto por jugador)
        public static string DefaultFor(int index)
        {
            var c = Load();
            if (ClassicAllowed || c == null) return Classic;
            var l = Order(false);
            return l.Count > 0 ? l[index % l.Count] : Classic;
        }

        static HeroCatalog cached;
        static bool loaded;
        public static HeroCatalog Load()
        {
            if (!loaded) { loaded = true; cached = Resources.Load<HeroCatalog>("Heroes"); }
            return Art.UseAssets ? cached : null;
        }

        public static HeroEntry Get(string id)
        {
            var c = Load();
            if (c == null || string.IsNullOrEmpty(id) || id == Classic) return null;
            foreach (var h in c.Heroes) if (h != null && h.Id == id && h.Prefab) return h;
            return null;
        }

        // Orden de la sala: solo los personajes con modelo (con classic = true, también el clásico, el primero)
        public static List<string> Order(bool classic = false)
        {
            var l = new List<string>();
            if (classic || Load() == null) l.Add(Classic);
            var c = Load();
            if (c != null) foreach (var h in c.Heroes) if (h != null && h.Prefab) l.Add(h.Id);
            return l;
        }

        public static string NameOf(string id) { var h = Get(id); return h != null ? h.Name : "Aventurero clásico"; }
        public static string IconOf(string id) { var h = Get(id); return h != null ? h.Icon : "🍲"; }
        public static string AbilityOf(string id) { var h = Get(id); return h != null && Abilities.Get(h.Ability) != null ? h.Ability : null; }
    }
}
