using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PİST ÇEVRESİ PROP SERPİŞTİRİCİ
///
/// Pist prosedürel üretildiği için ağaç/kaya/çim gibi objeleri elle
/// yerleştirmek imkânsız — her oyunda pist farklı yerde oluşuyor. Bu script
/// pist üretildikten sonra yolun İKİ YANINA otomatik olarak prop diziyor.
///
/// TrackGenerator.GetTrackPoints() ile yolun gerçek eğrisini alıyor, her
/// noktada gidiş yönüne dik (perpendicular) yönde yoldan belirli bir mesafe
/// uzağa prop koyuyor — yani proplar yolu takip ediyor, üstüne binmiyor.
///
/// DETERMİNİZM (multiplayer için önemli): Rastgelelik TrackGenerator'ın
/// SEED'inden türetiliyor. Seed host'tan client'lara TrackSeedSync ile zaten
/// gidiyor, dolayısıyla herkeste AYNI ağaçlar AYNI yerde çıkıyor — ekstra
/// network mesajı gerekmiyor (IceBombSkill'deki mantığın aynısı).
/// Proplar sadece görsel; collider'ları isteğe bağlı.
///
/// KULLANIM:
///  1. TrackGenerator'ın olduğu objeye (ya da boş bir GameObject'e) ekle.
///  2. propPrefabs listesine ağaç/kaya/çim modellerini sürükle
///     (ör. BOXOPHOBIC/Skybox Cubemap Extended/Demo/Meshes altındaki
///     "LowPoly - FirTree A/B", "Rock A/B", "Grass A/B").
///  3. Play'e bas — pist üretilince proplar otomatik dizilir.
/// </summary>
public class TrackPropScatter : MonoBehaviour
{
    [Header("Proplar")]
    [Tooltip("Yol kenarına dizilecek modeller — listeden rastgele seçilir.")]
    [SerializeField] private GameObject[] propPrefabs;

    [Header("Yoğunluk")]
    [Tooltip("Yol boyunca kaç birimde bir prop denemesi yapılsın. Küçük değer = daha sık.")]
    [SerializeField] private float spacing = 6f;
    [Tooltip("Her denemede prop koyma ihtimali (1 = her seferinde, 0.5 = yarısında). Düzenli sıra görünümünü kırar.")]
    [Range(0f, 1f)]
    [SerializeField] private float spawnChance = 0.85f;
    [Tooltip("Yol boyunca her durakta, HER TARAF için kaç prop denensin. " +
             "1 = eski davranış (yolu takip eden tek sıra). Büyütürsen çevre " +
             "bir şerit değil, gerçek bir orman gibi dolar.")]
    [Range(1, 10)]
    [SerializeField] private int propsPerSide = 3;
    [SerializeField] private bool bothSides = true;

    [Header("Yoldan Uzaklık")]
    [Tooltip("Yol KENARINDAN itibaren en yakın mesafe (yolun yarı genişliği otomatik ekleniyor).")]
    [SerializeField] private float minDistanceFromRoad = 4f;
    [Tooltip("Yol kenarından en uzak mesafe. Bunu büyütmek 'çevre'yi büyüten " +
             "asıl ayar — proplar yola yapışık bir şerit yerine geniş bir alana yayılır.")]
    [SerializeField] private float maxDistanceFromRoad = 120f;

    [Tooltip("Propların yol yönünde rastgele kaydırılma miktarı (metre). " +
             "0 ise proplar düzgün sıralar halinde dizilir ve yapay görünür.")]
    [SerializeField] private float forwardJitter = 4f;

    [Tooltip("Güvenlik sınırı — toplam prop sayısı bunu aşmaz. " +
             "Yoğunluk ayarlarını fazla açarsan sahne kilitlenmesin diye.")]
    [SerializeField] private int maxProps = 2500;

    [Header("Çeşitlilik")]
    [SerializeField] private bool randomYaw = true;
    [SerializeField] private float minScale = 0.8f;
    [SerializeField] private float maxScale = 1.4f;
    [Tooltip("Prop'un dikey konumu — yolun yüksekliğine göre kaydırma.")]
    [SerializeField] private float heightOffset = 0f;

    [Header("Performans / Fizik")]
    [Tooltip("Propların collider'ları kaldırılsın mı? Sadece dekorsa AÇIK bırak — çok sayıda collider performansı düşürür.")]
    [SerializeField] private bool removeColliders = true;

