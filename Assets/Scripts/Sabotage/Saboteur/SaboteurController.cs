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
    [Tooltip("ESC'ye basınca imleç serbest kalır ve kamera durur. Tekrar ESC ya da sol tık ile geri kilitlenir.")]
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
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;
    [Tooltip("Kamera dönüşünün yumuşatılma hızı — büyük değer = daha sert/anlık, küçük değer = daha yumuşak/gecikmeli.")]
    [SerializeField] private float lookSmoothing = 25f;
    [Tooltip("Page Up/Page Down tuşlarıyla oyun içinde hassasiyeti ayarlamayı aç/kapat.")]
    [SerializeField] private bool allowSensitivityAdjustment = true;
    [SerializeField] private float sensitivityStep = 0.2f;
    [SerializeField] private float minSensitivity = 0.2f;
    [SerializeField] private float maxSensitivity = 8f;

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

    private Text sensitivityLabel;
    private Coroutine hideSensitivityLabelRoutine;

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
        HandleSensitivityAdjustment();

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
    /// ESC → imleci serbest bırak / geri kilitle. İmleç serbestken sol tık da
    /// tekrar kilitliyor (standart FPS davranışı).
    /// </summary>
    private void HandleCursorToggle()
    {
        if (!escUnlocksCursor) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(!cursorLocked);
            return;
        }

        if (!cursorLocked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);
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

    /// <summary>
    /// Page Up/Page Down ile mouse hassasiyetini oyun içinde ayarlar — henüz
    /// gerçek bir ayarlar menüsü olmadığı için hızlı/geçici bir çözüm.
    /// ([ ve ] yerine bunu seçtik: Türkçe klavyede [ ] AltGr gerektiriyor,
    /// Page Up/Down her klavyede sabit ve tek tuşla erişilebiliyor.)
    /// Değişiklik sadece bu client'ta (local), network'e gitmiyor.
    /// </summary>
    private void HandleSensitivityAdjustment()
    {
        if (!allowSensitivityAdjustment || Keyboard.current == null) return;

        bool decreased = Keyboard.current.pageDownKey.wasPressedThisFrame;
        bool increased = Keyboard.current.pageUpKey.wasPressedThisFrame;
        if (!decreased && !increased) return;

        mouseSensitivity = Mathf.Clamp(
            mouseSensitivity + (increased ? sensitivityStep : -sensitivityStep),
            minSensitivity, maxSensitivity);

        ShowSensitivityLabel();
    }

    private void ShowSensitivityLabel()
    {
        if (sensitivityLabel == null)
            CreateSensitivityLabel();

        sensitivityLabel.text = $"Fare Hassasiyeti: {mouseSensitivity:F1}  (Page Up / Page Down)";
        sensitivityLabel.gameObject.SetActive(true);

        if (hideSensitivityLabelRoutine != null) StopCoroutine(hideSensitivityLabelRoutine);
        hideSensitivityLabelRoutine = StartCoroutine(HideSensitivityLabelAfterDelay());
    }

    private System.Collections.IEnumerator HideSensitivityLabelAfterDelay()
    {
        yield return new WaitForSeconds(1.5f);
        if (sensitivityLabel != null)
            sensitivityLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// Crosshair'in hemen altına, geçici bir bilgi yazısı — runtime'da
    /// oluşturulur. Crosshair kapalıysa (showCrosshair false) bile bir
    /// Canvas'a ihtiyacı var — UI Text'in ekranda görünmesi için 3D dünya
    /// objesine değil mutlaka bir Canvas'a bağlı olması gerekiyor.
    /// </summary>
    private void CreateSensitivityLabel()
    {
        Transform canvasParent = crosshairRoot != null ? crosshairRoot.transform : CreateFallbackCanvas().transform;

        GameObject labelObj = new GameObject("SensitivityLabel");
        labelObj.transform.SetParent(canvasParent, false);

        sensitivityLabel = labelObj.AddComponent<Text>();
        sensitivityLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sensitivityLabel.fontSize = 20;
        sensitivityLabel.alignment = TextAnchor.MiddleCenter;
        sensitivityLabel.color = Color.white;
        sensitivityLabel.raycastTarget = false;

        RectTransform rect = sensitivityLabel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -50f); // crosshair'in altı
        rect.sizeDelta = new Vector2(400f, 40f);

        labelObj.SetActive(false);
    }

    /// <summary>showCrosshair kapalıyken sensitivityLabel için kullanılan yedek Canvas.</summary>
    private GameObject CreateFallbackCanvas()
    {
        GameObject canvasObj = new GameObject("SaboteurUICanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObj.AddComponent<CanvasScaler>();
        return canvasObj;
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

        // ZIPLAMA: sadece yerdeyken. Formül, istenen yüksekliğe tam ulaşacak
        // başlangıç hızını veriyor (v = √(2 * g * h) fiziğinden geliyor) —
        // böylece jumpHeight'ı metre cinsinden doğrudan girebiliyorsun.
        if (jumpHeight > 0f && controller.isGrounded && Keyboard.current.spaceKey.wasPressedThisFrame)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = currentPlanarVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
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

    /// <summary>Karakter yok olurken nişangahı ve hassasiyet yazısını da temizle (sahne değişimi vb.).</summary>
    void OnDestroy()
    {
        if (crosshairRoot != null)
            Destroy(crosshairRoot);

        // sensitivityLabel crosshairRoot'un ÇOCUĞU değilse (crosshair kapalıyken
        // oluşan yedek Canvas), o ayrı objeyi de temizlememiz gerekiyor.
        if (sensitivityLabel != null && (crosshairRoot == null || sensitivityLabel.transform.root != crosshairRoot.transform))
            Destroy(sensitivityLabel.transform.root.gameObject);
    }
}
