using UnityEngine;

public class DamageInfo
{
    public float Damage { get; }

    public Vector3 HitDirection { get; }

    public Transform Attacker { get; }

    public DamageInfo(
        float damage,
        Vector3 hitDirection,
        Transform attacker)
    {
        Damage = damage;
        HitDirection = hitDirection;
        Attacker = attacker;
    }
}