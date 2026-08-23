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

    [SyncVar(hook = nameof(OnLabelChanged))]
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

    /// <summary>
    /// SADECE bu objenin sahibi olan client'ta çalışıyor — ana menüde yazılan
    /// ismi sunucuya bildiriyor.
    ///
    /// NEDEN BAĞLANTI ANINDA DEĞİL DE BURADA: Mirror'da bağlantı kurulurken
    /// veri taşımak için özel mesaj tipi tanımlamak gerekir. Obje spawn
    /// olduktan sonra sahibi bir [Command] göndermek çok daha basit ve bu
    /// projedeki diğer akışlarla (CmdSetReady) aynı desende kalıyor.
    /// İsmin bir kare geç gelmesi lobide sorun değil.
    /// </summary>
    public override void OnStartAuthority()
    {
        base.OnStartAuthority();
        CmdSetName(PlayerNameSettings.PlayerName);
    }

    /// <summary>
    /// İsmi sunucuya yazar. 🚨 GELEN METNE GÜVENİLMİYOR: `Sanitize` burada
    /// TEKRAR çağrılıyor. Client tarafındaki kutu zaten 20 karakterle
    /// sınırlıyor ama değiştirilmiş bir build istediğini gönderebilir —
    /// oyuncu listesi ve leaderboard TextMeshPro zengin metni işlediği için
    /// temizlenmemiş bir isim herkesin ekranını bozabilirdi.
    /// </summary>
    [Command]
    private void CmdSetName(string requestedName)
    {
        string clean = PlayerNameSettings.Sanitize(requestedName);

        playerLabel = string.IsNullOrEmpty(clean) ? $"Oyuncu {netId}" : clean;
    }

    private void OnLabelChanged(string oldValue, string newValue)
    {
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

    /// <summary>
    /// MyNetworkManager.OnServerDisconnect, lobide biri çıktıktan SONRA
    /// (bir kare gecikmeyle, liste güncellensin diye) bunu çağırıyor.
    ///
    /// NEDEN GEREKLİ: "herkes hazır mı" kontrolü normalde sadece biri
    /// hazır butonuna bastığında çalışıyor. 3 kişilik lobide 2 kişi hazır
    /// olup 3. kişi çıkarsa, kimse butona basmadığı için kontrol bir daha
    /// hiç çalışmıyor ve kalan iki oyuncu sonsuza kadar bekliyordu.
    ///
    /// Kontrol herhangi bir LobbyPlayer üzerinden çalıştırılabilir; metod
    /// zaten AllLobbyPlayers listesinin tamamına bakıyor, "this" kimse
    /// olursa olsun sonuç aynı.
    /// </summary>
    [Server]
    public static void ServerRecheckAllReady()
    {
        LobbyPlayer any = AllLobbyPlayers.FirstOrDefault(p => p != null);
        any?.ServerCheckAllReady();
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

        // İSİM TAŞIMA: LobbyPlayer objeleri sahne geçişinde yok oluyor, yani
        // isimler onlarla birlikte kaybolurdu. Rol ve renkle AYNI yolu
        // kullanıyoruz — DontDestroyOnLoad olan NetworkManager'a aktarılıyor,
        // orada Online Scene'deki araç spawn'ında geri okunuyor.
        var nameMap = new System.Collections.Generic.Dictionary<NetworkConnectionToClient, string>();
        for (int i = 0; i < players.Count; i++)
            nameMap[players[i].connectionToClient] = players[i].PlayerLabel;

        MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;
        if (netManager != null)
        {
            netManager.PrepareGridForRace(racerCount);
            netManager.SetRoleAssignments(roleMap);
            netManager.SetColorAssignments(colorMap);
            netManager.SetNameAssignments(nameMap);
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
