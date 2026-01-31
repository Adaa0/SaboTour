using UnityEngine;

public class CheckpointSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject prefabToSpawn;

    [Header("Preview")]
    [SerializeField] private GameObject previewPrefab;

    // ❌ Artık inspector’dan atanmasına gerek yok
    private ChickenTrapManager chickenTrapManager;
    private Transform trapPoint;

    private Transform currentSpawnPoint;
    private GameObject currentPreviewInstance;

    void Awake()
    {
        // Sahnede TrapManager adında objeyi bul
        GameObject managerObj = GameObject.Find("TrapManager");
        if (managerObj != null)
            chickenTrapManager = managerObj.GetComponent<ChickenTrapManager>();
        else
            Debug.LogWarning("TrapManager objesi bulunamadı!");

        // Sahnede ChickenTruck adında objeyi bul
        GameObject trapObj = GameObject.Find("ChickenTruck");
        if (trapObj != null)
            trapPoint = trapObj.transform;
        else
            Debug.LogWarning("ChickenTruck objesi bulunamadı!");
    }

    void Update()
    {
        // 0–9 checkpoint seçimi
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(i.ToString()))
                SelectCheckpoint(i);
        }

        // F ile spawn + preview sil
        if (Input.GetKeyDown(KeyCode.F))
        {
            SpawnPrefab();
            ClearPreview();
        }

        // G ile tavuk skill tetikleme
        if (Input.GetKeyDown(KeyCode.G))
            ActivateChickenSkill();
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

        ClearPreview();

        currentPreviewInstance = Instantiate(
            previewPrefab,
            currentSpawnPoint.position + Vector3.up * 1.5f,
            currentSpawnPoint.rotation
        );
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

        Instantiate(prefabToSpawn, currentSpawnPoint.position, currentSpawnPoint.rotation);
        Debug.Log("Prefab spawnlandı.");
    }

    void ActivateChickenSkill()
    {
        if (chickenTrapManager == null || trapPoint == null)
        {
            Debug.LogWarning("ChickenTrapManager veya trapPoint atanmadı!");
            return;
        }

        chickenTrapManager.ActivateChickenTrap(trapPoint);
        Debug.Log("Tavuk skill aktifleşti!");
    }
}
