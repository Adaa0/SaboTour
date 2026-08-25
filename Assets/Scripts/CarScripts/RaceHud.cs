using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// YARIŞ HUD'I — üstteki süre çubuğu + "Tekrar Oyna" butonu.
///
/// ─── SÜRE ÇUBUĞU NEDEN GEREKLİ ────────────────────────────────────────
/// Sabotajcı süreyle kazanıyor ama o saati KİMSE göremiyordu. Yarışçı acele
/// etmesi gerektiğini bilmiyor, sabotajcı da kazanmaya ne kadar yaklaştığını
/// bilmiyordu. Asimetrik oyunların gerilim motoru tam olarak budur.
///
/// TEK ÇUBUK, İKİ OKUMA (bilinçli): çubuk boşaldıkça yarışçı "acele et",
/// sabotajcı "yaklaştım" diye okuyor. İki ayrı görsel yapmak hem iki kat
/// kod hem de gereksiz — aynı bilgi zaten iki tarafın da işine yarıyor.
///
/// ─── AĞ MALİYETİ SIFIR ────────────────────────────────────────────────
/// Kalan süre HER KARE senkronlanmıyor. `RacePodiumManager` sadece süre
/// SINIRINI ve yarışın BAŞLAMA ANINI (NetworkTime) yayınlıyor; kalan süreyi
/// her makine kendisi hesaplıyor. Geri sayımdaki numaranın aynısı.
///
/// ─── YÜKLEME ──────────────────────────────────────────────────────────
/// `PauseMenu` ve `RacerMinimap` ile AYNI desen: `[RuntimeInitializeOnLoadMethod]`
/// ile Resources'tan yükleniyor, DontDestroyOnLoad, sahneye elle hiçbir şey
/// eklenmiyor. Prefabı düzenlemek için çift tıkla; bozulursa üst menüden
/// **SaboTour > Yarış HUD Prefabını Oluştur**.
/// </summary>
public class RaceHud : MonoBehaviour
{
    private static RaceHud instance;

    /// <summary>
    /// Sol üstteki sıralama tablosu kutusu. `RaceLeaderboard` metni buraya
    /// yazıyor.
    ///
    /// NEDEN STATIC BİR KAPI: `RaceLeaderboard` Online Scene'in içinde
    /// yaşıyor, bu HUD ise DontDestroyOnLoad'da. İkisinin hangi sırayla
    /// hazır olduğu garanti değil, o yüzden `RaceLeaderboard` referansı
    /// bir kere değil, bulana kadar HER yenilemede istiyor.
    /// </summary>
    public static TMP_Text LeaderboardText => instance != null ? instance.leaderboardText : null;

    [Header("Prefab Referansları")]
    public GameObject barRoot;
    public Image barFill;
    public TMP_Text timeText;

    [Header("Sıralama Tablosu")]
    [Tooltip("Sol üstteki sıralama tablosu. Metni `RaceLeaderboard` yazıyor; " +
             "burada sadece kutunun kendisi duruyor (konum/font/boyut prefabtan).")]
    public TMP_Text leaderboardText;

    [Header("Tekrar Oyna")]
    public GameObject rematchRoot;
    public Button rematchButton;
    public TMP_Text rematchInfoText;

    [Header("Renkler")]
    [Tooltip("Süre bolken çubuğun rengi.")]
    public Color calmColor = new Color(0.35f, 0.85f, 0.55f);

    [Tooltip("Süre azalmaya başlayınca (uyarı eşiği) geçilen renk.")]
    public Color warnColor = new Color(0.98f, 0.78f, 0.25f);

    [Tooltip("Son saniyelerde (tehlike eşiği) geçilen renk.")]
    public Color dangerColor = new Color(0.90f, 0.20f, 0.25f);

    [Header("Eşikler")]
    [Tooltip("Kalan süre bunun altına inince sarıya döner (saniye).")]
    public float warnSeconds = 60f;

