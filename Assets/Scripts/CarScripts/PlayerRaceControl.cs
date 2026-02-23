using UnityEngine;
using TMPro;

public class PlayerRaceController : MonoBehaviour
{
    [HideInInspector] public int totalCheckpoints;
    public int maxLaps = 3;

    private int currentCheckpoint = -1;
    private int currentLap = 0;
    public bool isRacing = true;

    [Header("UI")]
    public TextMeshProUGUI LapCount;
    public TextMeshProUGUI CheckpointInfo;
    
    [Header("Timer UI")]
    public TextMeshProUGUI TotalTimeText;
    public TextMeshProUGUI LastLapTimeText;

    // Timer değişkenleri
    private float totalTime = 0f;
    private float currentLapStartTime = 0f;
    private float lastLapTime = 0f;
    private bool timerRunning = false;

    public void Start()
    {
        // Lap Count Text'i bul
        if (LapCount == null)
        {
            LapCount = GameObject.Find("LapCountText")?.GetComponent<TextMeshProUGUI>();
            if (LapCount == null)
            {
                Debug.LogWarning("LapCountText bulunamadı! Canvas'ta 'LapCountText' adlı TextMeshProUGUI oluştur.");
            }
        }

        // Checkpoint Info Text'i bul
        if (CheckpointInfo == null)
        {
            CheckpointInfo = GameObject.Find("CheckpointInfoText")?.GetComponent<TextMeshProUGUI>();
            if (CheckpointInfo == null)
            {
                Debug.LogWarning("CheckpointInfoText bulunamadı! Canvas'ta 'CheckpointInfoText' adlı TextMeshProUGUI oluştur.");
            }
        }

        // Total Time Text'i bul
        if (TotalTimeText == null)
        {
            TotalTimeText = GameObject.Find("TotalTimeText")?.GetComponent<TextMeshProUGUI>();
            if (TotalTimeText == null)
            {
                Debug.LogWarning("TotalTimeText bulunamadı! Canvas'ta 'TotalTimeText' adlı TextMeshProUGUI oluştur.");
            }
        }

        // Last Lap Time Text'i bul
        if (LastLapTimeText == null)
        {
            LastLapTimeText = GameObject.Find("LastLapTimeText")?.GetComponent<TextMeshProUGUI>();
            if (LastLapTimeText == null)
            {
                Debug.LogWarning("LastLapTimeText bulunamadı! Canvas'ta 'LastLapTimeText' adlı TextMeshProUGUI oluştur.");
            }
        }

        // UI'ları başlat
        if (LapCount != null)
            LapCount.text = $"Lap : {currentLap} / {maxLaps}";
        
        UpdateCheckpointUI();
        
        // Timer UI'ını başlat
        if (TotalTimeText != null)
            TotalTimeText.text = "00:00.000";
        
        if (LastLapTimeText != null)
        {
            LastLapTimeText.text = "";
            LastLapTimeText.color = Color.red;
        }

        Debug.Log("✅ PlayerRaceController UI elementleri yüklendi.");
    }

    void Update()
    {
        // Timer çalışıyorsa zamanı güncelle
        if (timerRunning && isRacing)
        {
            totalTime += Time.deltaTime;
            UpdateTimerUI();
        }
    }

    public void Initialize(int totalCheckpoints)
    {
        this.totalCheckpoints = totalCheckpoints <= 0 ? 1 : totalCheckpoints;

        currentCheckpoint = -1;
        currentLap = 0;
        isRacing = true;
        
        // Timer'ı sıfırla
        totalTime = 0f;
        currentLapStartTime = 0f;
        lastLapTime = 0f;
        timerRunning = false;

        UpdateCheckpointUI();
        Debug.Log($"{name} initialized with {this.totalCheckpoints} checkpoints.");
    }

    public void ReachedCheckpoint(int index, bool isFinishLine)
    {
        if (!isRacing || totalCheckpoints <= 0) return;

        // İlk checkpoint'e ulaşınca timer'ı başlat
        if (!timerRunning && currentCheckpoint == -1)
        {
            timerRunning = true;
            currentLapStartTime = totalTime;
            Debug.Log("⏱️ Timer başlatıldı!");
        }

        if (index == (currentCheckpoint + 1) % totalCheckpoints)
        {
            currentCheckpoint = index;
            UpdateCheckpointUI();

            Debug.Log($"{name} reached checkpoint {index}");

            if (isFinishLine && index == totalCheckpoints - 1)
            {
                currentLap++;
                
                // Lap süresi hesapla
                float lapTime = totalTime - currentLapStartTime;
                lastLapTime = lapTime;
                currentLapStartTime = totalTime;

                Debug.Log($"🏁 Lap {currentLap} tamamlandı! Süre: {FormatTime(lapTime)}");

                LapCount.text = $"Lap : {currentLap} / {maxLaps}";
                
                // Son lap süresini göster
                if (LastLapTimeText != null)
                {
                    LastLapTimeText.text = $"Last: {FormatTime(lastLapTime)}";
                }

                if (currentLap >= maxLaps)
                {
                    FinishRace();
                }
            }
        }
    }

    private void UpdateCheckpointUI()
    {
        if (CheckpointInfo == null) return;

        int passed = currentCheckpoint; // -1 olabilir (başta)
        int next = (currentCheckpoint + 1) % totalCheckpoints;

        if (passed < 0)
        {
            CheckpointInfo.text =
                $"Checkpoint: - / {totalCheckpoints - 1}\nNext: {next}";
        }
        else
        {
            CheckpointInfo.text =
                $"Checkpoint: {passed} / {totalCheckpoints - 1}\nNext: {next}";
        }
    }

    private void UpdateTimerUI()
    {
        if (TotalTimeText != null)
        {
            TotalTimeText.text = FormatTime(totalTime);
        }
    }

    private string FormatTime(float timeInSeconds)
    {
        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);
        int milliseconds = Mathf.FloorToInt((timeInSeconds * 1000f) % 1000f);

        return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
    }

    private void FinishRace()
    {
        isRacing = false;
        timerRunning = false;
        
        LapCount.text = "FINISHED!";
        CheckpointInfo.text = "Race Complete";
        
        Debug.Log($"🏆 {name} FINISHED THE RACE!");
        Debug.Log($"⏱️ Total Time: {FormatTime(totalTime)}");
    }
}