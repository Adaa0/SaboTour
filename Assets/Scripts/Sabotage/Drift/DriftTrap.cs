using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// DRIFT TRAP SKİLL
/// 0-9 ile checkpoint seç, C ile tuzak kur.
/// O checkpoint'i geçen (veya 10s içinde geçecek olan) araçlar
/// bir sonraki checkpoint'e kadar drift sürelerinin 2 katı ceza alır.
/// Drift tespiti: skidSmokes particle sistemi çalışıyorsa drift sayılır.
/// Drift atıldıkça anlık "Drift Cezası: +X.Xs" LastLapText'te gösterilir.
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

    // Reflection cache
    private static FieldInfo skidSmokesField;
    private static FieldInfo currentCheckpointField;

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
    void Awake()
    {
        // Reflection field'larını bir kere cache'le
        skidSmokesField = typeof(CarController).GetField("skidSmokes",
            BindingFlags.NonPublic | BindingFlags.Instance);

        currentCheckpointField = typeof(PlayerRaceController).GetField("currentCheckpoint",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (skidSmokesField == null)
            Debug.LogError("[DriftTrap] CarController'da 'skidSmokes' field'ı bulunamadı! İsim değişmiş olabilir.");

        if (currentCheckpointField == null)
            Debug.LogError("[DriftTrap] PlayerRaceController'da 'currentCheckpoint' field'ı bulunamadı!");
    }

    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();
        if (checkpointManager == null)
            Debug.LogWarning("[DriftTrap] CheckpointManager bulunamadı!");
        else if (showDebugLogs)
            Debug.Log("[DriftTrap] Hazır. 0-9 ile checkpoint seç, C ile tuzak kur.");
    }

    void Update()
    {
        HandleInput();

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

    // ─── Girdi ───────────────────────────────────────────────────
    void HandleInput()
    {
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(i.ToString()))
                SelectCheckpoint(i);
        }

        if (Input.GetKeyDown(KeyCode.C))
            ActivateTrap();
    }

    void SelectCheckpoint(int index)
    {
        if (checkpointManager == null || index >= checkpointManager.checkpoints.Count)
        {
            if (showDebugLogs) Debug.LogWarning($"[DriftTrap] Checkpoint {index} mevcut değil.");
            return;
        }
        selectedCheckpointIndex = index;
        if (showDebugLogs) Debug.Log($"[DriftTrap] Checkpoint {index} seçildi. C → tuzak kur.");
    }

    // ─── Tuzak Aktivasyonu ────────────────────────────────────────
    void ActivateTrap()
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

    // ─── Şu An Checkpointler Arasında Olan Araçları Yakala ───────
    void CheckCarsCurrentlyBetweenCheckpoints(int nextIndex)
    {
        if (currentCheckpointField == null) return;

        PlayerRaceController[] allPlayers = FindObjectsByType<PlayerRaceController>(FindObjectsSortMode.None);

        foreach (var player in allPlayers)
        {
            if (!player.isRacing) continue;

            // currentCheckpoint: oyuncunun en son geçtiği checkpoint index'i
            int lastCP = (int)currentCheckpointField.GetValue(player);

            // Oyuncu tam olarak trapCheckpoint'i son geçtiği checkpoint olarak tutuyorsa
            // yani trapCP'yi geçmiş ama nextCP'yi henüz geçmemiş → arasında
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

    // ─── Drift Takibi (her frame) ─────────────────────────────────
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

            bool drifting = IsCarDrifting(tc.car);

            if (drifting)
            {
                tc.driftTime += Time.deltaTime;

                // Anlık ceza miktarını UI'da göster (her frame güncelle)
                float currentPenalty = tc.driftTime * penaltyMultiplier;
                tc.raceController.ShowLiveDriftPenalty(currentPenalty);
            }
        }

        foreach (var car in toRemove)
            trackedCars.Remove(car);
    }

    // ─── Drift Tespiti ────────────────────────────────────────────
    bool IsCarDrifting(CarController car)
    {
        if (car == null || skidSmokesField == null) return false;

        ParticleSystem[] smokes = (ParticleSystem[])skidSmokesField.GetValue(car);
        if (smokes == null) return false;

        foreach (var smoke in smokes)
        {
            if (smoke != null && smoke.isPlaying)
                return true;
        }
        return false;
    }

    // ─── Checkpoint'e Ulaşma (Checkpoint.cs'den çağrılır) ─────────
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

    // ─── Ceza Uygula ─────────────────────────────────────────────
    void ApplyPenalty(TrackedCar tc)
    {
        tc.penaltyApplied = true;

        // Canlı gösteriyi temizle
        tc.raceController.ClearLiveDriftPenalty();

        if (tc.driftTime <= 0.05f)
        {
            if (showDebugLogs) Debug.Log("[DriftTrap] Drift yapılmadı, ceza yok.");
            trapActive = false;
            return;
        }

        float penalty = tc.driftTime * penaltyMultiplier;
        tc.raceController.AddTimePenalty(penalty, tc.driftTime);

        if (showDebugLogs)
            Debug.Log($"[DriftTrap]  CEZA! Drift: {tc.driftTime:F2}s × {penaltyMultiplier} = +{penalty:F2}s");

        trapActive = false;
    }

    // ─── Gizmos ───────────────────────────────────────────────────
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
}