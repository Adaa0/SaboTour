using UnityEngine;
using UnityEngine.UI;
using Mirror;
using TMPro;
using System.Linq;
using System.Text;

/// <summary>
/// Lobi UI'ını yönetir: bağlı oyuncu listesini gösterir, "Hazırım" butonuna
/// basılınca server'a bildirir (LobbyPlayer.CmdSetReady üzerinden), loading
/// screen panelini açıp kapatır.
///
/// NOT: Bu script NetworkBehaviour DEĞİL — sadece UI. Asıl ağ mantığı
/// (herkes hazır mı kontrolü, sahne geçişi, loading screen RPC'si)
/// LobbyPlayer.cs içinde (çünkü [Command]/[ClientRpc] sadece
/// NetworkBehaviour'da çalışabiliyor).
///
/// Bu obje NetworkManager'ın ALTINDA (child) duruyor, böylece
/// DontDestroyOnLoad sayesinde Offline Scene'den Online Scene'e geçerken
/// yok olmuyor — loading screen sahne geçişi boyunca ekranda kalabiliyor.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    // ══ KENDİNİ ONARAN INSTANCE ══════════════════════════════════════════
    // Düz bir `static LobbyManager Instance` alanı bu projede iki kere bug
    // üretti, çünkü Mirror ana menüye dönerken ESKİ NetworkManager objesini
    // (ve altındaki bu Canvas'ı) YOK EDİYOR, yerine Offline Scene'in kendi
    // taze kopyası geliyor. Yok edilmiş bir MonoBehaviour'a Unity'de `== null`
    // sorulduğunda `true` dönüyor — bu property tam olarak onu kullanıyor:
    // elindeki referans ölmüşse sahnedeki YAŞAYAN kopyayı bulup ona geçiyor.
    // Böylece "Instance ölü bir objeyi gösteriyor" durumu imkânsız hale
    // geliyor; kim kimi ezdi tartışmasına hiç girmiyoruz.
    private static LobbyManager _instance;

    public static LobbyManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindAnyObjectByType<LobbyManager>(FindObjectsInactive.Include);

            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Lobi UI")]
    public GameObject LobbyPanel;
    public TextMeshProUGUI PlayerListText;
    public Button ReadyButton;
    public TextMeshProUGUI ReadyButtonLabel;

    [Header("Loading Screen")]
    public GameObject LoadingScreenPanel;

    // ─── SESLER ──────────────────────────────────────────────────────────
    // Hepsi 2D — lobi bir menü ekranı, konumlu ses anlamsız olurdu.
    [Header("Sesler")]
    [Tooltip("'Hazırım' butonuna basınca çalar.")]
    public AudioClip readyClip;
    [Tooltip("'Hazır Değilim' (hazır durumundan çıkma) sesi. Boş bırakılırsa ready sesi kullanılır.")]
    public AudioClip unreadyClip;
    [Tooltip("Lobiye YENİ bir oyuncu katılınca çalar — herkesin ekranına bakmadan da birinin geldiğini anlaması için.")]
    public AudioClip playerJoinedClip;
    [Tooltip("Herkes hazır olup yarış başlarken (loading screen açılırken) çalar.")]
    public AudioClip raceStartingClip;
    [Range(0f, 1f)] public float lobbyVolume = 0.8f;

    private bool localReady = false;

    // Oyuncu katılma sesini SADECE sayı ARTTIĞINDA çalmak için. RefreshPlayerList
    // her hazır-durumu değişiminde de çağrılıyor (bkz. LobbyPlayer.OnReadyChanged),
    // bu takip olmadan her "hazırım" tıkında da katılma sesi çalardı.
    private int lastKnownPlayerCount = -1;

    void Awake()
    {
        // ══ EN YENİ KOPYA HER ZAMAN SAHİPTİR — BU BİR BUG DÜZELTMESİ ══
        // 16 Ağustos'ta buraya "var olan Instance'ı asla ezme" koruması
        // konmuştu ve BU KORUMA YANLIŞTI. Dayandığı varsayım şuydu: "ana
        // menüye dönünce Offline Scene'in TAZE kopyası gelir, Mirror onu
        // siler, DontDestroyOnLoad'daki ESKİ kopya yaşar."
        //
        // GERÇEK TAM TERSİ (Mirror kaynağında doğrulandı — NetworkManager.cs
        // StopServer satır ~587 ve OnClientDisconnectInternal satır ~1280):
        // Mirror ana menüye dönmeden ÖNCE NetworkManager objesini
        // `SceneManager.MoveGameObjectToScene` ile DontDestroyOnLoad'DAN
        // ÇIKARIP o anki sahneye taşıyor — kendi yorumuyla "let a fresh
        // Network Manager be created". Yani ESKİ kopya (ve altındaki bu
        // Canvas) ana menü yüklenirken YOK EDİLİYOR, yaşayan taze kopya
        // Offline Scene'den gelen YENİ kopya oluyor.
        //
        // Sahne yüklemesi ASENKRON olduğu için yeni kopyanın Awake'i, eski
        // kopya HÂLÂ HAYATTAYKEN çalışabiliyor. O anda eski koruma devreye
        // girip YAŞAYACAK olan kopyayı "fazlalık" sayıyor ve kendini
        // kaydettirmiyordu. Hemen ardından eski kopya ölüp Instance null
        // kalıyordu. İKİ SONUÇ (ikisi de gerçek testte yaşandı):
        //   1. Start() erken dönüyordu → `LoadingScreenPanel.SetActive(false)`
        //      hiç çalışmıyor → sahnede varsayılan olarak AÇIK duran yükleme
        //      ekranı lobinin üstünü kapatıyor → "hiçbir buton gözükmüyor".
        //      Ayrıca "Hazırım" butonuna listener da bağlanmıyordu.
        //   2. Aynı hata SteamLobbyManager'da daha da sertti (orada kopya
        //      kendini Destroy ediyordu) → "Oyun Kur" butonu ölüyor, ikinci
        //      bir lobi kurulamıyordu.
        //
        // ÇÖZÜM: Kim kimi ezecek tartışmasına hiç girme. En son Awake olan
        // kopya sahibi olsun (o, yaşamaya devam edecek olan); ölen kopya
        // zaten OnDestroy'da bayrağı bırakıyor ve yukarıdaki kendini onaran
        // property her koşulda yaşayan kopyayı buluyor.
        Instance = this;
    }

    void Start()
    {
        SetupUI();
    }

    /// <summary>
    /// Buton bağlama + panel başlangıç durumu. Start()'tan ayrı bir metot
    /// çünkü ARTIK KOŞULSUZ çalışıyor (bkz. Awake'teki uzun not) ve aynı
    /// kopyada birden fazla kez çağrılması zararsız olmalı — bu yüzden
    /// listener önce KALDIRILIP sonra ekleniyor (aksi halde iki kere bağlanıp
    /// "Hazırım" tek tıkta iki kez tetiklenir, yani hiç değişmemiş görünürdü).
    /// </summary>
    private void SetupUI()
    {
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);

        if (ReadyButton != null)
        {
            ReadyButton.onClick.RemoveListener(ToggleReady);
            ReadyButton.onClick.AddListener(ToggleReady);
        }

        if (LobbyPanel != null) LobbyPanel.SetActive(true);

        RefreshPlayerList();
    }

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void OnDestroy()
    {
        // `Instance` yerine doğrudan `_instance`: property'nin getter'ı
        // gerekirse sahneyi tarıyor, sahne yıkılırken bunu tetiklemek
        // gereksiz (ve Unity'de riskli). Bayrağı sadece bırakıyoruz —
        // bir sonraki okumada getter yaşayan kopyayı zaten bulacak.
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// İKİNCİ GÜVENLİK AĞI: lobi ekranını sahne yüklendikten SONRA da düzeltir.
    ///
    /// NEDEN GEREKLİ: MyNetworkManager.OnStopClient → ResetToLobby() zinciri,
    /// Mirror ana menü sahnesini YÜKLEMEDEN ÖNCE çalışıyor. O ana kadar her
    /// şey doğru olsa bile, arada bir sahne geçişi var ve bu geçişte bir şey
    /// ters giderse (yukarıdaki Instance sorunu gibi) panel kapalı kalıyordu.
    /// Burada ağ oturumunun gerçekten bittiğini görüp paneli açıyoruz — kim
    /// çağırmayı unutursa unutsun lobi ekranı kullanılabilir hale geliyor.
    /// </summary>
    private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                   UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // 🚨 BURADA ESKİDEN `if (Instance != this) return;` VARDI — "Tekrar
        // Oyna"yı BOZAN ŞEY TAM OLARAK BUYDU.
        //
        // Lobi sahnesi yüklenirken o sahnenin KENDİ NetworkManager'ı da
        // geliyor ve altındaki LobbyManager'ın Awake'i çalışıp `Instance`'ı
        // ele geçiriyor. Mirror birazdan o fazlalık objeyi yok ediyor
        // ("Multiple NetworkManagers detected" uyarısı bundan) — ama TAM O
        // ARALIKTA `sceneLoaded` tetikleniyor ve YAŞAYAN kopya `Instance`
        // artık kendisi olmadığı için erken dönüyordu. Sonuç: `ResetToLobby`
        // hiç çalışmıyor, `LobbyPanel` yarış başlarken kapatıldığı yerde
        // kapalı kalıyor ve oyuncu boş bir lobi sahnesine bakıyordu.
        //
        // Guard KALDIRILDI: hangi kopya çalışırsa çalışsın kendi
        // referanslarını düzeltiyor. `ResetToLobby` idempotent, iki kopyanın
        // birden çalışması zararsız — biri zaten hemen yok olacak.

        // YARIŞ sahnesi yüklendiyse lobiyi açma.
        //
        // ⚠️ ESKİDEN buradaki kural "ağ oturumu açıksa dokunma" idi. O kural
        // "Tekrar Oyna" ile BOZULDU: lobiye dönerken oturum bilerek AÇIK
        // kalıyor (grup dağılmasın diye), yani eski kontrol lobiyi hiç
        // açmazdı. Artık sahnenin KENDİSİNE bakıyoruz — hem oturum kapanınca
        // hem tekrar oyna ile dönünce doğru çalışıyor.
        string gameScene = (NetworkManager.singleton as MyNetworkManager)?.GameSceneName;
        if (!string.IsNullOrEmpty(gameScene) && scene.name == gameScene) return;

        ResetToLobby();
    }

    // ─── Ready Butonu (Client) ──────────────────────────────────
    public void ToggleReady()
    {
        LobbyPlayer localLobbyPlayer = NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<LobbyPlayer>()
            : null;

        if (localLobbyPlayer == null) return;

        localReady = !localReady;
        localLobbyPlayer.CmdSetReady(localReady);

        AudioClip clip = localReady ? readyClip : (unreadyClip != null ? unreadyClip : readyClip);
        SfxPlayer.PlayUI(clip, lobbyVolume);

        if (ReadyButtonLabel != null)
            ReadyButtonLabel.text = Loc.T(localReady ? "menu.notready" : "menu.ready");
    }

    // ─── Oyuncu Listesi UI (Her Client'ta) ──────────────────────
    public void RefreshPlayerList()
    {
        var players = LobbyPlayer.AllLobbyPlayers.Where(p => p != null).ToList();

        // Ses kontrolü metnin YAZILMASINDAN ÖNCE ve PlayerListText null
        // kontrolünden BAĞIMSIZ — UI referansı atanmamış olsa bile ses
        // çalışmaya devam etsin diye.
        if (lastKnownPlayerCount >= 0 && players.Count > lastKnownPlayerCount)
            SfxPlayer.PlayUI(playerJoinedClip, lobbyVolume);

        lastKnownPlayerCount = players.Count;

        if (PlayerListText == null) return;

        var sb = new StringBuilder();
        foreach (var p in players)
            sb.AppendLine($"{p.PlayerLabel} — {Loc.T(p.IsReady ? "menu.playerready" : "menu.playerwaiting")}");

        PlayerListText.text = sb.ToString();
    }

    // ─── Loading Screen ──────────────────────────────────────────
    // İKİ AYRI YERDEN ÇAĞRILIYOR (bilinçli, ikisi de gerekli):
    //  1. LobbyPlayer.RpcShowLoadingScreen — host dahil herkese.
    //  2. MyNetworkManager.OnClientChangeScene — client'ın sahne değiştirme
    //     mesajını aldığı an. RPC'ye tek başına GÜVENMİYORUZ: ClientRpc
    //     `SendToReadyObservers` ile gidiyor ve `ServerChangeScene` hemen
    //     ardından `SetAllClientsNotReady()` çağırıyor — bu kadar dar bir
    //     aralıkta sıraya güvenmek yerine ikinci bir garanti koyduk.
    // Metot idempotent, iki kere çağrılması zararsız.

    private bool loadingScreenVisible;

    public void ShowLoadingScreen()
    {
        // Ses SADECE ilk çağrıda — iki giriş noktası var, iki kere çalmasın.
        if (!loadingScreenVisible)
            SfxPlayer.PlayUI(raceStartingClip, lobbyVolume);

        loadingScreenVisible = true;

        // Bekleyen bir "gizle" işi varsa iptal et, yoksa yeni gösterdiğimiz
        // ekranı eski coroutine kapatabilir.
        if (hideRoutine != null) { StopCoroutine(hideRoutine); hideRoutine = null; }

        if (LobbyPanel != null) LobbyPanel.SetActive(false);
        if (LoadingScreenPanel == null) return;

        LoadingScreenPanel.SetActive(true);
        MakeLoadingScreenOpaque();
        BringCanvasToFront();
    }

    /// <summary>
    /// Yükleme ekranının arkası TAM SİYAH olmalı.
    ///
    /// SEBEBİ SOMUT: sahnedeki panelin Image rengi (0,0,0, **alpha 0.95**)
    /// olarak kaydedilmişti — yani arkadaki dünya %5 oranında görünüyordu.
    /// Online Scene'in sabit kamerası kulenin altına bakıyor ve o görüntü
    /// yükleme ekranının içinden sızıyordu. Kodla zorlamak, sahne dosyası
    /// ileride tekrar değişse bile garanti veriyor.
    /// </summary>
    private void MakeLoadingScreenOpaque()
    {
        Image background = LoadingScreenPanel.GetComponent<Image>();
        if (background == null) return;

        Color c = background.color;
        if (c.a >= 1f && c.r <= 0f && c.g <= 0f && c.b <= 0f) return;

        background.color = new Color(0f, 0f, 0f, 1f);
    }

    /// <summary>
    /// Lobi canvas'ını her şeyin ÜSTÜNE alır.
    ///
    /// NEDEN: Online Scene'de birden fazla Screen Space Overlay canvas var ve
    /// HEPSİNİN `sortingOrder`'ı 0. Eşit sıra numarasında Unity'nin çizim
    /// sırası hiyerarşiye/sahne sırasına bağlı — DontDestroyOnLoad'daki bir
    /// canvas ile sahneden gelen canvas'lar arasında bu tamamen belirsiz.
    /// Yükleme ekranının HUD'ın altında kalma ihtimalini tamamen kaldırıyoruz.
    /// </summary>
    private void BringCanvasToFront()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        if (canvas.sortingOrder < 500) canvas.sortingOrder = 500;
    }

    public void HideLoadingScreen()
    {
        if (hideRoutine != null) { StopCoroutine(hideRoutine); hideRoutine = null; }

        loadingScreenVisible = false;

        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);
    }

    // Yükleme ekranını "oyuncu hazır olunca" kapatan coroutine.
    private Coroutine hideRoutine;

    [Header("Yükleme Ekranı")]
    [Tooltip("Oyuncu objesi geldikten sonra ekranın kapanması için beklenen ek süre — kameranın devralması bir-iki kare sürüyor.")]
    [SerializeField] private float loadingSettleSeconds = 0.35f;

    [Tooltip("Bu süre dolunca oyuncu hâlâ doğmamış olsa bile ekran kapanır. Takılı kalan yükleme ekranı, erken kapanandan daha kötü.")]
    [SerializeField] private float loadingMaxWaitSeconds = 15f;

    /// <summary>
    /// ══ İKİ BUG'I BİRDEN DÜZELTİYOR (23 Ağustos 2026) ══
    /// BELİRTİLER: (a) yükleme ekranı erken bitiyordu ve sabotajcı yarışa
    /// geç başlamış gibi oluyordu, (b) bazen yükleme ekranı yerine
    /// "kulenin altı" görünüyordu.
    ///
    /// TEK SEBEP: `MyNetworkManager.OnClientSceneChanged` ekranı SAHNE
    /// YÜKLENİR YÜKLENMEZ kapatıyordu. Ama oyuncunun objesi o an henüz YOK:
    /// yarışçılar `SpawnPlayerWhenTrackReady` ile pist üretilene kadar
    /// (5 saniyeye kadar) bekletiliyor, sabotajcı da ondan sonra geliyor.
    /// O boşlukta ekranda ne var? Online Scene'in SABİT Main Camera'sı —
    /// yani sahnede nereye bakıyorsa orası. "Kule altı" tam olarak buydu.
    ///
    /// ÇÖZÜM: ekran, YEREL OYUNCU OBJESİ gelene kadar açık kalıyor. Objenin
    /// `OnStartAuthority`'si kamerayı devralıyor (yarışçıda CarCam,
    /// sabotajcıda FPCam), o yüzden bir de kısa bir oturma payı bırakıyoruz.
    ///
    /// ⏱️ Zaman aşımı ŞART: bir şey ters giderse oyuncu sonsuza kadar
    /// yükleme ekranına bakmasın — takılı kalan ekran, erken kapanandan
    /// daha kötü bir hata.
    /// </summary>
    public void HideLoadingScreenWhenPlayerReady()
    {
        if (LoadingScreenPanel == null) return;

        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideWhenReadyRoutine());
    }

    private System.Collections.IEnumerator HideWhenReadyRoutine()
    {
        float waited = 0f;

        // İKİ KOŞUL BİRDEN:
        //  1. Kendi oyuncu objem doğdu mu (kameram devraldı mı).
        //  2. Geri sayım KURULDU mu — yani SUNUCU tüm yarışçıların doğduğunu
        //     gördü mü (RacePodiumManager.ServerTryArmCountdown).
        //
        // İkincisi olmadan şu oluyordu: hızlı yüklenen oyuncu ekranı kapatıp
        // arabasının başında bekliyor, yavaş yüklenen (Multiplayer Play Mode'da
        // 2. oyuncu her zaman ~2sn geç açılıyor) hâlâ yükleniyor. Şimdi ikisi
        // de aynı anda "3"ü görüyor.
        while (waited < loadingMaxWaitSeconds)
        {
            bool mineReady = NetworkClient.localPlayer != null;
            bool everyoneReady = RacePodiumManager.CountdownArmed;

            if (mineReady && everyoneReady) break;

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (waited >= loadingMaxWaitSeconds)
        {
            Debug.LogWarning($"[LobbyManager] {loadingMaxWaitSeconds}sn doldu — yükleme ekranı yine de " +
                             $"kapatılıyor. (Kendi oyuncum: {(NetworkClient.localPlayer != null ? "var" : "YOK")}, " +
                             $"geri sayım kuruldu mu: {RacePodiumManager.CountdownArmed}). " +
                             "Spawn ya da pist üretimi tarafında bir sorun olabilir.");
        }
        else
        {
            // Kameranın devralması (OnStartAuthority) ve ilk karenin çizilmesi
            // için küçük bir pay. Bu olmadan sahnenin sabit kamerası bir-iki
            // kare görünebiliyor.
            yield return null;
            yield return new WaitForSecondsRealtime(loadingSettleSeconds);
        }

        loadingScreenVisible = false;
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);
        hideRoutine = null;
    }

    /// <summary>
    /// OYUN OTURUMU BİTİNCE (host durdu / bağlantı koptu / oyundan ayrılındı)
    /// lobi ekranını ilk günkü hâline döndürür. MyNetworkManager.OnStopClient
    /// çağırıyor.
    ///
    /// ══ BU METOD BİR BUG'I DÜZELTİYOR ══
    /// Bu Canvas, NetworkManager'ın ALTINDA (child) duruyor, yani
    /// DontDestroyOnLoad — sahne değişse bile yok olmuyor. `ShowLoadingScreen()`
    /// yarış başlarken `LobbyPanel`i KAPATIYOR ve onu tekrar AÇAN hiçbir yer
    /// yoktu. Sonuç: bir oyun kurup çıktıktan sonra ana menüye dönülüyor ama
    /// lobi paneli hâlâ kapalı kalıyordu — ekranda "Oyun Kur"/"Hazırım"
    /// butonları görünmediği için ikinci bir lobi kurmak İMKÂNSIZDI, oyunu
    /// kapatıp yeniden açmak gerekiyordu.
    ///
    /// Panelin yanı sıra "hazırım" durumu da sıfırlanmalı: localReady bu
    /// objede yaşadığı ve obje sahne geçişinde ölmediği için, ikinci oyunda
    /// oyuncu hâlâ "hazır" sayılıyor ve butona basınca hazır olmak yerine
    /// hazırlıktan ÇIKIYORDU.
    /// </summary>
    public void ResetToLobby()
    {
        localReady = false;
        lastKnownPlayerCount = -1; // katılma sesi yeni oturumda baştan saysın

        if (ReadyButtonLabel != null) ReadyButtonLabel.text = Loc.T("menu.ready");
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);
        if (LobbyPanel != null) LobbyPanel.SetActive(true);

        RefreshPlayerList();
    }
}
