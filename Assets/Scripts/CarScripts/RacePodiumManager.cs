using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;

/// <summary>
/// SAHNEYE ELLE YERLEŞTİRİLECEK TEK OBJE (DriftTrap/CheckpointCooldownManager
/// ile aynı desen — bir NetworkIdentity + bu component, Online Scene'de bir
/// kere var olur, spawn edilmez). Ekledikten sonra CTRL+S ile sahneyi kaydet
/// (sceneId ataması için).
///
/// NE İŞE YARAR:
/// 1. SABOTAJCI KAZANMA SÜRESİ — yarışçı sayısına göre değişen bir geri
///    sayım tutar (raceTimeLimitByRacerCount). Süre dolana kadar hiçbir
///    yarışçı turlarını bitirmemişse sabotajcı kazanır.
/// 2. YARIŞÇI KAZANMA — PlayerRaceController.OnPlayerFinishedRace event'ini
///    dinler, herhangi bir yarışçı bitirince yarışçılar kazanır.
/// 3. PODYUM — kazanan taraf belli olunca TÜM client'larda podyum
///    kolonlarını/kamerasını aktive eder: yarışçılar kazandıysa her
///    yarışçı için bir kolon (rank sırasına göre), sabotajcı kazandıysa
///    tek bir sabotajcı kolonu.
///
/// NETWORK NOTU: yarış sonucu SADECE tek bir "raceOutcome" SyncVar'ı
/// üzerinden yayılıyor. Podyum kolonlarının/kamera noktalarının pozisyonları
/// SAHNE objeleri olduğu için (network mesajı gerekmez) her client zaten
/// kendi kopyasında aynı yerde duruyor — sadece "hangi kolonu aktif et,
/// kamerayı nereye koy" kararını bu SyncVar'dan okuyoruz.
/// </summary>
public class RacePodiumManager : NetworkBehaviour
{
    [Header("Sabotajcı Kazanma Süresi (Yarışçı Sayısına Göre)")]
    [Tooltip("index 0 = 1 yarışçı, index 1 = 2 yarışçı, ... index 4 = 5 yarışçı. Bu süre (saniye) dolup hiçbir yarışçı bitirmemişse sabotajcı kazanır.")]
    [SerializeField] private float[] raceTimeLimitByRacerCount = { 300f, 290f, 280f, 275f, 270f };

    [Header("Podyum - Yarışçı Kolonları (en fazla 5, sırayla)")]
    [Tooltip("Her kolonun görünür/aktif GameObject'i. Yarışçı sayısı kadarı baştan itibaren aktif edilir, gerisi kapalı kalır.")]
    [SerializeField] private GameObject[] racerColumnVisuals = new GameObject[5];
    [Tooltip("Her kolonun ÜSTÜNDE, arabanın ışınlanacağı boş GameObject (spawn noktası). racerColumnVisuals ile aynı sırada olmalı.")]
    [SerializeField] private Transform[] racerColumnSpawnPoints = new Transform[5];

    [Header("Podyum Kamerası")]
    [Tooltip("Podyum alanına sabit yerleştirilmiş, tüm sütunları gören ayrı bir Camera (+ AudioListener). Konumu SABİT kalıyor, script sadece açıp kapatıyor. Başlangıçta KAPALI olmalı.")]
    [SerializeField] private Camera podiumCamera;

    // ─── SESLER ──────────────────────────────────────────────────────────
    // 2D (PlayUI) çalıyorlar: bunlar dünyada bir yerden gelen sesler değil,
    // "sen kazandın / kaybettin" bildirimi. Hangi klibin çalacağı HER
    // CLIENT'ta ayrı hesaplanıyor — aynı anda sabotajcı zafer sesini,
    // yarışçılar yenilgi sesini duyabiliyor.
    [Header("Sesler")]
    [Tooltip("Yarışı KAZANAN tarafın duyduğu zafer sesi/fanfarı.")]
    [SerializeField] private AudioClip victoryClip;
    [Tooltip("KAYBEDEN tarafın duyduğu ses.")]
    [SerializeField] private AudioClip defeatClip;
    [Range(0f, 1f)][SerializeField] private float podiumVolume = 0.9f;

