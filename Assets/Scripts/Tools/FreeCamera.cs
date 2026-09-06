using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SERBEST KAMERA (ekran görüntüsü / kadraj aracı)
///
/// Play modundayken F8'e basınca araba (ya da sabotajcı) kamerasından çıkıp
/// sahnede serbestçe uçabildiğin bir kameraya geçersin. Tekrar F8 ile geri
/// dönersin.
///
/// NASIL ÇALIŞIYOR: Aktif kameraları geçici olarak kapatıp, tam onların
/// durduğu yerde yeni bir kamera oluşturuyor — yani geçiş "zıplama" gibi
/// hissettirmiyor, baktığın yerden devam ediyorsun. F8'e tekrar basınca
/// yeni kamera silinip eskiler geri açılıyor.
///
/// ZAMAN DONDURMA: Varsayılan olarak F8'e basınca oyun donuyor
/// (Time.timeScale = 0). Böylece hareket halindeki bir anı (drift, patlama)
/// dondurup etrafında gezerek en iyi açıyı bulabiliyorsun. Kamera
/// Time.unscaledDeltaTime kullandığı için oyun donmuşken bile hareket eder.
///
/// KONTROLLER (serbest kamera açıkken):
///   WASD      → ileri/geri/yanlara
///   Q / E     → aşağı / yukarı
///   Mouse     → bakış
///   Shift     → hızlı,  Ctrl → yavaş
///   Fare tekerleği → temel hızı ayarla
///
/// KULLANIM: Online Scene'de boş bir GameObject'e ekle (ScreenshotCapture
/// ile aynı objede durabilir). SADECE geliştirme/çekim aracı — yayınlanan
/// oyunda bu objeyi kapatman ya da silmen gerekir.
/// </summary>
public class FreeCamera : MonoBehaviour
{
    [Header("Tuş")]
    [SerializeField] private Key toggleKey = Key.F8;

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 12f;
    [SerializeField] private float fastMultiplier = 3f;
    [SerializeField] private float slowMultiplier = 0.25f;
    [SerializeField] private float mouseSensitivity = 0.12f;

    [Header("Davranış")]
    [Tooltip("Serbest kamera açıkken oyunu dondur (hareketli anı yakalamak için). Kamera yine de hareket eder.")]
    [SerializeField] private bool freezeTimeWhenActive = true;
    [Tooltip("Serbest kamerada post-processing (bloom, renk ayarı vb.) açık olsun mu? Kapalıysa renkler soluk görünür.")]
    [SerializeField] private bool enablePostProcessing = true;

    private Camera freeCam;
    private readonly List<Camera> disabledCameras = new List<Camera>();

    private float yaw;
    private float pitch;
    private bool isActive;

    private float previousTimeScale = 1f;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            Toggle();

