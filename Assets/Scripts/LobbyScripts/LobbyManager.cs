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
    public static LobbyManager Instance { get; private set; }

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
        Instance = this;
    }

    void Start()
    {
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);
        if (ReadyButton != null) ReadyButton.onClick.AddListener(ToggleReady);
        RefreshPlayerList();
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