    // NEDEN TEK SyncVar (İKİ AYRI bool DEĞİL): Mirror, SyncVar'ları tanımlanma
    // sırasına göre deserialize ediyor ve her birinin hook'unu O ANDA
    // tetikliyor. Eskiden raceEnded+saboteurWon iki ayrı SyncVar'dı; client'ta
    // raceEnded deserialize olur olmaz hook (ActivatePodiumLocally) hemen
    // çalışıyordu ama saboteurWon aynı paketin İÇİNDE henüz sırası gelmediği
    // için eski/varsayılan değerinde kalabiliyordu — server doğru görürken
    // uzak client'lar yanlış tarafı kazanan sanıyordu (gerçek test edilmiş
    // bug). Tek bir alan olunca "kısmi güncel" bir okuma ihtimali kalmıyor.
    private enum RaceOutcome { Ongoing = 0, RacersWon = 1, SaboteurWon = 2 }

    [SyncVar(hook = nameof(OnRaceOutcomeChanged))]
    private RaceOutcome raceOutcome = RaceOutcome.Ongoing;

    private float elapsed;

    // ═══════════════════════════════════════════════════════════════════
    //  YARIŞ BAŞLANGICI — GERİ SAYIM (23 Ağustos 2026)
    // ═══════════════════════════════════════════════════════════════════
    //
    // 🚨 DÜZELTİLEN BUG: İki saat AYRI ANLARDA başlıyordu.
    //   • Sabotajcının kazanma süresi (`elapsed`) sahne yüklenir yüklenmez,
    //     `OnStartServer`'da başlıyordu.
    //   • Yarışçının süresi (`PlayerRaceController.totalTime`) ise ancak
    //     checkpoint 0'ı GEÇİNCE başlıyordu.
    // Aradaki fark (spawn + araçların yere oturması + başlangıç çizgisine
    // kadar sürme) yarışçının aleyhineydi: sabotajcının saati çoktan
    // işlemeye başlamış oluyordu. Artık İKİSİ DE aynı ana bağlı.
    //
    // ZAMAN `NetworkTime.time` İLE TAŞINIYOR, `Time.time` ile DEĞİL:
    // `Time.time` her makinede farklı (uygulamanın açılışından beri geçen
    // süre). Karşıya geçen her zaman değeri NetworkTime olmalı — aynı ders
    // SkillCooldownState'te de yazılı.

    [Header("Yarış Başlangıcı")]
    [Tooltip("Herkes doğduktan sonra kaç saniye geri sayılsın (3 = '3, 2, 1, BAŞLA').")]
    [SerializeField] private float countdownSeconds = 3f;

    [Tooltip("Tüm yarışçıların doğması bu kadar saniyede tamamlanmazsa geri sayım yine de başlar — pist üretimi takılırsa yarış sonsuza kadar beklemesin.")]
    [SerializeField] private float maxSpawnWaitSeconds = 8f;

    [Header("Pist Bilgisi Bildirimi")]
    [Tooltip("Geri sayım ('3, 2, 1') başlamadan ÖNCE, pist uzunluğu + tahmini " +
             "süreleri gösteren bir bilgi ekranı çıkar — bu kaç saniye ekranda kalsın.\n\n" +
             "NEDEN GEREKLİ: pist prosedürel üretildiği için uzunluğu her yarışta " +
             "değişiyor (600 pistlik ölçümde ~2900-3900 m arası). Sabotajcının " +
             "kazanma süresi ise SABİT (yarışçı sayısına göre, uzunluğa göre DEĞİL) " +
             "— yani hangi pist düştüğü, ekstra bilgi olmadan, yarışçıya görünmez " +
             "bir zorluk farkı gibi hissettiriyordu. Bu ekran o farkı görünür " +
             "kılıyor: 'bu sefer pay az, acele et' ya da 'bu sefer rahatsın'.")]
    [SerializeField] private float trackInfoNoticeSeconds = 4f;

    [Tooltip("Ekrandaki 'saf tur tahmini' bu hıza göre hesaplanıyor. 24 Ağustos " +
             "2026'da 5 gerçek pistte ölçülen ilk-tur ortalaması: 199-217 km/h " +
             "(araç azamisinin %90-98'i, ortalama %94.9). Gerçek oyuncu davranışı " +
             "değişirse (yeni oyuncular, farklı araç ayarları) bu değeri playtest " +
             "sonuçlarına göre güncelle — tahminin doğruluğu buna bağlı.")]
    [SerializeField] private float estimatedAvgLapSpeedKmh = 209f;

    // Her yarışta YENİ bir RacePodiumManager sahneye yükleniyor (Online
    // Scene her seferinde baştan açılıyor), yani bu alan ROL İPUCU'ndaki
    // gibi static OLMAK ZORUNDA DEĞİL — doğal olarak her yarışta sıfırlanıyor.
    private bool trackInfoShown;

    /// <summary>
    /// Yarışın fiilen başlayacağı an (NetworkTime cinsinden). -1 = henüz
    /// belirlenmedi (oyuncular doğmayı bekliyor).
    /// </summary>
    [SyncVar]
    private double raceStartTime = -1d;

