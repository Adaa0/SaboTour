using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kule odasındaki fiziksel minimap masası. Tamamen LOCAL bir görsel sistem
/// (NetworkBehaviour DEĞİL) — checkpoint pozisyonları zaten TrackSeedSync ile
/// her client'ta aynı üretiliyor, arabaların pozisyonu da NetworkTransform ile
/// zaten senkron, o yüzden minimap'in kendisinin network'e ihtiyacı yok.
///
/// ÇİZDİKLERİ (hepsi runtime'da otomatik, manuel prefab kurmaya gerek yok):
///  1. Siyah zemin (masa yüzeyi).
///  2. GERÇEK yol şekli — TrackGenerator.GetTrackPoints() ile pistin mesh'ini
///     üreten aynı eğri noktaları kullanılıyor ve yol, TrackGenerator'ın
///     kendi mesh algoritmasıyla (yöne dik sağ/sol vertex'ler + üçgen şerit)
///     çiziliyor. Bu yüzden minimap'teki yol gerçek pistle birebir aynı.
///  3. Checkpoint marker'ları — üzerlerinde numarasıyla, tıklanabilir.
///  4. Araba marker'ları — her frame gerçek araba pozisyonuna göre güncellenir,
///     minimap üzerinde canlı hareket eder.
///
/// ÖLÇEKLEME NOTU: X ve Z aynı çarpanla ölçekleniyor (oran korumalı), yoksa
/// pist minimap karesini doldurmak için ezilip gerçek şeklini kaybederdi.
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("Yerleşim")]
    [Tooltip("AÇIK: minimap, bu objenin (masa/küp) ÜST YÜZEYİNE oturur ve boyutunu o yüzeyden otomatik alır — mapWidth/mapDepth'i elle girmeye gerek kalmaz. KAPALI: obje merkezine, aşağıdaki elle girilen boyutta çizilir.")]
    [SerializeField] private bool fitToSurface = true;
    [Tooltip("Üst yüzeyden ne kadar yukarıda dursun (z-fighting olmasın diye).")]
    [SerializeField] private float surfaceOffset = 0.005f;

    [Header("Minimap Boyutu (DÜNYA birimi — objenin scale'inden BAĞIMSIZ)")]
    [Tooltip("fitToSurface KAPALIYSA kullanılır.")]
    [SerializeField] private float mapWidth = 2f;
    [SerializeField] private float mapDepth = 2f;
    [Tooltip("Pistin minimap kenarlarına değmemesi için bırakılan boşluk oranı (0.1 = %10 kenar boşluğu).")]
    [Range(0f, 0.4f)]
    [SerializeField] private float mapPadding = 0.1f;

    [Header("Yol")]
    [SerializeField] private bool drawRoad = true;
    [Tooltip("Yol şeridinin kalınlığı gerçek roadWidth'e göre bu çarpanla ölçeklenir. 1 = gerçek genişlik, büyütürsen yol daha kalın görünür.")]
    [SerializeField] private float roadWidthMultiplier = 1f;
    [Tooltip("Minimap yolunun materyali (ör. asfalt dokusu). Boş bırakılırsa düz gri bir materyal kullanılır.")]
    [SerializeField] private Material roadMaterial;

    [Header("Checkpoint Marker")]
    [Tooltip("SADECE prefab atanmadığında kullanılan otomatik kürenin çapı. Prefab atarsan prefabın KENDİ scale'i VE materyali aynen korunur.")]
    [SerializeField] private float markerRadius = 0.03f;
    [SerializeField] private bool showCheckpointNumbers = true;
    [Tooltip("Kendi checkpoint marker prefabın (kendi materyaliyle gelir). Boş bırakılırsa küre + numara otomatik oluşturulur.")]
    [SerializeField] private GameObject markerPrefab;

    [Header("Araba Marker")]
    [SerializeField] private bool showCarMarkers = true;
    [Tooltip("Minimap'te arabayı temsil edecek prefab (küçültülmüş araba modelin, kendi materyaliyle gelir). Boş bırakılırsa küçük bir kutu kullanılır.")]
    [SerializeField] private GameObject carMarkerPrefab;
    [Tooltip("SADECE prefab atanmadığında kullanılan otomatik kutunun boyutu.")]
    [SerializeField] private float carMarkerScale = 0.05f;

    [Header("Yükseklikler (zeminden itibaren, çakışmayı önlemek için)")]
    [SerializeField] private float roadHeight = 0.005f;
    [SerializeField] private float markerHeight = 0.02f;

    [Header("Kenarlık (Kerb)")]
    [Tooltip("Minimap'teki yolun iki yanına gerçek pistteki gibi kırmızı-beyaz kenarlık çizilsin mi?")]
    [SerializeField] private bool drawCurbs = true;
    [Tooltip("Boş bırakılırsa kırmızı-beyaz çizgili bir doku otomatik üretilir.")]
    [SerializeField] private Material curbMaterial;
    [Tooltip("Minimap'te tek bir kırmızı/beyaz bandın uzunluğu (minimap birimi). " +
             "Gerçek pistteki curbStripeLength'ten bağımsız — minimap çok küçük " +
             "olduğu için orada okunabilir bir değer burada aşırı sık görünür.")]
    [SerializeField] private float curbStripeLength = 0.05f;
    [Tooltip("Kenarlığın yoldan ne kadar yukarıda çizileceği. Yolla aynı seviyede " +
             "olursa iki yüzey birbirine girip titrer (z-fighting).")]
    [SerializeField] private float curbHeight = 0.006f;

    [Tooltip("Minimap kenarlığının kalınlık çarpanı. 1 = gerçek pistle birebir " +
             "orantılı (minimap küçük olduğu için genelde çok ince kalır). " +
             "Büyütmek SADECE minimap'i etkiler, gerçek pistteki kenarlığa dokunmaz.")]
    [Range(0.5f, 10f)]
    [SerializeField] private float curbWidthMultiplier = 3f;
    [SerializeField] private float carHeight = 0.03f;

    [Header("Görünüm")]
    [Tooltip("Minimap zemininin materyali. Boş bırakılırsa düz siyah kullanılır.")]
    [SerializeField] private Material backgroundMaterial;
    [Tooltip("ZORUNLU (build'de çalışması için) — Unlit/basit bir materyal ata (ör. Universal Render Pipeline/Unlit shader'lı boş bir materyal). " +
             "Kod, checkpoint/kenarlık/zemin/araba materyallerini bunun bir kopyası üzerinden üretir. Boş bırakılırsa eski Shader.Find() yöntemine " +
             "geri düşer — bu, Editor'de çalışır ama BUILD'de shader'ın stripelenip kaybolma riski taşır (yaşanan bug tam buydu).")]
    [SerializeField] private Material unlitBaseMaterial;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    private CheckpointManager checkpointManager;
    private TrackGenerator trackGenerator;

    private readonly List<GameObject> spawnedMarkers = new List<GameObject>();
    // Araba marker'ları: hangi araba hangi marker'a ait (her frame pozisyon güncellemek için)
    private readonly Dictionary<PlayerRaceController, Transform> carMarkers = new Dictionary<PlayerRaceController, Transform>();

    private GameObject roadObject;
    private GameObject curbObject;
    private static Shader cachedUnlitShader;

    // Dünya → minimap dönüşümü için hesaplanan değerler
    private Vector2 worldCenter;
    private float worldToMapScale = 1f;
    private bool isBuilt;

    // Minimap'in gerçek kökü. Bu objenin (masa/küp) scale'i genelde yassıdır
    // (ör. 1, 0.1, 1) — çocuklar bunu miras aldığı için her şey ezik görünür.
    // Bu kök, parent'ın scale'ini TERSİYLE çarparak nötrler (dünya scale'i 1
    // olur), böylece prefabların kendi boyutu bozulmadan görünür.
    private Transform mapRoot;

    void Start()
    {
        CreateMapRoot();
        CreateBackground();

        checkpointManager = FindAnyObjectByType<CheckpointManager>();
        trackGenerator = FindAnyObjectByType<TrackGenerator>();

        StartCoroutine(BuildWhenTrackReady());
    }

    /// <summary>
    /// Ölçek nötrleyici kök objeyi oluşturur ve (fitToSurface açıksa)
    /// masanın üst yüzeyine yerleştirir.
    /// </summary>
    private void CreateMapRoot()
    {
        mapRoot = new GameObject("MinimapRoot").transform;
        mapRoot.SetParent(transform, false);

        // Parent'ın dünya scale'ini tersle → mapRoot'un dünya scale'i (1,1,1)
        Vector3 parentScale = transform.lossyScale;
        mapRoot.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? 1f : 1f / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? 1f : 1f / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? 1f : 1f / parentScale.z);

        if (!fitToSurface) return;

        // Masanın ÜST yüzeyine otur + boyutu o yüzeyden al.
        if (TryGetComponent(out Renderer surfaceRenderer))
        {
            Bounds bounds = surfaceRenderer.bounds; // dünya birimi
            mapRoot.position = new Vector3(
                bounds.center.x,
                bounds.max.y + surfaceOffset,
                bounds.center.z);

            mapWidth = bounds.size.x;
            mapDepth = bounds.size.z;

            if (showDebugLogs)
                Debug.Log($"[MinimapController] Yüzeye oturtuldu — boyut {mapWidth:F2} x {mapDepth:F2}, üst y={bounds.max.y:F2}");
        }
        else if (showDebugLogs)
        {
            Debug.LogWarning("[MinimapController] Bu objede Renderer yok (boş GameObject?) — üst yüzey bulunamadı, " +
                             "minimap obje merkezine çiziliyor. İstersen fitToSurface'i kapatıp mapWidth/mapDepth'i elle gir.");
        }
    }

    void Update()
    {
        if (isBuilt && showCarMarkers)
            UpdateCarMarkers();
    }

    /// <summary>
    /// Pist prosedürel üretildiği için minimap'in hazır olmasını beklemesi
    /// gerekiyor (CheckpointManager'daki RetryUntilLoaded ile aynı mantık).
    /// </summary>
    private IEnumerator BuildWhenTrackReady()
    {
        while (true)
        {
            if (checkpointManager == null) checkpointManager = FindAnyObjectByType<CheckpointManager>();
            if (trackGenerator == null) trackGenerator = FindAnyObjectByType<TrackGenerator>();

            bool checkpointsReady = checkpointManager != null && checkpointManager.checkpoints.Count > 0;
            if (checkpointsReady)
                break;

            yield return new WaitForSeconds(0.5f);
        }

        Build();
    }

    private void Build()
    {
        List<Vector3> trackPoints = trackGenerator != null ? trackGenerator.GetTrackPoints() : null;

        // Ölçeklemeyi yol noktalarına göre yap (yoksa checkpoint'lere göre).
        List<Vector3> boundsSource = (trackPoints != null && trackPoints.Count > 1)
            ? trackPoints
            : CheckpointPositions();

        CalculateTransform(boundsSource);

        if (drawRoad && trackPoints != null && trackPoints.Count > 1)
            DrawRoad(trackPoints);
        else if (drawRoad && showDebugLogs)
            Debug.LogWarning("[MinimapController] TrackGenerator yol noktaları alınamadı — yol çizilmedi, sadece checkpoint'ler gösteriliyor.");

        BuildCheckpointMarkers();

        isBuilt = true;
    }

    /// <summary>
    /// CheckpointCooldownManager server'da bir checkpoint'in cooldown'a
    /// girdiğini ClientRpc ile bildirdiğinde çağrılır. checkpointIndex,
    /// spawnedMarkers listesindeki sırayla birebir eşleşiyor (BuildCheckpointMarkers
    /// checkpoint'leri sırayla, index'leriyle aynı sırada oluşturuyor).
    /// </summary>
    public void PlayCheckpointCooldownVisual(int checkpointIndex, float duration)
    {
        if (checkpointIndex < 0 || checkpointIndex >= spawnedMarkers.Count) return;
        if (spawnedMarkers[checkpointIndex] == null) return;

        MinimapCheckpointMarker marker = spawnedMarkers[checkpointIndex].GetComponent<MinimapCheckpointMarker>();
        marker?.PlayCooldown(duration);
    }

    private List<Vector3> CheckpointPositions()
    {
        List<Vector3> positions = new List<Vector3>();
        foreach (Transform cp in checkpointManager.checkpoints)
            if (cp != null) positions.Add(cp.position);
        return positions;
    }

    /// <summary>
    /// Dünya koordinatlarını minimap'in local koordinatlarına çeviren merkez +
    /// ölçek değerlerini hesaplar. X ve Z için TEK bir ölçek kullanılıyor —
    /// böylece pistin gerçek oranı (şekli) korunuyor.
    /// </summary>
    private void CalculateTransform(List<Vector3> points)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (Vector3 p in points)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
        }

        worldCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);

        float worldRangeX = Mathf.Max(maxX - minX, 0.001f);
        float worldRangeZ = Mathf.Max(maxZ - minZ, 0.001f);

        float usableWidth = mapWidth * (1f - mapPadding * 2f);
        float usableDepth = mapDepth * (1f - mapPadding * 2f);

        // İki eksenden hangisi daha kısıtlıysa o belirler (pist taşmasın).
        worldToMapScale = Mathf.Min(usableWidth / worldRangeX, usableDepth / worldRangeZ);
    }

    private Vector3 WorldToMapLocal(Vector3 worldPos, float height)
    {
        return new Vector3(
            (worldPos.x - worldCenter.x) * worldToMapScale,
            height,
            (worldPos.z - worldCenter.y) * worldToMapScale);
    }

    /// <summary>Minimap'in siyah zemini — masanın üstüne yatay bir kare.</summary>
    private void CreateBackground()
    {
        GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
        background.name = "MinimapBackground";
        Destroy(background.GetComponent<Collider>()); // zemine tıklanmasın, sadece marker'lara tıklansın
        background.transform.SetParent(mapRoot, false);
        background.transform.localPosition = Vector3.zero;
        background.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // yukarı (+Y) baksın
        background.transform.localScale = new Vector3(mapWidth, mapDepth, 1f);
        background.GetComponent<Renderer>().material = backgroundMaterial != null
            ? backgroundMaterial
            : MakeUnlitMaterial(Color.black);
    }

    /// <summary>
    /// Yolu MESH olarak çizer — TrackGenerator.GenerateRoadMesh() ile birebir
    /// aynı algoritma: her nokta için gidiş yönüne dik (perpendicular) sağa ve
    /// sola yarım yol genişliği kadar birer köşe noktası (vertex) konup
    /// aralarına üçgenler geriliyor.
    ///
    /// NEDEN LineRenderer DEĞİL: LineRenderer dar virajlarda şeridi kendi
    /// üstüne bindirip basamaklı/kırık kenarlar üretiyordu. Mesh yönteminde
    /// bu sorun yok ve minimap'teki yol, oyundaki gerçek yol mesh'iyle
    /// birebir aynı şekilde oluşuyor.
    /// </summary>
    private void DrawRoad(List<Vector3> trackPoints)
    {
        if (roadObject == null)
        {
            roadObject = new GameObject("MinimapRoad");
            roadObject.transform.SetParent(mapRoot, false);
            roadObject.transform.localPosition = Vector3.zero;
            roadObject.transform.localRotation = Quaternion.identity;
            roadObject.AddComponent<MeshFilter>();
            roadObject.AddComponent<MeshRenderer>().material = roadMaterial != null
                ? roadMaterial
                : MakeUnlitMaterial(new Color(0.35f, 0.35f, 0.38f));
        }

        // Yol kalınlığı: gerçek roadWidth minimap ölçeğine çevriliyor, böylece
        // yol minimap'te de gerçek genişliğiyle orantılı görünüyor.
        float halfWidth = trackGenerator.roadWidth * worldToMapScale * roadWidthMultiplier * 0.5f;

        int count = trackPoints.Count;
        Vector3[] vertices = new Vector3[count * 2];
        int[] triangles = new int[count * 6];

        Vector3 lastRight = Vector3.right;

        for (int i = 0; i < count; i++)
        {
            Vector3 curr = WorldToMapLocal(trackPoints[i], roadHeight);
            Vector3 prevPt = WorldToMapLocal(trackPoints[(i - 1 + count) % count], roadHeight);
            Vector3 next = WorldToMapLocal(trackPoints[(i + 1) % count], roadHeight);

            // ÖNEMLİ: DrawCurbs ile BİREBİR aynı miter hesabı — ikisi farklı
            // offset yöntemi kullanırsa keskin virajlarda yol ile kenarlık
            // birbirinden ayrılıp aralarında boşluk oluşuyor (yaşanmış bug).
            Vector3 right = ComputeMiterRight(prevPt, curr, next, lastRight);
            lastRight = right.sqrMagnitude > 0.0001f ? right.normalized : lastRight;

            vertices[i * 2] = curr - right * halfWidth;
            vertices[i * 2 + 1] = curr + right * halfWidth;

            int baseIndex = i * 2;
            int nextIndex = ((i + 1) % count) * 2;
            int t = i * 6;

            triangles[t] = baseIndex;
            triangles[t + 1] = nextIndex;
            triangles[t + 2] = baseIndex + 1;

            triangles[t + 3] = nextIndex;
            triangles[t + 4] = nextIndex + 1;
            triangles[t + 5] = baseIndex + 1;
        }

        Mesh mesh = new Mesh { name = "MinimapRoadMesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter filter = roadObject.GetComponent<MeshFilter>();
        if (filter.mesh != null) Destroy(filter.mesh);
        filter.mesh = mesh;

        DrawCurbs(trackPoints, halfWidth);
    }

    /// <summary>
    /// Minimap'teki yolun iki yanına kırmızı-beyaz kenarlık çizer — gerçek
    /// pistteki TrackGenerator.GenerateCurbMesh() ile aynı mantık, minimap
    /// ölçeğine uyarlanmış hali.
    ///
    /// Gerçek pistten iki farkı var:
    ///  1. KABARTMA YOK — minimap yukarıdan bakılan düz bir çizim, dış kenarı
    ///     yükseltmek burada anlamsız olurdu. Sadece yolun birazcık üstünde
    ///     duruyor ki iki yüzey birbirine girip titremesin (z-fighting).
    ///  2. Bant uzunluğu ayrı bir alandan geliyor — minimap gerçek pistin
    ///     yüzde biri kadar bir alana sığdığı için gerçek değer burada
    ///     görünmeyecek kadar sık desen üretirdi.
    /// </summary>
    /// <summary>
    /// "Miter join" — TrackGenerator.ComputeMiterRight ile BİREBİR aynı
    /// mantık (gerçek pist ve minimap ayrı sınıflar olduğu için kod
    /// tekrarlanıyor, projenin genel deseni bu). Offset yönü önceki+sonraki
    /// segmentin açıortayından hesaplanıyor, keskin virajlarda ardışık
    /// dörtgenlerin hizasızlığından doğan çentik/burulmayı önlüyor.
    /// fallback: yön hesaplanamayan dejenere durumlarda (üst üste binen
    /// noktalar) kullanılan önceki geçerli yön.
    /// </summary>
    private static Vector3 ComputeMiterRight(Vector3 prev, Vector3 curr, Vector3 next, Vector3 fallback, float miterLimit = 3f)
    {
        Vector3 dirIn = curr - prev;
        Vector3 dirOut = next - curr;

        if (dirIn.sqrMagnitude < 0.0000001f || dirOut.sqrMagnitude < 0.0000001f)
            return fallback;

        dirIn.Normalize();
        dirOut.Normalize();

        Vector3 rightIn = Vector3.Cross(Vector3.up, dirIn).normalized;
        Vector3 rightOut = Vector3.Cross(Vector3.up, dirOut).normalized;

        Vector3 miter = rightIn + rightOut;
        if (miter.sqrMagnitude < 0.0001f) return rightOut; // ~180° dönüş, çok nadir

        miter.Normalize();

        float dot = Vector3.Dot(rightIn, miter);
        float scale = dot > (1f / miterLimit) ? 1f / dot : miterLimit;

        return miter * scale;
    }

    private void DrawCurbs(List<Vector3> trackPoints, float roadHalfWidth)
    {
        if (curbObject != null)
        {
            Destroy(curbObject);
            curbObject = null;
        }

        if (!drawCurbs || trackPoints == null || trackPoints.Count < 3) return;

        curbObject = new GameObject("MinimapCurbs");
        curbObject.transform.SetParent(mapRoot, false);
        curbObject.transform.localPosition = Vector3.zero;
        curbObject.transform.localRotation = Quaternion.identity;
        curbObject.AddComponent<MeshFilter>();
        curbObject.AddComponent<MeshRenderer>().material =
            curbMaterial != null ? curbMaterial : MakeCurbStripeMaterial();

        // Kenarlık genişliği gerçek pistteki curbWidth'ten türetiliyor, ama
        // minimap çok küçük olduğu için birebir oran burada saç teli gibi
        // kalıyor — curbWidthMultiplier ile okunabilir kalınlığa çıkarılıyor.
        float curbW = trackGenerator.curbWidth * worldToMapScale * roadWidthMultiplier * curbWidthMultiplier;

        int count = trackPoints.Count;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();

        // Minimap koordinatındaki toplam uzunluk — deseni halkaya tam
        // oturtmak için gerekiyor (gerçek pisttekiyle aynı düzeltme).
        float totalLength = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = WorldToMapLocal(trackPoints[i], curbHeight);
            Vector3 b = WorldToMapLocal(trackPoints[(i + 1) % count], curbHeight);
            totalLength += Vector3.Distance(a, b);
        }

        int stripeCount = Mathf.Max(1, Mathf.RoundToInt(totalLength / Mathf.Max(0.0001f, curbStripeLength)));
        float fittedStripeLength = totalLength / stripeCount;

        for (int side = 0; side < 2; side++)
        {
            float sideSign = (side == 0) ? -1f : 1f;
            int sideStartIndex = vertices.Count;

            float cumulative = 0f;
            Vector3 lastRight = Vector3.right;

            // Son noktadan sonra ilk noktanın KOPYASI ekleniyor — böylece
            // halkanın kapandığı yerde UV değeri sıfıra düşüp deseni
            // sıkıştırmıyor (gerçek pistte yaşanan bugun aynısı).
            for (int i = 0; i <= count; i++)
            {
                int pointIndex = i % count;

                Vector3 curr = WorldToMapLocal(trackPoints[pointIndex], curbHeight);
                Vector3 prevPt = WorldToMapLocal(trackPoints[(pointIndex - 1 + count) % count], curbHeight);
                Vector3 next = WorldToMapLocal(trackPoints[(pointIndex + 1) % count], curbHeight);

                if (i > 0)
                    cumulative += Vector3.Distance(prevPt, curr);

                // Miter join — gerçek pistteki TrackGenerator.ComputeMiterRight
                // ile AYNI mantık (bkz. oradaki açıklama): offset yönü sadece
                // bir sonraki segmente değil, önceki+sonraki segmentin
                // açıortayına göre hesaplanıyor, keskin virajlarda çentik
                // oluşmasını önlüyor.
                Vector3 right = ComputeMiterRight(prevPt, curr, next, lastRight);
                lastRight = right.sqrMagnitude > 0.0001f ? right.normalized : lastRight;

                vertices.Add(curr + right * (sideSign * roadHalfWidth));
                vertices.Add(curr + right * (sideSign * (roadHalfWidth + curbW)));

                float v = cumulative / fittedStripeLength;
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < count; i++)
            {
                int baseIndex = sideStartIndex + i * 2;
                int nextIndex = sideStartIndex + (i + 1) * 2;

                // Sağ kenarlık yolla aynı sarımı, sol kenarlık ters sarımı
                // kullanmalı — yoksa yüzü aşağı bakıp görünmez olur.
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

        Mesh mesh = new Mesh { name = "MinimapCurbMesh" };
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        curbObject.GetComponent<MeshFilter>().mesh = mesh;
    }

    /// <summary>
    /// Kırmızı-beyaz çizgili unlit materyal — minimap'in diğer parçaları gibi
    /// ışıktan etkilenmiyor ki masanın üstünde her açıdan aynı okunsun.
    /// </summary>
    private Material MakeCurbStripeMaterial()
    {
        const int size = 32;
        var texture = new Texture2D(size, size) { name = "MinimapCurbStripes" };

        for (int y = 0; y < size; y++)
        {
            Color stripe = (y < size / 2) ? Color.red : Color.white;
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, stripe);
        }

        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.Apply();

        Material material = MakeUnlitMaterial(Color.white);
        material.mainTexture = texture;

        // Keskin virajlarda miterLimit'e rağmen kenarlığın kendi üçgenleri
        // (aynı yükseklikte, birbirine çok yakın) hafifçe üst üste binebiliyor
        // — bu, kamera hareket ettikçe kare kare hangi üçgenin "kazandığını"
        // değiştiren klasik bir z-fighting titremesi olarak görünüyor.
        // Render Queue'yu normal Opaque'ın (2000) ÜSTÜNE çıkarıp ZWrite'ı
        // kapatıyoruz — bu sayede kenarlık HER ZAMAN kendi üzerine binen
        // parçaların üstünde, tutarlı şekilde çiziliyor (titreme yerine sabit
        // kırmızı/beyaz görünüyor).
        material.renderQueue = 2500;
        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 0);

        return material;
    }

    private void BuildCheckpointMarkers()
    {
        foreach (GameObject marker in spawnedMarkers)
            if (marker != null) Destroy(marker);
        spawnedMarkers.Clear();

        List<Transform> checkpoints = checkpointManager.checkpoints;

        for (int i = 0; i < checkpoints.Count; i++)
        {
            Transform cp = checkpoints[i];
            if (cp == null) continue;

            // Prefab atanmışsa KENDİ scale'i korunuyor — SetParent(.., false)
            // localScale'e dokunmaz, kod da ayrıca ölçeklemiyor.
            GameObject markerObj = markerPrefab != null
                ? Instantiate(markerPrefab, mapRoot)
                : CreateDefaultMarker(i);

            markerObj.transform.SetParent(mapRoot, false);
            markerObj.transform.localPosition = WorldToMapLocal(cp.position, markerHeight);
            markerObj.transform.localRotation = Quaternion.identity;

            MinimapCheckpointMarker marker = markerObj.GetComponent<MinimapCheckpointMarker>();
            if (marker == null)
                marker = markerObj.AddComponent<MinimapCheckpointMarker>();
            marker.checkpointIndex = i;

            spawnedMarkers.Add(markerObj);
        }

        if (showDebugLogs)
            Debug.Log($"[MinimapController] {spawnedMarkers.Count} checkpoint marker'ı yerleştirildi.");
    }

    /// <summary>Marker prefabı atanmadıysa: küçük bir küre + üzerinde checkpoint numarası.</summary>
    private GameObject CreateDefaultMarker(int index)
    {
        GameObject markerObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerObj.name = $"CheckpointMarker_{index}";
        markerObj.transform.localScale = Vector3.one * markerRadius;
        // Rengi burada sabitlemiyoruz — MinimapCheckpointMarker kendi
        // Awake()'inde varsayılan (yeşil = hazır) rengi uyguluyor ve
        // seçim/cooldown durumuna göre yönetiyor. Burada sadece unlit bir
        // materyal olması yeterli (shader property'leri MinimapCheckpointMarker'ın
        // beklediğiyle uyumlu olsun diye).
        markerObj.GetComponent<Renderer>().material = MakeUnlitMaterial(Color.white);

        if (!showCheckpointNumbers) return markerObj;

        GameObject label = new GameObject("Label");
        label.transform.SetParent(markerObj.transform, false);
        label.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // yukarıdan bakınca okunsun
        label.transform.localScale = Vector3.one * 4f;

        TextMesh text = label.AddComponent<TextMesh>();
        text.text = index.ToString();
        text.fontSize = 48;
        text.characterSize = 0.05f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.white;

        return markerObj;
    }

    /// <summary>
    /// Her frame arabaların minimap'teki konumunu/yönünü günceller.
    /// PlayerRaceController.AllPlayers zaten her client'ta tüm oyuncuları
    /// tutuyor (OnStartClient'ta kaydoluyorlar) ve arabaların pozisyonu
    /// NetworkTransform ile senkron — bu yüzden ekstra network mesajı
    /// GEREKMİYOR, sadece yerel pozisyonu okuyup minimap'e çeviriyoruz.
    /// </summary>
    private void UpdateCarMarkers()
    {
        foreach (PlayerRaceController player in PlayerRaceController.AllPlayers)
        {
            if (player == null) continue;

            if (!carMarkers.TryGetValue(player, out Transform marker) || marker == null)
            {
                marker = CreateCarMarker(player).transform;
                carMarkers[player] = marker;
            }

            // Yarışı bitiren oyuncunun arabası pistten kalkıyor (izleyici
            // moduna geçiyor, bkz. RacerSpectator.cs) — minimap'te de
            // görünmemeli, yoksa sabotajcı artık orada olmayan bir arabayı
            // hedeflemeye çalışırdı.
            bool visible = !player.HasFinished;
            if (marker.gameObject.activeSelf != visible)
                marker.gameObject.SetActive(visible);

            if (!visible) continue;

            Transform carTransform = player.transform;
            marker.localPosition = WorldToMapLocal(carTransform.position, carHeight);
            // Arabanın baktığı yön minimap'te de doğru görünsün (yaw açısı).
            marker.localRotation = Quaternion.Euler(0f, carTransform.eulerAngles.y, 0f);
        }

        // Oyundan çıkan/yok olan arabaların marker'larını temizle
        CleanupDeadCarMarkers();
    }

    private void CleanupDeadCarMarkers()
    {
        List<PlayerRaceController> dead = null;

        foreach (var kvp in carMarkers)
        {
            if (kvp.Key == null)
            {
                (dead ??= new List<PlayerRaceController>()).Add(kvp.Key);
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
        }

        if (dead == null) return;
        foreach (PlayerRaceController player in dead)
            carMarkers.Remove(player);
    }

    private GameObject CreateCarMarker(PlayerRaceController player)
    {
        GameObject markerObj;

        // Marker'a AYRI bir CarController eklemek yerine, oyuncunun gerçek
        // arabasındaki (zaten SyncVar ile senkronize edilmiş) rengi doğrudan
        // okuyoruz — marker'ın kendi network/component'ine ihtiyacı yok.
        Color markerColor = new Color(0.2f, 0.6f, 1f); // renk bulunamazsa varsayılan
        CarController carController = player.GetComponent<CarController>();
        if (carController != null && carController.ColorIndex >= 0 && carController.ColorIndex < CarController.ColorPalette.Length)
            markerColor = CarController.ColorPalette[carController.ColorIndex];

        if (carMarkerPrefab != null)
        {
            // Prefabın KENDİ scale'i aynen korunuyor — kod ölçeklemiyor,
            // boyutu prefab üzerinden ayarlıyorsun. Renk, gerçek arabadaki
            // gibi SADECE "CarBody" isimli materyal slotuna uygulanıyor.
            markerObj = Instantiate(carMarkerPrefab, mapRoot);
            ApplyColorToCarBodySlots(markerObj, markerColor);
        }
        else
        {
            markerObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(markerObj.GetComponent<Collider>()); // arabaya tıklanmasın
            markerObj.GetComponent<Renderer>().material = MakeUnlitMaterial(markerColor);
            // Arabanın hangi yöne baktığı belli olsun diye ileri doğru uzun bir kutu
            markerObj.transform.localScale = new Vector3(0.6f, 0.4f, 1f) * carMarkerScale;
        }

        markerObj.name = $"CarMarker_{player.PlayerLabel}";
        markerObj.transform.SetParent(mapRoot, false);

        return markerObj;
    }

    /// <summary>
    /// CarController.ApplyCarColor() ile AYNI mantık: bir objenin (ve
    /// çocuklarının) TÜM Renderer'larını tarar, sadece ismi "CarBody" içeren
    /// materyal slotuna renk uygular — aynı Renderer'daki başka bir materyal
    /// (ör. colormap) etkilenmez.
    /// </summary>
    private static void ApplyColorToCarBodySlots(GameObject root, Color color)
    {
        MaterialPropertyBlock block = null;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material mat = materials[slot];
                if (mat == null) continue;
                if (mat.name.IndexOf("CarBody", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                block ??= new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, slot);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block, slot);
            }
        }
    }

    private Material MakeUnlitMaterial(Color color)
    {
        Material mat;

        // Inspector'dan gerçek bir materyal atandıysa ONUN bir kopyası kullanılıyor —
        // bu, build'de asla stripelenmez (script alanına sürüklenmiş asset'ler
        // Unity tarafından garanti dahil edilir). Shader.Find() ise sadece
        // Editor'de güvenilir, build'de shader stripping'e takılabiliyor.
        if (unlitBaseMaterial != null)
        {
            mat = new Material(unlitBaseMaterial);
        }
        else
        {
            if (cachedUnlitShader == null)
            {
                cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (cachedUnlitShader == null) cachedUnlitShader = Shader.Find("Unlit/Color");
                if (cachedUnlitShader == null) cachedUnlitShader = Shader.Find("Standard");
            }
            mat = new Material(cachedUnlitShader);
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }
}
