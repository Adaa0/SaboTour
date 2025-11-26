using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;
    
    [Header("Camera Settings")]
    public Vector3 offset = new Vector3(0f, 2f, -4f);
    public float sensitivity = 3f;
    public float rotationSmoothTime = 0.1f;

    [Header("Rotation Limits")]
    public float minY = -30f;
    public float maxY = 60f;

    private float yaw;
    private float pitch;
    private Vector3 currentRotation;
    private Vector3 rotationSmoothVelocity;

    void Start()
    {
        // Target otomatik bul
        if (target == null)
        {
            ThirdPersonMovement player = FindAnyObjectByType<ThirdPersonMovement>();
            if (player != null)
            {
                target = player.transform;
                Debug.Log("Kamera target'i otomatik bulundu: " + target.gameObject.name);
            }
            else
            {
                Debug.LogError("Target bulunamadı! Sahnede ThirdPersonMovement script'li karakter olmalı.");
            }
        }
        
        LockCursor();
    }

    void Update()
    {
        HandleCursorLock();
    }

    void LateUpdate()
    {
        // Target yoksa çalışma
        if (target == null)
        {
            Debug.LogWarning("Kamera target'i atanmamış!");
            return;
        }
        
        if (Cursor.lockState != CursorLockMode.Locked) return;

        yaw += Input.GetAxis("Mouse X") * sensitivity;
        pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        pitch = Mathf.Clamp(pitch, minY, maxY);

        Vector3 targetRotation = new Vector3(pitch, yaw);
        currentRotation = Vector3.SmoothDamp(currentRotation, targetRotation, ref rotationSmoothVelocity, rotationSmoothTime);
        transform.eulerAngles = currentRotation;

        Vector3 desiredPosition = target.position + Quaternion.Euler(currentRotation) * offset;

        // Raycast ile çarpmayı engellemek istersen aç:
        // RaycastHit hit;
        // if (Physics.Linecast(target.position, desiredPosition, out hit))
        // {
        //     desiredPosition = hit.point;
        // }

        transform.position = desiredPosition;
    }

    void HandleCursorLock()
    {
        // ESC ile çık
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // LMB (sol tık) ile tekrar kilitle
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor();
        }
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}