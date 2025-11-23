using UnityEngine;

public class CarSpawner : MonoBehaviour
{
    [Header("References")]
    public TrackGenerator trackGenerator;
    public GameObject carPrefab;

    [Header("Spawn Settings")]
    public bool spawnOnStart = true;
    public bool autoInitializePlayer = true;

    private void Start()
    {
        if (trackGenerator == null)
        {
            Debug.LogError("TrackGenerator not assigned to CarSpawner!");
            return;
        }

        trackGenerator.onTrackGenerated.AddListener(SpawnCarAtFinishLine);

        if (spawnOnStart && trackGenerator.GetCheckpoints().Count > 0)
        {
            SpawnCarAtFinishLine();
        }
    }

    public void SpawnCarAtFinishLine()
    {
        if (carPrefab == null)
        {
            Debug.LogError("Car prefab not assigned!");
            return;
        }

        var checkpoints = trackGenerator.GetCheckpoints();
        if (checkpoints == null || checkpoints.Count == 0)
        {
            Debug.LogWarning("No checkpoints available. Generate track first.");
            return;
        }

        // Son checkpoint = finish line
        GameObject finishLine = checkpoints[checkpoints.Count - 1];
        if (finishLine == null)
        {
            Debug.LogError("Finish line checkpoint is missing!");
            return;
        }

        // Aracı finish line üzerinde spawn et
        Vector3 spawnPosition = finishLine.transform.position - Vector3.up * 3f; // Yol seviyesine indir
        Quaternion spawnRotation = finishLine.transform.rotation;
        
        GameObject car = Instantiate(carPrefab, spawnPosition, spawnRotation);
        car.name = "PlayerCar";

        // Player tag'ini ata
        car.tag = "Player";

        // Rigidbody kontrolü
        if (!car.GetComponent<Rigidbody>())
        {
            car.AddComponent<Rigidbody>();
        }

        // PlayerRaceController'ı başlat
        if (autoInitializePlayer)
        {
            var playerController = car.GetComponent<PlayerRaceController>();
            if (playerController != null)
            {
                playerController.Initialize(trackGenerator.checkpointsPerLap);
            }
            else
            {
                Debug.LogWarning("PlayerRaceController not found on car prefab!");
            }
        }
        
        Debug.Log($"Car spawned at finish line: {spawnPosition}");
    }

    private void OnDestroy()
    {
        if (trackGenerator != null)
            trackGenerator.onTrackGenerated.RemoveListener(SpawnCarAtFinishLine);
    }
}