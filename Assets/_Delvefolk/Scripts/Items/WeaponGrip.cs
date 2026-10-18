using UnityEngine;

namespace Delvefolk
{
    // Va en la raíz del modelo importado de un arma (en la mano o en el suelo). En el editor vuelve a aplicar
    // la entrada del catálogo cada frame: al cambiar el agarre en Resources/WeaponModels se ve al instante.
    public sealed class WeaponGrip : MonoBehaviour
    {
        public WeaponModelEntry Entry;
        Transform norm;

        public void Init(WeaponModelEntry e, Transform n)
        {
            Entry = e; norm = n;
            WeaponModels.Apply(Entry, transform, norm);
        }

#if UNITY_EDITOR
        void LateUpdate()
        {
            if (Entry != null && norm) WeaponModels.Apply(Entry, transform, norm);
        }
#endif
    }
}
