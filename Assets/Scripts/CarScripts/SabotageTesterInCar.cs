using UnityEngine;

public class CheckpointSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject prefabToSpawn;

    [Header("Preview")]
    [SerializeField] private GameObject previewPrefab; // <-- ÜSTÜNDE ÇIKACAK OLAN

    private Transform currentSpawnPoint;
    private GameObject currentPreviewInstance; // aktif preview

    void Update()
    {
        // 0–9 checkpoint seçimi
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(i.ToString()))
            {
                SelectCheckpoint(i);
            }
        }

        // F ile spawn + preview sil
        if (Input.GetKeyDown(KeyCode.F))
        {
            SpawnPrefab();
            ClearPreview();
        }
    }

    void SelectCheckpoint(int index)
    {
        CheckpointManager manager = FindAnyObjectByType<CheckpointManager>();

        if (manager == null)
        {
            Debug.LogWarning("CheckpointManager yok.");
            return;
        }

        if (index < 0 || index >= manager.checkpoints.Count)
        {
            Debug.LogWarning("Checkpoint " + index + " yok.");
            return;
        }

        currentSpawnPoint = manager.checkpoints[index];
        Debug.Log("Checkpoint seçildi: " + index);

        ShowPreview();
    }

    void ShowPreview()
    {
        if (previewPrefab == null || currentSpawnPoint == null)
            return;

        // Önce eski preview varsa sil
        ClearPreview();

        currentPreviewInstance = Instantiate(
            previewPrefab,
            currentSpawnPoint.position,
            currentSpawnPoint.rotation
        );

        // Hafif yukarı al (checkpoint'in içine gömülmesin)
        currentPreviewInstance.transform.position += Vector3.up * 1.5f;
    }

    void ClearPreview()
    {
        if (currentPreviewInstance != null)
        {
            Destroy(currentPreviewInstance);
            currentPreviewInstance = null;
        }
    }

    void SpawnPrefab()
    {
        if (prefabToSpawn == null || currentSpawnPoint == null)
        {
            Debug.LogWarning("Prefab veya checkpoint seçilmemiş.");
            return;
        }

        Instantiate(
            prefabToSpawn,
            currentSpawnPoint.position,
            currentSpawnPoint.rotation
        );

        Debug.Log("Prefab spawnlandı.");
    }
}