    [Tooltip("Kalan süre bunun altına inince kırmızıya döner ve nabız atar (saniye).")]
    public float dangerSeconds = 30f;

    [Tooltip("Tehlike bölgesinde saniyedeki nabız sayısı.")]
    public float pulseSpeed = 2.2f;

    /// <summary>
    /// Oyun açılır açılmaz prefabı kurar. Sahneye elle hiçbir şey eklenmiyor.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>("UI/RaceHud");
        if (prefab == null)
        {
            Debug.LogWarning("[RaceHud] Assets/Resources/UI/RaceHud.prefab bulunamadı — " +
                             "üst menüden 'SaboTour > Yarış HUD Prefabını Oluştur' çalıştır. " +
                             "Süre çubuğu ve Tekrar Oyna butonu görünmeyecek (oyunun geri kalanı normal çalışır).");
            return;
        }

        GameObject go = Instantiate(prefab);
        go.name = "RaceHud (otomatik)";
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        // Tekillik: DontDestroyOnLoad olduğu için ikinci bir kopya oluşursa
        // iki çubuk üst üste binerdi.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (rematchButton != null)
        {
            rematchButton.onClick.RemoveListener(OnRematchClicked);
            rematchButton.onClick.AddListener(OnRematchClicked);
        }

        SetVisible(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        RacePodiumManager podium = RacePodiumManager.Instance;

        UpdateLeaderboard(podium != null);

        // Yarış sahnesinde değilsek (lobi, ana menü) HUD tamamen kapalı.
        if (podium == null)
        {
            SetVisible(false);
            return;
        }

        UpdateBar(podium);
        UpdateRematch(podium);
    }

    private void SetVisible(bool value)
    {
        if (barRoot != null && barRoot.activeSelf != value) barRoot.SetActive(value);
        if (!value && rematchRoot != null && rematchRoot.activeSelf) rematchRoot.SetActive(false);
    }

    /// <summary>
    /// Yarış sahnesinden çıkınca sıralama tablosunu temizler.
    ///
    /// 🚨 NEDEN GEREKLİ (gerçekten yaşandı): Tablo eskiden Online Scene'in
    /// İÇİNDEydi, yani sahne kapanınca objeyle birlikte kendiliğinden yok
    /// oluyordu. 24 Ağustos'ta bu prefaba taşındı ve prefab
    /// DontDestroyOnLoad — artık sahne geçişinde ÖLMÜYOR. Kimse temizlemeyince
    /// yazdığı son tablo lobinin üstünde asılı kalıyordu ("Tekrar Oyna"dan
    /// sonra sol üstte duran yarış sırası tam olarak buydu).
    ///
    /// DERS: bir UI'ı sahneden DontDestroyOnLoad bir prefaba taşımak, onu
    /// temizleme sorumluluğunu da beraberinde getiriyor.
    ///
    /// Objeyi SetActive ile kapatmak yerine metni boşaltıyoruz: kapalı bir
    /// obje `GameObject.Find` ile bulunamıyor ve `RaceLeaderboard`'un yedek
    /// arama yolu kırılırdı. Boş metin zaten hiçbir şey çizmiyor.
    /// </summary>
    private void UpdateLeaderboard(bool inRace)
    {
        if (leaderboardText == null) return;
        if (inRace) return;

        // Her karede yazmaya gerek yok — sadece kalıntı varsa temizle.
        if (leaderboardText.text.Length > 0)
            leaderboardText.text = "";
    }

