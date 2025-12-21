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
    public TextMeshProUGUI CheckpointInfo; // <-- YENİ

    public void Start()
    {
        if (LapCount == null)
            LapCount = GameObject.Find("LapCountText").GetComponent<TextMeshProUGUI>();

        if (CheckpointInfo == null)
            CheckpointInfo = GameObject.Find("CheckpointInfoText").GetComponent<TextMeshProUGUI>();

        LapCount.text = $"Lap : {currentLap} / {maxLaps}";
        UpdateCheckpointUI();
    }

    public void Initialize(int totalCheckpoints)
    {
        this.totalCheckpoints = totalCheckpoints <= 0 ? 1 : totalCheckpoints;

        currentCheckpoint = -1;
        currentLap = 0;
        isRacing = true;

        UpdateCheckpointUI();
        Debug.Log($"{name} initialized with {this.totalCheckpoints} checkpoints.");
    }

    public void ReachedCheckpoint(int index, bool isFinishLine)
    {
        if (!isRacing || totalCheckpoints <= 0) return;

        if (index == (currentCheckpoint + 1) % totalCheckpoints)
        {
            currentCheckpoint = index;
            UpdateCheckpointUI();

            Debug.Log($"{name} reached checkpoint {index}");

            if (isFinishLine && index == totalCheckpoints - 1)
            {
                currentLap++;
                LapCount.text = $"Lap : {currentLap} / {maxLaps}";

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


    private void FinishRace()
    {
        isRacing = false;
        LapCount.text = "FINISHED!";
        CheckpointInfo.text = "Race Complete";
        Debug.Log($"{name} FINISHED THE RACE!");
    }
}
