using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Ekranda FPS sayacı gösterir. Kendi Canvas'ını ve yazısını RUNTIME'DA
/// oluşturuyor — sahnede elle Canvas kurmana, TextMeshPro objesi atamana
/// gerek yok. (Projedeki SaboteurController'ın crosshair'i de aynı yöntemle
/// yapılıyor.)
///
/// Mevcut FPSCounter'dan farkı: o, Inspector'dan bir TextMeshProUGUI atanmasını
/// zorunlu kılıyordu ve sahne başına ayrı kurulum gerekiyordu.
///
/// GÖSTERDİKLERİ:
///   "120 FPS   min 96   8.3 ms"
///   - FPS: yumuşatılmış anlık değer
///   - min: son birkaç saniyedeki EN DÜŞÜK değer. Asıl önemli sayı bu —
///     ortalama 120 görünüp arada 20'ye düşen bir oyun kötü hissettirir,
///     tek başına ortalamaya bakınca bu hiç fark edilmez.
///   - ms: bir karenin çizilme süresi. 16.7ms = 60 FPS, 8.3ms = 120 FPS.
///
/// KURULUM: Offline Scene'de boş bir GameObject'e ekle, Ctrl+S. Sahne
/// geçişlerinde yaşamaya devam ediyor, her sahneye ayrı eklemene gerek yok.
///
/// NOT: F9 ile aldığın capsule/ekran görüntülerine bu yazı GİRMEZ —
/// ScreenshotCapture kamerayı doğrudan render ettiği için ekran üstü UI'ı
/// yakalamıyor. Yani çekim yaparken kapatmana gerek yok.
/// </summary>
[DefaultExecutionOrder(-400)]
public class FpsDisplay : MonoBehaviour
{
    public enum Corner { SolUst, SagUst, SolAlt, SagAlt }

    [Header("Görünüm")]
    [SerializeField] private Corner corner = Corner.SagUst;
    [SerializeField] private int fontSize = 18;
    [Tooltip("Kenarlardan boşluk (piksel).")]
    [SerializeField] private Vector2 padding = new Vector2(14f, 12f);

    [Header("Davranış")]
    [Tooltip("Oyun açılınca sayaç görünür olsun mu.")]
    [SerializeField] private bool startVisible = true;
    [Tooltip("Sayacı açıp kapatan tuş. F4-F10 çekim araçlarında kullanılıyor.")]
    [SerializeField] private Key toggleKey = Key.F3;
    [Tooltip("'min' değerinin kaç saniyelik pencerede aranacağı. Kısa tutarsan " +
             "takılmaları anlık yakalarsın, uzun tutarsan genel resmi görürsün.")]
    [SerializeField] private float minWindowSeconds = 3f;
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Renk Eşikleri")]
    [Tooltip("Bu değerin üstü yeşil.")]
    [SerializeField] private int goodFps = 60;
    [Tooltip("Bu değerin üstü sarı, altı kırmızı.")]
    [SerializeField] private int okFps = 30;

    private static FpsDisplay instance;

    private Text label;
    private float smoothedDeltaTime;
    private float minFpsInWindow = float.MaxValue;
    private float windowResetTime;

    void Awake()
    {
        if (dontDestroyOnLoad)
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        BuildUI();
        SetVisible(startVisible);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            SetVisible(label != null && !label.enabled);

        if (label == null || !label.enabled) return;

        // Time.unscaledDeltaTime kullanıyoruz — F7 ile timeScale sıfırlansa
        // bile sayaç doğru çalışmaya devam etsin diye.
        float delta = Time.unscaledDeltaTime;

        // Yumuşatma: ham değer kareden kareye çok zıplıyor, okunmuyor.
        smoothedDeltaTime += (delta - smoothedDeltaTime) * 0.1f;

        float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
        float instantFps = delta > 0f ? 1f / delta : 0f;

        // min değeri YUMUŞATILMAMIŞ anlık değerden alınıyor — takılmalar
        // yumuşatma yüzünden kaybolmasın.
        if (instantFps < minFpsInWindow) minFpsInWindow = instantFps;

        if (Time.unscaledTime >= windowResetTime)
        {
            windowResetTime = Time.unscaledTime + minWindowSeconds;
            minFpsInWindow = instantFps;
        }

        label.text = $"{Mathf.Round(fps)} FPS    min {Mathf.Round(minFpsInWindow)}    {smoothedDeltaTime * 1000f:0.0} ms";
        label.color = fps >= goodFps ? Color.green
                    : fps >= okFps ? Color.yellow
                    : Color.red;
    }

    public void SetVisible(bool visible)
    {
        if (label != null) label.enabled = visible;
    }

    /// <summary>
    /// Canvas + yazıyı koddan kurar. Screen Space Overlay kullanıyoruz ki
    /// hangi kamera aktif olursa olsun (araba / sabotajcı / fotoğraf kamerası)
    /// sayaç görünmeye devam etsin.
    /// </summary>
    private void BuildUI()
    {
        GameObject canvasObject = new GameObject("FpsDisplayCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // her şeyin üstünde kalsın

        // 🚨 CanvasScaler'ı VARSAYILAN bırakmak = Constant Pixel Size:
        // sayaç 2K/4K ekranda fiziksel olarak küçülüyor ve köşeden bıraktığı
        // boşluk ekrana göre daralıyordu. 1920×1080 referansıyla ölçekleyince
        // her çözünürlükte 1080p'deki gibi duruyor.
        // (Aynı hata projedeki kodda kurulan bütün UI'larda vardı.)
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject labelObject = new GameObject("FpsLabel");
        labelObject.transform.SetParent(canvasObject.transform, false);

        label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false; // tıklamaları engellemesin

        // Açık gökyüzünde de okunsun diye ince bir kontur.
        Outline outline = labelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        ApplyCorner(label.rectTransform);
    }

    /// <summary>
    /// Yazıyı seçilen köşeye sabitler. Anchor ve pivot'u aynı köşeye
    /// koyunca farklı ekran çözünürlüklerinde de yeri kaymıyor.
    /// </summary>
    private void ApplyCorner(RectTransform rect)
    {
        Vector2 anchor;
        Vector2 offset;

        switch (corner)
        {
            case Corner.SolUst:
                anchor = new Vector2(0f, 1f);
                offset = new Vector2(padding.x, -padding.y);
                break;
            case Corner.SolAlt:
                anchor = new Vector2(0f, 0f);
                offset = new Vector2(padding.x, padding.y);
                break;
            case Corner.SagAlt:
                anchor = new Vector2(1f, 0f);
                offset = new Vector2(-padding.x, padding.y);
                break;
            default: // SagUst
                anchor = new Vector2(1f, 1f);
                offset = new Vector2(-padding.x, -padding.y);
                break;
        }

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(400f, 30f);
    }
}
