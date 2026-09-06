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

    [Header("Ses")]
    [Tooltip("Sürü ilk doğduğu anda spawn noktasında çalan tek seferlik toplu gıdaklama/kanat çırpma sesi. Boş bırakılabilir — asıl uyarı sürekli sesten (aşağısı + tek tek tavuklar) geliyor.")]
    [SerializeField] private AudioClip flockSpawnClip;
    [Range(0f, 1f)][SerializeField] private float flockSpawnVolume = 0.9f;

    [Tooltip("SÜREKLİ DÖNEN (loop) sürü sesi — sürü yaşadığı sürece sürünün ortasında " +
             "çalar, sürü koştukça takip eder, tavuklar azaldıkça kısılır, hepsi bitince susar. " +
             "'Skill geldi, bir şey yaklaşıyor' hissini asıl bu veriyor. Dikişsiz loop'lu bir " +
             "'çok tavuk / kümes ambiyansı' klibi koy. Boş bırakılırsa sadece tek tek tavukların " +
             "gıdaklaması kalır (o da geniş menzilli).")]
    [SerializeField] private AudioClip flockLoopClip;
    [Range(0f, 1f)][SerializeField] private float flockLoopVolume = 0.7f;
    [Tooltip("Loop sesinin kaç metreden itibaren kısılmaya başladığı / tamamen kesildiği.")]
    [SerializeField] private float flockLoopMinDistance = 20f;
    [SerializeField] private float flockLoopMaxDistance = 220f;

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

        // Sürünün TOPLU sesi sadece BİR KERE, ilk dalgadan hemen önce çalıyor
        // (her dalgada tekrar çalsaydı üst üste binip gürültü olurdu).
        // Menzil normalden geniş tutuldu: bu bir uyarı sesi, yarışçı sürüyü
        // görmeden önce duyabilmeli.
        SfxPlayer.PlayAt(flockSpawnClip, centerPoint, flockSpawnVolume, 0.05f, 15f, 200f);

        // Bu sürünün canlı tavukları — hem loop sesi hem merkez hesabı buna bakıyor.
        List<Transform> flock = new List<Transform>();
        int flockTargetCount = totalChickens;
        StartCoroutine(FlockAmbienceRoutine(flock, flockTargetCount));

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
                    flock.Add(chicken.transform);
                    spawnedTotal++;
                }
            }

            if (wave < numberOfWaves - 1)
                yield return new WaitForSeconds(delayBetweenWaves);
        }
    }

    /// <summary>
    /// SÜREKLİ SÜRÜ SESİ. Sürü yaşadığı sürece bir loop AudioSource'u sürünün
    /// ortasında tutar; tavuklar koştukça merkez kayar, tavuk sayısı azaldıkça
    /// ses kısılır, hepsi yok olunca kısa bir fade ile susup temizlenir.
    ///
    /// Neden ChickenFlockManager objesinin ALTINDA ayrı bir GameObject: bu obje
    /// sahnede kalıcı (yok olmuyor), ama loop kaynağının sürüyle birlikte gelip
    /// gitmesi gerekiyor. Ayrı obje = kolay Destroy + konumu bağımsız sürülüyor.
    ///
    /// `flockLoopClip` boşsa hiç kaynak kurulmuyor — tek tek tavukların
    /// gıdaklaması (Chicken.cs, geniş menzil) yine de "bir şey geliyor"u veriyor.
    /// </summary>
    private System.Collections.IEnumerator FlockAmbienceRoutine(List<Transform> flock, int targetCount)
    {
        if (flockLoopClip == null) yield break;

        GameObject go = new GameObject("FlockAmbience");
        go.transform.SetParent(transform, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = flockLoopClip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = flockLoopMinDistance;
        src.maxDistance = flockLoopMaxDistance;
        src.volume = 0f;
        src.Play();

        float safety = 0f;   // sürü bir şekilde hiç bitmezse (30sn) yine de kapan

        while (safety < 30f)
        {
            safety += Time.deltaTime;

            // Ölmüş/yok olmuş tavukları listeden at, merkezi hesapla.
            Vector3 sum = Vector3.zero;
            int alive = 0;
            for (int i = flock.Count - 1; i >= 0; i--)
            {
                if (flock[i] == null) { flock.RemoveAt(i); continue; }
                sum += flock[i].position;
                alive++;
            }

            if (alive == 0) break;

            go.transform.position = sum / alive;

            // Sürü inceldikçe ses kısılıyor (yarısı öldü = yarı ses).
            float fill = targetCount > 0 ? (float)alive / targetCount : 1f;
            float targetVol = flockLoopVolume * Mathf.Clamp01(fill) * AudioBus.WorldFinal * SfxPlayer.MasterVolume;
            src.volume = Mathf.MoveTowards(src.volume, targetVol, Time.deltaTime * 2f);

            yield return null;
        }

        // Sürü bitti — kısa fade, sonra temizle.
        float t = 0f;
        float startVol = src.volume;
        while (t < 0.4f)
        {
            t += Time.deltaTime;
            src.volume = Mathf.Lerp(startVol, 0f, t / 0.4f);
            yield return null;
        }

        Destroy(go);
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

    // ─────────────────────────────────────────────────────────────────────
    // GEÇİCİ ÇEKİM ARAÇLARI — Steam görselleri bitince SİLİNECEK.
    // (bkz. CLAUDE.md silme listesi)
    //
    // Normalde tavuk sürüsü sabotajcının skilini kullanmasıyla çağrılıyor.
    // Fotoğraf sahnesinde ne sabotajcı ne de lobi var, o yüzden aşağıdaki
    // menü komutlarıyla elle tetikliyoruz.
    //
    // NASIL KULLANILIR: Play modundayken ChickenFlockManager component'inin
    // sağ üstündeki üç noktaya (⋮) tıkla, açılan menüden seç.
    // ─────────────────────────────────────────────────────────────────────

    [Header("Fotoğraf Modu (GEÇİCİ — çekim bitince silinecek)")]
    [Tooltip("Aşağıdaki 'Tavuk Sürüsü Çağır' komutunun hangi checkpoint'e " +
             "sürü göndereceği. Kadrajındaki checkpoint hangisiyse onu yaz.")]
    [SerializeField] private int photoCheckpointIndex = 0;

    [ContextMenu("Fotoğraf: Tavuk Sürüsü Çağır")]
    private void PhotoSpawnFlock()
    {
        SpawnChickenFlockAtCheckpoint(photoCheckpointIndex);
    }

    /// <summary>
    /// Sahnedeki tüm tavukları olduğu yerde dondurur.
    ///
    /// F7 (FreezeFrame) yerine bunu kullanmanın avantajı: F7 tüm oyunu
    /// durduruyor, yani arabanın DUMANI ve LASTİK İZİ de donuyor. Bu komut
    /// sadece tavukların Update()'ini kapatıyor — tavuklar koşar pozisyonda
    /// kalıyor, araba ve efektleri canlı devam ediyor.
    /// </summary>
    [ContextMenu("Fotoğraf: Tavukları Dondur")]
    private void PhotoFreezeChickens()
    {
        Chicken[] chickens = FindObjectsByType<Chicken>(FindObjectsSortMode.None);

        foreach (Chicken chicken in chickens)
        {
            chicken.enabled = false;

            // ÖNEMLİ: Tavuklar Rigidbody ile hareket ediyor (Chicken.cs'te
            // rb.linearVelocity atanıyor). Sadece scripti kapatmak yetmez —
            // Rigidbody son hızını koruyup kaymaya devam eder. Hızı sıfırlayıp
            // isKinematic yapıyoruz ki ne momentum ne yerçekimi onu oynatsın,
            // arabaya çarpsa bile yerinden kıpırdamasın.
            Rigidbody rb = chicken.GetComponent<Rigidbody>();
            if (rb == null) continue;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        Debug.Log($"[ChickenFlockManager] {chickens.Length} tavuk donduruldu (momentum sıfırlandı).");
    }

    [ContextMenu("Fotoğraf: Tavukları Çöz")]
    private void PhotoUnfreezeChickens()
    {
        foreach (Chicken chicken in FindObjectsByType<Chicken>(FindObjectsSortMode.None))
        {
            Rigidbody rb = chicken.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            chicken.enabled = true;
        }
    }

    [ContextMenu("Fotoğraf: Tavukları Temizle")]
    private void PhotoClearChickens()
    {
        Chicken[] chickens = FindObjectsByType<Chicken>(FindObjectsSortMode.None);

        foreach (Chicken chicken in chickens)
        {
            if (chicken == null) continue;

            if (Application.isPlaying) Destroy(chicken.gameObject);
            else DestroyImmediate(chicken.gameObject);
        }

        Debug.Log($"[ChickenFlockManager] {chickens.Length} tavuk silindi.");
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