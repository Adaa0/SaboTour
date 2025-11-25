using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float turnSmoothTime = 0.1f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float gravity = -9.81f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;

    // Components
    private CharacterController controller;

    // Movement variables
    private Vector3 velocity;
    private float turnSmoothVelocity;
    private bool isGrounded;

    // Animation parameter hashes (daha performanslı)
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");

    private void Start()
    {
        InitializeComponents();
    }

    private void Update()
    {
        CheckGroundStatus();
        HandleMovement();
        HandleJump();
        ApplyGravity();
    }

    private void InitializeComponents()
    {
        controller = GetComponent<CharacterController>();
        
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (cameraTransform == null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void CheckGroundStatus()
    {
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // Animator'e ground durumunu gönder
        if (animator != null)
        {
            animator.SetBool(IsGroundedHash, isGrounded);
        }
    }

    private void HandleMovement()
    {
        // Input değerlerini al
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;

        // Hareket var mı?
        if (direction.magnitude >= 0.1f)
        {
            // Karakteri kamera yönüne göre döndür
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            float smoothAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);

            // Hareket yönünü hesapla
            Vector3 moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            // Koşma kontrolü
            bool isRunning = Input.GetKey(KeyCode.LeftShift);
            float currentSpeed = isRunning ? runSpeed : walkSpeed;

            // Karakteri hareket ettir
            controller.Move(moveDirection.normalized * currentSpeed * Time.deltaTime);

            // Animasyonları güncelle
            UpdateMovementAnimations(currentSpeed, isRunning);
        }
        else
        {
            // Duruyorsa Idle animasyonuna geç
            UpdateMovementAnimations(0f, false);
        }
    }

    private void HandleJump()
    {
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            // Zıplama hızını hesapla
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            // Zıplama animasyonunu tetikle
            if (animator != null)
            {
                animator.SetTrigger(JumpHash);
            }
        }
    }

    private void ApplyGravity()
    {
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void UpdateMovementAnimations(float speed, bool isRunning)
    {
        if (animator == null) return;

        // Speed parametresini güncelle (0 = Idle, 4 = Walk, 8 = Run)
        animator.SetFloat(SpeedHash, speed);

        // IsRunning bool parametresini güncelle
        animator.SetBool(IsRunningHash, isRunning);
    }
}