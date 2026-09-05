using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Events;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TrackGenerator : MonoBehaviour
{
    [Header("Track Shape Parameters")]
    public int numPoints = 20;
    public Vector2 xBounds = new Vector2(-150, 150);
    public Vector2 yBounds = new Vector2(-150, 150);
    public bool generateOnStart = false;

    [Tooltip("GEÇİCİ ÇEKİM AYARI — sadece fotoğraf sahnesi için, gerçek oyunda " +
             "KAPALI kalmalı.\n\n" +
             "Açıkken, Play'e basıldığında pist rastgele DEĞİL, aşağıdaki 'Debug " +
             "Info > Seed' değeriyle yeniden üretilir. Yani editörde beğenip " +
             "sabitlediğin pistin BİREBİR AYNISI oluşur.\n\n" +
             "NEDEN GEREKLİ: Yol noktalarının listesi (GetTrackPoints) sahneye " +
             "kaydedilmiyor, sadece çalışma anında var oluyor. Mesh'i bake etsen " +
             "bile Play'e basınca o liste boş olur ve minimap yolu çizemez. Bu " +
             "seçenek listeyi aynı seed'le yeniden doldurup sorunu çözüyor.")]
    public bool useSavedSeedOnStart = false;

    [Header("F1-Style Generation Settings")]
    [Range(1, 10)] public int trackComplexity = 6;
    public int minCorners = 5;
    [Range(0.1f, 0.4f)] public float cornerSmoothness = 0.3f;
    public int cornerSegments = 20;
    [Range(0, 180)] public float minTurnAngle = 30f;
    [Range(0, 180)] public float maxTurnAngle = 150f;

    [Header("Mesh Settings")]
    [Range(5f, 50f)] public float roadWidth = 25f;
    public float uvTiling = 10f;

    [Header("Kenarlık (Kerb) Ayarları")]
    [Tooltip("Yolun iki yanına kırmızı-beyaz kabartmalı kenarlık üretilsin mi?")]
    public bool generateCurbs = true;
    [Tooltip("Kenarlığın genişliği (metre) — yolun DIŞINA doğru uzuyor, yolu daraltmıyor.")]
    [Range(0.2f, 5f)] public float curbWidth = 1.5f;
    [Tooltip("Kenarlığın DIŞ kenarının yoldan ne kadar yüksek olacağı (metre). " +
             "İç kenar yol seviyesinde kalıyor, yani hafif bir rampa oluşuyor.")]
    [Range(0f, 1f)] public float curbHeight = 0.12f;
    [Tooltip("Tek bir kırmızı ya da beyaz bandın uzunluğu (metre).")]
    public float curbStripeLength = 2f;
    [Tooltip("Boş bırakılırsa otomatik kırmızı-beyaz çizgili bir materyal üretilir.")]
    public Material curbMaterial;
    [Tooltip("Kenarlığa çarpışma yüzeyi eklensin mi? (Araba üstünden geçerken sarsılsın diye.)")]
    public bool curbCollider = true;

    [Header("Viraj Yarıçapı (Kenarlık Bozulmasını Önler)")]
    [Tooltip("AÇIKKEN: her virajın yarıçapı ölçülür. Yol + kenarlık sığmayacak " +
             "kadar dar bir viraj varsa önce köşe yuvarlatması otomatik " +
             "artırılır; o da yetmezse pist reddedilip yeni bir seed denenir.\n\n" +
             "NEDEN GEREKLİ: Yol ve kenarlık, orta çizginin İKİ YANINA " +
             "(roadWidth/2 + curbWidth) kadar uzayan şeritler. Virajın yarıçapı " +
             "bu mesafeden küçükse virajın İÇ tarafındaki şerit kendi üstüne " +
             "KATLANIYOR — ekranda burulmuş, ters dönmüş kenarlık olarak " +
             "görünüyor. Miter join bunu çözmez, çünkü sorun hizalama değil " +
             "şeridin kendisiyle kesişmesi.")]
    public bool enforceMinCornerRadius = true;

    [Tooltip("Virajın en dar noktasındaki orta çizgi yarıçapı, " +
             "(yolun yarısı + kenarlık genişliği) değerinin EN AZ bu kadar " +
             "üstünde olmalı (metre). Büyütürsen virajlar daha ferah olur ama " +
             "daha çok seed elenir; küçültürsen keskin virajlar artar, " +
             "kenarlık daha sıkışık görünür.")]
    [Range(0f, 30f)] public float cornerRadiusMargin = 5f;

    [Tooltip("Dar bir viraj bulununca köşe yuvarlatması (cornerSmoothness) " +
             "bu değere kadar otomatik artırılabilir. 0.5 üstü olamaz — " +
             "iki komşu viraj aralarındaki düzlüğü paylaştığı için 0.5+0.5 " +
             "aynı düzlüğü ikinci kez tüketip birbirine girerdi.")]
    [Range(0.3f, 0.49f)] public float maxCornerSmoothness = 0.49f;

    [Header("Pist Uzunluğu Tutarlılığı")]
    [Tooltip("AÇIKKEN: pist üretildikten sonra toplam uzunluğu ölçülür. " +
             "Hedef aralığın dışındaysa (targetTrackLength ± toleranceı) o " +
             "seed çöpe atılıp yenisi denenir — aynı 'viraj yarıçapı' " +
             "güvenlik ağıyla BİREBİR aynı desen.\n\n" +
             "NEDEN GEREKLİ: 600 pistlik ölçümde doğal uzunluk dağılımı " +
             "2433-4306 m arası çıktı (~%76 uçtan uca fark). Sabotajcının " +
             "kazanma süresi pist uzunluğuna bakmadan sabit olduğu için bu " +
             "fark doğrudan 'şansına kalmış' bir zorluk farkına dönüşüyordu " +
             "— kısa pist düşen yarışçı kolay kazanıyor, uzun pist düşen " +
             "zorlanıyordu. Bu, o farkı üretim aşamasında küçültüyor.")]
    public bool enforceTrackLength = true;

    [Tooltip("Hedef pist uzunluğu (metre). Varsayılan, 600 pistlik ölçümün " +
             "ortalaması (~3418 m) — değiştirirsen ölçüm de geçersiz olur.")]
    public float targetTrackLength = 3400f;

    [Tooltip("Kabul edilen sapma: hedefin ±yüzde kaçı. Ölçülen ödünleşim " +
             "(600 pist): ±20 → %4 reddedilir (ort. 1.04 deneme), " +
             "±15 → %8 (1.08 deneme), ±10 → %23 (1.30 deneme), " +
             "±8 → %35 (1.53 deneme). Küçültürsen pistler daha tek tip " +
             "olur ama üretim biraz daha fazla seed dener (maliyeti hâlâ " +
             "milisaniyeler mertebesinde).")]
    [Range(5f, 30f)] public float trackLengthTolerancePercent = 15f;

    [Header("Merkez Boşluğu (Sabotajcı Kulesi)")]
    [Tooltip("Pist üretildikten sonra dünya merkezine (0,0,0) ortalanır ve yolun merkezden uzak kalması garanti edilir — kule oraya dikilecek.")]
    public bool keepCenterClear = true;
    [Tooltip("Yolun KENARINDAN dünya merkezine olan en küçük mesafe. Yolun yarı genişliği otomatik ekleniyor.")]
    public float centerClearance = 40f;

    [Header("Checkpoint Settings")]
    public GameObject checkpointPrefab;
    [Range(3, 30)] public int checkpointsPerLap = 10;
    public bool showCheckpointsInEditor = true;

    [HideInInspector] public UnityEvent onTrackGenerated;

    [Header("Debug Info")]
    [SerializeField] private int _seed;
    public int seed { get { return _seed; } private set { _seed = value; } }

    private List<Vector3> _trackPoints;
    private List<Vector2> _refinedPoints;
    private List<GameObject> _checkpoints = new List<GameObject>();
    private GameObject _curbObject;

    void Start()
    {
        if (!Application.isPlaying) return;

        // GEÇİCİ ÇEKİM YOLU: kayıtlı seed ile üret → editörde sabitlediğin
        // pistin aynısı çıkar, ama yol noktası listesi de dolar (minimap için).
        if (useSavedSeedOnStart)
        {
            GenerateTrackWithSeed(_seed);
            return;
        }

        if (generateOnStart)
            GenerateTrack();
    }

    /// <summary>
    /// Rastgele (DateTime tabanlı) bir seed ile pist üretir.
    /// EDİTÖR BUTONU ve tek oyunculu test için kullanılır.
    /// MULTIPLAYER'DA BUNU KULLANMA — TrackSeedSync.cs bunun yerine
    /// GenerateTrackWithSeed(int) çağıracak, böylece host ve tüm client'lar
    /// AYNI seed ile AYNI pisti üretir.
    /// </summary>
    public void GenerateTrack()
    {
        int randomSeed = (int)(System.DateTime.Now.Ticks % int.MaxValue);
        GenerateTrackWithSeed(randomSeed);
    }

    /// <summary>
    /// Belirli bir seed ile deterministik pist üretir. Aynı seed her zaman
    /// aynı pisti üretir (Unity'nin Random sınıfı seed'e göre deterministik
    /// çalışır) — bu yüzden host'un seed'ini client'lara göndermek yeterli,
    /// tüm pist verisini network üzerinden göndermeye gerek yok.
    /// </summary>
    public void GenerateTrackWithSeed(int seedValue)
    {
        ClearTrack();
        _seed = seedValue;
        Random.InitState(_seed);

        _trackPoints = CreateRacetrack();

        if (_trackPoints != null && _trackPoints.Count > 2)
        {
            GenerateRoadMesh(_trackPoints);
            GenerateCurbMesh(_trackPoints);
            GenerateCheckpoints(_trackPoints);
            onTrackGenerated.Invoke();
            Debug.Log($"Track generated with seed: {_seed}. Checkpoints: {checkpointsPerLap}");
        }
        else
        {
            Debug.LogWarning("Failed to generate valid track points");
        }
    }

    public void ClearTrack()
    {
        var mf = GetComponent<MeshFilter>();
        var mc = GetComponent<MeshCollider>();

        if (mf != null && mf.sharedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(mf.sharedMesh);
            else
                DestroyImmediate(mf.sharedMesh);
            mf.sharedMesh = null;
        }
        if (mc != null) mc.sharedMesh = null;

        ClearCurbs();

        foreach (var cp in _checkpoints)
        {
            if (Application.isPlaying) Destroy(cp);
            else DestroyImmediate(cp);
        }
        _checkpoints.Clear();

        _trackPoints = null;
        _refinedPoints = null;

        Debug.Log("Track cleared.");
    }

    public List<Vector3> CreateRacetrack()
    {
        List<Vector2> basePoints;
        List<Vector2> finalPath = null;
        int attempts = 0;
        const int maxAttempts = 100000;

        while (true)
        {
            if (attempts > 0)
            {
                _seed = Random.Range(0, int.MaxValue);
                Random.InitState(_seed);
                Debug.Log($"Attempting new track with seed: {_seed} (Attempt {attempts + 1})");
            }

            attempts++;
            if (attempts > maxAttempts)
            {
                Debug.LogError("Could not generate a valid track after " + maxAttempts + " attempts. Check parameters.");
                return new List<Vector3>();
            }

            var randomPoints = new List<Vector2>();
            for (int i = 0; i < numPoints; i++)
            {
                randomPoints.Add(new Vector2(
                    Random.Range(xBounds.x, xBounds.y),
                    Random.Range(yBounds.x, yBounds.y)
                ));
            }

            basePoints = GetConvexHull(randomPoints);
            if (basePoints.Count < 3) continue;

            basePoints = RefineTrackShape(basePoints, trackComplexity + Mathf.Max(0, minCorners - basePoints.Count));
            if (basePoints.Count < 3) continue;

            if (!ValidateTrackAnglesAndDistances(basePoints)) continue;

            // Virajlar yol + kenarlık genişliğine göre yeterince geniş mi?
            // Yuvarlatmayı sonuna kadar açsak bile sığmıyorsa bu seed çöpe
            // gidiyor — o pistte kenarlık kaçınılmaz olarak katlanırdı.
            if (!HasSafeCornerRadii(basePoints)) continue;

            // Köşeleri Bezier ile yumuşat. Merkez boşluğu kontrolü BU son hal
            // üzerinde yapılmalı — yol mesh'i de, minimap da bu noktaları
            // kullanıyor, yani gerçekte görünen yol bu.
            List<Vector2> curved = CurveCorners(basePoints);

            // Uzunluk kontrolü CENTER CLEARANCE'DAN ÖNCE: ikisi de O(n) ama
            // bu daha ucuz (tek toplama), reddedilecek adaylarda daha pahalı
            // olan segment-mesafe taramasını (HasCenterClearance) atlatıyor.
            if (enforceTrackLength && !HasAcceptableLength(curved)) continue;

            if (keepCenterClear)
            {
                curved = RecenterAroundOrigin(curved);

                // Merkezde kuleye yer yoksa bu seed'i çöpe atıp yenisini dene.
                // Ortalama alma sayesinde bu nadiren gerekiyor — sadece pistin
                // merkeze doğru derin bir girinti yaptığı durumlar için güvenlik ağı.
                if (!HasCenterClearance(curved)) continue;
            }

            finalPath = curved;
            break;
        }

        _refinedPoints = finalPath;
        return _refinedPoints.Select(p => new Vector3(p.x, 0, p.y)).ToList();
    }

    /// <summary>
    /// Pisti dünya merkezine (0,0) taşır. NEDEN: Sabotajcı kulesi 0,0,0'a
    /// dikilecek (CLAUDE.md madde 1/6), ama pist rastgele noktaların convex
    /// hull'undan üretildiği için merkezi her seed'de başka yere kayıyordu.
    ///
    /// Ortalama (mean) DEĞİL, bounding box merkezi kullanılıyor: köşeler
    /// Bezier ile onlarca noktaya bölündüğü için ortalama, virajın yoğun
    /// olduğu tarafa doğru kayardı.
    ///
    /// Bu bir ÖTELEME (translation) olduğu için deterministik — aynı seed
    /// her client'ta aynı pisti üretmeye devam ediyor.
    /// </summary>
    private List<Vector2> RecenterAroundOrigin(List<Vector2> points)
    {
        if (points == null || points.Count == 0) return points;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (Vector2 p in points)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        var moved = new List<Vector2>(points.Count);
        foreach (Vector2 p in points)
            moved.Add(p - center);

        return moved;
    }

    /// <summary>
    /// Merkezde kuleye yer var mı? İki şeyi birlikte kontrol ediyor:
    ///  1. Merkez pistin İÇİNDE mi — kule pistin ortasında kalmalı, merkezi
    ///     dışarıda bırakan hilal şeklindeki pistler reddedilir.
    ///  2. Yol merkeze yeterince uzak mı — yolun yarı genişliği ekleniyor,
    ///     çünkü mesh merkez çizgisinin İKİ YANINA roadWidth/2 kadar uzuyor.
    ///
    /// Nokta mesafesi yerine SEGMENT mesafesi ölçülüyor: yol kesintisiz bir
    /// şerit, iki örnek nokta arasından geçen kısım merkeze daha yakın olabilir.
    /// </summary>
    private bool HasCenterClearance(List<Vector2> points)
    {
        if (points == null || points.Count < 3) return false;
        if (!IsOriginInsideTrack(points)) return false;

        float required = roadWidth * 0.5f + centerClearance;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];

            if (DistancePointToSegment(Vector2.zero, a, b) < required)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Ray casting (ışın atma) yöntemi: 0,0 noktasından bir yöne ışın atıp
    /// pistin kaç kenarını kestiğini sayıyor — TEK sayı ise nokta içeride,
    /// ÇİFT sayı ise dışarıda. Kapalı bir eğri için standart yöntem.
    /// </summary>
    private static bool IsOriginInsideTrack(List<Vector2> points)
    {
        bool inside = false;

        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            Vector2 pi = points[i];
            Vector2 pj = points[j];

            if ((pi.y > 0f) != (pj.y > 0f) &&
                0f < (pj.x - pi.x) * (0f - pi.y) / (pj.y - pi.y) + pi.x)
                inside = !inside;
        }

        return inside;
    }

    #region Track Shape Generation
    private bool DoSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);

        if (Mathf.Abs(d) < 0.0001f) return false;

        float t = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / d;
        float u = ((p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x)) / d;

        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }

    private float DistancePointToSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLength = line.magnitude;

        if (lineLength < 0.0001f)
            return Vector2.Distance(point, lineStart);

        float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (lineLength * lineLength));
        Vector2 projection = lineStart + t * line;

        return Vector2.Distance(point, projection);
    }

    private bool ValidateTrackAnglesAndDistances(List<Vector2> points)
    {
        if (points.Count < 3) return false;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 prev = points[(i - 1 + points.Count) % points.Count];
            Vector2 curr = points[i];
            Vector2 next = points[(i + 1) % points.Count];

            if (Vector2.Distance(prev, curr) < roadWidth * 0.8f ||
                Vector2.Distance(curr, next) < roadWidth * 0.8f)
                return false;

            float angle = Vector2.Angle(curr - prev, next - curr);
            if (angle < minTurnAngle || angle > maxTurnAngle)
                return false;

            for (int j = i + 2; j < points.Count + i - 1; j++)
            {
                int index = j % points.Count;
                if (index == i || index == (i - 1 + points.Count) % points.Count)
                    continue;

                if (Vector2.Distance(curr, points[index]) < roadWidth * 1.5f)
                    return false;
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % points.Count];

            for (int j = i + 2; j < points.Count; j++)
            {
                if (i == 0 && j == points.Count - 1) continue;

                Vector2 p3 = points[j];
                Vector2 p4 = points[(j + 1) % points.Count];

                if (DoSegmentsIntersect(p1, p2, p3, p4))
                {
                    return false;
                }
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 segStart = points[i];
            Vector2 segEnd = points[(i + 1) % points.Count];

            for (int j = 0; j < points.Count; j++)
            {
                if (j == i || j == (i + 1) % points.Count ||
                    j == (i - 1 + points.Count) % points.Count)
                    continue;

                float dist = DistancePointToSegment(points[j], segStart, segEnd);
                if (dist < roadWidth * 1.2f)
                    return false;
            }
        }

        return true;
    }

    private List<Vector2> RefineTrackShape(List<Vector2> points, int iterations)
    {
        if (points.Count < 3) return points;

        var refined = new List<Vector2>(points);

        for (int i = 0; i < iterations; i++)
        {
            int longestEdgeIndex = -1;
            float maxEdgeLengthSq = 0f;

            for (int j = 0; j < refined.Count; j++)
            {
                float distSq = (refined[j] - refined[(j + 1) % refined.Count]).sqrMagnitude;
                if (distSq > maxEdgeLengthSq)
                {
                    maxEdgeLengthSq = distSq;
                    longestEdgeIndex = j;
                }
            }

            if (longestEdgeIndex == -1) continue;

            Vector2 p1 = refined[longestEdgeIndex];
            Vector2 p2 = refined[(longestEdgeIndex + 1) % refined.Count];

            if (Vector2.Distance(p1, p2) < roadWidth * 2f) continue;

            Vector2 mid = (p1 + p2) / 2f;
            Vector2 dir = (p2 - p1).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x);

            bool validPointFound = false;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                float disp = Random.Range(roadWidth * 1.5f, Mathf.Sqrt(maxEdgeLengthSq) * 0.4f);
                if (Random.value < 0.5f) disp = -disp;

                Vector2 newPoint = mid + normal * disp;

                if (IsHairpin(p1, newPoint, p2)) continue;
                if (IsPointTooCloseToTrack(refined, newPoint, roadWidth * 2.0f)) continue;

                var testRefined = new List<Vector2>(refined);
                testRefined.Insert(longestEdgeIndex + 1, newPoint);

                if (ValidateTrackAnglesAndDistances(testRefined))
                {
                    refined = testRefined;
                    validPointFound = true;
                    break;
                }
            }

            if (!validPointFound) break;
        }

        return refined;
    }

    private bool IsPointTooCloseToTrack(List<Vector2> points, Vector2 newPoint, float minDistance)
    {
        foreach (var point in points)
            if (Vector2.Distance(point, newPoint) < minDistance)
                return true;
        return false;
    }

    private bool IsHairpin(Vector2 prev, Vector2 curr, Vector2 next)
    {
        Vector2 v1 = (curr - prev).normalized;
        Vector2 v2 = (next - curr).normalized;
        float angle = Vector2.Angle(v1, v2);
        return angle < minTurnAngle || angle > maxTurnAngle;
    }

    /// <summary>
    /// Yolun orta çizgisinin virajda EN AZ ne kadar yarıçapa sahip olması
    /// gerektiği. Yol mesh'i orta çizginin iki yanına roadWidth/2, kenarlık
    /// ise onun da dışına curbWidth kadar uzuyor — yani en dıştaki vertex
    /// orta çizgiden (roadWidth/2 + curbWidth) uzakta. Virajın yarıçapı bu
    /// mesafeden küçükse virajın İÇ tarafındaki şerit kendi üstüne katlanır.
    /// Üstüne bir de güvenlik payı (cornerRadiusMargin) ekleniyor, çünkü tam
    /// sınırda katlanmıyor ama iç kenar bir noktaya sıkışıp çirkin duruyor.
    /// </summary>
    private float RequiredCornerRadius =>
        roadWidth * 0.5f + (generateCurbs ? curbWidth : 0f) + cornerRadiusMargin;

    /// <summary>
    /// Bir köşenin EN DAR noktasındaki eğrilik yarıçapını, yuvarlatma
    /// katsayısı s = 1 içinmiş gibi döndürür.
    ///
    /// NEDEN BU İŞE YARIYOR: `CurveCorners` köşeyi kuadratik Bezier ile
    /// çiziyor ve iki bacağını da s ile ölçekliyor. Kuadratik Bezier'in
    /// yarıçapı s ile TAM DOĞRU ORANTILI — yani gerçek yarıçap = s × (bu
    /// fonksiyonun döndürdüğü değer). Bu sayede "istediğim yarıçap için s
    /// kaç olmalı" sorusu tek bölmeyle cevaplanıyor, deneme yanılma yok.
    ///
    /// MATEMATİK: Kuadratik Bezier'de ikinci türev sabit, bu yüzden eğrilik
    /// sadece hızın (birinci türev) en küçük olduğu yerde en büyük olur.
    /// R_min = 2·d³ / |u × v| — burada u ve v köşenin iki bacağı, d ise
    /// orijinin [u, v] doğru parçasına olan uzaklığı (yani |B'(t)|/2'nin
    /// alabildiği en küçük değer).
    /// </summary>
    private static float CornerRadiusPerSmoothness(Vector2 prev, Vector2 curr, Vector2 next)
    {
        Vector2 u = curr - prev;
        Vector2 v = next - curr;

        float cross = Mathf.Abs(u.x * v.y - u.y * v.x);
        if (cross < 0.0001f) return 1e9f;   // düz gidiş — viraj yok, yarıçap sonsuz

        Vector2 w = v - u;
        float t = w.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector2.Dot(-u, w) / w.sqrMagnitude)
            : 0f;
        float d = (u + w * t).magnitude;

        return 2f * d * d * d / cross;
    }

    /// <summary>
    /// Bu köşe için kullanılacak yuvarlatma katsayısı. Taban değer
    /// `cornerSmoothness`; viraj yol+kenarlığın sığamayacağı kadar darsa
    /// gereken değere kadar YÜKSELTİLİYOR (asla düşürülmüyor — geniş
    /// virajlar bugüne kadarki görünümünü aynen koruyor).
    /// </summary>
    private float SmoothnessForCorner(Vector2 prev, Vector2 curr, Vector2 next)
    {
        if (!enforceMinCornerRadius) return cornerSmoothness;

        float radiusPerUnit = CornerRadiusPerSmoothness(prev, curr, next);
        if (radiusPerUnit <= 0.0001f) return cornerSmoothness;

        float needed = RequiredCornerRadius / radiusPerUnit;
        float upperLimit = Mathf.Min(0.49f, Mathf.Max(cornerSmoothness, maxCornerSmoothness));

        return Mathf.Clamp(needed, cornerSmoothness, upperLimit);
    }

    /// <summary>
    /// Yuvarlatmayı sonuna kadar açsak bile yol+kenarlığın sığamayacağı bir
    /// viraj var mı? Varsa bu aday pist reddedilip yeni seed deneniyor
    /// (`CreateRacetrack` içindeki döngü zaten bunun için var).
    ///
    /// Kontrol Bezier'e çevirmeden ÖNCE, ham kontrol noktaları üzerinde
    /// yapılıyor — kötü bir aday için yüzlerce nokta üretmeye gerek yok.
    /// </summary>
    private bool HasSafeCornerRadii(List<Vector2> points)
    {
        if (!enforceMinCornerRadius || points == null || points.Count < 3) return true;

        float required = RequiredCornerRadius;
        float upperLimit = Mathf.Min(0.49f, Mathf.Max(cornerSmoothness, maxCornerSmoothness));

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 prev = points[(i - 1 + points.Count) % points.Count];
            Vector2 next = points[(i + 1) % points.Count];

            if (CornerRadiusPerSmoothness(prev, points[i], next) * upperLimit < required)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Pistin toplam uzunluğu hedef aralığın içinde mi? `points` kapalı bir
    /// döngü (halka) varsayılıyor — son nokta ilk noktaya bağlanıyor.
    /// </summary>
    private bool HasAcceptableLength(List<Vector2> points)
    {
        if (points == null || points.Count < 3) return false;

        float total = 0f;
        for (int i = 0; i < points.Count; i++)
            total += Vector2.Distance(points[i], points[(i + 1) % points.Count]);

        float tolerance = trackLengthTolerancePercent / 100f;
        float lo = targetTrackLength * (1f - tolerance);
        float hi = targetTrackLength * (1f + tolerance);

        return total >= lo && total <= hi;
    }

    private List<Vector2> CurveCorners(List<Vector2> points)
    {
        if (points == null || points.Count < 3) return new List<Vector2>(points);

        var finalPath = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 prev = points[(i - 1 + points.Count) % points.Count];
            Vector2 curr = points[i];
            Vector2 next = points[(i + 1) % points.Count];

            // Yuvarlatma artık her köşede AYNI değil: dar virajlarda kenarlık
            // katlanmasın diye otomatik açılıyor (bkz. SmoothnessForCorner).
            float smoothness = SmoothnessForCorner(prev, curr, next);

            Vector2 start = Vector2.Lerp(curr, prev, smoothness);
            Vector2 end = Vector2.Lerp(curr, next, smoothness);

            for (int j = 0; j <= cornerSegments; j++)
            {
                float t = (float)j / cornerSegments;
                finalPath.Add(QuadraticBezier(start, curr, end, t));
            }
        }
        return finalPath;
    }

    private Vector2 QuadraticBezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float u = 1 - t;
        return u * u * p0 + 2 * u * t * p1 + t * t * p2;
    }
    #endregion

    #region Checkpoint & Mesh Generation
    public List<GameObject> GetCheckpoints() => _checkpoints;

    /// <summary>
    /// Yolun tam eğrisi (köşeleri Bezier ile yumuşatılmış, yüzlerce nokta).
    /// Yol mesh'i bu noktalardan üretiliyor — MinimapController da aynı
    /// noktaları kullanarak minimap'te gerçek pist şeklini çiziyor
    /// (checkpoint'leri düz çizgiyle birleştirmek yerine).
    /// Pist henüz üretilmediyse null döner.
    /// </summary>
    public List<Vector3> GetTrackPoints() => _trackPoints;

    private void GenerateCheckpoints(List<Vector3> trackPoints)
    {
        if (checkpointPrefab == null || checkpointsPerLap <= 0) return;

        foreach (var cp in _checkpoints)
        {
            if (Application.isPlaying) Destroy(cp);
            else DestroyImmediate(cp);
        }
        _checkpoints.Clear();

        // Nokta İNDEKSİ değil, gerçek YOL UZUNLUĞU (arc length) baz alınıyor.
        // NEDEN: trackPoints köşelerde (CurveCorners) sık, düz kısımlarda
        // seyrek — index bazlı eski yöntem checkpoint'lerin virajlara
        // yığılıp düzlüklerde seyrekleşmesine sebep oluyordu. Artık toplam
        // pist uzunluğu hesaplanıp checkpointsPerLap'e eşit dilimlere
        // bölünüyor, her checkpoint kendi hedef mesafesine iki nokta
        // arasında lineer enterpolasyonla yerleştiriliyor.
        float totalLength = 0f;
        for (int i = 0; i < trackPoints.Count; i++)
            totalLength += Vector3.Distance(trackPoints[i], trackPoints[(i + 1) % trackPoints.Count]);

        float spacing = totalLength / checkpointsPerLap;

        for (int i = 0; i < checkpointsPerLap; i++)
        {
            (Vector3 pos, Vector3 forward) = GetPointAndForwardAtDistance(trackPoints, i * spacing, totalLength);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            GameObject cpObject = Instantiate(checkpointPrefab, pos + Vector3.up * 5f, rotation, transform);
            cpObject.name = $"Checkpoint_{i}";

            Checkpoint cp = cpObject.GetComponent<Checkpoint>();
            if (cp != null)
            {
                cp.checkpointIndex = i;
                cp.isFinishLine = (i == 0);
                cp.RefreshVisual();
            }

            _checkpoints.Add(cpObject);
        }
    }

    /// <summary>
    /// Kapalı pist eğrisi üzerinde, başlangıçtan itibaren verilen mesafedeki
    /// (targetDistance) noktayı ve o noktadaki ileri yönü döndürür. İki
    /// örnek nokta arasına düşüyorsa aradaki oranla (Lerp) enterpolasyon
    /// yapılır, böylece checkpoint tam istenen mesafeye oturur.
    /// </summary>
    private (Vector3 pos, Vector3 forward) GetPointAndForwardAtDistance(List<Vector3> points, float targetDistance, float totalLength)
    {
        if (totalLength > 0f)
            targetDistance = Mathf.Repeat(targetDistance, totalLength);

        float accumulated = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 curr = points[i];
            Vector3 next = points[(i + 1) % points.Count];
            float segLength = Vector3.Distance(curr, next);

            if (accumulated + segLength >= targetDistance)
            {
                float t = segLength > 0.0001f ? (targetDistance - accumulated) / segLength : 0f;
                Vector3 pos = Vector3.Lerp(curr, next, Mathf.Clamp01(t));
                Vector3 forward = segLength > 0.0001f ? (next - curr).normalized : transform.forward;
                return (pos, forward);
            }

            accumulated += segLength;
        }

        Vector3 fallbackForward = points.Count > 1 ? (points[1] - points[0]).normalized : transform.forward;
        return (points[0], fallbackForward);
    }

    private void GenerateRoadMesh(List<Vector3> points)
    {
        if (points == null || points.Count < 3) return;

        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        var mc = GetComponent<MeshCollider>();

        if (mf.sharedMesh != null)
        {
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
        }

        var mesh = new Mesh { name = "Procedural Road Mesh" };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();

        float cumulativeDistance = 0f;
        var cumulativeDistances = new List<float>();

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) cumulativeDistance += Vector3.Distance(points[i - 1], points[i]);
            cumulativeDistances.Add(cumulativeDistance);

            Vector3 curr = points[i];
            Vector3 prevPt = points[(i - 1 + points.Count) % points.Count];
            Vector3 next = points[(i + 1) % points.Count];
            // ÖNEMLİ: GenerateCurbMesh ile BİREBİR aynı miter hesabı kullanılıyor
            // (ComputeMiterRight) — ikisi farklı offset yöntemi kullanırsa
            // keskin virajlarda yol kenarı ile kenarlık birbirinden ayrılıp
            // aralarında boşluk oluşuyor (yaşanmış bug).
            Vector3 right = ComputeMiterRight(prevPt, curr, next);

            vertices.Add(curr - right * (roadWidth / 2f));
            vertices.Add(curr + right * (roadWidth / 2f));

            float v = cumulativeDistances[i] / uvTiling;
            uvs.Add(new Vector2(0, v));
            uvs.Add(new Vector2(1, v));
        }

        for (int i = 0; i < points.Count; i++)
        {
            int baseIndex = i * 2;
            int nextIndex = ((i + 1) % points.Count) * 2;

            triangles.Add(baseIndex);
            triangles.Add(nextIndex);
            triangles.Add(baseIndex + 1);

            triangles.Add(nextIndex);
            triangles.Add(nextIndex + 1);
            triangles.Add(baseIndex + 1);
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.2f, 0.2f, 0.2f),
                enableInstancing = true
            };
    }

    /// <summary>
    /// Yolun İKİ YANINA kabartmalı (kırmızı-beyaz) kenarlık üretir.
    ///
    /// NEDEN AYRI BİR OBJE: Yol mesh'i bu objenin kendi MeshFilter'ında
    /// duruyor ve tek bir materyal kullanıyor. Kenarlığın farklı bir
    /// materyali (çizgili) olması gerektiği için ayrı bir çocuk obje olarak
    /// üretiliyor.
    ///
    /// ŞEKİL: Her nokta için gidiş yönüne dik (perpendicular) bir vektör
    /// hesaplanıyor — GenerateRoadMesh ile BİREBİR aynı hesap, o yüzden
    /// kenarlık pistin kıvrımını tam olarak takip ediyor. Yolun kenarından
    /// (roadWidth/2) başlayıp curbWidth kadar DIŞARI uzanan bir şerit
    /// geriliyor. İç kenar yol seviyesinde, dış kenar curbHeight kadar
    /// yukarıda — yani araba üstünden geçerken tırmandığı hafif bir rampa
    /// oluşuyor (gerçek yarış pistlerindeki kerb gibi).
    ///
    /// KIRMIZI-BEYAZ DESEN: Elle blok yerleştirmeye gerek yok. Her noktanın
    /// pist başından itibaren KÜMÜLATİF MESAFESİ hesaplanıp UV'nin V eksenine
    /// yazılıyor; materyal bu UV'yi tekrar eden bir dokuyla boyuyor. Pist ne
    /// kadar rastgele olursa olsun bantlar yol boyunca eşit aralıkta çıkıyor.
    /// </summary>
    /// <summary>
    /// "Miter join" — kenarlık gibi bir yol boyunca offset şerit çizerken,
    /// her noktanın offset yönünü SADECE kendi segmentinin değil, ÖNCEKİ ve
    /// SONRAKİ segmentin AÇIORTAYINDAN alır. Naif tek-segment offseti
    /// (sadece "next - curr" yönüne dik) keskin virajlarda ardışık
    /// dörtgenlerin birbiriyle tam hizalanmamasına sebep oluyordu — ekranda
    /// görünen çentik/burulma buydu. Bu, vektör çizim programlarının (SVG
    /// stroke vb.) aynı sorunu çözmek için kullandığı standart teknik.
    /// Pistin virajlarının KESKİNLİĞİNE hiç dokunmuyor, sadece offset
    /// geometrisini düzeltiyor.
    ///
    /// miterLimit: çok keskin köşelerde (neredeyse U dönüşü) ucun sonsuza
    /// uzamasını engelliyor — bu noktadan sonra normal (bevel'e yakın) bir
    /// uzunluğa sabitleniyor.
    /// </summary>
    private static Vector3 ComputeMiterRight(Vector3 prev, Vector3 curr, Vector3 next, float miterLimit = 3f)
    {
        Vector3 dirIn = (curr - prev).normalized;
        Vector3 dirOut = (next - curr).normalized;

        Vector3 rightIn = Vector3.Cross(Vector3.up, dirIn).normalized;
        Vector3 rightOut = Vector3.Cross(Vector3.up, dirOut).normalized;

        Vector3 miter = rightIn + rightOut;
        if (miter.sqrMagnitude < 0.0001f) return rightOut; // ~180° dönüş, çok nadir

        miter.Normalize();

        float dot = Vector3.Dot(rightIn, miter);
        float scale = dot > (1f / miterLimit) ? 1f / dot : miterLimit;

        return miter * scale;
    }

    private void GenerateCurbMesh(List<Vector3> points)
    {
        // Önceki kenarlık varsa temizle (yeniden üretimde birikmesin).
        ClearCurbs();

        if (!generateCurbs || points == null || points.Count < 3) return;

        var curbObject = new GameObject("Track Curbs");
        curbObject.transform.SetParent(transform, false);
        _curbObject = curbObject;

        // ÖNEMLİ: Yolla AYNI layer'a koyuyoruz. CarController süspansiyonu
        // zemini `drivable` layer maskesiyle ışın atarak buluyor — kenarlık
        // farklı bir layer'da kalırsa araba onu "yol" saymaz ve üstünden
        // geçerken tekerlekler kenarlığın içine gömülür, kabartma hissi olmaz.
        curbObject.layer = gameObject.layer;

        // CarController kenarlığı bu tag'den tanıyıp ekstra sürtünme
        // uyguluyor (bkz. CarController.curbTag) — Unity'de tag KULLANMADAN
        // ÖNCE Project Settings > Tags and Layers'da tanımlı olmalı, "Curb"
        // zaten eklendi (TagManager.asset).
        curbObject.tag = "Curb";

        var mf = curbObject.AddComponent<MeshFilter>();
        var mr = curbObject.AddComponent<MeshRenderer>();

        var mesh = new Mesh { name = "Procedural Curb Mesh" };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();

        float halfRoad = roadWidth * 0.5f;

        // Pistin TOPLAM uzunluğu (halkayı kapatan son segment dahil).
        float totalLength = 0f;
        for (int i = 0; i < points.Count; i++)
            totalLength += Vector3.Distance(points[i], points[(i + 1) % points.Count]);

        // DESENİ HALKAYA TAM OTURT: İstenen bant uzunluğunu olduğu gibi
        // kullanırsak, pistin toplam uzunluğu bunun tam katı olmadığı için
        // başlangıç noktasında kırmızı-beyaz dizilim tutmaz. Bant uzunluğunu
        // en yakın tam sayıda banda bölünecek şekilde milimetrik ayarlıyoruz.
        int stripeCount = Mathf.Max(1, Mathf.RoundToInt(totalLength / Mathf.Max(0.01f, curbStripeLength)));
        float fittedStripeLength = totalLength / stripeCount;

        // İki kenarlık üretiyoruz: sol (-1) ve sağ (+1).
        // Her biri kendi vertex bloğunu alıyor, sonra üçgenlerle bağlanıyor.
        for (int side = 0; side < 2; side++)
        {
            // side 0 = sol, side 1 = sağ. Yön çarpanı ile aynı kodu iki kez
            // kullanabiliyoruz.
            float sideSign = (side == 0) ? -1f : 1f;
            int sideStartIndex = vertices.Count;

            float cumulativeDistance = 0f;

            // DİKKAT: Döngü points.Count'a KADAR DEĞİL, points.Count DAHİL
            // gidiyor — yani ilk noktanın bir KOPYASI sona ekleniyor.
            //
            // NEDEN: Son noktayı doğrudan 0. noktaya bağlarsak, o segmentin
            // UV değeri koca bir sayıdan aniden 0'a düşer ve doku o tek
            // parçanın içine yüzlerce kez sıkışır (ekranda sık, ince çizgili
            // bozuk bir bölge olarak görünür — tam da başlangıç çizgisinde).
            // Kopya noktanın UV'si "toplam uzunluk" olarak devam ettiği için
            // böyle bir sıçrama olmuyor.
            for (int i = 0; i <= points.Count; i++)
            {
                int pointIndex = i % points.Count;

                if (i > 0)
                    cumulativeDistance += Vector3.Distance(points[(i - 1) % points.Count], points[pointIndex]);

                Vector3 curr = points[pointIndex];
                Vector3 prevPt = points[(pointIndex - 1 + points.Count) % points.Count];
                Vector3 next = points[(pointIndex + 1) % points.Count];
                Vector3 right = ComputeMiterRight(prevPt, curr, next);

                // İç kenar: yolun tam kenarı, yol seviyesinde.
                Vector3 inner = curr + right * (sideSign * halfRoad);
                // Dış kenar: curbWidth kadar dışarıda ve curbHeight kadar yukarıda.
                Vector3 outer = curr + right * (sideSign * (halfRoad + curbWidth))
                                     + Vector3.up * curbHeight;

                vertices.Add(inner);
                vertices.Add(outer);

                // V ekseni = yol boyunca kat edilen mesafe / bant uzunluğu.
                // Böylece her bantta desen bir kez tekrarlanıyor.
                float v = cumulativeDistance / fittedStripeLength;
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
            }

            // Üçgenler: ardışık iki nokta arasındaki dörtgeni iki üçgene bölüyoruz.
            // Sonda kopya nokta olduğu için modulo'ya gerek yok.
            for (int i = 0; i < points.Count; i++)
            {
                int baseIndex = sideStartIndex + i * 2;
                int nextIndex = sideStartIndex + (i + 1) * 2;

                // GÜVENLİK AĞI — KATLANMIŞ PARÇAYI HİÇ ÇİZME.
                // Viraj yarıçapı offset mesafesinden küçükse (bkz.
                // enforceMinCornerRadius) kenarlığın İÇ taraftaki kenarı geri
                // dönüp kendi üstüne biniyor ve ekranda burulmuş bir papyon
                // gibi görünüyor. O parçanın yön vektörü orta çizginin gidiş
                // yönüne TERS düşer — bunu yakalayıp dörtgeni atlıyoruz.
                // Kenarlıkta minik bir boşluk kalması, burulmuş bir kenarlıktan
                // çok daha az göze batıyor.
                //
                // Vertex'ler yerinde duruyor, sadece üçgen üretilmiyor —
                // yani yol mesh'iyle ortak olan offset matematiği bozulmuyor.
                Vector3 flow = points[(i + 1) % points.Count] - points[i];
                Vector3 innerStep = vertices[nextIndex] - vertices[baseIndex];
                Vector3 outerStep = vertices[nextIndex + 1] - vertices[baseIndex + 1];

                if (Vector3.Dot(innerStep, flow) < 0f || Vector3.Dot(outerStep, flow) < 0f)
                    continue;

                // Sol ve sağ kenarlığın üçgen sarım yönü (winding) TERS olmalı,
                // yoksa yüzü aşağı bakar ve üstten bakınca görünmez olur
                // (arka yüzler çizilmiyor — backface culling).
                //
                // Nasıl belirlendi: Yol mesh'i her nokta için [sol, sağ]
                // sırasıyla vertex koyuyor, yani vertex çiftinin yönü +right.
                // SAĞ kenarlıkta iç→dış yönü de +right olduğu için YOLLA AYNI
                // sarım doğru. SOL kenarlıkta iç→dış yönü -right, yani ters.
                if (sideSign > 0f)
                {
                    triangles.Add(baseIndex);
                    triangles.Add(nextIndex);
                    triangles.Add(baseIndex + 1);

                    triangles.Add(nextIndex);
                    triangles.Add(nextIndex + 1);
                    triangles.Add(baseIndex + 1);
                }
                else
                {
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(nextIndex);

                    triangles.Add(nextIndex);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(nextIndex + 1);
                }
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;
        mr.sharedMaterial = curbMaterial != null ? curbMaterial : CreateDefaultCurbMaterial();

        if (curbCollider)
        {
            var mc = curbObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }
    }

    /// <summary>
    /// curbMaterial atanmadıysa kullanılan yedek: kırmızı-beyaz çizgili bir
    /// doku RUNTIME'DA üretiliyor (dışarıdan bir texture dosyası gerekmesin
    /// diye). Doku dikey olarak tekrar ettiği için yol boyunca bantlar
    /// oluşuyor.
    /// </summary>
    private Material CreateDefaultCurbMaterial()
    {
        const int size = 64;
        var texture = new Texture2D(size, size) { name = "Curb Stripes" };

        for (int y = 0; y < size; y++)
        {
            // Dokunun üst yarısı bir renk, alt yarısı diğeri — UV tekrar
            // ettikçe kırmızı-beyaz-kırmızı-beyaz diziliyor.
            Color stripe = (y < size / 2) ? Color.red : Color.white;

            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, stripe);
        }

        texture.filterMode = FilterMode.Point; // bantların arası bulanıklaşmasın
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.Apply();

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // 🚨 BUILD GÜVENLİĞİ — BU PROJEDE BİR KEZ GERÇEKTEN YAŞANDI.
        // `Shader.Find` yalnızca build'e DAHİL EDİLMİŞ shader'ları bulabiliyor.
        // Editor'de tüm shader'lar yüklü olduğu için burası hep çalışıyor, ama
        // gerçek build'de shader stripping devreye giriyor: hiçbir materyalin
        // referans vermediği bir shader build'e HİÇ girmiyor ve `Shader.Find`
        // null dönüyor. `new Material(null)` ise ArgumentNullException fırlatıp
        // pist üretimini YARIDA KESİYOR — minimap'te tam olarak bu olmuştu
        // (kenarlık VE checkpoint'ler birden görünmez olmuştu).
        //
        // Son çare olarak YOLUN KENDİ materyalinin shader'ını kullanıyoruz:
        // o materyal sahnede elle atanmış olduğu için Unity onu build'e
        // GARANTİ dahil ediyor. Görüntü ideal olmayabilir ama kenarlık
        // hiç görünmemektense kesinlikle daha iyi.
        if (shader == null)
        {
            var roadRenderer = GetComponent<MeshRenderer>();
            if (roadRenderer != null && roadRenderer.sharedMaterial != null)
                shader = roadRenderer.sharedMaterial.shader;
        }

        if (shader == null)
        {
            Debug.LogError("[TrackGenerator] Kenarlık için hiçbir shader bulunamadı — " +
                           "kenarlık çizilmeyecek. Çözüm: TrackGenerator > Curb Material " +
                           "alanına Inspector'dan bir materyal ata (o zaman bu kod hiç çalışmaz).");
            return null;
        }

        var material = new Material(shader) { name = "Curb Material (auto)" };
        material.mainTexture = texture;
        material.enableInstancing = true;

        // ÖNEMLİ: URP/Lit'in varsayılan Smoothness'i 0.5, yani yarı parlak.
        // Kenarlığın dış kenarı yükseltilmiş olduğu için (curbHeight) orada
        // eğimli bir yüzey var; alçak güneş o eğime yalayarak vurduğunda güçlü
        // bir parlama (specular) çıkıyor ve beyaz bantlar ışığın rengini alıp
        // turuncuya kayarak deseni yok ediyordu. Boyalı beton mat bir yüzey —
        // parlaklığı kısıyoruz.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f); // Standard shader yedeği
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

        return material;
    }

    /// <summary>Üretilmiş kenarlık objesini (varsa) siler.</summary>
    private void ClearCurbs()
    {
        if (_curbObject == null)
        {
            // Sahne yeniden yüklendiğinde referans kaybolmuş olabilir —
            // isme göre de arayıp temizliyoruz ki kopyalar birikmesin.
            Transform existing = transform.Find("Track Curbs");
            if (existing != null) _curbObject = existing.gameObject;
        }

        if (_curbObject == null) return;

        var mf = _curbObject.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
        }

        if (Application.isPlaying) Destroy(_curbObject);
        else DestroyImmediate(_curbObject);

        _curbObject = null;
    }

    private List<Vector2> GetConvexHull(List<Vector2> points)
    {
        if (points.Count < 3) return points;

        var uniquePoints = points.Distinct().ToList();
        if (uniquePoints.Count < 3) return uniquePoints;

        Vector2 start = uniquePoints.OrderBy(p => p.y).ThenBy(p => p.x).First();
        var sorted = uniquePoints.Where(p => p != start)
                               .OrderBy(p => Mathf.Atan2(p.y - start.y, p.x - start.x))
                               .ThenBy(p => Vector2.Distance(start, p)).ToList();

        var stack = new Stack<Vector2>();
        stack.Push(start);
        stack.Push(sorted[0]);

        for (int i = 1; i < sorted.Count; i++)
        {
            Vector2 top = stack.Pop();
            while (stack.Count > 0 && CrossProduct(stack.Peek(), top, sorted[i]) <= 0)
                top = stack.Pop();
            stack.Push(top);
            stack.Push(sorted[i]);
        }

        return stack.Reverse().ToList();
    }

    private float CrossProduct(Vector2 p1, Vector2 p2, Vector2 p3) =>
        (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);

    private void OnDrawGizmos()
    {
        if (_refinedPoints != null && _refinedPoints.Count > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < _refinedPoints.Count; i++)
            {
                Vector3 p1 = new Vector3(_refinedPoints[i].x, 0, _refinedPoints[i].y);
                Vector3 p2 = new Vector3(_refinedPoints[(i + 1) % _refinedPoints.Count].x, 0, _refinedPoints[(i + 1) % _refinedPoints.Count].y);
                Gizmos.DrawSphere(p1, 1f);
                Gizmos.DrawLine(p1, p2);
            }
        }

        if (showCheckpointsInEditor && !Application.isPlaying)
        {
            foreach (var cp in _checkpoints)
            {
                if (cp == null) continue;
                Checkpoint checkpoint = cp.GetComponent<Checkpoint>();
                Gizmos.color = checkpoint.isFinishLine ? Color.red : Color.green;
                Gizmos.DrawCube(cp.transform.position, Vector3.one * 5f);
            }
        }

        // Kuleye ayrılan merkez alanı göster — kuleyi sahneye yerleştirirken
        // bu dairenin içinde kalmasına bakabilirsin.
        if (keepCenterClear && _refinedPoints != null && _refinedPoints.Count > 0)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Vector3.zero, centerClearance + roadWidth * 0.5f);
        }
    }
    #endregion
}