    /// <summary>
    /// BU MAKİNEDE yarış fiilen başladı mı? Server ve client'lar aynı
    /// `raceStartTime`e baktığı için ikisi de aynı anda true oluyor.
    /// Yarışçının süresi de sabotajcının süresi de buna bağlı.
    /// </summary>
    public static bool RaceStarted { get; private set; }

    /// <summary>
    /// Geri sayım sürüyor mu — yani araçların girdisi kilitli mi?
    /// Sahnede RacePodiumManager YOKSA (fotoğraf sahnesi, test) false kalır
    /// ve hiçbir aracı kilitlemez.
    /// </summary>
    public static bool StartLockActive { get; private set; }

    /// <summary>
    /// Geri sayım KURULDU mu — yani sunucu tüm yarışçıların doğduğunu gördü mü.
    /// `LobbyManager` yükleme ekranını buna bakarak kapatıyor: hızlı yüklenen
    /// oyuncu, yavaş yükleneni beklesin ve ikisi de aynı anda "3"ü görsün.
    /// </summary>
    public static bool CountdownArmed { get; private set; }

    /// <summary>
    /// Sahnedeki tekil örnek — HUD (RaceHud) buradan durum okuyor.
    /// </summary>
    public static RacePodiumManager Instance { get; private set; }

    /// <summary>
    /// Sabotajcının kazanma süresi, client'lara açık hâli.
    ///
    /// 🚨 `elapsed` HER KARE SENKRONLANMIYOR — gereksiz ağ trafiği olurdu.
    /// Bunun yerine sadece süre SINIRI bir kere yayınlanıyor; kalan süreyi
    /// her makine `raceStartTime` (zaten SyncVar) üzerinden KENDİSİ
    /// hesaplıyor. Geri sayımdaki numaranın aynısı: tek bir zaman damgası
    /// yayınlayıp gerisini yerel olarak hesaplamak.
    /// </summary>
    [SyncVar]
    private float syncedTimeLimit = -1f;

    /// <summary>
    /// Yarışın bitmesine kalan saniye. Süre sınırı yoksa ya da yarış henüz
    /// başlamadıysa -1. HUD bunu okuyup çubuğu çiziyor.
    /// </summary>
    public float RemainingSeconds
    {
        get
        {
            if (syncedTimeLimit <= 0f || raceStartTime < 0d) return -1f;
            if (raceOutcome != RaceOutcome.Ongoing) return 0f;

            double passed = NetworkTime.time - raceStartTime;
            if (passed < 0d) return syncedTimeLimit;   // geri sayım sürüyor

            return Mathf.Max(0f, syncedTimeLimit - (float)passed);
        }
    }

    /// <summary>Toplam yarış süresi (çubuğun doluluk oranını hesaplamak için).</summary>
    public float TimeLimit => syncedTimeLimit;

    /// <summary>Yarış bitti mi — HUD "Tekrar Oyna" butonunu buna göre gösteriyor.</summary>
    public bool RaceOver => raceOutcome != RaceOutcome.Ongoing;

    // Client'ta ekranda en son gösterilen sayı (3, 2, 1). Aynı sayıyı her
    // karede tekrar yazmamak için.
    private int lastShownCount = int.MinValue;

    // Server: oyuncuların doğmasını beklemeye başladığımız an.
    private float spawnWaitStarted = -1f;

    // Süre sınırı yarış BAŞINDA bir kere hesaplanıp donduruluyor.
    //
    // ÖNCEDEN NE YANLIŞTI: limit her karede `PlayerRaceController.AllPlayers.
    // Count`tan yeniden okunuyordu. Bir yarışçı oyundan çıkınca o liste
    // küçülüyor ve süre sınırı yarışın ORTASINDA değişiyordu (3 yarışçıyla
    // başlayan 280sn'lik yarış, biri çıkınca 290sn'ye uzuyordu). Artık
    // MyNetworkManager.RacerCount kullanılıyor — o değer lobide, roller
    // dağıtılırken bir kere belirleniyor ve yarış boyunca sabit.
    private float raceTimeLimit = -1f;

    /// <summary>Yarış hâlâ sürüyor mu? MyNetworkManager, bağlantı koptuğunda buna bakıp erken bitirme gerekip gerekmediğine karar veriyor.</summary>
    public bool RaceInProgress => raceOutcome == RaceOutcome.Ongoing;

