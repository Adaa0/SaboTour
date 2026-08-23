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
        if (Instance != this) return;

        // Ağ oturumu hâlâ açıksa yarıştayız/lobideyiz — dokunma.
        if (NetworkServer.active || NetworkClient.active) return;

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
            ReadyButtonLabel.text = localReady ? "Hazır Değilim" : "Hazırım";
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
            sb.AppendLine($"{p.PlayerLabel} — {(p.IsReady ? "Hazır" : "Bekleniyor")}");

        PlayerListText.text = sb.ToString();
    }

    // ─── Loading Screen (LobbyPlayer'ın RPC'si buradan çağırır) ──
    public void ShowLoadingScreen()
    {
        SfxPlayer.PlayUI(raceStartingClip, lobbyVolume);

        if (LobbyPanel != null) LobbyPanel.SetActive(false);
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(true);
    }

    // MyNetworkManager, client sahne yüklemesini bitirince bunu çağırır.
    public void HideLoadingScreen()
    {
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);
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

        if (ReadyButtonLabel != null) ReadyButtonLabel.text = "Hazırım";
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);
        if (LobbyPanel != null) LobbyPanel.SetActive(true);

        RefreshPlayerList();
    }
}