#if UNITY_EDITOR
[CustomEditor(typeof(TrackGenerator))]
public class TrackGeneratorEditor : Editor
{
    /// <summary>Inspector'daki elle yazılabilir seed kutusunun değeri.</summary>
    private int _seedField;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TrackGenerator tg = (TrackGenerator)target;

        GUILayout.Space(10);
        GUILayout.Label("Track Controls", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("checkpointPrefab"));
        serializedObject.ApplyModifiedProperties();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate Track"))
        {
            tg.GenerateTrack();
            _seedField = tg.seed;
            MarkDirty(tg);
        }

        if (GUILayout.Button("Clear Track"))
        {
            tg.ClearTrack();
            MarkDirty(tg);
        }
        GUILayout.EndHorizontal();

        // ─────────────────────────────────────────────────────────────────
        // FOTOĞRAF / CAPSULE SAHNESİ ARAÇLARI
        // Pist prosedürel üretildiği için normalde sadece Play modunda var
        // oluyor ve her seferinde değişiyor. Aşağıdaki araçlar belirli bir
        // pisti sabitleyip sahneye kalıcı olarak gömmeyi sağlıyor.
        // ─────────────────────────────────────────────────────────────────
        GUILayout.Space(12);
        GUILayout.Label("Fotoğraf Sahnesi Araçları", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Beğendiğin pisti bulunca Seed değerini not et — aynı seed her zaman " +
            "aynı pisti üretir, yani o pisti bir daha kaybetmezsin.",
            MessageType.Info);

