using UnityEngine;

public class DummyTarget : MonoBehaviour, IDamageable
{
    public void TakeDamage(DamageInfo damageInfo)
    {
        Debug.Log($"{name} recibió un impacto.");

        Debug.Log($"Daño: {damageInfo.Damage}");

        Debug.Log($"Dirección: {damageInfo.HitDirection}");
    }
}