    void Awake()
    {
        // Statik bayrakları temiz başlat. Static olmak ZORUNDALAR (araba ve
        // sabotajcı her yarışta yeniden doğuyor, instance alanı hatırlamaz)
        // ama static olan her şey bir önceki yarıştan kirli gelebilir.
        Instance = this;

        RaceStarted = false;
        CountdownArmed = false;
        StartLockActive = true;   // geri sayım kurulana kadar araçlar kilitli
        lastShownCount = int.MinValue;
    }

    void OnDestroy()
    {
        // Sahneden çıkarken kilidi MUTLAKA bırak — yoksa lobiye ya da
        // fotoğraf sahnesine dönüldüğünde araçlar sonsuza kadar kilitli
        // kalırdı. (Bu projede "static bayrak bir sonraki oturuma sızdı"
        // hatası daha önce yaşandı.)
        RaceStarted = false;
        CountdownArmed = false;
        StartLockActive = false;

        if (Instance == this) Instance = null;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TEKRAR OYNA (23 Ağustos 2026)
    // ═══════════════════════════════════════════════════════════════════

    [Header("Tekrar Oyna")]
    [Tooltip("Yarış bitince lobiye dönüş için kullanılacak sahne yolu. MyNetworkManager'ın Offline Scene'i ile aynı olmalı.")]
    [SerializeField] private string lobbyScenePath = "Assets/Scenes/Mirror/Offline Scene.unity";

    // Butona arka arkaya basılmasını engelliyor — ServerChangeScene bir kaç
    // kare sürüyor ve ikinci çağrı sahne yüklemesini yarıda kesebilir.
    private bool returningToLobby;

    /// <summary>Host butonunun görünmesi için: yarış bitti ve henüz dönüş başlamadı.</summary>
    public bool CanReturnToLobby => RaceOver && !returningToLobby;

    /// <summary>
    /// GRUBU BOZMADAN LOBİYE DÖNER — "Tekrar Oyna".
    ///
    /// ─── NEDEN GEREKLİ ────────────────────────────────────────────────
    /// Yarış bitince herkes ana menüye düşüyordu; host yeniden oyun kurmak,
    /// diğerleri yeniden katılmak zorundaydı. 4 kişilik bir grupta bu her
    /// turda bir dakikadan fazla sürtünme ve genelde birileri düşüyor.
    /// Party oyununda oturum başına oynanan tur sayısı doğrudan buna bağlı.
    ///
    /// ─── NEDEN SADECE HOST ÇAĞIRIYOR ──────────────────────────────────
    /// Host aynı zamanda SUNUCU, yani bu metodu doğrudan çağırabiliyor —
    /// Command'a, dolayısıyla oyuncu objesi üzerinden bir yetki zincirine
    /// gerek kalmıyor. Client'larda buton hiç gösterilmiyor.
    ///
    /// ─── NASIL ÇALIŞIYOR ──────────────────────────────────────────────
    /// `ServerChangeScene(lobi)` — oturumu KAPATMIYOR, sadece sahneyi
    /// değiştiriyor. Client'lar lobi sahnesini yükleyip yeniden "ready"
    /// oluyor, Mirror da her bağlantı için `OnServerAddPlayer`'ı tekrar
    /// çağırıyor. Oyun sahnesinde olmadığımız için orası `SpawnLobbyPlayer`
    /// dalına giriyor — yani herkes yeni bir LobbyPlayer alıyor, hazır
    /// durumları sıfırdan başlıyor ve roller bir sonraki yarışta yeniden
    /// dağıtılıyor.
    ///
    /// ⚠️ `StopHost()` ya da `SceneManager.LoadScene` ile DEĞİL: ilki
    /// oturumu kapatır (herkes düşer), ikincisi Mirror'ı baypas eder ve
    /// spawn edilmiş objeler ortada kalır.
    /// </summary>
    [Server]
    public void ServerReturnToLobby()
    {
        if (returningToLobby) return;
        returningToLobby = true;

        Debug.Log("[RacePodium] Tekrar oyna — lobiye dönülüyor.");

        // Yarış başlarken kilitlenen Steam lobisini tekrar aç: gruba yeni
        // biri katılmak isterse turlar arasında girebilsin.
        if (SteamLobbyManager.Instance != null)
            SteamLobbyManager.Instance.SetLobbyJoinable(true);

        NetworkManager.singleton.ServerChangeScene(lobbyScenePath);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        elapsed = 0f;
        raceOutcome = RaceOutcome.Ongoing;
        raceTimeLimit = ResolveRaceTimeLimit();
        syncedTimeLimit = raceTimeLimit;   // HUD çubuğu bunu okuyor
        PlayerRaceController.OnPlayerFinishedRace += HandleRacerFinished;
    }

    [Server]
    private float ResolveRaceTimeLimit()
    {
        if (raceTimeLimitByRacerCount == null || raceTimeLimitByRacerCount.Length == 0)
        {
            Debug.LogWarning("[RacePodium] Race Time Limit By Racer Count dizisi BOŞ — sabotajcının süreyle kazanması devre dışı kaldı.");
            return -1f;
        }

        int racerCount = (NetworkManager.singleton as MyNetworkManager)?.RacerCount ?? 1;
        racerCount = Mathf.Clamp(racerCount, 1, raceTimeLimitByRacerCount.Length);

        float limit = raceTimeLimitByRacerCount[racerCount - 1];
        Debug.Log($"[RacePodium] Yarışçı sayısı {racerCount} → sabotajcı kazanma süresi {limit}sn (yarış boyunca sabit).");
        return limit;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        PlayerRaceController.OnPlayerFinishedRace -= HandleRacerFinished;
    }

    /// <summary>
    /// GÜNCEL KURAL: yarışçılar KAZANMAK için TÜMÜ turlarını bitirmiş olmalı —
    /// ilk bitiren tek başına yeterli DEĞİL (eski davranış buydu).
    ///
    /// NEDEN DEĞİŞTİ (bug fix'i de içeriyor): ilk bitiren anında podyumu
    /// tetiklediğinde, henüz bitirmemiş diğer yarışçılar da podyuma ZORLA
    /// ışınlanıyordu ama onların `isRacing` bayrağı hâlâ true kalıyordu
    /// (sadece sabotajcı-süre-doldu senaryosunda ServerStopForRaceEnd()
    /// çağrılıyordu, bu senaryoda hiç çağrılmıyordu). CheckpointRecovery
    /// sistemi bunu "hâlâ yarışıyor ama pistten (podyumdan!) çok uzakta"
    /// sanıp birkaç saniye sonra onları checkpoint'e geri ışınlıyordu —
    /// podyum yarım kalıyordu. Artık podyum ancak SON kişi bitirdiğinde
    /// açıldığı için, o ana kadar herkesin isRacing'i zaten kendi bitirişiyle
    /// false olmuş oluyor, çakışma ihtimali kalmıyor.
    /// </summary>
    [Server]
    private void HandleRacerFinished(PlayerRaceController finisher)
    {
        bool allFinished = PlayerRaceController.AllPlayers.Count > 0 &&
                            PlayerRaceController.AllPlayers.All(p => p != null && p.HasFinished);

        if (allFinished)
            ServerEndRace(bySaboteur: false);
    }

    void Update()
    {
        // ── SERVER: geri sayımı kur, sonra yarış saatini işlet ──
        if (isServer && raceStartTime < 0d)
            ServerTryArmCountdown();

        // ── HER MAKİNE: geri sayım bitti mi? ──
        UpdateStartState();

        if (!isServer || raceOutcome != RaceOutcome.Ongoing) return;

        // Sabotajcının kazanma süresi ANCAK yarış başlayınca işliyor —
        // eskiden sahne yüklenir yüklenmez işlemeye başlıyordu ve yarışçı
        // henüz başlangıç çizgisine bile varmamış oluyordu.
        if (!RaceStarted) return;

        elapsed += Time.deltaTime;

        if (raceTimeLimit > 0f && elapsed >= raceTimeLimit)
            ServerEndRace(bySaboteur: true);
    }

    /// <summary>
    /// Tüm yarışçılar doğduğunda (ya da bekleme süresi dolduğunda) geri
    /// sayımı başlatır. Beklemek şart: araçlar `SpawnPlayerWhenTrackReady`
    /// ile pist hazır olana kadar bekletiliyor, yani sahne yüklenir yüklenmez
    /// ortada araç olmayabiliyor.
    /// </summary>
    [Server]
    private void ServerTryArmCountdown()
    {
        if (spawnWaitStarted < 0f) spawnWaitStarted = Time.time;

        int expected = (NetworkManager.singleton as MyNetworkManager)?.RacerCount ?? 1;
        int spawned = 0;

        foreach (PlayerRaceController p in PlayerRaceController.AllPlayers)
            if (p != null) spawned++;

        bool everyoneHere = spawned > 0 && spawned >= expected;
        bool waitedTooLong = Time.time - spawnWaitStarted >= maxSpawnWaitSeconds;

        if (!everyoneHere && !waitedTooLong) return;

        // Toplam bekleme: önce pist bilgisi ekranı, sonra "3-2-1". İkisi
        // ayrı pencereler — UpdateStartState bunları `remaining`in
        // countdownSeconds'ın üstünde/altında olmasına göre ayırıyor.
        raceStartTime = NetworkTime.time + Mathf.Max(0f, trackInfoNoticeSeconds) + Mathf.Max(0f, countdownSeconds);

        Debug.Log($"[RacePodium] Geri sayım başladı — {spawned}/{expected} yarışçı hazır, " +
                  $"{trackInfoNoticeSeconds}sn pist bilgisi + {countdownSeconds}sn geri sayım." + (waitedTooLong ? " (bekleme süresi doldu)" : ""));
    }

    /// <summary>
    /// Geri sayımı ekranda gösterir ve bittiğinde `RaceStarted`'ı açar.
    /// SERVER'DA DA ÇALIŞIYOR — host aynı zamanda bir oyuncu ve onun da
    /// saatinin aynı anda başlaması gerekiyor.
    /// </summary>
    private void UpdateStartState()
    {
        if (raceStartTime < 0d)
        {
            // Henüz kurulmadı: araçlar kilitli beklesin ki geri sayım
            // gelmeden kimse hareket etmesin.
            StartLockActive = true;
            RaceStarted = false;
            CountdownArmed = false;
            return;
        }

        CountdownArmed = true;

        double remaining = raceStartTime - NetworkTime.time;

        if (remaining <= 0d)
        {
            if (!RaceStarted)
            {
                RaceStarted = true;
                StartLockActive = false;
                ScreenNotice.Show("BAŞLA!", 1.2f);
            }

            return;
        }

        StartLockActive = true;
        RaceStarted = false;

        // İLK PENCERE: "3-2-1" henüz başlamadı, pist bilgisi ekranındayız.
        // Bir kez gösterip sıradaki kareler için hemen çıkıyoruz — `shown`
        // hesaplamasına (aşağıda) hiç girmiyoruz, yoksa büyük bir sayıdan
        // ("7" gibi) geri saymaya başlardı.
        if (remaining > countdownSeconds)
        {
            if (!trackInfoShown)
            {
                trackInfoShown = true;
                ShowTrackInfoNotice();
            }
            return;
        }

        // İKİNCİ PENCERE: 3 → 2 → 1. Aynı sayıyı her karede yeniden
        // yazmıyoruz; ScreenNotice yeni mesajı eskisinin yerine koyduğu
        // için sayaç kendiliğinden güncelleniyor.
        int shown = Mathf.CeilToInt((float)remaining);
        if (shown == lastShownCount) return;

        lastShownCount = shown;
        ScreenNotice.Show(shown.ToString(), 1.1f);
    }

    /// <summary>
    /// Pist uzunluğu + tahmini süreleri gösteren bilgi ekranı.
    ///
    /// 🚨 RPC YOK — "3-2-1" sayacıyla AYNI FELSEFE: hiçbir veri ağdan
    /// gönderilmiyor, her client bunu KENDİSİ hesaplıyor. Pist zaten synced
    /// seed'den deterministik üretildiği için (bkz. TrackSeedSync) tüm
    /// client'larda birebir aynı uzunluk çıkıyor; `TimeLimit` de zaten
    /// SyncVar. Yani hesaplamak için ağa hiç ihtiyaç yok.
    /// </summary>
    private void ShowTrackInfoNotice()
    {
        TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
        List<Vector3> points = trackGenerator != null ? trackGenerator.GetTrackPoints() : null;

        // Pist henüz üretilmemişse (olmaması gereken bir durum) sessizce
        // geç — bu bilgi ekranı kritik değil, gösterilmemesi yarışı bozmaz.
        if (points == null || points.Count < 3) return;

        float lengthMeters = 0f;
        for (int i = 0; i < points.Count; i++)
            lengthMeters += Vector3.Distance(points[i], points[(i + 1) % points.Count]);

        int laps = 3;
        foreach (PlayerRaceController p in PlayerRaceController.AllPlayers)
        {
            if (p == null) continue;
            laps = p.maxLaps;
            break;
        }

        float pureLapSeconds = lengthMeters / (estimatedAvgLapSpeedKmh / 3.6f);
        float limit = TimeLimit;
        float targetLapSeconds = laps > 0 ? limit / laps : 0f;
        float margin = limit - pureLapSeconds * laps;

        string message =
            $"PİST: {lengthMeters / 1000f:F1} km\n" +
            $"Saf tur tahmini ~{pureLapSeconds:F0} sn · süre sınırı {limit:F0} sn\n" +
            $"Hedef ortalama tur: {targetLapSeconds:F0} sn (pay {margin:F0} sn)";

        ScreenNotice.Show(message, trackInfoNoticeSeconds);
    }

    /// <summary>
    /// Yarışı süre dolmadan, dışarıdan gelen bir sebeple bitirir — şu an
    /// tek kullanıcısı MyNetworkManager.OnServerDisconnect (biri oyundan
    /// çıktığında). Normal bitişlerden (süre dolması / yarışçının turları
    /// tamamlaması) ayrı bir giriş noktası olması, sebebin log'a
    /// yazılabilmesi ve ileride farklı davranış gerekirse tek yerden
    /// değiştirilebilmesi için.
    /// </summary>
    [Server]
    public void ServerForceEndRace(bool saboteurWins, string reason)
    {
        if (raceOutcome != RaceOutcome.Ongoing) return;

        Debug.Log($"[RacePodium] Yarış erken bitirildi — sebep: {reason} (kazanan: {(saboteurWins ? "sabotajcı" : "yarışçılar")})");

        // Kalan oyunculara SEBEBİ de söyle. Bu olmadan podyum bir anda
        // açılıyor ve oyuncu neden kazandığını/kaybettiğini anlamıyor —
        // "oyun bozuldu" izlenimi veriyordu.
        RpcAnnounceEarlyEnd(saboteurWins
            ? "Tüm yarışçılar oyundan ayrıldı.\nYarış sona erdi."
            : "Sabotajcı oyundan ayrıldı.\nYarış sona erdi.");

        ServerEndRace(saboteurWins);
    }

    [ClientRpc]
    private void RpcAnnounceEarlyEnd(string message)
    {
        ScreenNotice.Show(message, 6f);
    }

    [Server]
    private void ServerEndRace(bool bySaboteur)
    {
        if (raceOutcome != RaceOutcome.Ongoing) return;

        raceOutcome = bySaboteur ? RaceOutcome.SaboteurWon : RaceOutcome.RacersWon;

        if (bySaboteur)
        {
            // Süre dolduğunda hâlâ yarışan (bitirmemiş) yarışçıları durdur —
            // yoksa checkpoint/timer işlemeye devam ederlerdi.
            foreach (var p in PlayerRaceController.AllPlayers)
            {
                if (p != null && p.isRacing)
                    p.ServerStopForRaceEnd();
            }
        }
    }

    // ─── Podyum Aktivasyonu (HER client'ta, hook üzerinden) ──────────────

    private void OnRaceOutcomeChanged(RaceOutcome oldValue, RaceOutcome newValue)
    {
        if (newValue != RaceOutcome.Ongoing)
            ActivatePodiumLocally();
    }

    private void ActivatePodiumLocally()
    {
        var ordered = PlayerRaceController.AllPlayers
            .Where(p => p != null)
            .OrderByDescending(p => p.CurrentLap)
            .ThenByDescending(p => p.CurrentCheckpoint)
            .ThenBy(p => p.TotalTime)
            .ToList();

        Debug.Log($"[RacePodium] ActivatePodiumLocally: raceOutcome={raceOutcome}, ordered.Count={ordered.Count}, localPlayer={(NetworkClient.localPlayer != null ? NetworkClient.localPlayer.name : "NULL")}");

        if (raceOutcome == RaceOutcome.SaboteurWon)
        {
            // Ayrı bir "sabotajcı kolonu" YOK — 1. sütun (racerColumnVisuals[0])
            // iki amaca da hizmet ediyor: yarışçılar kazanınca 1.'nin kolonu,
            // sabotajcı kazanınca sabotajcının tek başına durduğu kolon.
            SetRacerColumnsActive(1);
            TeleportLocalSaboteur();
        }
        else
        {
            int count = Mathf.Min(ordered.Count, racerColumnVisuals.Length);
            SetRacerColumnsActive(count);

            int myRank = ordered.FindIndex(p => p != null && p.isOwned);
            Debug.Log($"[RacePodium] myRank={myRank} (count={count})");
            if (myRank >= 0) TeleportLocalRacer(myRank);
        }

        SwitchToPodiumCamera();
        PlayOutcomeSound();
    }

    /// <summary>
    /// Bu makinedeki oyuncu kazanan tarafta mı, ona göre zafer ya da yenilgi
    /// sesi çalar. Rolü, local oyuncu objesinin üzerindeki component'ten
    /// anlıyoruz: sabotajcıda SaboteurController, yarışçıda CarController var
    /// (ayrı bir "rol" SyncVar'ı okumaya gerek yok).
    /// </summary>
    private void PlayOutcomeSound()
    {
        var localIdentity = NetworkClient.localPlayer;
        if (localIdentity == null) return;

        bool isSaboteur = localIdentity.GetComponent<SaboteurController>() != null;
        bool iWon = (raceOutcome == RaceOutcome.SaboteurWon) == isSaboteur;

        SfxPlayer.PlayUI(iWon ? victoryClip : defeatClip, podiumVolume);
    }

    private void SetRacerColumnsActive(int count)
    {
        for (int i = 0; i < racerColumnVisuals.Length; i++)
        {
            if (racerColumnVisuals[i] != null)
                racerColumnVisuals[i].SetActive(i < count);
        }
    }

    private void TeleportLocalRacer(int rankIndex)
    {
        if (rankIndex < 0 || rankIndex >= racerColumnSpawnPoints.Length)
        {
            Debug.LogWarning($"[RacePodium] TeleportLocalRacer: rankIndex ({rankIndex}) dizinin dışında (racerColumnSpawnPoints.Length={racerColumnSpawnPoints.Length}).");
            return;
        }
        if (racerColumnSpawnPoints[rankIndex] == null)
        {
            Debug.LogWarning($"[RacePodium] TeleportLocalRacer: racerColumnSpawnPoints[{rankIndex}] Inspector'da ATANMAMIŞ (null).");
            return;
        }

        var localIdentity = NetworkClient.localPlayer;
        if (localIdentity == null)
        {
            Debug.LogWarning("[RacePodium] TeleportLocalRacer: NetworkClient.localPlayer NULL.");
            return;
        }

        var car = localIdentity.GetComponent<CarController>();
        if (car == null)
        {
            Debug.LogWarning($"[RacePodium] TeleportLocalRacer: localPlayer ({localIdentity.name}) üzerinde CarController YOK.");
            return;
        }

        // Bu oyuncu yarışı erken bitirip izleyici moduna geçmiş olabilir —
        // o haldeyken arabası görünmez ve Rigidbody'si kinematic. Işınlamadan
        // ÖNCE normale döndürmezsek araba podyumda ya görünmez kalır ya da
        // kolonun üstüne düşmez. RacerSpectator zaten podyumun açıldığını
        // kendi de fark ediyor ama o bir sonraki karede olurdu; burada açıkça
        // çağırmak sırayı garantiliyor (metod idempotent, iki kere çağrılması
        // zararsız).
        localIdentity.GetComponent<RacerSpectator>()?.ExitSpectatorMode();

        Debug.Log($"[RacePodium] Araba ışınlanıyor -> {racerColumnSpawnPoints[rankIndex].position}");
        car.TeleportTo(racerColumnSpawnPoints[rankIndex].position, racerColumnSpawnPoints[rankIndex].rotation);
        car.FreezeForRaceEnd();
    }

    private void TeleportLocalSaboteur()
    {
        // Sabotajcı kazanınca da 1. sütunun spawn noktası kullanılıyor
        // (ayrı bir sabotajcı spawn noktası yok, aynı kolon her iki senaryoda
        // da paylaşılıyor).
        if (racerColumnSpawnPoints.Length == 0 || racerColumnSpawnPoints[0] == null) return;

        var localIdentity = NetworkClient.localPlayer;
        if (localIdentity == null) return;

        var saboteur = localIdentity.GetComponent<SaboteurController>();
        if (saboteur == null) return;

        saboteur.TeleportTo(racerColumnSpawnPoints[0].position, racerColumnSpawnPoints[0].rotation);
        saboteur.FreezeForRaceEnd();
    }

    /// <summary>
    /// Kamera SABİT — sadece açıp kapatıyoruz, pozisyonuna dokunmuyoruz
    /// (podyum sahnede tek, değişmeyen bir yerde duruyor, tüm sütunları
    /// zaten görecek şekilde elle konumlandırıldı).
    /// </summary>
    private void SwitchToPodiumCamera()
    {
        var localIdentity = NetworkClient.localPlayer;
        if (localIdentity != null)
        {
            var carCamActivator = localIdentity.GetComponent<CarCameraActivator>();
            if (carCamActivator != null) carCamActivator.SetCarCamActive(false);

            var saboteur = localIdentity.GetComponent<SaboteurController>();
            if (saboteur != null) saboteur.HideCameraForPodium();
        }

        // Online Scene'deki sabit Main Camera (CinemachineBrain'in üzerinde
        // durduğu kamera) hâlâ etkinse podyum kamerasıyla üst üste render
        // olur / çift AudioListener uyarısı verir — kapatıyoruz.
        if (Camera.main != null)
            Camera.main.gameObject.SetActive(false);

        if (podiumCamera != null)
            podiumCamera.gameObject.SetActive(true);
    }
}
