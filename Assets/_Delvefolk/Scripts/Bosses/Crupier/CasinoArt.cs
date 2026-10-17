using UnityEngine;

namespace Delvefolk
{
    // Modelos de la sala del trono del Crupier (Assets/_Custom/Jefe final escenario models). Lo rellena
    // Delvefolk/Preparar sala del Crupier. Si falta un modelo, esa pieza se omite o se usa el placeholder.
    [CreateAssetMenu(fileName = "CasinoArt", menuName = "Delvefolk/Catálogo del casino")]
    public sealed class CasinoArt : ScriptableObject
    {
        public GameObject Throne;
        public GameObject Lion;
        public GameObject Chandelier;
        public GameObject Emblem;          // alfombra octogonal con corona y cuatro brazos
        public GameObject Runner;          // alfombra alargada con tréboles
        public GameObject CasinoTable;
        public GameObject SlotMachine;
        public GameObject Column;          // columna con estandarte de picas
        public GameObject Wheel;           // prefab RuedaCrupier (marco fijo + "Modelo/Giro")

        [Header("Giro (grados) para que cada modelo mire hacia +Z en el juego")]
        public float ThroneYaw = 180f;
        public float LionYaw = 180f;
        public float TableYaw = 0f;
        public float SlotYaw = 180f;
        public float ColumnYaw = 0f;

        static CasinoArt cached;
        static bool loaded;
        public static CasinoArt Load()
        {
            if (!loaded) { loaded = true; cached = Resources.Load<CasinoArt>("CasinoArt"); }
            return Art.UseAssets ? cached : null;
        }
    }
}