        if (isActive)
            HandleFreeCam();
    }

    private void Toggle()
    {
        if (isActive) Deactivate();
        else Activate();
    }

    private void Activate()
    {
        // Şu an aktif olan kameraları bul ve kapat. Camera.allCameras sadece
        // AÇIK olan kameraları döndürür — kapattıklarımızı listede tutup
        // çıkışta geri açıyoruz.
        disabledCameras.Clear();

        Vector3 startPosition = transform.position;
        Quaternion startRotation = Quaternion.identity;
        Camera sourceCamera = null;

        foreach (Camera cam in Camera.allCameras)
        {
            if (sourceCamera == null)
            {
                sourceCamera = cam;
                startPosition = cam.transform.position;
                startRotation = cam.transform.rotation;
            }

            cam.enabled = false;
            disabledCameras.Add(cam);
        }

        GameObject camObject = new GameObject("FreeCamera");
        camObject.transform.SetPositionAndRotation(startPosition, startRotation);
        freeCam = camObject.AddComponent<Camera>();

        // Oyun kamerasının ayarlarını (FOV, clipping, culling mask, arka plan)
        // kopyala — yoksa serbest kamera farklı bir görüntü verir.
        if (sourceCamera != null)
        {
            freeCam.CopyFrom(sourceCamera);
            freeCam.targetTexture = null; // ekrana render etsin, texture'a değil
            freeCam.enabled = true;
        }

        // URP'de RUNTIME'DA oluşturulan kameralarda post-processing VARSAYILAN
        // OLARAK KAPALI gelir — bu yüzden serbest kamerada renkler soluk
        // görünüyordu (bloom/renk ayarı uygulanmıyordu). El ile açıyoruz.
        UniversalAdditionalCameraData freeCamData = freeCam.GetUniversalAdditionalCameraData();
        freeCamData.renderPostProcessing = enablePostProcessing;

        // Kenar yumuşatma ayarını da oyun kamerasından al ki görüntü aynı olsun.
        if (sourceCamera != null)
        {
            UniversalAdditionalCameraData sourceData = sourceCamera.GetUniversalAdditionalCameraData();
            if (sourceData != null)
            {
                freeCamData.antialiasing = sourceData.antialiasing;
                freeCamData.antialiasingQuality = sourceData.antialiasingQuality;
            }
        }

        // Bakış açılarını mevcut kameranın açısıyla başlat ki geçişte sıçrama olmasın.
        Vector3 euler = startRotation.eulerAngles;
        yaw = euler.y;
        pitch = NormalizePitch(euler.x);

        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (freezeTimeWhenActive)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        isActive = true;
        Debug.Log("[FreeCamera] Serbest kamera AÇIK. WASD+mouse ile gez, Q/E ile alçal-yüksel, F8 ile çık.");
    }

    private void Deactivate()
    {
        if (freeCam != null)
            Destroy(freeCam.gameObject);
        freeCam = null;

        foreach (Camera cam in disabledCameras)
            if (cam != null) cam.enabled = true;
        disabledCameras.Clear();

        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;

        if (freezeTimeWhenActive)
            Time.timeScale = previousTimeScale;

        isActive = false;
        Debug.Log("[FreeCamera] Serbest kamera KAPALI, oyun kamerasına dönüldü.");
    }

    private void HandleFreeCam()
    {
        if (freeCam == null) return;

        // Oyun donmuş olabileceği için deltaTime DEĞİL unscaledDeltaTime —
        // timeScale = 0 iken deltaTime da 0 olur ve kamera hiç hareket etmez.
        float dt = Time.unscaledDeltaTime;

        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            yaw += delta.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -89f, 89f);

            // Tekerlek ile temel hızı ayarla (yakın çekim / geniş plan arası geçiş)
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                moveSpeed = Mathf.Clamp(moveSpeed * (scroll > 0f ? 1.15f : 0.87f), 0.5f, 200f);
        }

        freeCam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        if (Keyboard.current == null) return;

        Vector3 input = Vector3.zero;
        if (Keyboard.current.wKey.isPressed) input += freeCam.transform.forward;
        if (Keyboard.current.sKey.isPressed) input -= freeCam.transform.forward;
        if (Keyboard.current.dKey.isPressed) input += freeCam.transform.right;
        if (Keyboard.current.aKey.isPressed) input -= freeCam.transform.right;
        if (Keyboard.current.eKey.isPressed) input += Vector3.up;
        if (Keyboard.current.qKey.isPressed) input -= Vector3.up;

        float speed = moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed) speed *= fastMultiplier;
        if (Keyboard.current.leftCtrlKey.isPressed) speed *= slowMultiplier;

        freeCam.transform.position += input.normalized * speed * dt;
    }

    /// <summary>Euler açısı 0-360 gelir; pitch clamp'i için -180..180 aralığına çevir.</summary>
    private static float NormalizePitch(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    /// <summary>Play modundan çıkarken zaman ölçeğini geri ver (donmuş kalmasın).</summary>
    void OnDisable()
    {
        if (isActive && freezeTimeWhenActive)
            Time.timeScale = previousTimeScale;
    }
}
