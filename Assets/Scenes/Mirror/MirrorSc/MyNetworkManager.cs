using UnityEngine;
using Mirror;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// MİMARİ NOT: Pist prosedürel üretildiği için (TrackGenerator + seed)
/// checkpoint 0'ın (start line) dünya pozisyonu her yarışta değişebilir.
/// Bu yüzden Mirror'ın statik "NetworkStartPosition" sistemi yerine,
/// OnServerAddPlayer() burada override edilip spawn pozisyonu her
/// bağlanan oyuncu için checkpoint 0'a göre RUNTIME'da hesaplanıyor.
/// </summary>
public class MyNetworkManager : NetworkManager
{
    [Header("Lobi")]
    [Tooltip("Bağlanan oyuncunun lobide (henüz yarış başlamadan) aldığı hafif obje — araba değil, sadece isim/hazır durumu.")]
    [SerializeField] private GameObject lobbyPlayerPrefab;
    [Tooltip("Gerçek yarış sahnesinin adı. Aktif sahne bu değilse, oyuncular lobide sayılır.")]
    [SerializeField] private string gameSceneName = "Online Scene";

    [Header("Sabotajcı")]
    [Tooltip("Sabotajcı rolüne seçilen oyuncunun aldığı karakter prefabı (araba değil, kule içinde yürüyen 1st person karakter).")]
    [SerializeField] private GameObject saboteurPrefab;

    // LobbyPlayer.ServerCheckAllReady() herkes hazır olduğunda bunu doldurur:
    // hangi bağlantının sabotajcı, hangisinin yarışçı olduğu bilgisi. Online
    // Scene'e geçince OnServerAddPlayer bu haritaya bakıp doğru prefabı verir.
    private Dictionary<NetworkConnectionToClient, bool> roleAssignments = new();

    public void SetRoleAssignments(Dictionary<NetworkConnectionToClient, bool> assignments)
    {
        roleAssignments = assignments;
    }

    // LobbyPlayer.ServerCheckAllReady() ile aynı anda doldurulur — hangi
    // bağlantının hangi araba rengini (CarController.ColorPalette indeksi)
    // aldığı bilgisi. Araba spawn olurken bu haritadan okunup uygulanıyor.
    private Dictionary<NetworkConnectionToClient, int> colorAssignments = new();

    public void SetColorAssignments(Dictionary<NetworkConnectionToClient, int> assignments)
    {
        colorAssignments = assignments;
    }

    [Header("Yarış Izgarası (F1 Dizilişi)")]
    [Tooltip("Aynı anda kaç araç için ızgara slotu ayrılacak.")]
    [SerializeField] private int maxGridSlots = 4;
    [Tooltip("Arka arkaya sıralar (row) arasındaki mesafe.")]
    [SerializeField] private float rowSpacing = 5f;
    [Tooltip("Sol/sağ sütun (column) arasındaki mesafe.")]
    [SerializeField] private float columnSpacing = 3f;
    [Tooltip("Suspension sistemi araca oturana kadar hafif yukarıda spawn olsun diye.")]
    [SerializeField] private float heightOffset = 1f;

    // "Kim hangi pozisyonda" tamamen rastgele olsun istendiği için,
    // her sunucu başlangıcında ızgara slotları karılıyor (Fisher-Yates).
    // Bağlanan her oyuncu sırayla bu karılmış listeden bir slot alıyor.
    private List<int> shuffledSlots;
    private int nextSlotCursor;

    public override void OnStartServer()
    {
        base.OnStartServer();
        ShuffleGrid();
    }

    private void ShuffleGrid()
    {
        shuffledSlots = new List<int>(maxGridSlots);
        for (int i = 0; i < maxGridSlots; i++)
            shuffledSlots.Add(i);

        for (int i = shuffledSlots.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffledSlots[i], shuffledSlots[j]) = (shuffledSlots[j], shuffledSlots[i]);
        }

        nextSlotCursor = 0;
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // İki durum var:
        // 1) Hâlâ lobideyiz (Online Scene henüz yüklenmedi) → hafif
        //    LobbyPlayer objesi ver, araç yok.
        // 2) Online Scene'deyiz (LobbyManager herkes hazır deyip
        //    ServerChangeScene çağırdıktan sonra Mirror bu metodu HER
        //    bağlantı için OTOMATİK olarak tekrar çağırıyor) → gerçek
        //    araç + grid pozisyonu ver.
        bool inGameScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == gameSceneName;

        if (!inGameScene)
        {
            SpawnLobbyPlayer(conn);
            return;
        }

        bool isSaboteur = roleAssignments.TryGetValue(conn, out bool saboteurFlag) && saboteurFlag;

        if (isSaboteur)
        {
            SpawnSaboteur(conn);
            return;
        }

