using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GEÇİCİ ÇEKİM ARACI — Steam görselleri bitince silinecek (FreeCamera,
/// FreezeFrame, GhostReplay, ScreenshotCapture ile aynı grupta).
///
/// NE İŞE YARAR: Sahneye elle yerleştirdiğin SABİT kameralarla arabanın
/// kendi kamerası (Main Camera + CinemachineBrain) arasında tek tuşla geçiş
/// yapar. Akış şöyle oluyor:
///   1. Araba kamerasındayken pisti sür, arabayı istediğin noktaya getir
///   2. F4 ile sabit kameraya geç (kadraj tam senin kurduğun gibi)
///   3. F7 ile dondur, F9 ile çek
///
/// FreeCamera (F8) ile farkı: serbest kamerada her seferinde kadrajı elden
/// yakalamaya çalışıyorsun ve aynı kareyi bir daha tutturamıyorsun. Sabit
/// kamera sahneye kaydedildiği için AYNI kadrajı istediğin kadar tekrar
/// çekebilirsin — kapak revize ederken bu çok işe yarıyor.
///
/// KURULUM:
///  1. Sahnede boş bir GameObject'e bu component'i ekle (Tools objesi olabilir).
///  2. Sahneye istediğin kadar boş GameObject + Camera component'i koy,
///     Scene view'da kadrajı ayarla (GameObject > Align With View kısayolu:
///     kamerayı seçip Ctrl+Shift+F — Scene view'da ne görüyorsan kamera onu alır).
///  3. Bu component'teki "Cameras" listesine SIRAYLA sürükle. İlk sıraya
///     Main Camera'yı (araba görüşü) koyman önerilir.
///  4. Play'de F4 ile aralarında dolaş.
///
/// TAG NOTU: Sabit kameraları da "MainCamera" tag'iyle etiketlemen iyi olur.
/// Camera.main "tag'i MainCamera olan İLK AKTİF kamera" demek — Main Camera
/// kapalıyken sabit kamera etiketsizse Camera.main null döner ve onu kullanan
/// başka scriptler hata verebilir.
/// </summary>
public class PhotoCameraSwitcher : MonoBehaviour
{
    [Header("Kameralar")]
    [Tooltip("Geçiş yapılacak kameralar, SIRAYLA. İlk sıraya Main Camera'yı " +
             "(araba görüşü) koy. Boş bırakırsan sahnedeki tüm kameraları " +
             "kendisi bulur, ama sıralamayı garanti edemez.")]
    [SerializeField] private Camera[] cameras;

    [Header("Tuşlar")]
    [Tooltip("Sıradaki kameraya geçer. F5-F10 diğer çekim araçlarında kullanılıyor.")]
    [SerializeField] private Key cycleKey = Key.F4;
    [Tooltip("Bir öncekine döner (Shift + geçiş tuşu).")]
    [SerializeField] private bool shiftGoesBackwards = true;

    [Header("Ekran Yazısı")]
    [Tooltip("Geçiş yapınca kameranın adını kısa süre ekranda gösterir. " +
             "DİKKAT: F9 ekran görüntüsü OnGUI yazılarını da yakalar — çekimden " +
             "hemen önce geçiş yaparsan yazı fotoğrafa girebilir. Yazı süresi " +
             "dolmadan çekim yapma ya da bunu kapat.")]
    [SerializeField] private bool showLabel = true;
    [SerializeField] private float labelDuration = 1.5f;

    private int activeIndex;
    private float labelHideTime;
    private string labelText = "";

    void Start()
    {
        if (cameras == null || cameras.Length == 0)
            AutoCollectCameras();

        // Başlangıçta zaten aktif olan kamerayı bul ki ilk tuşa basışta
        // beklenmedik bir yere atlamasın.
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled && cameras[i].gameObject.activeInHierarchy)
            {
                activeIndex = i;
                break;
            }
        }

        Apply(activeIndex, announce: false);
    }

    void Update()
    {
        if (Keyboard.current == null) return;
        if (cameras == null || cameras.Length < 2) return;

        if (!Keyboard.current[cycleKey].wasPressedThisFrame) return;

        bool backwards = shiftGoesBackwards && Keyboard.current.leftShiftKey.isPressed;
        int step = backwards ? -1 : 1;

        // Null slot'ları atlayarak ilerle — Inspector'da boş bırakılmış bir
        // eleman varsa kilitlenmesin diye tur sayısını sınırlıyoruz.
        int next = activeIndex;
        for (int i = 0; i < cameras.Length; i++)
        {
            next = (next + step + cameras.Length) % cameras.Length;
            if (cameras[next] != null) break;
        }

        activeIndex = next;
        Apply(activeIndex, announce: true);
    }

    /// <summary>
    /// Sadece seçili kamerayı açar, diğerlerini kapatır. AudioListener'ı da
    /// birlikte taşıyor — Unity aynı anda birden fazla aktif AudioListener
    /// olursa sürekli uyarı basıyor.
    /// </summary>
    private void Apply(int index, bool announce)
    {
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null) continue;

            bool isActive = i == index;

            cam.enabled = isActive;

            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = isActive;
        }

        if (!announce) return;

        Camera active = cameras[index];
        if (active == null) return;

        labelText = $"Kamera {index + 1}/{cameras.Length}: {active.name}  (FOV {active.fieldOfView:0})";
        labelHideTime = Time.unscaledTime + labelDuration;

        Debug.Log($"[PhotoCameraSwitcher] {labelText}");
    }

    /// <summary>
    /// Liste boşsa sahnedeki tüm kameraları toplar. Kapalı olanları da dahil
    /// ediyoruz, çünkü sabit çekim kameralarını genelde kapalı bırakıyorsun.
    /// </summary>
    private void AutoCollectCameras()
    {
        cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        Debug.Log($"[PhotoCameraSwitcher] Kamera listesi boştu, sahnede {cameras.Length} kamera bulundu. " +
                  "Sıralamayı kendin belirlemek istersen Inspector'daki listeyi doldur.");
    }

    /// <summary>
    /// OnGUI ile çiziliyor — Canvas'a bağlı olmadığı için F10 (HUD gizle)
    /// bunu gizlemez, ama süresi dolunca kendiliğinden kayboluyor.
    /// </summary>
    void OnGUI()
    {
        if (!showLabel) return;
        if (Time.unscaledTime > labelHideTime) return;
        if (string.IsNullOrEmpty(labelText)) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.UpperLeft
        };
        style.normal.textColor = Color.white;

        // Koyu bir gölge, açık gökyüzünde de okunsun diye.
        GUIStyle shadow = new GUIStyle(style);
        shadow.normal.textColor = Color.black;

        GUI.Label(new Rect(21, 21, 900, 40), labelText, shadow);
        GUI.Label(new Rect(20, 20, 900, 40), labelText, style);
    }
}
