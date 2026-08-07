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
/// NETWORK NOTU: raceEnded/saboteurWon SADECE bu iki SyncVar üzerinden
/// yayılıyor. Podyum kolonlarının/kamera noktalarının pozisyonları SAHNE
/// objeleri olduğu için (network mesajı gerekmez) her client zaten kendi
/// kopyasında aynı yerde duruyor — sadece "hangi kolonu aktif et, kamerayı
/// nereye koy" kararını SyncVar'lardan okuyoruz.
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

    [SyncVar(hook = nameof(OnRaceEndedChanged))]
    private bool raceEnded;

    [SyncVar]
    private bool saboteurWon;

    private float elapsed;

    public override void OnStartServer()
    {
        base.OnStartServer();
        elapsed = 0f;
        raceEnded = false;
        saboteurWon = false;
        PlayerRaceController.OnPlayerFinishedRace += HandleRacerFinished;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        PlayerRaceController.OnPlayerFinishedRace -= HandleRacerFinished;
    }

    [Server]
    private void HandleRacerFinished(PlayerRaceController finisher)
    {
        ServerEndRace(bySaboteur: false);
    }

    void Update()
    {
        if (!isServer || raceEnded) return;

        elapsed += Time.deltaTime;

        int racerCount = Mathf.Clamp(PlayerRaceController.AllPlayers.Count, 1, raceTimeLimitByRacerCount.Length);
        float limit = raceTimeLimitByRacerCount[racerCount - 1];

        if (elapsed >= limit)
            ServerEndRace(bySaboteur: true);
    }

    [Server]
    private void ServerEndRace(bool bySaboteur)
    {
        if (raceEnded) return;

        saboteurWon = bySaboteur;
        raceEnded = true;

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

    private void OnRaceEndedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
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

        if (saboteurWon)
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
            if (myRank >= 0) TeleportLocalRacer(myRank);
        }

        SwitchToPodiumCamera();
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
        if (rankIndex < 0 || rankIndex >= racerColumnSpawnPoints.Length) return;
        if (racerColumnSpawnPoints[rankIndex] == null) return;

        var localIdentity = NetworkClient.localPlayer;
        if (localIdentity == null) return;

        var car = localIdentity.GetComponent<CarController>();
        if (car == null) return;

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
