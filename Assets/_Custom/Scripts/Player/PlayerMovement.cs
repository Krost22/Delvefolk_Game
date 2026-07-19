using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Moving")]
    public float speed;
    public float rotationSpeed;
    [Header("Jump")]
    public float jumpForce = 5;
    Rigidbody rb;

    Vector3 forward, right; // vectores de la cámara proyectados al plano XZ, se calculan una vez en Start()
    [Header("Inputs")]
    private InputSystem_Actions inputActions;

    [Header("isGroundedCheck")]
    bool isGrounded = true;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundRadius = 0.25f;
    [SerializeField] private LayerMask groundLayer;


    private void Awake()
    {
        inputActions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        inputActions.Enable();
    }

    void Start()
    {
        // Obtenemos la dirección "hacia delante" de la cámara
        // (en isométrica no apunta al +Z del mundo, sino en diagonal)
        forward = Camera.main.transform.forward;
        forward.y = 0;                      // eliminamos la componente vertical
        forward = Vector3.Normalize(forward); // normalizamos para que mida 1

        // Misma idea pero para el eje "derecha" de la cámara
        right = Camera.main.transform.right;
        right.y = 0;
        right = Vector3.Normalize(right);

        // Ahora forward y right son los vectores correctos para movernos
        // relativos a lo que el jugador ve en pantalla

        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        // Leemos el valor del input como Vector2 (x = izq/der, y = arr/aba)
        Vector2 input = inputActions.Player.Move.ReadValue<Vector2>();

        // Convertimos el input 2D a una dirección 3D usando los vectores de la cámara
        // input.y (W/flecha arriba) -> forward (lejos de la cámara)
        // input.x (D/flecha derecha) -> right (a la derecha de la cámara)
        Vector3 moveDirection = (forward * input.y) + (right * input.x);

        // ClampMagnitude evita que en diagonal el jugador sea más rápido
        // (sin esto, la suma de dos vectores unitarios mide ~1.41)
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        // Movemos al jugador: dirección * velocidad * tiempo entre frames
        transform.position += moveDirection * speed * Time.deltaTime;

        // Si hay movimiento, rotamos al personaje para que mire hacia donde va
        if (moveDirection != Vector3.zero)
        {
            // LookRotation calcula la rotación que orienta el transform hacia esa dirección
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            // Slerp suaviza la rotación para que no sea instantánea
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        //Logica de Salto
        IsGrounded();
        if (inputActions.Player.Jump.WasPressedThisFrame() && isGrounded == true)
        {
            Jump();
        }
    }
    public void IsGrounded()
    {
        isGrounded = Physics.CheckSphere(
        groundCheck.position,
        groundRadius,
        groundLayer);
    }

    public void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);

        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }
    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundRadius);
    }
}