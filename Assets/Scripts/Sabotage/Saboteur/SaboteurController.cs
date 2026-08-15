using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
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
    [Tooltip("Space ile zıplama yüksekliği (metre). 0 yaparsan zıplama kapanır.")]
    [SerializeField] private float jumpHeight = 1.1f;

    [Header("İmleç / Duraklatma")]
    [Tooltip("Duraklatma menüsü (ESC) açıkken imleç serbest kalsın ve kamera dursun mu? Menüyü PauseMenuController yönetiyor; bu kutu sadece sabotajcının ona tepki verip vermeyeceğini belirliyor. Kapatırsan menü açıkken de kamera dönmeye devam eder (tavsiye edilmez).")]
    [SerializeField] private bool escUnlocksCursor = true;
    [Tooltip("İmleç serbestken yürüme de dursun mu? (Kapalıysa imleç serbestken de WASD çalışır.)")]
    [SerializeField] private bool blockMovementWhenUnlocked = true;

    [Header("Crosshair (nişangah)")]
    [Tooltip("Ekranın ortasında artı şeklinde nişangah gösterilsin mi? Sadece bu karakterin sahibi olan client'ta oluşturulur.")]
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private float crosshairSize = 18f;
    [SerializeField] private float crosshairThickness = 2f;
    [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.8f);

    [Header("Mouse Bakış")]
    [Tooltip("Hassasiyetin KENDİSİ artık burada DEĞİL — Ayarlar menüsünden (MouseSensitivitySettings) okunuyor. Eskiden Page Up/Page Down ile oyun içinde ayarlanıyordu, gerçek ayarlar menüsü gelince oraya taşındı.")]
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;
    [Tooltip("Kamera dönüşünün yumuşatılma hızı — büyük değer = daha sert/anlık, küçük değer = daha yumuşak/gecikmeli.")]
    [SerializeField] private float lookSmoothing = 25f;

    // ─── SESLER ──────────────────────────────────────────────────────────
    // Bu script'in tamamı zaten `isOwned` korumalı, yani bu sesler SADECE
    // sabotajcının kendi makinesinde çalışıyor. Sabotajcı kulede tek başına
    // olduğu için ayak sesinin diğer oyunculara gitmesi GEREKMİYOR — network
    // mesajı harcamamak bilinçli bir karar.
    [Header("Sesler")]
    [Tooltip("Yürürken çalan ayak sesleri. Birden fazla ekle — tek klip tekrarlanınca yürüyüş çok yapay duyuluyor. Kulenin zemini ahşap/metal olduğu için o yüzeye uygun sesler seç.")]
    [SerializeField] private AudioClip[] footstepClips;
    [Tooltip("Kaç metrede bir adım sesi çalsın. Küçültürsen adımlar sıklaşır. Karakterin gerçek adım uzunluğuna yakın bir değer doğal duyulur.")]
    [SerializeField] private float stepDistance = 2.2f;
    [Range(0f, 1f)][SerializeField] private float footstepVolume = 0.4f;
    [Tooltip("Zıplama anında çalar.")]
    [SerializeField] private AudioClip jumpClip;
    [Tooltip("Zıplamadan/düşüşten sonra yere değince çalar.")]
    [SerializeField] private AudioClip landClip;
    [Range(0f, 1f)][SerializeField] private float jumpLandVolume = 0.6f;

    // Adım sesi ZAMANA değil MESAFEYE göre çalıyor — zamana bağlasaydık
    // koşarken de yürürken de aynı sıklıkta adım duyulurdu (ayaklar hızlı
    // giderken sesler yavaş kalır, senkron bozuk hissedilir).
    private float stepAccumulator;
    private bool wasGroundedLastFrame = true;

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

    private bool cursorLocked = true;
    private GameObject crosshairRoot;
    private int lockedOnFrame = -1;


    /// <summary>
    /// İmleç kilitli mi (yani oyun modunda mıyız)?
    /// </summary>
    public bool IsCursorLocked => cursorLocked;

    /// <summary>
    /// SaboteurInteraction bunu okuyup tıklamanın skil tetikleyip
    /// tetiklemeyeceğine karar veriyor. İmleç serbestken false; ayrıca
    /// imleci GERİ KİLİTLEYEN tıkın olduğu karede de false — yoksa oyuna
    /// dönmek için attığın tık aynı anda skil de tetiklerdi.
    /// </summary>
    public bool CanInteract => cursorLocked && Time.frameCount != lockedOnFrame;

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

        SetCursorLocked(true);

        if (showCrosshair)
            CreateCrosshair();
    }

    // Yarış bitip podyum sahnesine geçilince true olur — kontrol tamamen
    // durur (RacePodiumManager tarafından FreezeForRaceEnd ile ayarlanır).
    private bool raceEndedFrozen = false;

    void Update()
    {
        if (!isOwned) return;

        if (raceEndedFrozen)
        {
            // Podyum spawn noktası kolonun biraz üstünde — yerçekimi hâlâ
            // uygulanmazsa sabotajcı havada asılı kalır. Yürüme/bakış/skil
            // tamamen kapalı ama düşüp kolonun üstüne oturması için gravity
            // çalışmaya devam ediyor (ESC ile duraklatmadaki ile aynı metod).
            ApplyGravityOnly();
            return;
        }

        HandleCursorToggle();

        // İmleç serbestken kamera dönmemeli — yoksa menüde/mouse ile
        // uğraşırken ekran savruluyor.
        if (cursorLocked)
            HandleLook();

        if (cursorLocked || !blockMovementWhenUnlocked)
            HandleMove();
        else
            ApplyGravityOnly();
    }

    /// <summary>
    /// İmleç kilidini duraklatma menüsünün durumuyla eşitler: menü açıkken
    /// imleç serbest, kapanınca geri kilitli.
    ///
    /// ESC'Yİ ARTIK BU SCRIPT OKUMUYOR — tek sahibi PauseMenuController.
    /// NEDEN: iki yer birden ESC okursa tek basışta hem menü açılır hem
    /// imleç AYRICA serbest bırakılırdı; menüyü kapatınca iki durum
    /// birbirini tutmaz ve kamera kilitli kalırdı. Menü zaten imleci
    /// serbest bıraktığı için buranın ESC'ye ihtiyacı da kalmadı.
    /// </summary>
    private void HandleCursorToggle()
    {
        if (!escUnlocksCursor) return;

        bool shouldBeLocked = !PauseMenuController.IsOpen;

        if (shouldBeLocked != cursorLocked)
            SetCursorLocked(shouldBeLocked);
    }

    private void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;
        if (locked) lockedOnFrame = Time.frameCount;

        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;

        // Nişangah sadece oyun modundayken görünsün.
        if (crosshairRoot != null)
            crosshairRoot.SetActive(locked);

        // Kilit açılıp kapanırken mouse'un biriken delta'sı kameraya
        // sıçrama yaptırmasın diye hedef açıları mevcut açılara eşitle.
        if (locked)
        {
            targetYaw = currentYaw;
            targetPitch = currentPitch;
        }
    }

    private void HandleLook()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue();

        float sensitivity = MouseSensitivitySettings.Raw;
        targetYaw += delta.x * sensitivity;
        targetPitch = Mathf.Clamp(targetPitch - delta.y * sensitivity, minPitch, maxPitch);

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

        // ZIPLAMA: sadece yerdeyken. Formül, istenen yüksekliğe tam ulaşacak
        // başlangıç hızını veriyor (v = √(2 * g * h) fiziğinden geliyor) —
        // böylece jumpHeight'ı metre cinsinden doğrudan girebiliyorsun.
        if (jumpHeight > 0f && controller.isGrounded && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            SfxPlayer.PlayAt(jumpClip, transform.position, jumpLandVolume, 0.06f, 3f, 30f);
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = currentPlanarVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        HandleFootstepAndLandingSounds();
    }

    /// <summary>
    /// Ayak sesi + yere iniş sesi. controller.Move() ÇAĞRILDIKTAN SONRA
    /// çalışması gerekiyor — `controller.isGrounded` değeri ancak Move()
    /// sonrasında bu karenin doğru sonucunu veriyor (Unity'nin
    /// CharacterController'ında bilinen bir davranış).
    /// </summary>
    private void HandleFootstepAndLandingSounds()
    {
        bool grounded = controller.isGrounded;

        // ── Yere iniş: önceki kare havada, bu kare yerde ──
        if (grounded && !wasGroundedLastFrame)
        {
            SfxPlayer.PlayAt(landClip, transform.position, jumpLandVolume, 0.06f, 3f, 30f);
            stepAccumulator = 0f; // inişin hemen ardından anında adım sesi gelmesin
        }
        wasGroundedLastFrame = grounded;

        // ── Ayak sesi: sadece yerde ve gerçekten hareket ederken ──
        if (!grounded)
        {
            stepAccumulator = 0f;
            return;
        }

        float planarSpeed = currentPlanarVelocity.magnitude;
        if (planarSpeed < 0.4f)
        {
            // Durunca sayaç neredeyse dolu kalsın ki tekrar yürümeye
            // başlayınca ilk adım hemen gelsin (uzun sessiz bir başlangıç
            // olmasın).
            stepAccumulator = Mathf.Min(stepAccumulator, stepDistance * 0.8f);
            return;
        }

        stepAccumulator += planarSpeed * Time.deltaTime;

        if (stepAccumulator < stepDistance) return;

        stepAccumulator = 0f;
        SfxPlayer.PlayRandomAt(footstepClips, transform.position, footstepVolume, 0.12f, 3f, 30f);
    }

    /// <summary>
    /// İmleç serbestken (menü/duraklatma) yürüme kapalı ama karakter havadaysa
    /// düşmeye devam etmeli — yoksa havada asılı kalırdı.
    /// </summary>
    private void ApplyGravityOnly()
    {
        currentPlanarVelocity = Vector3.zero;
        planarVelocitySmoothDamp = Vector3.zero;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -1f;

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }

    /// <summary>
    /// Ekran ortasına artı şeklinde bir nişangah oluşturur. Sadece bu
    /// karakterin SAHİBİ olan client'ta çağrılıyor (OnStartAuthority), yani
    /// yarışçıların ekranında görünmüyor. Runtime'da oluşturuluyor —
    /// sahnede elle Canvas kurmaya gerek yok.
    /// </summary>
    private void CreateCrosshair()
    {
        crosshairRoot = new GameObject("SaboteurCrosshair");

        Canvas canvas = crosshairRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // diğer UI'ların üstünde kalsın
        crosshairRoot.AddComponent<CanvasScaler>();

        CreateCrosshairBar(crosshairRoot.transform, new Vector2(crosshairSize, crosshairThickness)); // yatay
        CreateCrosshairBar(crosshairRoot.transform, new Vector2(crosshairThickness, crosshairSize)); // dikey
    }

    private void CreateCrosshairBar(Transform parent, Vector2 size)
    {
        GameObject bar = new GameObject("Bar");
        bar.transform.SetParent(parent, false);

        Image image = bar.AddComponent<Image>();
        image.color = crosshairColor;
        image.raycastTarget = false; // tıklamaları engellemesin

        RectTransform rect = bar.GetComponent<RectTransform>();
        // Ekranın tam ortasına sabitle
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    #region Yarış Sonu / Podyum

    /// <summary>
    /// RacePodiumManager, YARIŞ BİTTİĞİNDE sabotajcıyı podyum kolonundaki
    /// spawn noktasına ışınlamak için çağırır. CharacterController
    /// aktifken transform.position'ı doğrudan değiştirmek işe yaramıyor
    /// (fizik motoru üzerine yazıyor) — bu yüzden geçici olarak kapatılıp
    /// pozisyon/rotasyon ayarlandıktan sonra tekrar açılıyor.
    /// </summary>
    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        if (!isOwned) return;

        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        currentYaw = targetYaw = rotation.eulerAngles.y;
        currentPitch = targetPitch = 0f;
        verticalVelocity = 0f;
        controller.enabled = true;
    }

    /// <summary>
    /// Podyuma ışınlandıktan sonra hareketi/imleç kilidini tamamen kapatır.
    /// Kamera kapatmayı AYRI tutuyoruz (HideCameraForPodium) çünkü racers
    /// kazandığında sabotajcı ışınlanmaz ama kamerası yine de podyum
    /// görünümüne geçmeli.
    /// </summary>
    public void FreezeForRaceEnd()
    {
        raceEndedFrozen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (crosshairRoot != null)
            crosshairRoot.SetActive(false);
    }

    /// <summary>
    /// RacePodiumManager, podyum kamerasına geçerken sabotajcının kendi
    /// FPCam'ini kapatmak için çağırır (yarışçılar kazandığında bile,
    /// sabotajcı da podyum töreni kamerasından izlesin diye).
    /// </summary>
    public void HideCameraForPodium()
    {
        if (fpCam != null)
            fpCam.SetActive(false);
    }

    #endregion

    /// <summary>Karakter yok olurken nişangahı da temizle (sahne değişimi vb.).</summary>
    void OnDestroy()
    {
        if (crosshairRoot != null)
            Destroy(crosshairRoot);
    }
}
