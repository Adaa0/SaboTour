using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// PERSONA TARZI BUTON HİSSİ — fare üzerine gelince büyüme + kısa beyaz
/// ekran flaşı. DOTween KULLANMIYOR; UISlideIn.cs ile AYNI desende, kendi
/// yumuşatmasını Update() içinde hesaplıyor (bkz. UISlideIn.cs'in Update'i,
/// ease-out için 1-(1-t)^3).
///
/// NEDEN AYRI BİR "MenuKontrol" YÖNETİCİSİ / Event Trigger YOK: örnek
/// anlatımlar genelde tek bir merkezi script + Inspector'dan buton dizisi +
/// Event Trigger bağlamak istiyor. Burada her buton SADECE bu component'i
/// taşıyor; IPointerEnterHandler/IPointerExitHandler'ı kendi uyguluyor.
/// Event Trigger kurmaya, diziye elle sürüklemeye gerek yok — component
/// eklemek yeterli (Editor aracı zaten bunu otomatik ekliyor, bkz.
/// Assets/Editor/PersonaMenuStyler.cs).
///
/// FLAŞ TEK BİR PAYLAŞILAN GÖRSEL: her buton kendi flaşını üretmiyor,
/// `MenuFlash.Trigger()` çağırıyor (bkz. MenuFlash.cs — ScreenNotice/
/// crosshair'deki "gerekirse kendini kur" deseniyle aynı fikir).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PersonaButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("Üzerine gelince ne kadar büyüsün. 1.15 = %15 büyüme.")]
    [SerializeField] private float hoverScale = 1.15f;

    [Tooltip("Büyüme/küçülme ne kadar sürsün (saniye).")]
    [SerializeField] private float scaleDuration = 0.2f;

    [Tooltip("Üzerine gelince ekranda kısa bir beyaz flaş çaksın mı.")]
    [SerializeField] private bool flashOnHover = true;

    [Tooltip("Flaş ne kadar sürsün (saniye). ~2 kare için 0.03-0.05 iyi durur.")]
    [SerializeField] private float flashSeconds = 0.04f;

    private RectTransform rect;
    private Vector3 baseScale;
    private Vector3 fromScale;
    private Vector3 targetScale;
    private float elapsed;
    private bool growing;

    void Awake()
    {
        rect = (RectTransform)transform;
        baseScale = rect.localScale;
        targetScale = baseScale;
        fromScale = baseScale;
    }

    void OnDisable()
    {
        // Panel kapanıp tekrar açıldığında buton büyümüş kalmasın diye
        // sıfırlanmış boyutla bekliyor.
        if (rect != null) rect.localScale = baseScale;
        elapsed = 0f;
        growing = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        fromScale = rect.localScale;
        targetScale = baseScale * hoverScale;
        growing = true;
        elapsed = 0f;

        if (flashOnHover) MenuFlash.Trigger(flashSeconds);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        fromScale = rect.localScale;
        targetScale = baseScale;
        growing = false;
        elapsed = 0f;
    }

    void Update()
    {
        if (rect == null) return;
        if (rect.localScale == targetScale) return;

        elapsed += Time.unscaledDeltaTime / Mathf.Max(0.0001f, scaleDuration);
        float t = Mathf.Clamp01(elapsed);

        // Büyürken zıplayarak (ease-out), küçülürken düz (ease-in) —
        // UISlideIn'deki ile aynı cubic ease-out formülü.
        float eased = growing ? 1f - Mathf.Pow(1f - t, 3f) : t;

        rect.localScale = Vector3.LerpUnclamped(fromScale, targetScale, eased);
    }
}
