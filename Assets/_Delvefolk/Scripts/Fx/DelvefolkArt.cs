using UnityEngine;

namespace Delvefolk
{
    // Catálogo de modelos de los paquetes de assets instalados (mazmorra low-poly y monstruos).
    // Lo rellena el script de editor Delvefolk/Reconstruir catálogo de arte. Si falta algo (p. ej. un clon
    // sin los paquetes), el juego usa los modelos hechos por código del original.
    [CreateAssetMenu(fileName = "DelvefolkArt", menuName = "Delvefolk/Catálogo de arte")]
    public sealed class DelvefolkArt : ScriptableObject
    {
        public Shader FlatShader;

        [Header("Mazmorra (piezas de 4 u: se escalan a la casilla de 2 u)")]
        public GameObject[] Floors;
        public GameObject[] FloorsRare;
        public GameObject[] Walls;
        public GameObject Pillar;
        public GameObject ArchPost;
        public GameObject ArchLintel;
        public GameObject[] Crates;
        public GameObject Torch;
        public GameObject EntranceDoor;
        public GameObject GateBars;
        public GameObject Spikes;

        [Header("Decoración")]
        public GameObject[] Rubble;
        public GameObject[] Bones;
        public GameObject Skull;
        public GameObject[] Webs;
        public GameObject[] Banners;
        public GameObject[] Chains;
    }
}