        // ÖNEMLİ: Checkpoint'ler TrackGenerator tarafından üretilip
        // CheckpointManager'a kaydedilene kadar birkaç frame sürebiliyor
        // (CheckpointManager kendi içinde retry coroutine'i ile bekliyor).
        // Bu yüzden AddPlayerForConnection'ı hemen çağırmak yerine,
        // checkpoint'ler hazır olana kadar bekleyip öyle spawn ediyoruz —
        // yoksa herkes "start line bulunamadı" fallback'ine düşüp origin'de
        // üst üste spawn oluyordu.
        StartCoroutine(SpawnPlayerWhenTrackReady(conn));
    }

    private void SpawnLobbyPlayer(NetworkConnectionToClient conn)
    {
        GameObject lobbyPlayer = Instantiate(lobbyPlayerPrefab);
        NetworkServer.AddPlayerForConnection(conn, lobbyPlayer);
    }

    /// <summary>
    /// Sabotajcıyı kuledeki sabit noktada spawn eder. Checkpoint'lerin
    /// aksine kule pist yeniden üretilse de sahnede sabit durduğu için
    /// track'in hazır olmasını beklemeye gerek yok (araç spawn'ının aksine).
    /// </summary>
    private void SpawnSaboteur(NetworkConnectionToClient conn)
    {
        SaboteurSpawnPoint spawnPoint = FindAnyObjectByType<SaboteurSpawnPoint>();

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;

        if (spawnPoint != null)
        {
            pos = spawnPoint.transform.position;
            rot = spawnPoint.transform.rotation;
        }
        else
        {
            Debug.LogWarning("[MyNetworkManager] SaboteurSpawnPoint sahnede bulunamadı, origin'de spawn ediliyor.");
        }

        GameObject saboteur = Instantiate(saboteurPrefab, pos, rot);
        NetworkServer.AddPlayerForConnection(conn, saboteur);
    }

    /// <summary>
    /// LobbyManager, herkes hazır olup yarışı başlatmadan HEMEN ÖNCE bunu
    /// çağırır — ızgara slot sayısı sabit değil, o anki gerçek lobi oyuncu
    /// sayısına göre ayarlanır.
    /// </summary>
    public void PrepareGridForRace(int playerCount)
    {
        maxGridSlots = Mathf.Max(1, playerCount);
        RacerCount = maxGridSlots;
        ShuffleGrid();
    }

    /// <summary>
    /// Bu yarıştaki YARIŞÇI sayısı (sabotajcı hariç). Lobiden Online Scene'e
    /// geçerken PrepareGridForRace ile ayarlanıyor ve NetworkManager
    /// DontDestroyOnLoad olduğu için sahne geçişinde korunuyor — skiller
    /// cooldown'larını buna göre ölçekliyor (bkz. CheckpointCooldownManager).
    /// </summary>
    public int RacerCount { get; private set; } = 1;

    public override void OnClientSceneChanged()
    {
        base.OnClientSceneChanged();

        if (LobbyManager.Instance != null)
            LobbyManager.Instance.HideLoadingScreen();
    }

    private IEnumerator SpawnPlayerWhenTrackReady(NetworkConnectionToClient conn)
    {
        const float timeout = 5f;
        float waited = 0f;
        Transform startLine = FindStartLine();

        while (startLine == null && waited < timeout)
        {
            yield return null;
            waited += Time.deltaTime;
            startLine = FindStartLine();
        }

        if (conn == null) yield break; // bekleme sırasında bağlantı koptuysa vazgeç

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (startLine != null)
        {
            spawnRot = startLine.rotation;
            spawnPos = GetGridSlotPosition(startLine, NextGridSlot());
        }
        else
        {
            Debug.LogWarning("[MyNetworkManager] Pist zaman aşımına uğradı (5sn), oyuncu origin'de spawn ediliyor.");
            spawnPos = Vector3.zero;
            spawnRot = Quaternion.identity;
        }

        GameObject player = Instantiate(playerPrefab, spawnPos, spawnRot);

        // Renk ataması varsa uygula — AddPlayerForConnection'dan ÖNCE, böylece
        // ilk network spawn paketiyle birlikte doğru renk gidiyor, client'larda
        // "önce beyaz sonra renkli" gibi bir sıçrama olmuyor.
        if (colorAssignments.TryGetValue(conn, out int colorIndex) &&
            player.TryGetComponent(out CarController carController))
        {
            carController.SetColorIndex(colorIndex);
        }

        NetworkServer.AddPlayerForConnection(conn, player);
    }

    private Transform FindStartLine()
    {
        CheckpointManager checkpointManager = FindAnyObjectByType<CheckpointManager>();
        if (checkpointManager != null && checkpointManager.checkpoints.Count > 0)
            return checkpointManager.checkpoints[0];
        return null;
    }

    private int NextGridSlot()
    {
        if (shuffledSlots == null || shuffledSlots.Count == 0)
            ShuffleGrid();

        int slot = shuffledSlots[nextSlotCursor % shuffledSlots.Count];
        nextSlotCursor++;
        return slot;
    }

    /// <summary>
    /// Gerçek F1 tarzı zigzag diziliş: her pozisyon bir öncekinden hem daha
    /// GERİDE hem de KARŞI TARAFTA. Slot 0 (pole) en önde solda, slot 1 ondan
    /// bir adım geride sağda, slot 2 daha da geride solda, vb.
    /// </summary>
    private Vector3 GetGridSlotPosition(Transform startLine, int slot)
    {
        int col = slot % 2; // 0 = sol, 1 = sağ

        float sideOffset = (col == 0 ? -1f : 1f) * (columnSpacing * 0.5f);
        float backOffset = -(slot + 1) * rowSpacing;

        Vector3 offset = startLine.right * sideOffset
                        + startLine.forward * backOffset
                        + Vector3.up * heightOffset;

        return startLine.position + offset;
    }
}
