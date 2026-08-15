using UnityEngine;
using System.Collections.Generic;
using Mirror;

/// <summary>
/// DRIFT TRAP SKİLL - NETWORK VERSİYONU
///
/// MİMARİ NOT:
/// - Artık NetworkBehaviour — server-authoritative. Sahnede TEK bir kopyası
///   var (spawn edilen bir prefab değil, Online Scene'e elle yerleştirilmiş
///   bir GameObject + NetworkIdentity — bu yüzden sahne kaydedilirken
///   sceneId ataması için Ctrl+S şart).
/// - SelectCheckpoint/ActivateTrap [Server] — GEÇİCİ olarak
///   SaboteurSkillInput.cs (klavye: 0-9 + C) tarafından [Command] üzerinden
///   çağrılıyor. İLERİDE bu, kuledeki minimap/harita butonlarına bağlanacak
///   (CLAUDE.md'de not edildi) — SaboteurSkillInput tamamen silinecek, ama
///   bu iki metod ve server mantığı AYNEN kalacak.
/// - CLIENT FEEDBACK bölümü artık gerçek [TargetRpc] (bkz.
///   PlayerRaceController.cs) — sadece etkilenen yarışçının ekranında görünüyor.
/// - Reflection tamamen kaldırıldı: CarController.IsDrifting() ve
///   PlayerRaceController.CurrentCheckpoint public erişim sağlıyor.
/// </summary>
public class DriftTrap : NetworkBehaviour
{
    [Header("Drift Trap Ayarları")]
    [SerializeField] private float entryWindowSeconds = 10f;

    [Header("Yavaşlatma Cezası")]
    [Tooltip("Yakalanınca arabanın ivmesi bu orana düşer (1 = normal, 0.5 = motor gücünün yarısı). Aracı komple durdurmuyoruz — sert/haksız hissettirmesin diye.")]
    [Range(0.1f, 1f)][SerializeField] private float slowdownAccelerationMultiplier = 0.5f;
    [Tooltip("Ne kadar uzun drift ettiyse ceza da o kadar uzun sürsün — her 1 saniyelik drift, kaç saniye yavaşlatma cezası eklesin.")]
    [SerializeField] private float slowdownSecondsPerDriftSecond = 1f;
    [Tooltip("Az bir drift bile (ör. 0.2s) fark edilir bir ceza versin diye alt sınır.")]
    [SerializeField] private float minSlowdownDuration = 1.5f;
    [Tooltip("Çok uzun drift edilse bile cezanın sonsuza uzamaması için üst sınır.")]
    [SerializeField] private float maxSlowdownDuration = 4f;

    [Header("Cooldown")]
    [Tooltip("Tuzak kurulduktan sonra bu skilin tekrar kullanılabilir olması için beklenmesi gereken süre (entryWindowSeconds'tan bağımsız).")]
    [SerializeField] private float skillCooldownSeconds = 15f;

    [Header("Checkpoint Seçim Göstergesi")]
    [Tooltip("Eskiden CheckpointSpawner'da kullanılan kırmızı ok prefabı (Assets/Prefabs/Ok.prefab). Seçilen checkpoint'in üzerinde belirir.")]
    [SerializeField] private GameObject checkpointArrowPrefab;
    [SerializeField] private float arrowHeightOffset = 3f;

    [Header("Ses")]
    [Tooltip("Tuzak kurulduğu anda, HEDEF CHECKPOINT'İN KONUMUNDA çalar. Herkes duyar — yakındaki yarışçı 'burada bir şey oldu' diye uyarılsın, sabotajcı da uzaktan kısık bir onay sesi alsın diye. Uğursuz/mekanik bir kurulma sesi olmalı.")]
    [SerializeField] private AudioClip trapArmedClip;
    [Range(0f, 1f)][SerializeField] private float trapArmedVolume = 0.9f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // Tuzak durumu
    private int trapCheckpointIndex = -1;
    private bool trapActive = false;
    private float trapActivationTime = 0f;
    private int selectedCheckpointIndex = -1;
    private float nextReadyTime = 0f;

    private CheckpointCooldownManager checkpointCooldown;

    // Ok göstergesi HER client'ta ayrı ayrı tutulur (ClientRpc ile senkronize
    // ediliyor) — networked bir obje değil, sadece görsel bir işaretçi.
    private GameObject arrowInstance;

    // Takip edilen araçlar
    private Dictionary<CarController, TrackedCar> trackedCars = new Dictionary<CarController, TrackedCar>();

    private CheckpointManager checkpointManager;

