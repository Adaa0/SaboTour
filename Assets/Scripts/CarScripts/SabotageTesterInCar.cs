using UnityEngine;

public class CheckpointSpawner : MonoBehaviour
{
    [Header("Buz Bombası Spawn")]
    [SerializeField] private GameObject iceBombPrefab;   // Buz bombası prefab
    [SerializeField] private GameObject iceBombPreviewPrefab;   // Buz bombası preview

    [Header("Tavuk Sürüsü")]
    [SerializeField] private bool enableChickenFlock = true; // Tavuk sürüsünü aktif et

    private Transform currentSpawnPoint;
    private GameObject currentPreviewInstance;
    private int selectedCheckpointIndex = -1;

    private CheckpointManager checkpointManager;
    private ChickenFlockManager chickenFlockManager;

    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();
        
        if (checkpointManager == null)
        {
            Debug.LogError("❌ CheckpointManager bulunamadı!");
        }
        else
        {
            Debug.Log("✅ CheckpointManager bulundu");
        }
        
        if (enableChickenFlock)
        {
            chickenFlockManager = FindAnyObjectByType<ChickenFlockManager>();
            if (chickenFlockManager == null)
            {
                Debug.LogWarning("❌ ChickenFlockManager bulunamadı! Tavuk sürüsü spawn edilemeyecek.");
                Debug.LogWarning("💡 Sahneye bir GameObject ekleyip ChickenFlockManager script'ini ekleyin!");
            }
            else
            {
                Debug.Log("✅ ChickenFlockManager bulundu ve hazır!");
            }
        }
    }

    void Update()
    {
        // 0–9 checkpoint seçimi
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(i.ToString()))
                SelectCheckpoint(i);
        }

        // F ile buz bombası spawn + preview sil
        if (Input.GetKeyDown(KeyCode.F))
        {
            SpawnIceBomb();
            ClearPreview();
        }

        // E ile tavuk sürüsü spawn
        if (Input.GetKeyDown(KeyCode.E))
        {
            SpawnChickenFlock();
        }

        // R ile spawn noktalarını yeniden oluştur (debug için)
        if (Input.GetKeyDown(KeyCode.R))
        {
            RegenerateChickenSpawnPoints();
        }
    }

    void RegenerateChickenSpawnPoints()
    {
        if (chickenFlockManager == null)
        {
            chickenFlockManager = FindAnyObjectByType<ChickenFlockManager>();
        }

        if (chickenFlockManager != null)
        {
            Debug.Log("🔄 R tuşuna basıldı - Spawn noktaları yeniden oluşturuluyor...");
            chickenFlockManager.RegenerateSpawnPoints();
        }
        else
        {
            Debug.LogWarning("❌ ChickenFlockManager bulunamadı!");
        }
    }

    void SelectCheckpoint(int index)
    {
        if (checkpointManager == null)
        {
            Debug.LogWarning("CheckpointManager yok.");
            return;
        }

        if (index < 0 || index >= checkpointManager.checkpoints.Count)
        {
            Debug.LogWarning("Checkpoint " + index + " yok.");
            return;
        }

        currentSpawnPoint = checkpointManager.checkpoints[index];
        selectedCheckpointIndex = index;
        Debug.Log("Checkpoint seçildi: " + index);

        ShowPreview();
    }

    void ShowPreview()
    {
        if (iceBombPreviewPrefab == null || currentSpawnPoint == null)
            return;

        ClearPreview();

        currentPreviewInstance = Instantiate(
            iceBombPreviewPrefab,
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

    void SpawnIceBomb()
    {
        if (iceBombPrefab == null || currentSpawnPoint == null)
        {
            Debug.LogWarning("Buz bombası prefab veya checkpoint seçilmemiş.");
            return;
        }

        Instantiate(
            iceBombPrefab,
            currentSpawnPoint.position,
            currentSpawnPoint.rotation
        );

        Debug.Log("Buz bombası spawnlandı - Checkpoint " + selectedCheckpointIndex);
    }

    void SpawnChickenFlock()
    {
        Debug.Log("🐔 E tuşuna basıldı - Tavuk spawn işlemi başlıyor...");

        if (!enableChickenFlock)
        {
            Debug.LogWarning("❌ Tavuk sürüsü aktif değil! CheckpointSpawner'da 'Enable Chicken Flock' seçeneğini aç.");
            return;
        }

        if (chickenFlockManager == null)
        {
            Debug.LogWarning("❌ ChickenFlockManager bulunamadı! Sahneye ekle.");
            return;
        }

        if (selectedCheckpointIndex < 0)
        {
            Debug.LogWarning("❌ Önce bir checkpoint seçin! (0-9 tuşları ile)");
            return;
        }

        Debug.Log($"✅ Checkpoint {selectedCheckpointIndex} seçili - Tavuk spawn ediliyor...");
        chickenFlockManager.SpawnChickenFlockAtCheckpoint(selectedCheckpointIndex);
        Debug.Log($"✅ Tavuk sürüsü spawn komutu gönderildi - Checkpoint {selectedCheckpointIndex}");
    }
}