using UnityEngine;

public class AttackContext
{
    public Transform Attacker { get; }

    public Transform CombatOrigin { get; }

    public AttackContext(
        Transform attacker,
        Transform combatOrigin)
    {
        Attacker = attacker;
        CombatOrigin = combatOrigin;
    }
}