    [Tooltip("Prop dizilimi pistin seed'inden türetiliyor, yani aynı pistte hep " +
             "aynı dizilim çıkar. Bu sayıyı değiştirirsen PİST AYNI KALIR ama " +
             "ağaç/kaya dizilimi tamamen değişir. Inspector'daki 'Farklı Dizilim " +
             "Dene' butonu bunu 1 artırıyor.\n\n" +
             "NETWORK NOTU: Bu değer sahneyle birlikte kaydedildiği için tüm " +
             "oyuncularda aynı olur — dizilim senkron kalır, ekstra mesaj gerekmez.")]
    [SerializeField] private int propSeedOffset = 0;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    private TrackGenerator trackGenerator;
    private Transform propContainer;

    void Start()
    {
        trackGenerator = GetComponent<TrackGenerator>();
        if (trackGenerator == null)
            trackGenerator = FindAnyObjectByType<TrackGenerator>();

        if (trackGenerator == null)
        {
            Debug.LogWarning("[TrackPropScatter] TrackGenerator bulunamadı — prop serpiştirilemiyor.");
            return;
        }

        // Pist her üretildiğinde propları yeniden diz (CheckpointManager'ın
        // onTrackGenerated'e abone olmasıyla aynı desen).
        trackGenerator.onTrackGenerated.AddListener(Scatter);

        // Bu script geç başlarsa pist çoktan üretilmiş olabilir — o durumda
        // event bir daha tetiklenmez, bu yüzden bir kere de burada deniyoruz.
        if (trackGenerator.GetTrackPoints() != null)
            Scatter();
    }

    void OnDestroy()
    {
        if (trackGenerator != null)
            trackGenerator.onTrackGenerated.RemoveListener(Scatter);
    }

    public void Scatter()
    {
        // Start() sadece Play modunda çalışıyor, bu yüzden editör butonundan
        // çağrıldığında trackGenerator henüz atanmamış olur — burada çözüyoruz.
        if (trackGenerator == null)
        {
            trackGenerator = GetComponent<TrackGenerator>();
            if (trackGenerator == null)
                trackGenerator = FindAnyObjectByType<TrackGenerator>();
        }

        if (trackGenerator == null)
        {
            Debug.LogWarning("[TrackPropScatter] TrackGenerator bulunamadı — prop serpiştirilemiyor.");
            return;
        }

        if (propPrefabs == null || propPrefabs.Length == 0)
        {
            if (showDebugLogs)
                Debug.LogWarning("[TrackPropScatter] propPrefabs listesi boş — Inspector'dan ağaç/kaya modelleri ekle.");
            return;
        }

        List<Vector3> trackPoints = trackGenerator.GetTrackPoints();
        if (trackPoints == null || trackPoints.Count < 3) return;

        ClearProps();

        propContainer = new GameObject("TrackProps").transform;
        propContainer.SetParent(transform, false);

        // Seed'den türetilen rastgelelik → her client'ta aynı sonuç.
        // UnityEngine.Random yerine System.Random: global Random durumunu
        // bozmuyor (başka sistemler ondan sayı çekiyor olabilir).
        System.Random rng = new System.Random(trackGenerator.seed + propSeedOffset);

        float halfRoad = trackGenerator.roadWidth * 0.5f;
        float distanceSinceLastProp = 0f;
        int placedCount = 0;

        for (int i = 0; i < trackPoints.Count; i++)
        {
            Vector3 curr = trackPoints[i];
            Vector3 next = trackPoints[(i + 1) % trackPoints.Count];

            distanceSinceLastProp += Vector3.Distance(curr, next);
            if (distanceSinceLastProp < spacing) continue;
            distanceSinceLastProp = 0f;

            Vector3 dir = next - curr;
            if (dir.sqrMagnitude < 0.0001f) continue;
            Vector3 right = Vector3.Cross(Vector3.up, dir.normalized).normalized;

            Vector3 forward = dir.normalized;

            // Sağ ve/veya sol taraf
            int sideCount = bothSides ? 2 : 1;
            for (int s = 0; s < sideCount; s++)
            {
                // Her tarafta birden fazla prop deneniyor — çevrenin bir şerit
                // değil, dolu bir alan gibi görünmesini sağlayan kısım bu.
                for (int p = 0; p < propsPerSide; p++)
                {
                    if (placedCount >= maxProps) break;
                    if (NextFloat(rng) > spawnChance) continue;

                    float side = (s == 0) ? 1f : -1f;
                    float distance = halfRoad + Mathf.Lerp(minDistanceFromRoad, maxDistanceFromRoad, NextFloat(rng));

                    // Yol yönünde rastgele kaydırma — bu olmadan proplar
                    // duraklara hizalanıp görünür sıralar oluşturuyor.
                    float jitter = (NextFloat(rng) * 2f - 1f) * forwardJitter;

                    Vector3 position = curr + right * side * distance + forward * jitter;
                    position.y += heightOffset;

                    // Pist kendi üstüne kıvrıldığında prop, yolun BAŞKA bir
                    // bölümünün üstüne düşebilir — o yüzden tüm yola olan
                    // mesafeyi kontrol edip çakışanları atlıyoruz.
                    if (IsTooCloseToRoad(position, trackPoints, halfRoad + minDistanceFromRoad * 0.5f))
                        continue;

                    PlaceProp(position, rng);
                    placedCount++;
                }
            }

            if (placedCount >= maxProps) break;
        }

        if (showDebugLogs)
        {
            string capNote = placedCount >= maxProps
                ? $" — ÜST SINIRA TAKILDI ({maxProps}). Daha fazlası için Max Props'u yükselt."
                : "";
            Debug.Log($"[TrackPropScatter] {placedCount} prop yerleştirildi (seed {trackGenerator.seed}).{capNote}");
        }
    }

