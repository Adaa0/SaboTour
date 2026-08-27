using UnityEngine;

/// <summary>
/// ANA MENÜ ARKA PLANI İÇİN YAVAŞ DÖNEN KAMERA.
///
/// ─── NE İŞE YARIYOR ───────────────────────────────────────────────────
/// Ana menü şu an düz bir renk gösteriyor (kameranın Clear Flags'i "Solid
/// Color"). Bu script, kamerayı bir hedef noktanın etrafında yavaşça
/// döndürerek menünün arkasında canlı bir 3D manzara oluşturuyor — kule,
/// birkaç ağaç ve gökyüzü koyman yeterli, gerisini bu hallediyor.
///
/// Oyunun kendi assetlerini kullandığı için hem bedava hem de "bu oyunun
/// menüsü" gibi duruyor; hazır bir arka plan resminden çok daha iyi.
///
/// ─── KURULUM (kod yazmadan, tamamen Inspector) ────────────────────────
///  1. Offline Scene'i aç.
///  2. Sahneye kuleyi, birkaç ağacı/kayayı sürükle (Assets/Prefabs ve
///     Assets/Models altındakiler). Kamera etrafına dağıt.
///  3. `Main Camera`yı seç:
///       • Clear Flags → **Skybox** (şu an Solid Color, düz mavi veren bu)
///       • Bu script'i ekle (Add Component > Menu Camera Orbit)
///  4. `Target` alanına dönmesini istediğin merkezi sürükle (ör. kule).
///     Boş bırakırsan dünya merkezi (0,0,0) etrafında döner.
///  5. Play'e bas, hızı/yüksekliği beğenene kadar Inspector'dan oynat.
///
/// ⚠️ MENÜ KAMERASI OYUNU ETKİLEMEZ: bu sahne sadece ana menü/lobi. Yarış
/// başlayınca Online Scene yükleniyor ve orada kendi kameraları devralıyor.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MenuCameraOrbit : MonoBehaviour
{
    [Header("Neyin Etrafında Dönsün")]
    [Tooltip("Kameranın etrafında döneceği merkez. Boş bırakılırsa dünya " +
             "merkezi (0,0,0) kullanılır — kuleyi oraya koyduysan yeterli.")]
    [SerializeField] private Transform target;

    [Tooltip("Merkeze olan yatay mesafe (metre). Büyütürsen manzara " +
             "uzaklaşır, küçültürsen yakın plan olur.")]
    [SerializeField] private float distance = 45f;

    [Tooltip("Merkezden ne kadar yukarıda dursun (metre).")]
    [SerializeField] private float height = 18f;

    [Header("Dönüş")]
    [Tooltip("Saniyede kaç derece dönsün. 2 = tam tur 3 dakika. " +
             "Menüde göz yormasın diye YAVAŞ olması önemli — 5'in üstü " +
             "baş döndürür.")]
    [SerializeField] private float degreesPerSecond = 2f;

    [Tooltip("Başlangıç açısı (derece). Sahneyi kurarken en güzel görünen " +
             "açıyı bulup buraya yaz.")]
    [SerializeField] private float startAngle = 0f;

    [Header("Bakış")]
    [Tooltip("Kamera merkeze bakarken ne kadar yukarı/aşağı kaydırsın. " +
             "Pozitif = biraz yukarı bak (gökyüzü daha çok görünür).")]
    [SerializeField] private float lookHeightOffset = 4f;

    [Tooltip("Hafif yukarı-aşağı süzülme — tamamen sabit bir dönüş biraz " +
             "mekanik duruyor. 0 = kapalı.")]
    [SerializeField] private float bobAmount = 1.5f;

    [Tooltip("Süzülmenin hızı (saniyedeki döngü sayısı).")]
    [SerializeField] private float bobSpeed = 0.15f;

    private float angle;

    void Start()
    {
        angle = startAngle;
        ApplyPosition();
    }

    void Update()
    {
        // Time.deltaTime yerine unscaledDeltaTime: menüde Time.timeScale
        // ile oynayan bir şey olursa (duraklatma vb.) kamera yine akıcı kalsın.
        angle += degreesPerSecond * Time.unscaledDeltaTime;
        if (angle >= 360f) angle -= 360f;

        ApplyPosition();
    }

    private void ApplyPosition()
    {
        Vector3 center = target != null ? target.position : Vector3.zero;

        float radians = angle * Mathf.Deg2Rad;
        float bob = bobAmount > 0f
            ? Mathf.Sin(Time.unscaledTime * bobSpeed * Mathf.PI * 2f) * bobAmount
            : 0f;

        Vector3 offset = new Vector3(
            Mathf.Sin(radians) * distance,
            height + bob,
            Mathf.Cos(radians) * distance);

        transform.position = center + offset;
        transform.LookAt(center + Vector3.up * lookHeightOffset);
    }

    /// <summary>
    /// Sahneyi kurarken kamerayı Play'e basmadan görebilmek için — Inspector'da
    /// script'in sağ üstündeki ⋮ menüsünden "Kamerayı Şimdi Yerleştir".
    /// </summary>
    [ContextMenu("Kamerayı Şimdi Yerleştir")]
    private void PreviewNow()
    {
        angle = startAngle;
        ApplyPosition();
    }
}