        GUILayout.BeginHorizontal();
        _seedField = EditorGUILayout.IntField("Seed", _seedField);
        if (GUILayout.Button("Bu Seed ile Üret", GUILayout.Width(150)))
        {
            tg.GenerateTrackWithSeed(_seedField);
            MarkDirty(tg);
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Şu Anki Seed'i Kutuya Al"))
            _seedField = tg.seed;

        GUILayout.Space(6);

        if (GUILayout.Button("Propları Serpiştir (ağaç / kaya)"))
            ScatterProps(tg);

        if (GUILayout.Button("Mesh'leri Asset Olarak Kaydet (sahneye kalıcı göm)"))
            BakeMeshes(tg);

        EditorGUILayout.HelpBox(
            "1. Create a Checkpoint prefab with Trigger Collider and Checkpoint.cs\n" +
            "2. Assign it above\n" +
            "3. Ensure cars have 'Player' tag and PlayerRaceController.cs",
            MessageType.Info
        );
    }

    /// <summary>
    /// TrackPropScatter'ı bulup Scatter() çağırır. Scatter() kendi
    /// TrackGenerator referansını çözebildiği için editörden çağrılabiliyor.
    /// </summary>
    private static void ScatterProps(TrackGenerator tg)
    {
        TrackPropScatter scatter = tg.GetComponent<TrackPropScatter>();
        if (scatter == null)
            scatter = Object.FindAnyObjectByType<TrackPropScatter>();

        if (scatter == null)
        {
            Debug.LogWarning(
                "[TrackGeneratorEditor] Sahnede TrackPropScatter yok. TrackGenerator'ın " +
                "olduğu objeye TrackPropScatter ekle ve propPrefabs listesini doldur.");
            return;
        }

        scatter.Scatter();
        MarkDirty(scatter);
    }

