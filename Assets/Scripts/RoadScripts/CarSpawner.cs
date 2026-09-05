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

        GameObject startLine = checkpoints[0];
        if (startLine == null)
        {
            Debug.LogError("Start checkpoint is missing!");
            return;
        }

        
        Vector3 spawnPosition = startLine.transform.position - Vector3.up * 3f; 
        Quaternion spawnRotation = startLine.transform.rotation;
        
        GameObject car = Instantiate(carPrefab, spawnPosition, spawnRotation);
        car.name = "PlayerCar";

       
        car.tag = "Player";

        if (!car.GetComponent<Rigidbody>())
        {
            car.AddComponent<Rigidbody>();
        }

        // NOT: PlayerRaceController artık NetworkBehaviour — checkpoint sayısı
        // ve yarış durumu server tarafından OnStartServer()'da otomatik
        // ayarlanıyor (bkz. PlayerRaceControl.cs). Bu yüzden burada manuel
        // Initialize() çağrısına gerek kalmadı.

        Debug.Log($"Car spawned at start line (checkpoint 0): {spawnPosition}");
    }

    private void OnDestroy()
    {
        if (trackGenerator != null)
            trackGenerator.onTrackGenerated.RemoveListener(SpawnCarAtFinishLine);
    }
}