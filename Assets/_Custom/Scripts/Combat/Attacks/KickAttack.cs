using UnityEngine;

public class KickAttack : IAttack
{
    private const float Radius = 0.5f;
    public void Execute(AttackContext context)
    {
        Collider[] hits = Physics.OverlapSphere(context.CombatOrigin.position,Radius);

        foreach (Collider hit in hits)
        {
            if (hit.transform == context.Attacker)
                continue;

            //Solo golpea a los que tengan esta interfaz
            IDamageable damageable =
                hit.GetComponent<IDamageable>();

            if (damageable == null)
                continue;

            DamageInfo damageInfo =
                new DamageInfo(
                    0f,
                    context.Attacker.forward,
                    context.Attacker);

            damageable.TakeDamage(damageInfo);
        }
        //gizmo de la patada del jugador
        Debug.DrawLine(context.Attacker.position,context.CombatOrigin.position,Color.green,1f);
    }
}