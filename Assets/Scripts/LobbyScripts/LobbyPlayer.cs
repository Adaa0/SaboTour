using UnityEngine;
using Mirror;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Lobide (Offline Scene, herkes bağlanıp hazır olana kadar burada bekler)
/// her bağlanan oyuncu için spawn edilen HAFİF networked obje. Araba DEĞİL —
/// sadece isim + hazır durumu tutuyor. Herkes hazır olup yarış başlayınca bu
/// obje sahne geçişiyle birlikte yok edilir, yerine Online Scene'de gerçek
/// araba (Car prefab) spawn edilir (bkz. MyNetworkManager.OnServerAddPlayer).
///
/// NOT: "Herkes hazır mı" kontrolü ve yarışı başlatma (loading screen +
/// sahne geçişi) burada, NetworkBehaviour içinde yapılıyor — çünkü
/// [Command]/[ClientRpc] SADECE NetworkBehaviour'da çalışır. LobbyManager
/// (sahnedeki tekil UI kontrolcüsü) network'e bağlı DEĞİL, sadece bu
/// script'in çağırdığı public UI metodlarını barındırıyor.
/// </summary>
public class LobbyPlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnReadyChanged))]
    private bool isReady;
    public bool IsReady => isReady;

    [SyncVar]
    private string playerLabel = "Oyuncu";
    public string PlayerLabel => playerLabel;

    [Header("Yarış Başlatma Ayarları")]
    [Tooltip("Online Scene'in tam yolu (Build Settings'e eklenmiş olmalı).")]
    [SerializeField] private string onlineSceneName = "Assets/Scenes/Mirror/Online Scene.unity";
    [Tooltip("Yarışın başlayabilmesi için gereken minimum oyuncu sayısı.")]
    [SerializeField] private int minPlayersToStart = 1;

    // Her client, lobideki TÜM LobbyPlayer'ları burada tutar (kendisi dahil)
    // — LobbyManager bu listeyi okuyup oyuncu listesini gösteriyor, server
    // ise "herkes hazır mı" kontrolü için kullanıyor.
    public static readonly System.Collections.Generic.List<LobbyPlayer> AllLobbyPlayers = new();

    public override void OnStartServer()
    {
        base.OnStartServer();
        playerLabel = $"Oyuncu {netId}";
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!AllLobbyPlayers.Contains(this))
            AllLobbyPlayers.Add(this);

        if (LobbyManager.Instance != null)
            LobbyManager.Instance.RefreshPlayerList();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        AllLobbyPlayers.Remove(this);

        if (LobbyManager.Instance != null)
            LobbyManager.Instance.RefreshPlayerList();
    }

    [Command]
    public void CmdSetReady(bool ready)
    {
        isReady = ready;
        ServerCheckAllReady();
    }

    private void OnReadyChanged(bool oldValue, bool newValue)
    {
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.RefreshPlayerList();
    }

    [Server]
    private void ServerCheckAllReady()
    {
        var players = AllLobbyPlayers.Where(p => p != null).ToList();

        if (players.Count < minPlayersToStart) return;
        if (players.Any(p => !p.IsReady)) return;

        // ROL ATAMA: 2+ oyuncu varsa rastgele 1 kişi sabotajcı, geri kalanı
        // yarışçı olur. Tek oyuncuyla test ediliyorsa (solo) sabotajcı YOK,
        // herkes yarışçı sayılır — çünkü tek kişiyle hem araba hem kule
        // aynı anda test edilemez.
        int saboteurIndex = players.Count >= 2 ? Random.Range(0, players.Count) : -1;

        var roleMap = new System.Collections.Generic.Dictionary<NetworkConnectionToClient, bool>();
        for (int i = 0; i < players.Count; i++)
            roleMap[players[i].connectionToClient] = (i == saboteurIndex);

        int racerCount = saboteurIndex >= 0 ? players.Count - 1 : players.Count;

        // RENK ATAMA: 12 renklik paletten (CarController.ColorPalette),
        // oyuncu sayısı kadarını karıştırıp birer birer dağıtıyoruz — kimse
        // aynısını almasın diye. Sabotajcıya da bir renk düşüyor ama arabası
        // olmadığı için şu an kullanılmıyor (ileride kendi rengini
        // seçtiğinde bu atama zaten görmezden gelinecek).
        List<int> shuffledColors = Enumerable.Range(0, CarController.ColorPalette.Length).ToList();
        for (int i = shuffledColors.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffledColors[i], shuffledColors[j]) = (shuffledColors[j], shuffledColors[i]);
        }

        var colorMap = new System.Collections.Generic.Dictionary<NetworkConnectionToClient, int>();
        for (int i = 0; i < players.Count; i++)
            colorMap[players[i].connectionToClient] = shuffledColors[i % shuffledColors.Count];

        MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;
        if (netManager != null)
        {
            netManager.PrepareGridForRace(racerCount);
            netManager.SetRoleAssignments(roleMap);
            netManager.SetColorAssignments(colorMap);
        }

        RpcShowLoadingScreen();
        NetworkManager.singleton.ServerChangeScene(onlineSceneName);
    }

    [ClientRpc]
    private void RpcShowLoadingScreen()
    {
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.ShowLoadingScreen();
    }
}