    private void PlaceProp(Vector3 position, System.Random rng)
    {
        GameObject prefab = propPrefabs[rng.Next(propPrefabs.Length)];
        if (prefab == null) return;

        // ÖNEMLİ: Prefabın KENDİ rotasyonunu koruyup rastgele yaw'ı onun
        // ÜSTÜNE ekliyoruz. Doğrudan Quaternion.Euler(0, yaw, 0) verirsek
        // prefabın kendi duruşu silinir — Blender'dan gelen (Z-up) modeller
        // bu yüzden yan yatıyordu.
        Quaternion yawRotation = randomYaw
            ? Quaternion.Euler(0f, NextFloat(rng) * 360f, 0f)
            : Quaternion.identity;
        Quaternion rotation = yawRotation * prefab.transform.rotation;

        GameObject prop = Instantiate(prefab, position, rotation, propContainer);

        float scale = Mathf.Lerp(minScale, maxScale, NextFloat(rng));
        prop.transform.localScale = prefab.transform.localScale * scale;

        if (removeColliders)
            foreach (Collider col in prop.GetComponentsInChildren<Collider>())
                DestroySafe(col);
    }

    /// <summary>
    /// Play modunda Destroy, Editör'de DestroyImmediate kullanır. Unity edit
    /// modunda Destroy() çağrısını reddedip hata basıyor ("Destroy may not be
    /// called from edit mode"), o yüzden serpiştirmeyi editörden tetikleyebilmek
    /// için bu sarmalayıcı gerekli.
    /// </summary>
    private static void DestroySafe(UnityEngine.Object target)
    {
        if (target == null) return;

        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    /// <summary>
    /// Bir noktanın yola çok yakın olup olmadığını kontrol eder — pistin
    /// kendine yaklaştığı bölgelerde propların yola düşmesini engeller.
    /// </summary>
    private bool IsTooCloseToRoad(Vector3 position, List<Vector3> trackPoints, float minDistance)
    {
        float sqrMin = minDistance * minDistance;

        foreach (Vector3 point in trackPoints)
        {
            float dx = point.x - position.x;
            float dz = point.z - position.z;
            if (dx * dx + dz * dz < sqrMin) return true;
        }

        return false;
    }

    /// <summary>
    /// Sahnedeki propları siler. Inspector'daki "Propları Temizle" butonu
    /// bunu çağırıyor — ClearProps() private olduğu için dışarıya açık bir
    /// giriş noktası gerekiyordu.
    /// </summary>
    public void ClearAllProps()
    {
        ClearProps();

        if (showDebugLogs)
            Debug.Log("[TrackPropScatter] Proplar temizlendi.");
    }

    private void ClearProps()
    {
        if (propContainer != null)
            DestroySafe(propContainer.gameObject);

        // Sahnede eski bir konteyner kalmışsa (ör. sahne yeniden yüklendiyse) onu da temizle
        Transform existing = transform.Find("TrackProps");
        if (existing != null) DestroySafe(existing.gameObject);
    }

    /// <summary>System.Random 0-1 arası float üretmiyor, kendimiz sarmalıyoruz.</summary>
    private static float NextFloat(System.Random rng) => (float)rng.NextDouble();
}