    // ─── İç Sınıf ────────────────────────────────────────────────
    private class TrackedCar
    {
        public CarController car;
        public PlayerRaceController raceController;
        public float driftTime = 0f;
        public int targetCheckpointIndex;
        public bool penaltyApplied = false;
    }

    // ─── Unity ───────────────────────────────────────────────────
    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();
        checkpointCooldown = FindAnyObjectByType<CheckpointCooldownManager>();
        if (checkpointManager == null)
            Debug.LogWarning("[DriftTrap] CheckpointManager bulunamadı!");
        else if (showDebugLogs)
            Debug.Log("[DriftTrap] Hazır. Server tarafında SelectCheckpoint/ActivateTrap bekleniyor.");
    }

    void Update()
    {
        // Server-authoritative: bu mantık sadece server'da çalışır. Sahnede
        // bu objenin bir kopyası her client'ta da var (NetworkIdentity),
        // ama tuzak durumu (trapActive, trackedCars) sadece server'ınki
        // geçerli — client'lardaki kopyalar hiç güncellenmemeli.
        if (!isServer) return;

        if (trapActive)
        {
            // Pencere doldu mu kontrol et
            if (Time.time - trapActivationTime > entryWindowSeconds && trackedCars.Count == 0)
            {
                trapActive = false;
                if (showDebugLogs) Debug.Log("[DriftTrap] Kimse gelmedi, tuzak süresi doldu.");
                return;
            }

            TrackDrifts();
        }
    }

    #region INPUT — Server'da çalışır, çağrı Command üzerinden gelir

    /// <summary>
    /// Sabotajcı bir checkpoint seçer. GEÇİCİ olarak
    /// SaboteurSkillInput.CmdSelectCheckpoint() tarafından çağrılıyor
    /// (klavye 0-9 test girişi) — ileride minimap/harita butonu bunun
    /// yerine geçecek, bu metodun kendisi değişmeyecek.
    /// </summary>
    [Server]
    public void SelectCheckpoint(int index)
    {
        if (checkpointManager == null || index < 0 || index >= checkpointManager.checkpoints.Count)
        {
            if (showDebugLogs) Debug.LogWarning($"[DriftTrap] Checkpoint {index} mevcut değil.");
            return;
        }
        selectedCheckpointIndex = index;
        if (showDebugLogs) Debug.Log($"[DriftTrap] Checkpoint {index} seçildi. C → tuzak kur.");

        RpcShowCheckpointArrow(index);
    }

    /// <summary>
    /// Sabotajcı tuzağı aktive eder. Başarılıysa true döner. false dönerse
    /// ya skilin kendi cooldown'u (skillCooldownSeconds) ya da hedef
    /// checkpoint'in ortak cooldown'u (CheckpointCooldownManager) henüz
    /// dolmamıştır.
    /// </summary>
    [Server]
    public bool ActivateTrap()
    {
        if (selectedCheckpointIndex < 0)
        {
            if (showDebugLogs) Debug.LogWarning("[DriftTrap] Önce bir checkpoint seç!");
            return false;
        }
        if (checkpointManager == null || checkpointManager.checkpoints.Count < 2)
        {
            if (showDebugLogs) Debug.LogWarning("[DriftTrap] Yeterli checkpoint yok.");
            return false;
        }
        if (Time.time < nextReadyTime) return false;
        if (checkpointCooldown != null && !checkpointCooldown.IsCheckpointReady(selectedCheckpointIndex)) return false;

        trapCheckpointIndex = selectedCheckpointIndex;
        trapActive = true;
        trapActivationTime = Time.time;
        trackedCars.Clear();

        RpcClearCheckpointArrow();
        RpcPlayTrapArmedSound(trapCheckpointIndex);

        int nextIndex = (trapCheckpointIndex + 1) % checkpointManager.checkpoints.Count;

        if (showDebugLogs)
            Debug.Log($"[DriftTrap] ⚠️ Tuzak aktif! CP {trapCheckpointIndex} → ceza CP {nextIndex}'de. " +
                      $"{entryWindowSeconds}s pencere açık.");

        // KRİTİK: C'ye basıldığı anda zaten bu CP'yi geçmiş (arasında olan) araçları yakala
        CheckCarsCurrentlyBetweenCheckpoints(nextIndex);

        nextReadyTime = Time.time + (checkpointCooldown != null
            ? checkpointCooldown.ScaleSkillCooldown(skillCooldownSeconds)
            : skillCooldownSeconds);
        checkpointCooldown?.StartCooldown(trapCheckpointIndex);
        return true;
    }

    /// <summary>
    /// Seçilen checkpoint'in üzerinde ok gösterir. HERKESTE (server dahil
    /// tüm client'larda) çalışır — eskiden CheckpointSpawner'ın tek
    /// oyunculu ShowPreview() metodunun yaptığı işin network eşdeğeri.
    /// </summary>
    [ClientRpc]
    private void RpcShowCheckpointArrow(int index)
    {
        if (checkpointArrowPrefab == null || checkpointManager == null) return;
        if (index < 0 || index >= checkpointManager.checkpoints.Count) return;

        Transform cp = checkpointManager.checkpoints[index];
        Vector3 pos = cp.position + Vector3.up * arrowHeightOffset;

        if (arrowInstance == null)
            arrowInstance = Instantiate(checkpointArrowPrefab, pos, cp.rotation);
        else
        {
            arrowInstance.transform.SetPositionAndRotation(pos, cp.rotation);
            arrowInstance.SetActive(true);
        }
    }

    [ClientRpc]
    private void RpcClearCheckpointArrow()
    {
        if (arrowInstance != null)
            arrowInstance.SetActive(false);
    }

    /// <summary>
    /// Tuzağın kurulduğu sesi HER client'ta, tuzağın kurulduğu checkpoint'in
    /// konumunda çalar.
    ///
    /// NEDEN AYRI BİR ClientRpc (ses doğrudan ActivateTrap'te çalınamaz mı):
    /// ActivateTrap [Server] — yani o kod SADECE server makinesinde çalışıyor.
    /// Orada SfxPlayer çağırsaydık sesi yalnızca host duyardı, gerçek
    /// client'lar hiçbir şey duymazdı. Görsel ok göstergesinin ClientRpc ile
    /// yayılmasının sebebiyle birebir aynı gerekçe.
    /// </summary>
    [ClientRpc]
    private void RpcPlayTrapArmedSound(int index)
    {
        if (checkpointManager == null || index < 0 || index >= checkpointManager.checkpoints.Count) return;

        Transform cp = checkpointManager.checkpoints[index];
        if (cp != null)
            SfxPlayer.PlayAt(trapArmedClip, cp.position, trapArmedVolume, 0.05f, 12f, 150f);
    }

    #endregion

    #region SERVER LOGIC — Host-authoritative, mantık değişmeyecek

    /// <summary>
    /// Tuzak aktive edildiğinde zaten trapCheckpoint ile nextCheckpoint
    /// arasında olan araçları anında takibe alır.
    /// İLERİDE: Zaten server'da çalışacak, değişiklik gerekmez.
    /// </summary>
    void CheckCarsCurrentlyBetweenCheckpoints(int nextIndex)
    {
        PlayerRaceController[] allPlayers = FindObjectsByType<PlayerRaceController>(FindObjectsSortMode.None);

        foreach (var player in allPlayers)
        {
            if (!player.isRacing) continue;

            // Reflection yerine public property
            int lastCP = player.CurrentCheckpoint;

            // Oyuncu trapCP'yi geçmiş ama nextCP'yi henüz geçmemiş → arasında
            if (lastCP == trapCheckpointIndex)
            {
                CarController car = player.GetComponentInParent<CarController>();
                if (car == null) car = player.GetComponent<CarController>();
                if (car == null) car = player.transform.root.GetComponent<CarController>();

                if (car != null && !trackedCars.ContainsKey(car))
                {
                    trackedCars[car] = new TrackedCar
                    {
                        car = car,
                        raceController = player,
                        driftTime = 0f,
                        targetCheckpointIndex = nextIndex,
                        penaltyApplied = false
                    };

                    if (showDebugLogs)
                        Debug.Log($"[DriftTrap] 🎯 Araç zaten CP {trapCheckpointIndex} ile {nextIndex} arasında! Anında takibe alındı.");
                }
            }
        }
    }

    /// <summary>
    /// Her frame takip edilen araçların drift durumunu kontrol eder.
    /// İLERİDE: Server'da çalışır. driftTime SyncVar olabilir ama şart değil,
    /// sadece final ceza an itibariyle client'a gönderilir.
    /// </summary>
    void TrackDrifts()
    {
        List<CarController> toRemove = new List<CarController>();

        foreach (var kvp in trackedCars)
        {
            TrackedCar tc = kvp.Value;

            if (tc.penaltyApplied)
            {
                toRemove.Add(kvp.Key);
                continue;
            }

            // Reflection yerine public metod
            if (tc.car.IsDrifting())
            {
                tc.driftTime += Time.deltaTime;

                // CLIENT FEEDBACK — bkz. dosya sonundaki not
                tc.raceController.ShowLiveDriftPenalty(tc.driftTime);
            }
        }

        foreach (var car in toRemove)
            trackedCars.Remove(car);
    }

    /// <summary>
    /// Checkpoint.cs tarafından çağrılır (araç checkpoint'e ulaştığında).
    /// Checkpoint.cs artık bu çağrıyı sadece NetworkServer.active iken yapıyor,
    /// bu yüzden burası da güvenle server-only sayılabilir.
    /// </summary>
    [Server]
    public void OnCarReachedCheckpoint(CarController car, PlayerRaceController raceController, int checkpointIndex)
    {
        if (!trapActive || checkpointManager == null) return;

        int nextIndex = (trapCheckpointIndex + 1) % checkpointManager.checkpoints.Count;

        // ── Tuzak CP'sine girdi → takibe al ──
        if (checkpointIndex == trapCheckpointIndex)
        {
            float elapsed = Time.time - trapActivationTime;

            if (elapsed <= entryWindowSeconds)
            {
                if (!trackedCars.ContainsKey(car))
                {
                    trackedCars[car] = new TrackedCar
                    {
                        car = car,
                        raceController = raceController,
                        driftTime = 0f,
                        targetCheckpointIndex = nextIndex,
                        penaltyApplied = false
                    };

                    if (showDebugLogs)
                        Debug.Log($"[DriftTrap] 🎯 Araç tuzağa girdi! CP {trapCheckpointIndex} → CP {nextIndex} arası izleniyor.");
                }
            }
            else
            {
                // Pencere kapandı, kimse takipte değilse tuzağı kapat
                if (trackedCars.Count == 0)
                {
                    trapActive = false;
                    if (showDebugLogs) Debug.Log("[DriftTrap] Pencere kapandı.");
                }
            }
        }

        // ── Ceza CP'sine ulaştı → ceza ver ──
        if (checkpointIndex == nextIndex && trackedCars.ContainsKey(car))
        {
            TrackedCar tc = trackedCars[car];
            if (!tc.penaltyApplied)
                ApplyPenalty(tc);
        }
    }

    /// <summary>
    /// Server'da çalışır. ApplyDriftSlowdown çağrısı [TargetRpc] üzerinden
    /// ilgili client'a gönderiliyor (bkz. dosya sonu notu) — süre cezası
    /// DEĞİL, arabanın ivmesini geçici kısan gerçek/hissedilir bir ceza.
    /// </summary>
    void ApplyPenalty(TrackedCar tc)
    {
        tc.penaltyApplied = true;

        // CLIENT FEEDBACK
        tc.raceController.ClearLiveDriftPenalty();

        if (tc.driftTime <= 0.05f)
        {
            if (showDebugLogs) Debug.Log("[DriftTrap] Drift yapılmadı, ceza yok.");
            trapActive = false;
            return;
        }

        float duration = Mathf.Clamp(
            tc.driftTime * slowdownSecondsPerDriftSecond,
            minSlowdownDuration, maxSlowdownDuration);

        // CLIENT FEEDBACK
        tc.raceController.ApplyDriftSlowdown(slowdownAccelerationMultiplier, duration, tc.driftTime);

        if (showDebugLogs)
            Debug.Log($"[DriftTrap] CEZA! Drift: {tc.driftTime:F2}s → {duration:F1}s yavaşlatma (×{slowdownAccelerationMultiplier} ivme).");

        trapActive = false;
    }

    #endregion

    #region CLIENT FEEDBACK — Tamamlandı
    //
    // ShowLiveDriftPenalty(), ClearLiveDriftPenalty() ve ApplyDriftSlowdown()
    // DriftTrap'ten (server) hâlâ doğrudan çağrılıyor, ama PlayerRaceController
    // içindeki implementasyonları artık [TargetRpc] — otomatik olarak sadece
    // o aracın sahibi olan client'ın ekranını etkiliyor. Bu dosyada başka bir
    // değişiklik gerekmedi, tasarım tam olarak planlandığı gibi çalıştı.
    #endregion

    #region Gizmos
    void OnDrawGizmos()
    {
        if (!trapActive || checkpointManager == null) return;

        if (trapCheckpointIndex >= 0 && trapCheckpointIndex < checkpointManager.checkpoints.Count)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(checkpointManager.checkpoints[trapCheckpointIndex].position, 5f);
        }

        int nextIdx = (trapCheckpointIndex + 1) % checkpointManager.checkpoints.Count;
        if (nextIdx >= 0 && nextIdx < checkpointManager.checkpoints.Count)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(checkpointManager.checkpoints[nextIdx].position, 5f);
        }
    }
    #endregion
}