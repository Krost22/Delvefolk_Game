using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    private InputSystem_Actions inputActions;
    private IAttack currentAttack;

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
            currentAttack.Execute();
        }
    }
}