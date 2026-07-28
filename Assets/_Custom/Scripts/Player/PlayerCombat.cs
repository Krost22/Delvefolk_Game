using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    private InputSystem_Actions inputActions;
    private IAttack currentAttack;
    [SerializeField]
    private Transform combatOrigin;

    private void OnEnable()
    {
        inputActions.Enable();
    }

    private void Awake()
    {  
        inputActions = new InputSystem_Actions();
        currentAttack = new KickAttack();
    }

    private void Update()
    {
        if (inputActions.Player.Attack.WasPressedThisFrame())
        {
            AttackContext context = new AttackContext(transform, combatOrigin);

            currentAttack.Execute(context);
        }
    }
    //gizmo de la zona de la patada del jugador
    private void OnDrawGizmosSelected()
    {
        if (combatOrigin == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(combatOrigin.position, 0.5f);
    }
}