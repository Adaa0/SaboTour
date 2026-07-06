using UnityEngine;
using TMPro;

public class PlayerRaceController : MonoBehaviour
{
    [HideInInspector] public int totalCheckpoints;
    public int maxLaps = 3;

    private int currentCheckpoint = -1;
    public int CurrentCheckpoint => currentCheckpoint;
    private int currentLap = 0;
    public bool isRacing = true;

    [Header("UI")]
    public TextMeshProUGUI LapCount;
    public TextMeshProUGUI CheckpointInfo;

    [Header("Timer UI")]
    public TextMeshProUGUI TotalTimeText;
    public TextMeshProUGUI LastLapTimeText;

    // Timer
    private float totalTime = 0f;
    private float currentLapStartTime = 0f;
    private float lastLapTime = 0f;
    private bool timerRunning = false;

    // Son lap metnini hatırla (canlı drift gösterimi bozmasın)
    private string lastLapDisplayText = "";
    private bool showingDriftWarning = false;

    // ─── Start ───────────────────────────────────────────────────
    public void Start()
    {
        if (LapCount == null)
            LapCount = GameObject.Find("LapCountText")?.GetComponent<TextMeshProUGUI>();

        if (CheckpointInfo == null)
            CheckpointInfo = GameObject.Find("CheckpointInfoText")?.GetComponent<TextMeshProUGUI>();

        if (TotalTimeText == null)
            TotalTimeText = GameObject.Find("TotalTimeText")?.GetComponent<TextMeshProUGUI>();

        if (LastLapTimeText == null)
            LastLapTimeText = GameObject.Find("LastLapTimeText")?.GetComponent<TextMeshProUGUI>();

        if (LapCount != null) LapCount.text = $"Lap : {currentLap} / {maxLaps}";

        UpdateCheckpointUI();

        if (TotalTimeText != null) TotalTimeText.text = "00:00.000";

        if (LastLapTimeText != null)
        {
            LastLapTimeText.text = "";
            LastLapTimeText.color = Color.white;
            lastLapDisplayText = "";
        }
    }

    // ─── Update ──────────────────────────────────────────────────
    void Update()
    {
        if (timerRunning && isRacing)
        {
            totalTime += Time.deltaTime;
            UpdateTimerUI();
        }
    }

    // ─── Initialize ──────────────────────────────────────────────
    public void Initialize(int totalCheckpoints)
    {
        this.totalCheckpoints = totalCheckpoints <= 0 ? 1 : totalCheckpoints;
        currentCheckpoint = -1;
        currentLap = 0;
        isRacing = true;
        totalTime = 0f;
        currentLapStartTime = 0f;
        lastLapTime = 0f;
        timerRunning = false;
        showingDriftWarning = false;
        lastLapDisplayText = "";
        UpdateCheckpointUI();
    }

    // ─── ReachedCheckpoint ───────────────────────────────────────
    public void ReachedCheckpoint(int index, bool isFinishLine)
    {
        if (!isRacing || totalCheckpoints <= 0) return;

        if (!timerRunning && currentCheckpoint == -1)
        {
            timerRunning = true;
            currentLapStartTime = totalTime;
        }

        if (index == (currentCheckpoint + 1) % totalCheckpoints)
        {
            currentCheckpoint = index;
            UpdateCheckpointUI();

            if (isFinishLine && index == totalCheckpoints - 1)
            {
                currentLap++;

                float lapTime = totalTime - currentLapStartTime;
                lastLapTime = lapTime;
                currentLapStartTime = totalTime;

                if (LapCount != null) LapCount.text = $"Lap : {currentLap} / {maxLaps}";

                string lapText = $"Last: {FormatTime(lastLapTime)}";
                lastLapDisplayText = lapText;

                // Eğer drift uyarısı gösterilmiyorsa normal son tur süresini yaz
                if (!showingDriftWarning && LastLapTimeText != null)
                {
                    LastLapTimeText.color = Color.white;
                    LastLapTimeText.text = lapText;
                }

                if (currentLap >= maxLaps) FinishRace();
            }
        }
    }

    // ─── CANLI DRIFT CEZA GÖSTERİMİ ─────────────────────────────
    /// <summary>
    /// DriftTrap her frame çağırır: drift atılırken anlık ceza miktarını göster.
    /// </summary>
    public void ShowLiveDriftPenalty(float currentPenalty)
    {
        if (LastLapTimeText == null) return;

        showingDriftWarning = true;
        LastLapTimeText.color = new Color(1f, 0.4f, 0f); // Turuncu
        LastLapTimeText.text = $"Drift Cezasi: +{currentPenalty:F1}s";
    }

    /// <summary>
    /// Drift bitti veya ceza verildi: gösterimi temizle / son lap süresine dön.
    /// </summary>
    public void ClearLiveDriftPenalty()
    {
        showingDriftWarning = false;

        if (LastLapTimeText == null) return;

        LastLapTimeText.color = Color.white;
        LastLapTimeText.text = lastLapDisplayText;
    }

    // ─── CEZA UYGULA ─────────────────────────────────────────────
    /// <summary>
    /// Toplam zamana ceza ekle ve sonucu göster.
    /// </summary>
    public void AddTimePenalty(float penalty, float driftTime)
    {
        totalTime += penalty;

        if (LastLapTimeText != null)
        {
            StopAllCoroutines();
            StartCoroutine(ShowFinalPenaltyNotification(penalty, driftTime));
        }

        Debug.Log($"[PlayerRaceController] 💀 +{penalty:F2}s ceza eklendi. Yeni toplam: {FormatTime(totalTime)}");
    }

    private System.Collections.IEnumerator ShowFinalPenaltyNotification(float penalty, float driftTime)
    {
        if (LastLapTimeText == null) yield break;

        showingDriftWarning = true;
        LastLapTimeText.color = Color.red;
        LastLapTimeText.text = $"+{penalty:F1}s CEZA! ({driftTime:F1}s drift)";

        yield return new WaitForSeconds(4f);

        showingDriftWarning = false;
        LastLapTimeText.color = Color.white;
        LastLapTimeText.text = lastLapDisplayText;
    }

    // ─── Yardımcı ────────────────────────────────────────────────
    private void UpdateCheckpointUI()
    {
        if (CheckpointInfo == null) return;
        int next = (currentCheckpoint + 1) % (totalCheckpoints > 0 ? totalCheckpoints : 1);

        if (currentCheckpoint < 0)
            CheckpointInfo.text = $"Checkpoint: - / {totalCheckpoints - 1}\nNext: {next}";
        else
            CheckpointInfo.text = $"Checkpoint: {currentCheckpoint} / {totalCheckpoints - 1}\nNext: {next}";
    }

    private void UpdateTimerUI()
    {
        if (TotalTimeText != null)
            TotalTimeText.text = FormatTime(totalTime);
    }

    private string FormatTime(float t)
    {
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        int ms = Mathf.FloorToInt((t * 1000f) % 1000f);
        return $"{m:00}:{s:00}.{ms:000}";
    }

    private void FinishRace()
    {
        isRacing = false;
        timerRunning = false;
        if (LapCount != null) LapCount.text = "FINISHED!";
        if (CheckpointInfo != null) CheckpointInfo.text = "Race Complete";
        Debug.Log($"🏆 {name} BİTİRDİ! Toplam: {FormatTime(totalTime)}");
    }
}