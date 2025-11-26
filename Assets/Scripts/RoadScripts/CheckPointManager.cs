using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class CheckpointManager : MonoBehaviour
{
    public List<Transform> checkpoints = new List<Transform>();

    private bool checkpointsLoaded = false;

    void Update()
    {
        // Eğer daha önce listeyi doldurmadıysak ara
        if (!checkpointsLoaded)
        {
            GameObject[] found = GameObject.FindGameObjectsWithTag("Checkpoint");
            if (found.Length > 0)
            {
                checkpoints = found
                    .OrderBy(obj => ExtractIndex(obj.name))
                    .Select(obj => obj.transform)
                    .ToList();

                checkpointsLoaded = true;
                Debug.Log("CheckpointManager: " + checkpoints.Count + " checkpoint yüklendi.");
            }
        }
    }

    /// <summary>
    /// Yol scripti spawn ettiği anda listeye ekleme
    /// </summary>
    public void AddCheckpoint(Transform t)
    {
        if (!checkpoints.Contains(t))
        {
            checkpoints.Add(t);
            checkpoints = checkpoints.OrderBy(cp => ExtractIndex(cp.name)).ToList();
        }
    }

    /// <summary>
    /// Checkpoint isiminden index çıkartır. Checkpoint_0 -> 0
    /// </summary>
    private int ExtractIndex(string name)
    {
        string[] parts = name.Split('_');
        if (parts.Length < 2) return 0;

        int index;
        int.TryParse(parts[1], out index);
        return index;
    }
}