    private void UpdateBar(RacePodiumManager podium)
    {
        float remaining = podium.RemainingSeconds;
        float limit = podium.TimeLimit;

        // -1 = süre sınırı yok (dizi boş) ya da yarış henüz başlamadı.
        if (remaining < 0f || limit <= 0f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        if (barFill != null)
        {
            SetFillRatio(Mathf.Clamp01(remaining / limit));
            barFill.color = ResolveColor(remaining);
        }

        if (timeText != null)
        {
            int total = Mathf.CeilToInt(remaining);
            timeText.text = $"{total / 60:0}:{total % 60:00}";
            timeText.color = ResolveColor(remaining);
        }
    }

    /// <summary>
    /// Çubuğun dolu kısmını RECTTRANSFORM ile daraltıyor.
    ///
    /// 🚨 NEDEN `Image.fillAmount` DEĞİL: `fillAmount` yalnızca Image'ın bir
    /// SPRITE'ı varsa çalışıyor. Sprite atanmamış bir Image düz bir dikdörtgen
    /// çiziyor ve `type = Filled` tamamen yok sayılıyor — çubuk hiç azalmıyor,
    /// sadece rengi değişiyordu (gerçekten yaşandı). RectTransform'un
    /// `anchorMax.x`'ini kısmak hiçbir sprite'a ihtiyaç duymuyor, yani
    /// prefabta ne olursa olsun çalışıyor.
    /// </summary>
    private void SetFillRatio(float ratio)
    {
        RectTransform rect = barFill.rectTransform;

        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(ratio, 1f);

        // Sol/sağ kenar boşluklarını sıfırda tut — aksi halde anchor
        // değişince çubuk kayabiliyor.
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Renk üç kademeli: bol → uyarı → tehlike. Tehlike bölgesinde ayrıca
    /// nabız atıyor, çünkü son saniyelerde oyuncunun gözü çubukta değil
    /// pistte oluyor — hareket, renkten daha çok dikkat çekiyor.
    /// </summary>
    private Color ResolveColor(float remaining)
    {
        if (remaining > warnSeconds) return calmColor;

        if (remaining > dangerSeconds)
        {
            float t = Mathf.InverseLerp(warnSeconds, dangerSeconds, remaining);
            return Color.Lerp(calmColor, warnColor, t);
        }

        float danger = Mathf.InverseLerp(dangerSeconds, 0f, remaining);
        Color baseColor = Color.Lerp(warnColor, dangerColor, danger);

        // Nabız: 0.55 ile 1 arası parlaklık.
        float pulse = (Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        return baseColor * Mathf.Lerp(0.55f, 1f, pulse);
    }

    /// <summary>
    /// "Tekrar Oyna" SADECE HOST'ta görünüyor.
    ///
    /// NEDEN: host aynı zamanda sunucu, yani `ServerReturnToLobby()`'yi
    /// doğrudan çağırabiliyor — Command'a ve oyuncu objesi üzerinden bir
    /// yetki zincirine gerek kalmıyor. Client'lara da ne olduğunu anlatan
    /// bir satır gösteriyoruz ki "bende neden yok" sorusu doğmasın.
    /// </summary>
    private void UpdateRematch(RacePodiumManager podium)
    {
        if (rematchRoot == null) return;

        bool show = podium.RaceOver;

        if (rematchRoot.activeSelf != show) rematchRoot.SetActive(show);
        if (!show) return;

        bool isHost = NetworkServer.active;

        if (rematchButton != null)
        {
            bool canPress = isHost && podium.CanReturnToLobby;
            if (rematchButton.gameObject.activeSelf != isHost)
                rematchButton.gameObject.SetActive(isHost);
            rematchButton.interactable = canPress;
        }

        if (rematchInfoText != null)
            rematchInfoText.text = isHost ? "" : "Host yeni yarışı başlatabilir.";
    }

    private void OnRematchClicked()
    {
        RacePodiumManager podium = RacePodiumManager.Instance;

        // Güvenlik: buton sadece host'ta gösteriliyor ama yine de kontrol
        // ediyoruz — client'ta çağrılsa `[Server]` metodu zaten uyarı basıp
        // hiçbir şey yapmazdı, sessizce geçmesi daha temiz.
        if (podium == null || !NetworkServer.active) return;

        podium.ServerReturnToLobby();
    }
}
