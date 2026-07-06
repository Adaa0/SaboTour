using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class CheckpointManager : MonoBehaviour
{
    public List<Transform> checkpoints = new List<Transform>();

    void Start()
    {
        LoadCheckpoints();

        if (checkpoints.Count == 0)
        {
            TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
            if (trackGenerator != null)
                trackGenerator.onTrackGenerated.AddListener(LoadCheckpoints);

            StartCoroutine(RetryUntilLoaded());
        }
    }

    private IEnumerator RetryUntilLoaded()
    {
        while (checkpoints.Count == 0)
        {
            yield return new WaitForSeconds(0.5f);
            LoadCheckpoints();
        }
    }

    private void LoadCheckpoints()
    {
        GameObject[] found = GameObject.FindGameObjectsWithTag("Checkpoint");
        if (found.Length == 0) return;

        checkpoints = found
            .OrderBy(obj => ExtractIndex(obj.name))
            .Select(obj => obj.transform)
            .ToList();

        Debug.Log("CheckpointManager: " + checkpoints.Count + " checkpoint yüklendi.");
    }

    public void AddCheckpoint(Transform t)
    {
        if (!checkpoints.Contains(t))
        {
            checkpoints.Add(t);
            checkpoints = checkpoints.OrderBy(cp => ExtractIndex(cp.name)).ToList();
        }
    }

    private int ExtractIndex(string name)
    {
        string[] parts = name.Split('_');
        if (parts.Length < 2) return 0;
        int.TryParse(parts[1], out int index);
        return index;
    }

    private void OnDestroy()
    {
        TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
        if (trackGenerator != null)
            trackGenerator.onTrackGenerated.RemoveListener(LoadCheckpoints);
    }
}