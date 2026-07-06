using UnityEngine;
using System.Collections.Generic;

public class ChickenFlockManager : MonoBehaviour
{
    [Header("Tavuk Prefab")]
    [SerializeField] private GameObject chickenPrefab;

    [Header("Spawn Ayarları")]
    [SerializeField] private float spawnDistanceBehindCheckpoint = 100f;
    [SerializeField] private int minChickensPerFlock = 12;
    [SerializeField] private int maxChickensPerFlock = 15;
    [SerializeField] private float flockAreaSize = 8f;
    [SerializeField] private float minDistanceBetweenChickens = 1.5f;

    [Header("Parti Spawn Ayarları")]
    [SerializeField] private int numberOfWaves = 3;
    [SerializeField] private float delayBetweenWaves = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    private List<Vector3> spawnPoints = new List<Vector3>();
    private CheckpointManager checkpointManager;

    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();

        if (checkpointManager == null)
        {
            Debug.LogError("CheckpointManager bulunamadı!");
            return;
        }

        TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
        if (trackGenerator != null)
        {
            trackGenerator.onTrackGenerated.AddListener(OnTrackGenerated);
        }

        StartCoroutine(WaitForCheckpointsAndGenerate());
    }

    private void OnTrackGenerated()
    {
        StartCoroutine(GenerateAfterDelay());
    }

    private System.Collections.IEnumerator GenerateAfterDelay()
    {
        yield return new WaitForSeconds(2f);

        if (checkpointManager != null && checkpointManager.checkpoints != null && checkpointManager.checkpoints.Count > 0)
        {
            GenerateSpawnPoints();
        }
        else
        {
            yield return new WaitForSeconds(1f);
            if (checkpointManager != null && checkpointManager.checkpoints != null && checkpointManager.checkpoints.Count > 0)
                GenerateSpawnPoints();
            else
                Debug.LogError("Checkpoint'ler yüklenemedi! Manuel olarak R tuşuna basın.");
        }
    }

    private System.Collections.IEnumerator WaitForCheckpointsAndGenerate()
    {
        int attempts = 0;

        while (checkpointManager == null ||
               checkpointManager.checkpoints == null ||
               checkpointManager.checkpoints.Count == 0)
        {
            attempts++;
            yield return new WaitForSeconds(0.5f);

            if (attempts > 60)
            {
                Debug.LogError("30 saniye beklendi, checkpoint'ler yüklenemedi!");
                yield break;
            }
        }

        yield return null;
        GenerateSpawnPoints();
    }

    private void GenerateSpawnPoints()
    {
        spawnPoints.Clear();

        if (checkpointManager == null || checkpointManager.checkpoints == null || checkpointManager.checkpoints.Count < 2)
        {
            Debug.LogWarning("Yeterli checkpoint yok!");
            return;
        }

        for (int i = 0; i < checkpointManager.checkpoints.Count; i++)
        {
            Transform current = checkpointManager.checkpoints[i];
            if (current == null) continue;

            Vector3 checkpointPos = current.position;
            Vector3 directionFromOrigin = (checkpointPos - Vector3.zero).normalized;
            Vector3 spawnPoint = checkpointPos + (directionFromOrigin * spawnDistanceBehindCheckpoint);
            spawnPoint.y = 0.5f;

            spawnPoints.Add(spawnPoint);
        }

        Debug.Log($"{spawnPoints.Count} tavuk spawn noktası oluşturuldu.");
    }

    public void SpawnChickenFlockAtCheckpoint(int checkpointIndex)
    {
        if (chickenPrefab == null)
        {
            Debug.LogError("Tavuk prefab atanmamış!");
            return;
        }

        if (spawnPoints.Count == 0)
        {
            Debug.LogError("Spawn noktaları henüz oluşturulmamış!");
            return;
        }

        if (checkpointIndex < 0 || checkpointIndex >= spawnPoints.Count)
        {
            Debug.LogWarning($"Geçersiz checkpoint index: {checkpointIndex}");
            return;
        }

        StartCoroutine(SpawnWavesCoroutine(checkpointIndex));
    }

    private System.Collections.IEnumerator SpawnWavesCoroutine(int checkpointIndex)
    {
        Vector3 centerPoint = spawnPoints[checkpointIndex];
        int totalChickens = Random.Range(minChickensPerFlock, maxChickensPerFlock + 1);
        int chickensPerWave = Mathf.CeilToInt((float)totalChickens / numberOfWaves);

        List<Vector3> usedPositions = new List<Vector3>();
        int spawnedTotal = 0;

        for (int wave = 0; wave < numberOfWaves; wave++)
        {
            int chickensThisWave = Mathf.Min(chickensPerWave, totalChickens - spawnedTotal);

            for (int i = 0; i < chickensThisWave; i++)
            {
                Vector3 spawnPos = FindValidSpawnPosition(centerPoint, usedPositions);

                if (spawnPos != Vector3.zero)
                {
                    GameObject chicken = Instantiate(chickenPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                    chicken.name = $"Chicken_CP{checkpointIndex}_W{wave}_{i}";
                    usedPositions.Add(spawnPos);
                    spawnedTotal++;
                }
            }

            if (wave < numberOfWaves - 1)
                yield return new WaitForSeconds(delayBetweenWaves);
        }
    }

    private Vector3 FindValidSpawnPosition(Vector3 center, List<Vector3> usedPositions)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            float randomX = Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f);
            float randomZ = Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f);
            Vector3 candidate = center + new Vector3(randomX, 0, randomZ);
            candidate.y = 0.5f;

            bool isValid = true;
            foreach (Vector3 used in usedPositions)
            {
                if (Vector3.Distance(candidate, used) < minDistanceBetweenChickens)
                {
                    isValid = false;
                    break;
                }
            }

            if (isValid) return candidate;
        }

        return center + new Vector3(
            Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f),
            0,
            Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f)
        );
    }

    public void SpawnAllChickenFlocks()
    {
        for (int i = 0; i < spawnPoints.Count; i++)
            SpawnChickenFlockAtCheckpoint(i);
    }

    [ContextMenu("Regenerate Spawn Points")]
    public void RegenerateSpawnPoints()
    {
        if (checkpointManager == null)
            checkpointManager = FindAnyObjectByType<CheckpointManager>();

        if (checkpointManager == null || checkpointManager.checkpoints == null || checkpointManager.checkpoints.Count == 0)
        {
            Debug.LogError("CheckpointManager veya checkpoint'ler bulunamadı!");
            return;
        }

        GenerateSpawnPoints();
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || spawnPoints == null || spawnPoints.Count == 0) return;

        Gizmos.color = Color.yellow;
        foreach (Vector3 point in spawnPoints)
        {
            Gizmos.DrawWireSphere(point, 1f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(point, new Vector3(flockAreaSize, 0.1f, flockAreaSize));
        }

        if (checkpointManager != null && checkpointManager.checkpoints != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < Mathf.Min(spawnPoints.Count, checkpointManager.checkpoints.Count); i++)
            {
                if (checkpointManager.checkpoints[i] != null)
                    Gizmos.DrawLine(checkpointManager.checkpoints[i].position, spawnPoints[i]);
            }
        }
    }

    private void OnDestroy()
    {
        TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
        if (trackGenerator != null)
            trackGenerator.onTrackGenerated.RemoveListener(OnTrackGenerated);
    }
}