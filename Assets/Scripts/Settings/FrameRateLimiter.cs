using UnityEngine;

/// <summary>
/// Kare hızını (FPS) sınırlar.
///
/// NEDEN GEREKLİ: Sınır koymazsan Unity "elinden geleni yap" modunda çalışır
/// ve GPU'yu %100'de tutar. Low-poly bir oyunda bu saniyede yüzlerce gereksiz
/// kare demek — görüntü hiç iyileşmez ama fan gürültüsü, ısınma ve dizüstü
/// bilgisayarlarda pil tükenmesi olur. Oyuncular bunu fark edip yorumlarda yazıyor.
///
/// EN ÖNEMLİ İNCELİK: VSync AÇIKKEN Application.targetFrameRate TAMAMEN YOK
/// SAYILIR. İkisini birlikte ayarlamazsan sınır hiç çalışmaz — FPS sınırı
/// koymaya çalışıp da işe yaramamasının en yaygın sebebi budur.
///
/// KURULUM:
///  1. Offline Scene'de (oyunun ilk açılan sahnesi) boş bir GameObject oluştur,
///     adını "FrameRateLimiter" koy.
///  2. Bu script'i ekle.
///  3. Mode'u seç, Ctrl+S ile sahneyi kaydet.
///
/// Online Scene'e AYRICA eklemene gerek yok — "Dont Destroy On Load" açık
/// olduğu için sahne geçişinde hayatta kalıyor. Ama istersen oraya da
/// ekleyebilirsin; ikinci kopya kendini otomatik siliyor.
///
/// İleride ayarlar menüsü yapıldığında Apply(mod, fps) metodu oradan çağrılabilir.
/// </summary>
[DefaultExecutionOrder(-500)]
public class FrameRateLimiter : MonoBehaviour
{
    public enum Mode
    {
        /// <summary>Ekranın yenileme hızına kilitlenir (60Hz ekranda 60 FPS). Ekran yırtılması olmaz.</summary>
        VSync,
        /// <summary>Belirlediğin sayıda kareye sınırlanır. VSync kapatılır.</summary>
        SabitSinir,
        /// <summary>Sınırsız — GPU %100'de çalışır. Sadece performans testi için.</summary>
        Sinirsiz
    }

    [Header("Kare Hızı")]
    [SerializeField] private Mode mode = Mode.SabitSinir;

    [Tooltip("Mode 'SabitSinir' ise saniyedeki kare sayısı. 60 = en güvenli, " +
             "120/144 = yüksek yenileme hızlı ekranlar için.")]
    [Range(30, 300)]
    [SerializeField] private int targetFrameRate = 120;

    [Tooltip("Mode 'VSync' ise: 1 = her tarama, 2 = iki taramada bir (60Hz ekranda 30 FPS).")]
    [Range(1, 4)]
    [SerializeField] private int vSyncCount = 1;

    [Header("Davranış")]
    [Tooltip("Sahne geçişlerinde yaşamaya devam etsin mi. Açık bırak — " +
             "Offline Scene'e koyup Online Scene'de de geçerli olmasını sağlıyor.")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Tooltip("Editör'de de uygulansın mı. KAPALI önerilir: Editör'ün kendi " +
             "yükü FPS ölçümünü zaten yanıltıyor, sınır koymak testleri daha da " +
             "karıştırır. Gerçek ölçümü her zaman build'de yap.")]
    [SerializeField] private bool applyInEditor = false;

    private static FrameRateLimiter instance;

    void Awake()
    {
        // Sahne geçişinde ikinci bir kopya oluşursa (ör. hem Offline hem
        // Online Scene'e eklendiyse) yenisi kendini siliyor.
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

        Apply();
    }

    /// <summary>
    /// Inspector'da değer değiştirdiğinde Play modundayken anında uygular —
    /// böylece doğru sınırı bulmak için tekrar tekrar Play'e basmana gerek yok.
    /// </summary>
    void OnValidate()
    {
        if (Application.isPlaying) Apply();
    }

    /// <summary>
    /// Ayarları uygular. İleride ayarlar menüsü yapılırsa oradan da çağrılabilir.
    /// </summary>
    public void Apply()
    {
        if (Application.isEditor && !applyInEditor) return;

        switch (mode)
        {
            case Mode.VSync:
                QualitySettings.vSyncCount = vSyncCount;
                // VSync devredeyken bu değer zaten yok sayılıyor, -1 (sınırsız)
                // bırakmak en temizi — yanlışlıkla iki sınır çakışmasın.
                Application.targetFrameRate = -1;
                break;

            case Mode.SabitSinir:
                // ŞART: VSync 0 olmadan targetFrameRate hiç çalışmaz.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = targetFrameRate;
                break;

            case Mode.Sinirsiz:
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                break;
        }
    }

    /// <summary>
    /// Ayarlar menüsü için hazır giriş noktası — modu ve FPS'i dışarıdan
    /// değiştirip anında uygular.
    /// </summary>
    public void Apply(Mode newMode, int newTargetFrameRate)
    {
        mode = newMode;
        targetFrameRate = Mathf.Clamp(newTargetFrameRate, 30, 300);
        Apply();
    }
}
