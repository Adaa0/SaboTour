using UnityEngine;

public class PhysicalButton : MonoBehaviour
{
    [Header("Etkileşim Ayarları")]
    [SerializeField] private float maxDistance = 3f;
    [SerializeField] private float interactionAngle = 45f;
    [SerializeField] private KeyCode interactionKey = KeyCode.E;
    
    [Header("Buton Görsel")]
    [SerializeField] private GameObject buttonVisual;
    [SerializeField] private Material normalMaterial;
    [SerializeField] private Material highlightMaterial;
    
    [Header("Spawn Ayarları")]
    [SerializeField] private GameObject prefabToSpawn;
    [SerializeField] private Transform spawnPoint;
    
    [Header("Buton Animasyonu")]
    [SerializeField] private float pressDepth = 0.1f;
    [SerializeField] private float pressSpeed = 10f;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    
    private Camera mainCamera;
    private Transform playerTransform;
    private bool canInteract = false;
    private Renderer buttonRenderer;
    private Vector3 originalPosition;
    private Vector3 pressedPosition;
    private bool isPressed = false;

    void Start()
    {
        mainCamera = Camera.main;

        ThirdPersonMovement player = FindAnyObjectByType<ThirdPersonMovement>();
        if (player != null)
        {
            playerTransform = player.transform;
        }
        
        if (buttonVisual != null)
        {
            buttonRenderer = buttonVisual.GetComponent<Renderer>();
            originalPosition = buttonVisual.transform.localPosition;
            pressedPosition = originalPosition - new Vector3(0, pressDepth, 0);
        }
    }

    void Update()
    {
        CheckInteraction();
        HandleInput();
        AnimateButton();
    }

    void CheckInteraction()
    {
        if (mainCamera == null || playerTransform == null)
        {
            if (showDebugInfo) Debug.LogWarning("Kamera veya oyuncu bulunamadı!");
            return;
        }

        float distance = Vector3.Distance(playerTransform.position, transform.position);   
        Vector3 directionToButton = (transform.position - mainCamera.transform.position).normalized;
        float angle = Vector3.Angle(mainCamera.transform.forward, directionToButton);
        
        canInteract = distance <= maxDistance && angle <= interactionAngle;
    
        if (buttonRenderer != null && normalMaterial != null && highlightMaterial != null)
        {
            buttonRenderer.material = canInteract ? highlightMaterial : normalMaterial;
        }
        else if (showDebugInfo && buttonRenderer == null)
        {
            Debug.LogWarning("Button Renderer veya materyaller atanmamış!");
        }
    }

    void HandleInput()
    {
        if (canInteract && Input.GetKeyDown(interactionKey) && !isPressed)
        {
            PressButton();
        }
    }

    void PressButton()
    {
        isPressed = true;
        
        // Prefab oluştur
        if (prefabToSpawn != null && spawnPoint != null)
        {
            Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
            Debug.Log("Prefab oluşturuldu!");
        }
        else
        {
            Debug.LogWarning("Prefab veya spawn noktası atanmamış!");
        }

        Invoke(nameof(ReleaseButton), 1f);
    }

    void ReleaseButton()
    {
        isPressed = false;
    }

    void AnimateButton()
    {
        if (buttonVisual == null) return;
        
        Vector3 targetPosition = isPressed ? pressedPosition : originalPosition;
        buttonVisual.transform.localPosition = Vector3.Lerp(
            buttonVisual.transform.localPosition,
            targetPosition,
            Time.deltaTime * pressSpeed
        );
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxDistance);
        
        if (spawnPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
            Gizmos.DrawLine(transform.position, spawnPoint.position);
        }
        
        if (Application.isPlaying && mainCamera != null)
        {
            Gizmos.color = canInteract ? Color.green : Color.red;
            Gizmos.DrawLine(mainCamera.transform.position, transform.position);
        }
    }
}