    /// <summary>
    /// Üretilen yol/kenarlık mesh'lerini gerçek .asset dosyalarına çevirir.
    ///
    /// NEDEN GEREKLİ: Kod içinde "new Mesh()" ile üretilen mesh diske ait
    /// değildir. Sahneyi kaydedip Unity'yi kapatıp açtığında bu mesh'in
    /// kaybolma riski var (yol ve kenarlık görünmez olur). Bu buton kalıcı bir
    /// kopya yazıp MeshFilter ile MeshCollider'ı ona bağlıyor — pist artık
    /// sahnenin sabit parçası, tıpkı elle modellenmiş gibi.
    /// </summary>
    private static void BakeMeshes(TrackGenerator tg)
    {
        const string bakeFolder = "Assets/GeneratedTracks";

        if (!Directory.Exists(bakeFolder))
        {
            Directory.CreateDirectory(bakeFolder);
            AssetDatabase.Refresh();
        }

        string sceneName = tg.gameObject.scene.name;
        if (string.IsNullOrEmpty(sceneName)) sceneName = "Scene";

        int baked = 0;

        foreach (MeshFilter filter in tg.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            // Zaten diske kayıtlıysa (butona ikinci kez basıldıysa) atla.
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh))) continue;

            Mesh copy = Object.Instantiate(mesh);
            copy.name = $"{sceneName}_{tg.seed}_{filter.gameObject.name}";

            string path = AssetDatabase.GenerateUniqueAssetPath($"{bakeFolder}/{copy.name}.asset");
            AssetDatabase.CreateAsset(copy, path);

            filter.sharedMesh = copy;

            // Yolun MeshCollider'ı da aynı mesh'i kullanıyor — o da bağlanmalı,
            // yoksa arabalar görünmeyen eski mesh üzerinde sürer.
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMesh = copy;

            baked++;
        }

        AssetDatabase.SaveAssets();
        MarkDirty(tg);

        Debug.Log(baked > 0
            ? $"[TrackGeneratorEditor] {baked} mesh '{bakeFolder}' altına kaydedildi. Şimdi Ctrl+S ile sahneyi de kaydet."
            : "[TrackGeneratorEditor] Kaydedilecek yeni mesh yok (hepsi zaten asset olabilir ya da pist üretilmemiş olabilir).");
    }

    /// <summary>
    /// Unity'ye "bu sahnede kaydedilmemiş değişiklik var" der. SADECE
    /// SetDirty yeterli değil — sahne dirty işaretlenmezse Ctrl+S hiçbir şey
    /// kaydetmeyebilir ve yaptığın iş kaybolur.
    /// </summary>
    private static void MarkDirty(Component component)
    {
        if (component == null) return;

        EditorUtility.SetDirty(component);

        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);

        SceneView.RepaintAll();
    }
}
#endif