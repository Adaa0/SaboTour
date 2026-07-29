using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

/// <summary>
/// Sabotajcının kule içinde 1st person yürümesini sağlar. CarController'daki
/// "sadece yetkili client kendi hareketini hesaplar" mantığıyla aynı:
/// sadece isOwned olan client input okuyup CharacterController'ı hareket
/// ettiriyor. NetworkTransform (Sync Direction: Client To Server, Car'daki
/// ile aynı ayar) pozisyonu diğer client'lara yayıyor.
///
/// GEÇİCİ: Henüz el/karakter modeli yok — sadece kapsül + kamera ile temel
/// yürüme test ediliyor. El asseti gelince FPCam'in altına eklenecek.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SaboteurController : NetworkBehaviour
{
    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float sprintSpeed = 6f;
    [Tooltip("Hıza ulaşma/yavaşlama ne kadar sürede olsun (saniye) — 0 = anlık, eski davranış.")]
    [SerializeField] private float accelerationTime = 0.12f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Mouse Bakış")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;
    [Tooltip("Kamera dönüşünün yumuşatılma hızı — büyük değer = daha sert/anlık, küçük değer = daha yumuşak/gecikmeli.")]
    [SerializeField] private float lookSmoothing = 25f;

    [Header("Referanslar")]
    [Tooltip("Sabotajcının 1st person kamerası. Prefabda BAŞLANGIÇTA KAPALI olmalı (CarCam ile aynı desen).")]
    [SerializeField] private GameObject fpCam;
    [Tooltip("Yukarı/aşağı bakışta döndürülen obje — genelde FPCam'in kendisi (yaw gövdede, pitch kamerada).")]
    [SerializeField] private Transform cameraPitchTransform;

    private CharacterController controller;

    private float targetYaw;
    private float currentYaw;
    private float targetPitch;
    private float currentPitch;

    private Vector3 currentPlanarVelocity;
    private Vector3 planarVelocitySmoothDamp;
    private float verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        targetYaw = currentYaw = transform.eulerAngles.y;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"[SaboteurController] OnStartClient netId={netId} isOwned={isOwned}");
    }

    /// <summary>Mirror callback — sadece bu karakterin sahibi olan client'ta bir kere çağrılır.</summary>
    public override void OnStartAuthority()
    {
        base.OnStartAuthority();

        Debug.Log($"[SaboteurController] OnStartAuthority netId={netId} — bu client artık sabotajcıyı kontrol ediyor.");

        if (fpCam != null)
            fpCam.SetActive(true);
        else
            Debug.LogWarning("[SaboteurController] fpCam atanmamış! Inspector'dan FPCam objesini sürükle.");

        // Online Scene'deki sabit Main Camera'nın AudioListener'ı her zaman
        // açık duruyor (yarışçılar zaten CarCam'de kendi AudioListener'ı
        // olmadığı için sesi ondan alıyor). Sabotajcı FPCam'i aktif olunca
        // ikisi birden açık kalıp Unity'nin "2 audio listener" uyarısına
        // sebep oluyordu. Sabotajcı için FPCam'in kendi listener'ı yeterli
        // ve daha doğru (kafa hareketiyle ses konumu değişsin diye), bu
        // yüzden sabit kamerayı SADECE bu client'ta devre dışı bırakıyoruz.
        AudioListener sceneListener = Camera.main != null ? Camera.main.GetComponent<AudioListener>() : null;
        if (sceneListener != null)
            sceneListener.enabled = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!isOwned) return;

        HandleLook();
        HandleMove();
    }

    private void HandleLook()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue();

        targetYaw += delta.x * mouseSensitivity;
        targetPitch = Mathf.Clamp(targetPitch - delta.y * mouseSensitivity, minPitch, maxPitch);

        float t = 1f - Mathf.Exp(-lookSmoothing * Time.deltaTime);
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, t);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, t);

        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);

        if (cameraPitchTransform != null)
            cameraPitchTransform.localEulerAngles = new Vector3(currentPitch, 0f, 0f);
    }

    private void HandleMove()
    {
        if (Keyboard.current == null) return;

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        bool sprinting = Keyboard.current.leftShiftKey.isPressed;
        float speed = sprinting ? sprintSpeed : moveSpeed;

        Vector3 targetPlanarVelocity = (transform.right * input.x + transform.forward * input.y) * speed;

        currentPlanarVelocity = accelerationTime <= 0f
            ? targetPlanarVelocity
            : Vector3.SmoothDamp(currentPlanarVelocity, targetPlanarVelocity, ref planarVelocitySmoothDamp, accelerationTime);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -1f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = currentPlanarVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }
}
