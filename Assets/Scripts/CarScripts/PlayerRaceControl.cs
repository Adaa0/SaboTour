using UnityEngine;
using TMPro;

public class PlayerRaceController : MonoBehaviour
{
    [HideInInspector] public int totalCheckpoints;
    public int maxLaps = 3;

    private int currentCheckpoint = -1;
    private int currentLap = 0;
    public bool isRacing = true;

    public TextMeshProUGUI LapCount;

    public void Start()
    {
        if (LapCount == null)
            LapCount = GameObject.Find("LapCountText").GetComponent<TextMeshProUGUI>();

        LapCount.text = "Lap : " + currentLap + " / " + maxLaps;
    }

    public void Initialize(int totalCheckpoints)
    {
        if (totalCheckpoints <= 0)
        {
            Debug.LogError($"{name}: totalCheckpoints <= 0! Using fallback value 1.");
            this.totalCheckpoints = 1;
        }
        else
        {
            this.totalCheckpoints = totalCheckpoints;
        }

        currentCheckpoint = -1;
        currentLap = 0;
        isRacing = true;

        Debug.Log($"{name} initialized with {this.totalCheckpoints} checkpoints.");
    }

    public void ReachedCheckpoint(int index, bool isFinishLine)
    {
        if (!isRacing || totalCheckpoints <= 0) return;

        if (index == (currentCheckpoint + 1) % totalCheckpoints)
        {
            currentCheckpoint = index;
            Debug.Log($"{name} reached checkpoint {index}");

            if (isFinishLine && index == totalCheckpoints - 1)
            {
                currentLap++;
                Debug.Log($"{name} completed lap {currentLap}/{maxLaps}");

                LapCount.text = "Lap : " + currentLap + " / " + maxLaps;

                if (currentLap >= maxLaps)
                {
                    FinishRace();
                }
            }
        }
    }

    private void FinishRace()
    {
        isRacing = false;
        Debug.Log($"{name} FINISHED THE RACE!");
    }
}
