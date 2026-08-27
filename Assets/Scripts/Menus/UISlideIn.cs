using UnityEngine;

/// <summary>
/// MENÜ ÖĞELERİNİN SIRAYLA KAYARAK GİRMESİ (Persona 5 tarzı giriş animasyonu).
///
/// ─── NE İŞE YARIYOR ───────────────────────────────────────────────────
/// Persona 5 gibi menülerin "pahalı" hissettirmesinin sırrı karmaşık shader
/// değil, ZAMANLAMA: öğeler aynı anda değil, birbiri ardına milisaniyelerle
/// kayarak giriyor. Bu script tam olarak onu yapıyor ve tek bir UI objesine
/// eklenip Inspector'dan ayarlanıyor — kod yazmana gerek yok.
///
/// ─── KURULUM ──────────────────────────────────────────────────────────
///  1. Menüdeki her butona/panele bu script'i ekle (Add Component > UI Slide In).
///  2. Her birinde `Delay`i biraz artır: 0.00, 0.06, 0.12, 0.18...
///     Kademeli gecikme = o "şık" his. Hepsine aynı delay verirsen etki kaybolur.
///  3. `From` ile hangi yönden geleceğini seç (Persona 5'te genelde soldan
///     ya da alttan gelir).
///
/// İPUCU: Butonlara farklı yönler vermek (biri soldan, biri sağdan)
/// daha hareketli durur ama abartılırsa dağınık görünür — aynı yönde
/// kademeli gecikme genelde daha iyi sonuç veriyor.
///
/// ⚠️ Bu script objenin `anchoredPosition`'ını değiştiriyor. Objeyi Rect
/// Tool ile TAŞIMAK istersen önce Play'den çık — çalışırken taşırsan
/// animasyon bittiğinde eski yerine döner.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UISlideIn : MonoBehaviour
{
    public enum Direction { Soldan, Sagdan, Yukaridan, Asagidan }

    [Header("Nereden Gelsin")]
    [SerializeField] private Direction from = Direction.Soldan;

    [Tooltip("Kaç piksel uzaktan gelsin. Büyük değer = daha uzun yolculuk, " +
             "daha dramatik. 1920x1080 referansında 200-600 arası iyi durur.")]
    [SerializeField] private float travelDistance = 400f;

    [Header("Zamanlama")]
    [Tooltip("Bu öğe kaç saniye BEKLEDİKTEN sonra girsin. 🚨 ASIL SİHİR " +
             "BURADA: her butona sırayla 0.00 / 0.06 / 0.12 / 0.18 ver. " +
             "Hepsine 0 verirsen hepsi birlikte girer ve etki kaybolur.")]
    [SerializeField] private float delay = 0f;

    [Tooltip("Kayma ne kadar sürsün (saniye).")]
    [SerializeField] private float duration = 0.45f;

    [Header("Saydamlık")]
    [Tooltip("Kayarken aynı anda görünürlüğü de artsın mı. " +
             "Objede CanvasGroup yoksa otomatik ekleniyor.")]
    [SerializeField] private bool fadeIn = true;

    private RectTransform rect;
    private CanvasGroup canvasGroup;
    private Vector2 targetPosition;
    private Vector2 startPosition;
    private float elapsed;
    private bool playing;

    void Awake()
    {
        rect = GetComponent<RectTransform>();

        // Hedef = tasarımda objenin DURDUĞU yer. Bunu Awake'te yakalıyoruz,
        // çünkü animasyon başlayınca objeyi oradan uzaklaştıracağız.
        targetPosition = rect.anchoredPosition;

        if (fadeIn)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    void OnEnable()
    {
        // OnEnable'da başlıyor: panel her açıldığında (ör. ESC menüsünden
        // ana menüye dönünce) animasyon yeniden oynuyor.
        Restart();
    }

    private void Restart()
    {
        Vector2 offset = from switch
        {
            Direction.Soldan    => new Vector2(-travelDistance, 0f),
            Direction.Sagdan    => new Vector2( travelDistance, 0f),
            Direction.Yukaridan => new Vector2(0f,  travelDistance),
            _                   => new Vector2(0f, -travelDistance),
        };

        startPosition = targetPosition + offset;
        rect.anchoredPosition = startPosition;

        if (canvasGroup != null) canvasGroup.alpha = 0f;

        elapsed = 0f;
        playing = true;
    }

    void Update()
    {
        if (!playing) return;

        // unscaledDeltaTime: menü açıkken Time.timeScale değişse bile
        // animasyon takılmasın.
        elapsed += Time.unscaledDeltaTime;

        float t = (elapsed - delay) / Mathf.Max(0.0001f, duration);

        if (t <= 0f) return;   // henüz gecikme süresi dolmadı

        if (t >= 1f)
        {
            rect.anchoredPosition = targetPosition;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            playing = false;
            return;
        }

        // Yumuşama eğrisi: sonda yavaşlayan (ease-out) hareket, sabit hızlı
        // kaymadan çok daha "oturmuş" hissettiriyor. 1-(1-t)^3 = cubic ease-out.
        float eased = 1f - Mathf.Pow(1f - t, 3f);

        rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, targetPosition, eased);
        if (canvasGroup != null) canvasGroup.alpha = eased;
    }

    /// <summary>
    /// Animasyonu Play modunda tekrar oynatmak için (Inspector ⋮ menüsü) —
    /// zamanlamayı ayarlarken defalarca izlemek gerekiyor.
    /// </summary>
    [ContextMenu("Animasyonu Tekrar Oynat")]
    private void ReplayFromInspector()
    {
        if (Application.isPlaying) Restart();
    }
}
