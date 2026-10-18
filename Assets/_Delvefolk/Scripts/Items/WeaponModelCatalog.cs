using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Catálogo de modelos de armas (Resources/WeaponModels). Lo crea Delvefolk/Preparar armas de Meshy;
    // si falta un modelo, el arma usa su modelo hecho por código.
    [CreateAssetMenu(fileName = "WeaponModels", menuName = "Delvefolk/Modelos de armas")]
    public sealed class WeaponModelCatalog : ScriptableObject
    {
        public List<WeaponModelEntry> Entries = new List<WeaponModelEntry>();

        public WeaponModelEntry Get(string id)
        {
            foreach (var e in Entries) if (e != null && e.Id == id) return e;
            return null;
        }
    }
}
