using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// DRIFT TRAP SKİLL - LAN/Network Hazırlık Versiyonu
///
/// MİMARİ NOT (Mirror'a geçiş için):
/// - Bu script HOST/SERVER üzerinde çalışacak şekilde tasarlandı (host-authoritative).
/// - "INPUT" bölümündeki metodlar (SelectCheckpoint, ActivateTrap) ileride
///   [Command] olacak: Sabotajcı client'tan -> Server'a çağrılacak.
/// - "SERVER LOGIC" bölümü zaten server-only mantıkla çalışıyor, büyük
///   değişiklik gerekmez.
/// - "CLIENT FEEDBACK" bölümündeki PlayerRaceController çağrıları ileride
///   [TargetRpc] olacak (Server -> sadece ilgili client).
/// - Reflection tamamen kaldırıldı: CarController.IsDrifting() ve
///   PlayerRaceController.CurrentCheckpoint public erişim sağlıyor.
/// </summary>
public class DriftTrap : MonoBehaviour
{
    [Header("Drift Trap Ayarları")]
    [SerializeField] private float entryWindowSeconds = 10f;
    [SerializeField] private float penaltyMultiplier = 2f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // Tuzak durumu
    private int trapCheckpointIndex = -1;
    private bool trapActive = false;
    private float trapActivationTime = 0f;
    private int selectedCheckpointIndex = -1;

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
            Debug.Log("[DriftTrap] Hazır. 0-9 ile checkpoint seç, C ile tuzak kur. (TEST INPUT)");
    }

    void Update()
    {
        // ŞİMDİLİK: Tek oyunculu test girişi (klavye).
        // İLERİDE: Bu blok kaldırılacak. Sabotajcı UI'ından gelen tıklamalar
        // doğrudan SelectCheckpoint() ve ActivateTrap() metodlarını
        // bir [Command] üzerinden server'da çağıracak.
        HandleLocalTestInput();

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

    #region INPUT — İleride [Command] olacak kısım

    /// <summary>
    /// Geçici test girişi. Mirror'a geçince bu metod tamamen silinecek,
    /// yerine sabotajcı UI'ı SelectCheckpoint/ActivateTrap'i Command ile çağıracak.
    /// </summary>
    private void HandleLocalTestInput()
    {
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(i.ToString()))
                SelectCheckpoint(i);
        }

        if (Input.GetKeyDown(KeyCode.C))
            ActivateTrap();
    }

    /// <summary>
    /// Sabotajcı bir checkpoint seçer.
    /// İLERİDE: [Command] CmdSelectCheckpoint(int index) → server tarafında
    /// selectedCheckpointIndex'i set eder. Yetki kontrolü (sadece sabotajcı
    /// çağırabilsin) burada eklenecek.
    /// </summary>
    public void SelectCheckpoint(int index)
    {
        if (checkpointManager == null || index < 0 || index >= checkpointManager.checkpoints.Count)
        {
            if (showDebugLogs) Debug.LogWarning($"[DriftTrap] Checkpoint {index} mevcut değil.");
            return;
        }
        selectedCheckpointIndex = index;
        if (showDebugLogs) Debug.Log($"[DriftTrap] Checkpoint {index} seçildi. C → tuzak kur.");
    }

    /// <summary>
    /// Sabotajcı tuzağı aktive eder.
    /// İLERİDE: [Command] CmdActivateTrap() olacak. Cooldown kontrolü
    /// (sabotajcının skill cooldown'u bitti mi) burada eklenecek.
    /// </summary>
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

        int nextIndex = (trapCheckpointIndex + 1) % checkpointManager.checkpoints.Count;

        if (showDebugLogs)
            Debug.Log($"[DriftTrap] ⚠️ Tuzak aktif! CP {trapCheckpointIndex} → ceza CP {nextIndex}'de. " +
                      $"{entryWindowSeconds}s pencere açık.");

        // KRİTİK: C'ye basıldığı anda zaten bu CP'yi geçmiş (arasında olan) araçları yakala
        CheckCarsCurrentlyBetweenCheckpoints(nextIndex);
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
    /// İLERİDE: Checkpoint trigger'ları server-authoritative olacağı için
    /// bu metod zaten sadece server'da tetiklenecek. Değişiklik gerekmez.
    /// </summary>
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

    #region CLIENT FEEDBACK — Mirror notu
    //
    // ShowLiveDriftPenalty(), ClearLiveDriftPenalty() ve AddTimePenalty()
    // şu an PlayerRaceController üzerinde doğrudan çağrılıyor (local).
    //
    // Mirror'a geçince:
    // - PlayerRaceController bir NetworkBehaviour olacak.
    // - Bu üç metod [TargetRpc] attribute'u alacak, böylece sadece o aracın
    //   sahibi olan client'ın ekranında görünecek (diğer oyuncular görmemeli).
    // - DriftTrap (server'da) bu metodları çağırmaya devam edecek, sadece
    //   PlayerRaceController içindeki implementasyon RPC'ye dönüşecek.
    //
    // Yani DriftTrap.cs'de BAŞKA bir değişiklik gerekmeyecek — sadece
    // PlayerRaceController.cs'deki üç metodun başına [TargetRpc] eklenecek.
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