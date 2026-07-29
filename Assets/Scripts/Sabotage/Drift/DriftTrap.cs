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
    [SerializeField] private float penaltyMultiplier = 2f;

    [Header("Checkpoint Seçim Göstergesi")]
    [Tooltip("Eskiden CheckpointSpawner'da kullanılan kırmızı ok prefabı (Assets/Prefabs/Ok.prefab). Seçilen checkpoint'in üzerinde belirir.")]
    [SerializeField] private GameObject checkpointArrowPrefab;
    [SerializeField] private float arrowHeightOffset = 3f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // Tuzak durumu
    private int trapCheckpointIndex = -1;
    private bool trapActive = false;
    private float trapActivationTime = 0f;
    private int selectedCheckpointIndex = -1;

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
    /// Sabotajcı tuzağı aktive eder. GEÇİCİ olarak
    /// SaboteurSkillInput.CmdActivateTrap() tarafından çağrılıyor (klavye
    /// C tuşu test girişi). Cooldown kontrolü ileride buraya eklenebilir.
    /// </summary>
    [Server]
    public void ActivateTrap()
    {
        if (selectedCheckpointIndex < 0)
        {
            if (showDebugLogs) Debug.LogWarning("[DriftTrap] Önce 0-9 ile checkpoint seç!");
            return;
        }
        if (checkpointManager == null || checkpointManager.checkpoints.Count < 2)
        {
            if (showDebugLogs) Debug.LogWarning("[DriftTrap] Yeterli checkpoint yok.");
            return;
        }

        trapCheckpointIndex = selectedCheckpointIndex;
        trapActive = true;
        trapActivationTime = Time.time;
        trackedCars.Clear();

        RpcClearCheckpointArrow();

        int nextIndex = (trapCheckpointIndex + 1) % checkpointManager.checkpoints.Count;

        if (showDebugLogs)
            Debug.Log($"[DriftTrap] ⚠️ Tuzak aktif! CP {trapCheckpointIndex} → ceza CP {nextIndex}'de. " +
                      $"{entryWindowSeconds}s pencere açık.");

        // KRİTİK: C'ye basıldığı anda zaten bu CP'yi geçmiş (arasında olan) araçları yakala
        CheckCarsCurrentlyBetweenCheckpoints(nextIndex);
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

                float currentPenalty = tc.driftTime * penaltyMultiplier;

                // CLIENT FEEDBACK — bkz. dosya sonundaki not
                tc.raceController.ShowLiveDriftPenalty(currentPenalty);
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
    /// İLERİDE: Server'da çalışır. AddTimePenalty çağrısı [TargetRpc]
    /// üzerinden ilgili client'a gönderilecek (bkz. dosya sonu notu).
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

        float penalty = tc.driftTime * penaltyMultiplier;

        // CLIENT FEEDBACK
        tc.raceController.AddTimePenalty(penalty, tc.driftTime);

        if (showDebugLogs)
            Debug.Log($"[DriftTrap] CEZA! Drift: {tc.driftTime:F2}s × {penaltyMultiplier} = +{penalty:F2}s");

        trapActive = false;
    }

    #endregion

    #region CLIENT FEEDBACK — Tamamlandı
    //
    // ShowLiveDriftPenalty(), ClearLiveDriftPenalty() ve AddTimePenalty()
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