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

    public override void OnStartServer()
    {
        base.OnStartServer();
        elapsed = 0f;
        raceOutcome = RaceOutcome.Ongoing;
        raceTimeLimit = ResolveRaceTimeLimit();
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
        if (!isServer || raceOutcome != RaceOutcome.Ongoing) return;

        elapsed += Time.deltaTime;

        if (raceTimeLimit > 0f && elapsed >= raceTimeLimit)
            ServerEndRace(bySaboteur: true);
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
