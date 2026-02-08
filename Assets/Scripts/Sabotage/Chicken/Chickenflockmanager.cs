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
    [SerializeField] private float flockAreaSize = 8f; // Sürünün yayılacağı alan boyutu
    [SerializeField] private float minDistanceBetweenChickens = 1.5f; // Tavuklar arası minimum mesafe

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool createVisualMarkers = true; // Sahneye görsel marker koy
    [SerializeField] private Color markerColor = Color.yellow;

    private List<Vector3> spawnPoints = new List<Vector3>();
    private List<GameObject> visualMarkers = new List<GameObject>();
    private CheckpointManager checkpointManager;

    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();
        
        if (checkpointManager == null)
        {
            Debug.LogError("CheckpointManager bulunamadı! Tavuk spawn noktaları oluşturulamıyor.");
            return;
        }

        // TrackGenerator'ı da bul
        TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
        if (trackGenerator != null)
        {
            Debug.Log("✅ TrackGenerator bulundu - Event'e bağlanılıyor...");
            trackGenerator.onTrackGenerated.AddListener(OnTrackGenerated);
        }
        else
        {
            Debug.LogWarning("⚠️ TrackGenerator bulunamadı - Manuel kontrol başlatılıyor...");
        }

        // Yol oluşturulana kadar bekle
        StartCoroutine(WaitForCheckpointsAndGenerate());
    }

    /// <summary>
    /// TrackGenerator yol oluşturduğunda çağrılır
    /// </summary>
    private void OnTrackGenerated()
    {
        Debug.Log("🎉 TrackGenerator yolu oluşturdu - Tavuk spawn noktaları hazırlanıyor!");
        
        // Biraz bekle ki checkpoint'ler kesin yüklensin
        StartCoroutine(GenerateAfterDelay());
    }

    private System.Collections.IEnumerator GenerateAfterDelay()
    {
        Debug.Log("⏳ CheckpointManager'ın checkpoint'leri yüklemesi bekleniyor...");
        
        // 2 saniye bekle
        yield return new WaitForSeconds(2f);
        
        // Checkpoint'leri tekrar kontrol et
        if (checkpointManager != null && checkpointManager.checkpoints != null && checkpointManager.checkpoints.Count > 0)
        {
            Debug.Log($"✅ {checkpointManager.checkpoints.Count} checkpoint hazır - Spawn noktaları oluşturuluyor!");
            GenerateSpawnPoints();
        }
        else
        {
            Debug.LogWarning("⚠️ Checkpoint'ler hala yüklenmedi, tekrar deneniyor...");
            yield return new WaitForSeconds(1f);
            
            if (checkpointManager != null && checkpointManager.checkpoints != null && checkpointManager.checkpoints.Count > 0)
            {
                GenerateSpawnPoints();
            }
            else
            {
                Debug.LogError("❌ Checkpoint'ler yüklenemedi! Manuel olarak R tuşuna basın.");
            }
        }
    }

    private System.Collections.IEnumerator WaitForCheckpointsAndGenerate()
    {
        Debug.Log("🔄 Checkpoint'ler yüklenene kadar bekleniyor...");
        
        int attempts = 0;
        // Checkpoint'ler yüklenene kadar bekle
        while (checkpointManager == null || 
               checkpointManager.checkpoints == null || 
               checkpointManager.checkpoints.Count == 0)
        {
            attempts++;
            
            if (checkpointManager != null && checkpointManager.checkpoints != null)
            {
                Debug.Log($"⏳ Checkpoint sayısı: {checkpointManager.checkpoints.Count} - Bekleniyor...");
            }
            
            if (attempts % 10 == 0) // Her 5 saniyede bir log
            {
                Debug.Log($"⏳ Hala bekleniyor... Deneme: {attempts}");
            }
            
            yield return new WaitForSeconds(0.5f);
            
            // 30 saniyeden fazla bekleme
            if (attempts > 60)
            {
                Debug.LogError("❌ 30 saniye beklendi, checkpoint'ler hala yüklenemedi!");
                yield break;
            }
        }

        Debug.Log($"✅ Checkpoint'ler bulundu! Sayı: {checkpointManager.checkpoints.Count}");

        // Bir frame daha bekle, emin olmak için
        yield return null;

        // Spawn noktalarını oluştur
        GenerateSpawnPoints();
    }

    /// <summary>
    /// Her checkpoint için spawn noktası hesapla
    /// </summary>
    private void GenerateSpawnPoints()
    {
        spawnPoints.Clear();
        ClearVisualMarkers();

        if (checkpointManager == null)
        {
            Debug.LogError("❌ CheckpointManager null!");
            return;
        }

        if (checkpointManager.checkpoints == null)
        {
            Debug.LogError("❌ checkpoints list null!");
            return;
        }

        if (checkpointManager.checkpoints.Count < 2)
        {
            Debug.LogWarning($"❌ Yeterli checkpoint yok! Mevcut: {checkpointManager.checkpoints.Count}");
            return;
        }

        Debug.Log($"🔄 {checkpointManager.checkpoints.Count} checkpoint için spawn noktası oluşturuluyor...");

        for (int i = 0; i < checkpointManager.checkpoints.Count; i++)
        {
            Transform currentCheckpoint = checkpointManager.checkpoints[i];
            
            if (currentCheckpoint == null)
            {
                Debug.LogWarning($"⚠️ Checkpoint {i} null!");
                continue;
            }

            // Checkpoint'in konumu
            Vector3 checkpointPos = currentCheckpoint.position;

            // 0,0,0 noktasından checkpoint'e doğru vektör (YÖN)
            Vector3 directionFromOrigin = (checkpointPos - Vector3.zero).normalized;

            // Bu çizgiyi 100 birim daha uzat (checkpoint'i geçerek devam et)
            Vector3 spawnPoint = checkpointPos + (directionFromOrigin * spawnDistanceBehindCheckpoint);

            // Y pozisyonunu 0 yap (zemin seviyesi)
            spawnPoint.y = 0.5f;

            spawnPoints.Add(spawnPoint);

            Debug.Log($"✅ Spawn noktası {i} oluşturuldu: {spawnPoint}");

            // Debug için çizgiyi göster
            Debug.DrawLine(Vector3.zero, checkpointPos, Color.red, 10f);
            Debug.DrawLine(checkpointPos, spawnPoint, Color.green, 10f);

            // Görsel marker oluştur
            if (createVisualMarkers)
            {
                CreateVisualMarker(spawnPoint, i);
            }
        }

        Debug.Log($"✅ {spawnPoints.Count} tavuk spawn noktası oluşturuldu ve görselleştirildi!");
    }

    /// <summary>
    /// Spawn noktasında görsel marker oluştur
    /// </summary>
    private void CreateVisualMarker(Vector3 position, int index)
    {
        // Sphere oluştur
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = $"ChickenSpawnMarker_{index}";
        marker.transform.position = position;
        marker.transform.localScale = Vector3.one * 2f;

        // Renk ver
        Renderer rend = marker.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = markerColor;
            mat.SetFloat("_Metallic", 0.5f);
            mat.SetFloat("_Glossiness", 0.8f);
            rend.material = mat;
        }

        // Collider'ı kaldır (sadece görsel)
        Collider col = marker.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        // Text ekle
        GameObject textObj = new GameObject($"Text_{index}");
        textObj.transform.SetParent(marker.transform);
        textObj.transform.localPosition = Vector3.up * 2f;

        TextMesh textMesh = textObj.AddComponent<TextMesh>();
        textMesh.text = $"Chicken Spawn {index}";
        textMesh.fontSize = 50;
        textMesh.color = Color.white;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.1f;

        visualMarkers.Add(marker);
    }

    /// <summary>
    /// Görsel markerları temizle
    /// </summary>
    private void ClearVisualMarkers()
    {
        foreach (GameObject marker in visualMarkers)
        {
            if (marker != null)
            {
                Destroy(marker);
            }
        }
        visualMarkers.Clear();
    }

    /// <summary>
    /// Belirtilen checkpoint index'ine tavuk sürüsü spawn et
    /// </summary>
    public void SpawnChickenFlockAtCheckpoint(int checkpointIndex)
    {
        Debug.Log($"🐔 SpawnChickenFlockAtCheckpoint çağrıldı - Index: {checkpointIndex}");

        if (chickenPrefab == null)
        {
            Debug.LogError("❌ Tavuk prefab atanmamış! ChickenFlockManager Inspector'ında 'Chicken Prefab' alanını doldur!");
            return;
        }

        if (spawnPoints.Count == 0)
        {
            Debug.LogError("❌ Spawn noktaları henüz oluşturulmamış! Yol generate edildi mi?");
            return;
        }

        if (checkpointIndex < 0 || checkpointIndex >= spawnPoints.Count)
        {
            Debug.LogWarning($"❌ Geçersiz checkpoint index: {checkpointIndex} (Max: {spawnPoints.Count - 1})");
            return;
        }

        Vector3 centerPoint = spawnPoints[checkpointIndex];
        int chickenCount = Random.Range(minChickensPerFlock, maxChickensPerFlock + 1);

        Debug.Log($"📍 Spawn pozisyonu: {centerPoint}");
        Debug.Log($"🐔 Spawn edilecek tavuk sayısı: {chickenCount}");

        // Tavukları spawn et
        List<Vector3> usedPositions = new List<Vector3>();

        for (int i = 0; i < chickenCount; i++)
        {
            Vector3 spawnPos = FindValidSpawnPosition(centerPoint, usedPositions);
            
            if (spawnPos != Vector3.zero)
            {
                // Tavuğu spawn et
                GameObject chicken = Instantiate(chickenPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                chicken.name = $"Chicken_{checkpointIndex}_{i}";
                
                usedPositions.Add(spawnPos);
                Debug.Log($"✅ Tavuk {i} spawn edildi: {spawnPos}");
            }
            else
            {
                Debug.LogWarning($"⚠️ Tavuk {i} için pozisyon bulunamadı!");
            }
        }

        Debug.Log($"✅ Checkpoint {checkpointIndex}'de toplam {usedPositions.Count} tavuk spawn edildi!");
    }

    /// <summary>
    /// Diğer tavuklarla çakışmayan geçerli bir pozisyon bul
    /// </summary>
    private Vector3 FindValidSpawnPosition(Vector3 center, List<Vector3> usedPositions)
    {
        int maxAttempts = 30;
        
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Rastgele pozisyon üret (kare alan içinde)
            float randomX = Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f);
            float randomZ = Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f);
            
            Vector3 candidatePos = center + new Vector3(randomX, 0, randomZ);
            candidatePos.y = 0.5f; // Zemin seviyesi

            // Diğer tavuklarla mesafe kontrolü
            bool isValid = true;
            foreach (Vector3 usedPos in usedPositions)
            {
                if (Vector3.Distance(candidatePos, usedPos) < minDistanceBetweenChickens)
                {
                    isValid = false;
                    break;
                }
            }

            if (isValid)
            {
                return candidatePos;
            }
        }

        Debug.LogWarning("Geçerli pozisyon bulunamadı, son deneme pozisyonu kullanılıyor.");
        return center + new Vector3(Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f), 0, Random.Range(-flockAreaSize / 2f, flockAreaSize / 2f));
    }

    /// <summary>
    /// Tüm checkpoint'lere tavuk spawn et (test için)
    /// </summary>
    public void SpawnAllChickenFlocks()
    {
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            SpawnChickenFlockAtCheckpoint(i);
        }
    }

    /// <summary>
    /// Spawn noktalarını manuel olarak yeniden oluştur
    /// </summary>
    [ContextMenu("Regenerate Spawn Points")]
    public void RegenerateSpawnPoints()
    {
        Debug.Log("🔄 Spawn noktaları manuel olarak yeniden oluşturuluyor...");
        
        if (checkpointManager == null)
        {
            checkpointManager = FindAnyObjectByType<CheckpointManager>();
        }

        if (checkpointManager == null || checkpointManager.checkpoints == null || checkpointManager.checkpoints.Count == 0)
        {
            Debug.LogError("❌ CheckpointManager veya checkpoint'ler bulunamadı!");
            return;
        }

        GenerateSpawnPoints();
    }

    /// <summary>
    /// Spawn noktalarının durumunu kontrol et
    /// </summary>
    [ContextMenu("Check Spawn Points Status")]
    public void CheckSpawnPointsStatus()
    {
        Debug.Log("=== TAVUK SPAWN NOKTALARI DURUM RAPORU ===");
        Debug.Log($"CheckpointManager: {(checkpointManager != null ? "✅ Var" : "❌ Yok")}");
        
        if (checkpointManager != null)
        {
            Debug.Log($"Checkpoint Sayısı: {checkpointManager.checkpoints?.Count ?? 0}");
        }
        
        Debug.Log($"Spawn Noktası Sayısı: {spawnPoints.Count}");
        Debug.Log($"Görsel Marker Sayısı: {visualMarkers.Count}");
        Debug.Log($"Tavuk Prefab: {(chickenPrefab != null ? "✅ Atanmış" : "❌ Atanmamış")}");
        Debug.Log("=========================================");
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || spawnPoints == null || spawnPoints.Count == 0) return;

        // Spawn noktalarını göster
        Gizmos.color = Color.yellow;
        foreach (Vector3 point in spawnPoints)
        {
            Gizmos.DrawWireSphere(point, 1f);
            
            // Sürü alanını göster
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(point, new Vector3(flockAreaSize, 0.1f, flockAreaSize));
        }

        // Checkpoint'lerden spawn noktalarına çizgi çek
        if (checkpointManager != null && checkpointManager.checkpoints != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < Mathf.Min(spawnPoints.Count, checkpointManager.checkpoints.Count); i++)
            {
                if (checkpointManager.checkpoints[i] != null)
                {
                    Gizmos.DrawLine(checkpointManager.checkpoints[i].position, spawnPoints[i]);
                }
            }
        }
    }

    private void OnDestroy()
    {
        ClearVisualMarkers();
        
        // Event listener'ı temizle
        TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
        if (trackGenerator != null)
        {
            trackGenerator.onTrackGenerated.RemoveListener(OnTrackGenerated);
        }
    }
}