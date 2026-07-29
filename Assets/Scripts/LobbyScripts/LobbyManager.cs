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

    private bool localReady = false;

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

        if (ReadyButtonLabel != null)
            ReadyButtonLabel.text = localReady ? "Hazır Değilim" : "Hazırım";
    }

    // ─── Oyuncu Listesi UI (Her Client'ta) ──────────────────────
    public void RefreshPlayerList()
    {
        if (PlayerListText == null) return;

        var sb = new StringBuilder();
        foreach (var p in LobbyPlayer.AllLobbyPlayers.Where(p => p != null))
            sb.AppendLine($"{p.PlayerLabel} — {(p.IsReady ? "Hazır" : "Bekleniyor")}");

        PlayerListText.text = sb.ToString();
    }

    // ─── Loading Screen (LobbyPlayer'ın RPC'si buradan çağırır) ──
    public void ShowLoadingScreen()
    {
        if (LobbyPanel != null) LobbyPanel.SetActive(false);
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(true);
    }

    // MyNetworkManager, client sahne yüklemesini bitirince bunu çağırır.
    public void HideLoadingScreen()
    {
        if (LoadingScreenPanel != null) LoadingScreenPanel.SetActive(false);
    }
}
