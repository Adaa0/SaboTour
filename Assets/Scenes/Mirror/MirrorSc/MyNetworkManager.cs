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

    // ─── Steam Lobisi Temizliği ──────────────────────────────────
    // Oyun kapanınca/ana menüye dönünce Steam lobisinden de çıkılmalı —
    // yoksa arkadaşların Steam listesinde artık var olmayan bir oyuna
    // "Katıl" butonu görünmeye devam eder. Burada yapıyoruz ki geliştirici
    // her çıkış butonuna elle bağlamak zorunda kalmasın.
    // (SteamLobbyManager Editor'de zaten kendini kapattığı için burada
    // null kontrolü yeterli, ekstra platform kontrolü gerekmiyor.)

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (SteamLobbyManager.Instance != null)
            SteamLobbyManager.Instance.LeaveLobby();

        // Bu iki sözlük, ARTIK VAR OLMAYAN bağlantı nesnelerini anahtar
        // olarak tutuyor. Temizlenmezse bir sonraki oyunda ölü kayıtlar
        // birikiyor (şu an zararsız çünkü yeni bağlantılar yeni anahtar
        // oluyor, ama sızıntı ve ileride kafa karıştırıcı hata kaynağı).
        roleAssignments.Clear();
        colorAssignments.Clear();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        // Host'ta bu ikinci kez çağrılıyor (host = server + client), ama
        // LeaveLobby zaten "lobi yoksa hiçbir şey yapma" diye korumalı.
        if (SteamLobbyManager.Instance != null)
            SteamLobbyManager.Instance.LeaveLobby();

        // OYUN OTURUMU BİTTİ → lobi ekranını kullanılabilir hâle geri getir.
        // Bu olmadan ikinci bir oyun kurmak imkânsızdı (bkz.
        // LobbyManager.ResetToLobby açıklaması) — oyunu kapatıp yeniden
        // açmak gerekiyordu.
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.ResetToLobby();
    }

    #region Bağlantı Kopması Yönetimi
    // ─────────────────────────────────────────────────────────────────────
    // NEDEN HOST DEVRİ (host migration) YOK: Mirror'da host = server, yani
    // tüm yetkili durum (SyncVar'lar, netId'ler, checkpoint ilerlemesi,
    // roller, cooldown'lar) host'un sürecinde yaşıyor. Host çıkınca bunlar
    // onunla gidiyor; devretmek için tüm oyun durumunun yeni bir sunucuda
    // sıfırdan kurulması gerekirdi. Bunun yerine oyun DÜZGÜN ŞEKİLDE
    // sonlandırılıyor.
    // ─────────────────────────────────────────────────────────────────────

    [Header("Bağlantı Kopması")]
    [Tooltip("Bağlantı koptuğunda ekranda gösterilen mesajın süresi (saniye).")]
    [SerializeField] private float disconnectNoticeSeconds = 5f;

    // Oyuncu kendi isteğiyle mi çıkıyor (çıkış butonu), yoksa bağlantı mı
    // koptu? İkisini ayırmazsak, oyuncu "Çıkış"a bastığında da yüzüne
    // "bağlantı koptu" yazısı çıkardı.
    private bool leavingIntentionally;

    /// <summary>
    /// Çıkış/ana menü butonuna bağlanmak için. Şu an hiçbir butona bağlı
    /// DEĞİL (henüz öyle bir buton yok) — ayarlar/duraklatma menüsü
    /// yapıldığında buraya bağlanmalı, yoksa oyuncu kendi çıkışında
    /// "bağlantı koptu" uyarısı görür.
    /// </summary>
    public void LeaveGameIntentionally()
    {
        leavingIntentionally = true;

        if (NetworkServer.active && NetworkClient.isConnected) StopHost();
        else if (NetworkClient.isConnected) StopClient();
        else if (NetworkServer.active) StopServer();
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // ÖNEMLİ — SIRA KRİTİK: bu, base'den ÖNCE çalışmak zorunda.
        // base.OnServerDisconnect() bu bağlantının oyuncu objesini yok
        // ediyor; sonra çağırsaydık conn.identity null olurdu ve çıkan
        // kişinin sabotajcı mı yarışçı mı olduğunu anlayamazdık.
        HandleRaceDisconnect(conn);

        base.OnServerDisconnect(conn);

        // Lobi kontrolü ise base'den SONRA ve bir kare gecikmeli olmalı —
        // çıkan oyuncunun LobbyPlayer.AllLobbyPlayers listesinden düşmesini
        // beklememiz gerekiyor (bu, obje yok edilirken OnStopClient'ta
        // oluyor). Yoksa "herkes hazır mı" kontrolü hâlâ çıkmış oyuncuyu
        // sayar ve yarış hiç başlamaz.
        if (NetworkServer.active)
            StartCoroutine(RecheckLobbyReadyNextFrame());
    }

    /// <summary>
    /// Yarış sırasında biri çıkarsa oyunun kilitlenmemesi için gereken karar.
    /// Sadece Online Scene'de ve yarış sürerken anlamlı.
    /// </summary>
    [Server]
    private void HandleRaceDisconnect(NetworkConnectionToClient conn)
    {
        bool inGameScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == gameSceneName;
        if (!inGameScene) return;

        RacePodiumManager podium = FindAnyObjectByType<RacePodiumManager>();
        if (podium == null || !podium.RaceInProgress) return;

        bool wasSaboteur = conn.identity != null && conn.identity.GetComponent<SaboteurController>() != null;

        if (wasSaboteur)
        {
            // Yeni bir sabotajcı ATAMIYORUZ: oyunun ortasında bir yarışçıyı
            // kuleye ışınlamak, o oyuncuyu arabasından koparıp hiç
            // hazırlanmadığı bir role sokmak olurdu. Yarışçılar kazanmış
            // sayılıp yarış temiz şekilde bitiyor.
            podium.ServerForceEndRace(false, "sabotajcı oyundan ayrıldı");
            return;
        }

        // Çıkan bir yarışçıydı — geriye başka yarışçı kaldı mı?
        // ÖNEMLİ: çıkan oyuncunun objesi HENÜZ silinmedi (base'i sonra
        // çağırıyoruz), bu yüzden onu elle listeden düşüyoruz.
        int remainingRacers = 0;
        foreach (PlayerRaceController player in PlayerRaceController.AllPlayers)
        {
            if (player == null) continue;
            if (player.connectionToClient == conn) continue;
            remainingRacers++;
        }

        if (remainingRacers == 0)
        {
            // Sabotajcı boş bir pistte 270 saniye beklemesin.
            podium.ServerForceEndRace(true, "son yarışçı da oyundan ayrıldı");
        }
    }

    /// <summary>
    /// Lobide biri çıkarsa, KALANLAR zaten hazırsa yarış başlasın. Bu
    /// olmadan: 3 kişilik lobide 2 kişi hazır olur, 3. kişi çıkar ve kalan
    /// ikisi sonsuza kadar bekler (hazır durumunu kapatıp açmadan yarış
    /// hiç başlamaz).
    /// </summary>
    private IEnumerator RecheckLobbyReadyNextFrame()
    {
        yield return null;

        if (!NetworkServer.active) yield break;

        bool inGameScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == gameSceneName;
        if (inGameScene) yield break;

        LobbyPlayer.ServerRecheckAllReady();
    }

    public override void OnClientDisconnect()
    {
        // NetworkServer.active hâlâ true ise bu makine HOST'tur (Mirror
        // StopHost'ta önce client'ı, sonra server'ı kapatıyor). Host'a
        // "host oyundan ayrıldı" demek saçma olurdu.
        bool isHost = NetworkServer.active;

        if (!isHost && !leavingIntentionally)
            ScreenNotice.Show("Bağlantı koptu.\nHost oyundan ayrılmış olabilir.", disconnectNoticeSeconds);

        leavingIntentionally = false;

        // base, offlineScene'i (ana menü) yüklüyor. ScreenNotice
        // DontDestroyOnLoad olduğu için mesaj sahne geçişinde kaybolmuyor.
        base.OnClientDisconnect();
    }

    #endregion

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

        // Yarış başlıyor — Steam lobisini kilitle. Kilitlenmezse bir arkadaş
        // yarışın ortasında "Oyuna Katıl" diyebilir; rol ataması lobide
        // yapıldığı için o oyuncunun rolü hiç olmaz, varsayılan yarışçı
        // sayılıp yarışın ortasında başlangıç çizgisinde spawn olur.
        if (SteamLobbyManager.Instance != null)
            SteamLobbyManager.Instance.SetLobbyJoinable(false);
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
