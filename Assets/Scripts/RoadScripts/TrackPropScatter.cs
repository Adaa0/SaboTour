using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ÇEVRE PROP SERPİŞTİRİCİ (19 Ağustos 2026'da tamamen yeniden yazıldı)
///
/// ─── ESKİ DAVRANIŞ (ARTIK YOK) ────────────────────────────────────────
/// Eskiden proplar YOL ÇİZGİSİNİ TAKİP EDİYORDU: her yol noktasında gidiş
/// yönüne dik olarak sağa/sola diziliyorlardı. Sonuç, pistin etrafında bir
/// ŞERİT oluşuyor, haritanın geri kalanı bomboş kalıyordu.
///
/// ─── YENİ DAVRANIŞ ────────────────────────────────────────────────────
/// Proplar artık TÜM ALANA rastgele saçılıyor ve sadece "yasak bölgeler"
/// dışarıda bırakılıyor (reddetme örneklemesi / rejection sampling):
///   ❌ Pistin kendisi + kenarlığı + `trackClearance` kadar çevresi
///   ❌ Pistten `maxDistanceFromTrack`'tan daha uzağı (sonsuza gitmesinler)
///   ❌ Sabotajcı kulesinin merkezinden `towerClearance` yarıçaplı daire
///   ❌ Başka bir propun `minPropSpacing` mesafesi (iç içe geçmesinler)
/// Kalan her yer serbest — pistin İÇİ (halkanın ortası) dahil.
///
/// ─── COLLIDER ─────────────────────────────────────────────────────────
/// Hepsine collider vermek pahalı (8 Ağustos profili: tek gerçek maliyet
/// çizim tarafı, ama binlerce collider fizik tarafını da şişirir). Bu yüzden
/// SADECE piste yakın olanlar (`colliderBandWidth` şeridi içindekiler)
/// collider alıyor — yoldan çıkan araba onlara çarpsın, uzaktakiler sadece
/// dekor olarak kalsın. Ayrıca collider'lar mesh değil basit kapsül/kutu.
///
/// ─── DETERMİNİZM (ÇOK ÖNEMLİ, ARTIK COLLIDER OLDUĞU İÇİN DAHA DA) ─────
/// Rastgelelik TrackGenerator'ın SEED'inden türetiliyor, seed zaten
/// TrackSeedSync ile tüm client'lara gidiyor → herkeste AYNI ağaç AYNI
/// yerde. Bu eskiden sadece görsel bir konuydu; artık proplar collider'lı
/// olduğu için ZORUNLU: bir client'ta var olan ağaç diğerinde olmasaydı,
/// araba bir makinede çarpar öbüründe geçerdi.
/// Reddedilen adaylar da her makinede aynı sırayla reddedildiği için
/// rastgele sayı akışı bozulmuyor.
///
/// ─── ÜRETİM SIRASI ────────────────────────────────────────────────────
/// `onTrackGenerated` olayına abone — o olay yol mesh'i, kenarlık VE
/// checkpoint'ler üretildikten SONRA tetikleniyor (bkz. TrackGenerator.
/// GenerateTrackWithSeed). Yani proplar hiçbir zaman pistten önce oluşmuyor,
/// üst üste binme ihtimali yok.
/// </summary>
public class TrackPropScatter : MonoBehaviour
{
    /// <summary>Collider'lı propların alacağı basit çarpışma şekli.</summary>
    public enum PropColliderShape
    {
        /// <summary>Ağaç/direk gibi ince uzun objeler için.</summary>
        Kapsul,
        /// <summary>Kaya/kütük gibi tıknaz objeler için.</summary>
        Kutu
    }

    [Header("Proplar")]
    [Tooltip("Saçılacak modeller — listeden rastgele seçilir.")]
    [SerializeField] private GameObject[] propPrefabs;

    [Tooltip("Bu scatter'ın ürettiği propların toplanacağı obje adı.\n\n" +
             "AYNI SAHNEDE BİRDEN FAZLA TrackPropScatter KULLANIYORSAN HER BİRİNE " +
             "FARKLI BİR AD VER (ör. 'TrackProps_Agaclar' ve 'TrackProps_Kayalar').\n\n" +
             "Sebep: her scatter temizlik yaparken kendi konteynerini ADINA GÖRE " +
             "buluyor. İkisi aynı adı kullanırsa, ikinci scatter birincinin " +
             "proplarını siler ve sadece bir grup görünür.")]
    [SerializeField] private string groupName = "TrackProps";

    // ─── YASAK BÖLGELER ──────────────────────────────────────────────────
    [Header("YASAK BÖLGELER — proplar buralara KONMAZ")]
    [Tooltip("Pist KENARINDAN (asfalt + kenarlık bittikten sonra) itibaren kaç birim " +
             "boş kalsın. Yarışçının yoldan biraz çıkınca hemen ağaca tosladığı bir " +
             "his olmasın diye.")]
    [SerializeField] private float trackClearance = 20f;

    [Tooltip("Pistin orta çizgisine bu mesafeden UZAK yerlere prop konmaz — " +
             "haritanın sonsuza kadar ağaçla dolmasını engelleyen sınır bu.")]
    [SerializeField] private float maxDistanceFromTrack = 300f;

    [Tooltip("Sabotajcı kulesinin merkezinden itibaren DAİRESEL yasak alanın yarıçapı. " +
             "Kulenin dibi ve çevresi boş kalsın diye.")]
    [SerializeField] private float towerClearance = 50f;

    [Tooltip("Kulenin konumu. BOŞ BIRAKILABİLİR — o zaman dünya merkezi (0,0,0) " +
             "kullanılır, ki TrackGenerator'daki 'Keep Center Clear' pisti zaten " +
             "dünya merkezine ortalıyor ve kule oraya dikiliyor.")]
    [SerializeField] private Transform towerCenter;

    // ─── YOĞUNLUK ────────────────────────────────────────────────────────
    [Header("Yoğunluk")]
    [Tooltip("Kaç prop yerleştirilmeye ÇALIŞILSIN. Haritanın ne kadar dolu görüneceğini " +
             "belirleyen ASIL ayar bu. Yasak bölgeler ve aralık kuralı yüzünden bir kısmı " +
             "reddedilebilir, gerçekleşen sayı Console'a yazılıyor.")]
    [SerializeField] private int targetPropCount = 3000;

    [Tooltip("YOĞUNLUĞUN MESAFEYE GÖRE AZALMASI.\n\n" +
             "Yatay eksen: 0 = pistin hemen kenarı, 1 = Max Distance From Track.\n" +
             "Dikey eksen: 1 = en yoğun, 0 = hiç prop yok.\n\n" +
             "Varsayılan eğri pistin yanında yoğun başlayıp uzaklaştıkça seyreliyor. " +
             "Eğriyi Inspector'da tıklayıp elle şekillendirebilirsin — sağ ucu " +
             "yukarı çekersen uzaklar da dolar, aşağı çekersen tamamen boşalır.")]
    [SerializeField] private AnimationCurve densityByDistance = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f);

    [Tooltip("Pistin HEMEN YANINDA iki prop birbirine en fazla ne kadar yaklaşabilir. " +
             "Uzaklaştıkça bu mesafe yukarıdaki eğriye göre otomatik büyüyor (seyrekleşiyor). " +
             "Küçültürsen pist kenarı sıklaşır.")]
    [SerializeField] private float minPropSpacing = 4f;

    [Tooltip("Güvenlik sınırı — toplam prop sayısı bunu asla aşmaz. Target Prop Count " +
             "bundan büyükse Console uyarı veriyor.")]
    [SerializeField] private int maxProps = 6000;

    [Tooltip("Prop başına ortalama kaç deneme yapılsın. Toplam deneme bütçesi = " +
             "Target Prop Count × bu sayı. Hedefe ulaşılamıyorsa bunu artır.")]
    [Range(1, 50)]
    [SerializeField] private int maxAttemptsPerProp = 15;

    [Tooltip("Aynı sahnedeki DİĞER scatter'ların proplarıyla da mesafe korunsun mu.\n\n" +
             "AÇIK (önerilen): ağaç scatter'ı ile kaya scatter'ı birbirinin içine " +
             "prop koymaz. KAPALI: her grup sadece kendi içinde mesafe korur, " +
             "kaya bir ağacın dibinde doğabilir.")]
    [SerializeField] private bool shareSpacingWithOtherScatters = true;

    // ─── ZEMİN ───────────────────────────────────────────────────────────
    [Header("Zemin")]
    [Tooltip("Prop'un oturacağı zemini bulmak için yukarıdan aşağı ışın atılıyor. " +
             "Hangi katmanlar zemin sayılsın? (Proplar kendi katmanları otomatik " +
             "hariç tutuluyor — yeni prop eski propun tepesine konmasın diye.)")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("Işının atılacağı yükseklik. Haritanın en yüksek noktasından yukarıda olmalı.")]
    [SerializeField] private float groundRaycastHeight = 500f;

    [Tooltip("Zemin bulunamazsa prop bu Y yüksekliğine konur.")]
    [SerializeField] private float fallbackGroundY = 0f;

    [Tooltip("Prop'un dikey konumunu ince ayar — zemine biraz gömmek/kaldırmak için.")]
    [SerializeField] private float heightOffset = 0f;

    // ─── COLLIDER ────────────────────────────────────────────────────────
    [Header("Collider — SADECE piste yakın proplar")]
    [Tooltip("KAPALI: hiçbir propun collider'ı olmaz (eski davranış, en ucuzu).\n" +
             "AÇIK: sadece piste yakın olanlar collider alır, uzaktakiler dekor kalır.")]
    [SerializeField] private bool giveCollidersNearTrack = true;

    [Tooltip("Yasak bölgenin bittiği yerden itibaren kaç birimlik şerit collider alsın. " +
             "'Piste yakın 2-3 sıra' bunun karşılığı — proplar artık sıra sıra dizilmediği " +
             "için sıra sayısı yerine MESAFE ile tanımlanıyor. Yoğunluğa göre 25-40 arası " +
             "kabaca 2-3 ağaç derinliğine denk geliyor.")]
    [SerializeField] private float colliderBandWidth = 30f;

    [Tooltip("Collider şekli. Ağaç gibi ince uzun modeller için Kapsül, kaya gibi " +
             "tıknaz modeller için Kutu. (Ağaç ve kaya için ayrı scatter component'i " +
             "kullandığın için her birine uygun olanı seçebilirsin.)")]
    [SerializeField] private PropColliderShape colliderShape = PropColliderShape.Kapsul;

    [Tooltip("Kapsülün yarıçapı, modelin genişliğinin kaçta kaçı olsun. Ağaçta 1 vermek " +
             "yanlış olur — araba dala değil GÖVDEYE çarpmalı. 0.25 civarı gövde kalınlığı.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float colliderRadiusRatio = 0.25f;

    // ─── UZAKTAN KESME ───────────────────────────────────────────────────
    [Header("Uzaktan Kesme (performans)")]
    [Tooltip("Bu scatter'ın COLLIDER'SIZ propları hangi katmana konsun.\n\n" +
             "NEDEN: Unity katman BAŞINA çizim mesafesi ayarlanabiliyor (bkz. " +
             "PropCullDistances). Kaya/çalı gibi küçük proplar 150 birimden sonra " +
             "zaten görünmüyor ama ağaçla AYNI maliyeti ödüyorlar — erken kesilirse " +
             "bedava kazanç, o bütçe uzaktaki ağaçlara harcanabilir.\n\n" +
             "BOŞ BIRAKILIRSA prefabın kendi katmanı korunur.\n" +
             "⚠️ Collider'lı proplar HER ZAMAN 'Prop' katmanına gider (buz bombası " +
             "bypass'ı ona bağlı) — bu alan onları etkilemez.")]
    [SerializeField] private string cullLayerName = "";

    // ─── ÇEŞİTLİLİK ──────────────────────────────────────────────────────
    [Header("Çeşitlilik")]
    [SerializeField] private bool randomYaw = true;
    [SerializeField] private float minScale = 0.8f;
    [SerializeField] private float maxScale = 1.4f;

    // ─── PERFORMANS ──────────────────────────────────────────────────────
    [Header("Performans")]
    [Tooltip("Bu scatter'ın propları gölge yapsın mı?\n\n" +
             "EN BÜYÜK PERFORMANS AYARI BU. Gölge çizilirken her obje cascade " +
             "sayısı kadar TEKRAR çiziliyor — yani gölge yapan 1500 prop, " +
             "4 cascade ile 6000 ekstra çizim demek.\n\n" +
             "ÖNERİ: Ağaçlar için AÇIK, kaya/çalı/çim için KAPALI.")]
    [SerializeField] private bool castShadows = true;

    [Tooltip("⚠️ BU PROJEDE KAPALI KALMALI. Static batching, GPU Instancing'i DEVRE " +
             "DIŞI BIRAKIYOR — ikisi aynı anda çalışmıyor ve bizim senaryomuzda " +
             "(az sayıda model, çok kopya) instancing çok daha iyi sonuç veriyor.\n\n" +
             "Belirtisi: Profiler'da '(Instancing) Batches' sıfıra düşer, toplam " +
             "Batches birkaç bine fırlar.")]
    [SerializeField] private bool useStaticBatching = false;

    [Tooltip("Prop dizilimi pistin seed'inden türetiliyor, yani aynı pistte hep aynı " +
             "dizilim çıkar. Bu sayıyı değiştirirsen PİST AYNI KALIR ama ağaç/kaya " +
             "dizilimi tamamen değişir.\n\n" +
             "NETWORK NOTU: Bu değer sahneyle kaydedildiği için tüm oyuncularda aynı olur.")]
    [SerializeField] private int propSeedOffset = 0;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    [Tooltip("Scene view'da yasak bölgeleri çizer (kule dairesi + pist tampon şeridi).")]
    [SerializeField] private bool drawZoneGizmos = true;

    private TrackGenerator trackGenerator;
    private Transform propContainer;

    // Pistin SIKLAŞTIRILMIŞ hâli — bkz. DensifyTrack().
    private List<Vector2> denseTrack;
    private SpatialGrid trackGrid;
    private SpatialGrid placedGrid;

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

        // Pist her üretildiğinde propları yeniden diz. Bu olay yol mesh'i +
        // kenarlık + checkpoint'ler bittikten SONRA tetikleniyor, yani proplar
        // hiçbir zaman pistten önce oluşmuyor.
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

    private string ResolvedGroupName =>
        string.IsNullOrWhiteSpace(groupName) ? "TrackProps" : groupName;

    private Vector2 TowerCenterXZ => towerCenter != null
        ? new Vector2(towerCenter.position.x, towerCenter.position.z)
        : Vector2.zero;

    /// <summary>
    /// Aynı objede aynı grup adını kullanan başka bir scatter var mı diye bakar.
    /// Varsa ikisi birbirinin proplarını siler ve sadece biri görünür.
    /// </summary>
    private void WarnOnDuplicateGroupName()
    {
        foreach (TrackPropScatter other in GetComponents<TrackPropScatter>())
        {
            if (other == this) continue;
            if (other.ResolvedGroupName != ResolvedGroupName) continue;

            Debug.LogWarning(
                $"[TrackPropScatter] Bu objede '{ResolvedGroupName}' grup adını kullanan " +
                "BİRDEN FAZLA TrackPropScatter var. İkisi birbirinin proplarını siler ve " +
                "sadece bir grup görünür. Her scatter'ın 'Group Name' alanına FARKLI bir " +
                "ad yaz (ör. 'TrackProps_Agaclar' / 'TrackProps_Kayalar').", this);
            return;
        }
    }

    public void Scatter()
    {
        WarnOnDuplicateGroupName();

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

        propContainer = new GameObject(ResolvedGroupName).transform;
        propContainer.SetParent(transform, false);

        BuildTrackLookup(trackPoints);

        // Seed'den türetilen rastgelelik → her client'ta aynı sonuç.
        // UnityEngine.Random yerine System.Random: global Random durumunu
        // bozmuyor (TrackGenerator ondan sayı çekiyor).
        System.Random rng = new System.Random(trackGenerator.seed + propSeedOffset);

        // Yasak bölge yarıçapları
        float roadHalf = trackGenerator.roadWidth * 0.5f;
        float innerLimit = roadHalf + trackGenerator.curbWidth + trackClearance;
        float outerLimit = Mathf.Max(innerLimit + 1f, maxDistanceFromTrack);
        float colliderLimit = innerLimit + Mathf.Max(0f, colliderBandWidth);

        // Örnekleme alanı: pistin sınırlarını maxDistanceFromTrack kadar genişlet.
        GetTrackBounds(out Vector2 boundsMin, out Vector2 boundsMax);
        boundsMin -= Vector2.one * outerLimit;
        boundsMax += Vector2.one * outerLimit;

        placedGrid = ResolveSpacingGrid();

        if (targetPropCount > maxProps && showDebugLogs)
            Debug.LogWarning($"[TrackPropScatter] '{ResolvedGroupName}': Target Prop Count " +
                             $"({targetPropCount}) > Max Props ({maxProps}). Max Props seni " +
                             "sınırlıyor — daha fazla prop istiyorsan onu da yükselt.");

        int target = Mathf.Min(targetPropCount, maxProps);

        // Deneme bütçesi TOPLAM tutuluyor (prop başına değil): uzak bölgelerde
        // reddedilen adaylar bütçeden yiyor ama yakın bölgede yer varken
        // yerleştirmeyi durdurmuyor.
        int attemptBudget = target * maxAttemptsPerProp;
        int attempts = 0;
        int placedCount = 0;
        int withCollider = 0;

        while (placedCount < target && attempts < attemptBudget)
        {
            attempts++;

            Vector2 candidate = new Vector2(
                Mathf.Lerp(boundsMin.x, boundsMax.x, NextFloat(rng)),
                Mathf.Lerp(boundsMin.y, boundsMax.y, NextFloat(rng)));

            // ❌ Kule çevresi
            if ((candidate - TowerCenterXZ).sqrMagnitude < towerClearance * towerClearance)
                continue;

            // Pistin orta çizgisine mesafe. outerLimit'e kadar arıyoruz —
            // daha uzağını bilmemize gerek yok, zaten reddedilecek.
            float distanceToTrack = trackGrid.NearestDistance(candidate, outerLimit);

            // ❌ Pistin üstü / hemen yanı
            if (distanceToTrack < innerLimit) continue;

            // ❌ Pistten çok uzak (NearestDistance bulamazsa sonsuz döner)
            if (distanceToTrack > outerLimit) continue;

            // ─── MESAFEYE GÖRE YOĞUNLUK ─────────────────────────────────
            // Eğri İKİ İŞ birden yapıyor:
            //  (1) Bu mesafede prop KONMA İHTİMALİ — uzakta adayların çoğu
            //      daha en baştan eleniyor, böylece toplam sayı düşükken bile
            //      proplar pist çevresinde toplanıyor.
            //  (2) Proplar arası MİNİMUM BOŞLUK — yoğunluk alan başına düştükçe
            //      aralık büyümeli. Yoğunluk ~ 1/aralık² olduğu için aralık
            //      1/√yoğunluk ile ölçekleniyor (yarı yoğunluk = 1.41 kat aralık).
            float t = Mathf.InverseLerp(innerLimit, outerLimit, distanceToTrack);
            float density = Mathf.Clamp01(densityByDistance.Evaluate(t));

            if (density <= 0.001f) continue;                 // burası tamamen boş kalsın
            if (NextFloat(rng) > density) continue;           // (1) seyrelt

            float spacing = minPropSpacing / Mathf.Sqrt(density);   // (2) aralığı büyüt

            // ❌ Başka bir propun dibi
            if (placedGrid.HasAnyWithin(candidate, spacing)) continue;

            bool wantsCollider = giveCollidersNearTrack && distanceToTrack <= colliderLimit;

            if (!PlaceProp(candidate, rng, wantsCollider)) continue;

            placedGrid.Add(candidate);
            placedCount++;
            if (wantsCollider) withCollider++;
        }

        if (showDebugLogs)
        {
            string missNote = placedCount < target
                ? $" — hedef {target} idi, {target - placedCount} tanesine yer bulunamadı " +
                  "(Min Prop Spacing'i düşür, yoğunluk eğrisinin sağ ucunu yukarı çek " +
                  "ya da Max Attempts Per Prop'u artır)."
                : "";
            Debug.Log($"[TrackPropScatter] '{ResolvedGroupName}': {placedCount} prop yerleştirildi, " +
                      $"{withCollider} tanesi collider'lı, {attempts} deneme " +
                      $"(seed {trackGenerator.seed}).{missNote}");
        }

        ApplyStaticBatching();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PİSTE MESAFE — SIKLAŞTIRMA + IZGARA
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🚨 BU METOT NEDEN VAR — PROJEDE BİR KERE CANIMIZI YAKAN HATA:
    /// `GetTrackPoints()` listesi EŞİT ARALIKLI DEĞİL. Bezier yumuşatması
    /// virajlara çok nokta koyuyor, düzlüklere çok az — iki komşu nokta arası
    /// yüzlerce metre olabiliyor. "Yola olan mesafe"yi bu NOKTALARA bakarak
    /// ölçmek, düzlüklerde yolun tam ortasındaki bir yeri "yoldan 90 metre
    /// uzak" gösteriyor. Sonsuz ışınlanma bug'ı (CheckpointRecovery) tam
    /// olarak buydu.
    ///
    /// Burada çözüm: yolu önce SIKLAŞTIRIYORUZ (uzun parçaları bölüyoruz),
    /// böylece komşu noktalar en fazla `step` kadar ayrık kalıyor ve
    /// noktalara olan mesafe, çizgiye olan gerçek mesafeden en fazla
    /// `step / 2` sapıyor. `step` 4 birim → hata en fazla 2 birim, bizim
    /// 20+ birimlik tamponlarımızın yanında önemsiz.
    /// </summary>
    private void BuildTrackLookup(List<Vector3> trackPoints)
    {
        const float step = 4f;

        denseTrack = new List<Vector2>(trackPoints.Count * 2);

        for (int i = 0; i < trackPoints.Count; i++)
        {
            Vector3 a3 = trackPoints[i];
            Vector3 b3 = trackPoints[(i + 1) % trackPoints.Count];   // halka kapalı

            Vector2 a = new Vector2(a3.x, a3.z);
            Vector2 b = new Vector2(b3.x, b3.z);

            denseTrack.Add(a);

            float segmentLength = Vector2.Distance(a, b);
            int extra = Mathf.FloorToInt(segmentLength / step);
            for (int k = 1; k <= extra; k++)
                denseTrack.Add(Vector2.Lerp(a, b, k / (float)(extra + 1)));
        }

        // Izgara hücresi, sorgulanan mesafelere göre makul bir boyutta.
        trackGrid = new SpatialGrid(25f);
        foreach (Vector2 p in denseTrack) trackGrid.Add(p);
    }

    private void GetTrackBounds(out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        foreach (Vector2 p in denseTrack)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PAYLAŞILAN ARALIK IZGARASI
    // ═════════════════════════════════════════════════════════════════════

    private static SpatialGrid sharedGrid;
    private static int sharedGridFrame = -1;

    /// <summary>
    /// Propların birbirine yaklaşmasını engelleyen ızgarayı verir.
    ///
    /// NEDEN PAYLAŞILIYOR: Ağaç scatter'ı ile kaya scatter'ı AYRI
    /// component'ler. Her biri sadece kendi proplarını bilseydi, kaya bir
    /// ağacın tam dibinde (hatta içinde) doğabilirdi — "proplar birbirinin
    /// içine geçmesin" isteği ancak ortak bir ızgarayla karşılanıyor.
    ///
    /// NE ZAMAN SIFIRLANIYOR: İki scatter da AYNI KARE içinde çalışıyor
    /// (ikisi de `onTrackGenerated` olayına abone ve olay tek seferde
    /// tetikleniyor). Bu yüzden "kare numarası değiştiyse yeni bir pist
    /// üretilmiş demektir" kuralı yeterli: o karede ilk çalışan ızgarayı
    /// sıfırlıyor, ikincisi üstüne ekliyor. Ayrıca bir temizlik çağrısı
    /// tutmaya gerek kalmıyor.
    ///
    /// SIRA DETERMİNİSTİK: hangi scatter'ın önce çalışacağı component
    /// sırasına bağlı, o da sahneyle birlikte kaydedildiği için tüm
    /// oyuncularda aynı — dizilim senkron kalıyor.
    ///
    /// ⚠️ BİLİNEN KÜÇÜK SINIR: Editör butonundan SADECE bir grubu yeniden
    /// serpiştirirsen, diğer grubun mevcut propları hesaba katılmaz (ızgara
    /// o karede sıfırlanır). Play'de ikisi birlikte çalıştığı için sorun değil.
    /// </summary>
    private SpatialGrid ResolveSpacingGrid()
    {
        float cell = Mathf.Max(1f, minPropSpacing);

        if (!shareSpacingWithOtherScatters)
            return new SpatialGrid(cell);

        if (sharedGrid == null || sharedGridFrame != Time.frameCount)
        {
            sharedGrid = new SpatialGrid(cell);
            sharedGridFrame = Time.frameCount;
        }

        return sharedGrid;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  YERLEŞTİRME
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Zemini bulup prop'u oraya koyar. Zemin bulunamazsa (ışın hiçbir şeye
    /// çarpmazsa) `fallbackGroundY` kullanılır. false dönerse prop konulamadı.
    /// </summary>
    private bool PlaceProp(Vector2 positionXZ, System.Random rng, bool wantsCollider)
    {
        GameObject prefab = propPrefabs[rng.Next(propPrefabs.Length)];
        if (prefab == null) return false;

        float groundY = SampleGroundHeight(positionXZ);
        Vector3 position = new Vector3(positionXZ.x, groundY + heightOffset, positionXZ.y);

        // ÖNEMLİ: Prefabın KENDİ rotasyonunu koruyup rastgele yaw'ı onun
        // ÜSTÜNE ekliyoruz. Doğrudan Quaternion.Euler(0, yaw, 0) verirsek
        // prefabın kendi duruşu silinir — Blender'dan gelen (Z-up) modeller
        // bu yüzden yan yatıyordu.
        Quaternion yawRotation = randomYaw
            ? Quaternion.Euler(0f, NextFloat(rng) * 360f, 0f)
            : Quaternion.identity;

        GameObject prop = Instantiate(prefab, position, yawRotation * prefab.transform.rotation, propContainer);

        float scale = Mathf.Lerp(minScale, maxScale, NextFloat(rng));
        prop.transform.localScale = prefab.transform.localScale * scale;

        SetupColliders(prop, wantsCollider);
        ApplyLayer(prop, wantsCollider);

        // Gölge yapmayan proplar, gölge haritası çizilirken tamamen atlanıyor.
        // Görünürlükleri değişmiyor — sadece YERE gölge düşürmüyorlar.
        if (!castShadows)
            foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return true;
    }

    /// <summary>
    /// Yukarıdan aşağı ışın atıp zemin yüksekliğini bulur.
    ///
    /// Prop katmanı maskeden ÇIKARILIYOR — yoksa yeni bir prop, az önce
    /// konmuş collider'lı bir ağacın tepesine oturabilirdi.
    /// </summary>
    private float SampleGroundHeight(Vector2 positionXZ)
    {
        int mask = groundMask.value & ~PropLayerMask;

        Vector3 origin = new Vector3(positionXZ.x, groundRaycastHeight, positionXZ.y);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                            groundRaycastHeight * 2f, mask, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        return fallbackGroundY;
    }

    /// <summary>
    /// Prop'un collider'larını ayarlar.
    ///
    /// HER DURUMDA modelin KENDİ collider'ları siliniyor: FBX'lerden gelen
    /// mesh collider'lar hem pahalı hem de gereğinden ayrıntılı (araba bir
    /// ağacın her dalına ayrı ayrı çarpmamalı). Yerine, collider isteniyorsa
    /// modelin gerçek boyutundan hesaplanmış TEK bir basit şekil konuyor.
    /// </summary>
    private void SetupColliders(GameObject prop, bool wantsCollider)
    {
        foreach (Collider col in prop.GetComponentsInChildren<Collider>())
            DestroySafe(col);

        if (!wantsCollider) return;

        Bounds bounds = GetRendererBounds(prop);
        if (bounds.size.sqrMagnitude < 0.0001f) return;

        // Yerel (dönmemiş/ölçeklenmemiş) boyuta çeviriyoruz — collider
        // objenin kendi uzayında tanımlanıyor.
        Vector3 lossy = prop.transform.lossyScale;
        Vector3 localSize = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

        Vector3 localCenter = prop.transform.InverseTransformPoint(bounds.center);

        if (colliderShape == PropColliderShape.Kapsul)
        {
            CapsuleCollider capsule = prop.AddComponent<CapsuleCollider>();
            capsule.direction = 1;                       // Y ekseni boyunca
            capsule.height = localSize.y;
            capsule.radius = Mathf.Max(localSize.x, localSize.z) * 0.5f * colliderRadiusRatio;
            capsule.center = localCenter;
        }
        else
        {
            BoxCollider box = prop.AddComponent<BoxCollider>();
            box.size = localSize;
            box.center = localCenter;
        }
    }

    /// <summary>
    /// Prop'un katmanını belirler.
    ///
    /// İKİ FARKLI AMAÇ, İKİ FARKLI KATMAN:
    ///  - Collider'lı proplar → "Prop". Buz bombasıyla fırlayan aracın hangi
    ///    collider'ları yok sayacağı bu katmana bağlı, pazarlık yok.
    ///  - Collider'sız proplar → `cullLayerName` (varsa). Sadece ÇİZİM
    ///    mesafesini ayarlamak için; fizikle ilgisi yok.
    ///
    /// KATMAN TÜM ALT OBJELERE UYGULANIYOR: Unity çizim mesafesini her
    /// Renderer'ın KENDİ objesinin katmanına göre değerlendiriyor. Modelin
    /// mesh'i bir alt objedeyse sadece köke katman vermek hiçbir işe
    /// yaramazdı — prop görünmeye devam ederdi.
    /// </summary>
    private void ApplyLayer(GameObject prop, bool hasCollider)
    {
        if (hasCollider)
        {
            if (PropLayer >= 0) SetLayerRecursive(prop, PropLayer);
            return;
        }

        if (string.IsNullOrWhiteSpace(cullLayerName)) return;

        int layer = LayerMask.NameToLayer(cullLayerName);
        if (layer < 0)
        {
            if (!warnedMissingCullLayer)
            {
                warnedMissingCullLayer = true;
                Debug.LogWarning($"[TrackPropScatter] '{ResolvedGroupName}': '{cullLayerName}' " +
                                 "diye bir katman yok (Project Settings > Tags and Layers). " +
                                 "Proplar prefabın kendi katmanında kalıyor, uzaktan kesme çalışmayacak.", this);
            }
            return;
        }

        SetLayerRecursive(prop, layer);
    }

    private bool warnedMissingCullLayer;

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    /// <summary>Modelin tüm Renderer'larını kapsayan dünya bounds'u.</summary>
    private static Bounds GetRendererBounds(GameObject prop)
    {
        Renderer[] renderers = prop.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(prop.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PROP KATMANI
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Collider'lı propların katmanı. Proje ayarlarında "Prop" adında bir
    /// katman tanımlı değilse -1 döner — o durumda sistem yine çalışır ama
    /// buz bombasıyla fırlayan araba ağaçlara takılmaya devam eder.
    /// </summary>
    public static int PropLayer
    {
        get
        {
            if (cachedPropLayer == -2) cachedPropLayer = LayerMask.NameToLayer("Prop");
            return cachedPropLayer;
        }
    }
    private static int cachedPropLayer = -2;   // -2 = henüz bakılmadı

    private static int PropLayerMask => PropLayer >= 0 ? (1 << PropLayer) : 0;

    // ═════════════════════════════════════════════════════════════════════
    //  YARDIMCILAR
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tüm propları toplu mesh'lere birleştirir (static batching).
    /// MUTLAKA propların HEPSİ yerleştirildikten SONRA çağrılmalı.
    /// </summary>
    private void ApplyStaticBatching()
    {
        if (!useStaticBatching) return;
        if (propContainer == null) return;

        StaticBatchingUtility.Combine(propContainer.gameObject);

        if (showDebugLogs)
            Debug.Log("[TrackPropScatter] Proplar static batching ile birleştirildi.");
    }

    /// <summary>
    /// Play modunda Destroy, Editör'de DestroyImmediate kullanır. Unity edit
    /// modunda Destroy() çağrısını reddedip hata basıyor, o yüzden
    /// serpiştirmeyi editörden tetikleyebilmek için bu sarmalayıcı gerekli.
    /// </summary>
    private static void DestroySafe(UnityEngine.Object target)
    {
        if (target == null) return;

        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    /// <summary>
    /// Sahnedeki propları siler. Inspector'daki "Propları Temizle" butonu bunu çağırıyor.
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

        // Sahnede eski bir konteyner kalmışsa (ör. sahne yeniden yüklendiyse) onu
        // da temizle. SADECE KENDİ adımızı arıyoruz — başka bir TrackPropScatter'ın
        // konteynerine dokunmuyoruz.
        Transform existing = transform.Find(ResolvedGroupName);
        if (existing != null) DestroySafe(existing.gameObject);
    }

    /// <summary>System.Random 0-1 arası float üretmiyor, kendimiz sarmalıyoruz.</summary>
    private static float NextFloat(System.Random rng) => (float)rng.NextDouble();

    void OnDrawGizmosSelected()
    {
        if (!drawZoneGizmos) return;

        // Kule yasak alanı
        Gizmos.color = Color.red;
        Vector2 tower = TowerCenterXZ;
        DrawCircleGizmo(new Vector3(tower.x, 0f, tower.y), towerClearance);
    }

    private static void DrawCircleGizmo(Vector3 center, float radius, int segments = 64)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  BASİT IZGARA (SPATIAL HASH)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Noktaları hücrelere bölüp "yakınımda ne var" sorularını hızlandıran
    /// basit bir ızgara.
    ///
    /// NEDEN GEREKLİ: Her prop adayı için pistin TÜM noktalarına tek tek
    /// mesafe ölçmek O(aday × nokta) — sıklaştırılmış pistte binlerce nokta
    /// var ve binlerce aday deniyoruz, milyonlarca işlem eder. Aynı sorun
    /// propların birbirine mesafesinde de var (klasik O(n²)). Izgara ile
    /// sadece komşu hücrelere bakılıyor.
    /// </summary>
    private class SpatialGrid
    {
        private readonly float cellSize;
        private readonly Dictionary<long, List<Vector2>> cells = new Dictionary<long, List<Vector2>>();

        public SpatialGrid(float cellSize)
        {
            this.cellSize = Mathf.Max(0.01f, cellSize);
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        private int CellOf(float v) => Mathf.FloorToInt(v / cellSize);

        public void Add(Vector2 point)
        {
            long key = Key(CellOf(point.x), CellOf(point.y));
            if (!cells.TryGetValue(key, out List<Vector2> list))
            {
                list = new List<Vector2>();
                cells[key] = list;
            }
            list.Add(point);
        }

        /// <summary>
        /// Verilen mesafe içinde HERHANGİ bir nokta var mı. Prop'ların
        /// birbirine çok yaklaşmasını engellemek için kullanılıyor.
        /// </summary>
        public bool HasAnyWithin(Vector2 point, float distance)
        {
            if (distance <= 0f) return false;

            int rings = Mathf.CeilToInt(distance / cellSize);
            float sqr = distance * distance;
            int cx = CellOf(point.x), cy = CellOf(point.y);

            for (int x = cx - rings; x <= cx + rings; x++)
                for (int y = cy - rings; y <= cy + rings; y++)
                {
                    if (!cells.TryGetValue(Key(x, y), out List<Vector2> list)) continue;
                    foreach (Vector2 other in list)
                        if ((other - point).sqrMagnitude < sqr) return true;
                }

            return false;
        }

        /// <summary>
        /// En yakın noktaya olan mesafe. `maxSearch` içinde hiçbir nokta
        /// yoksa float.MaxValue döner.
        ///
        /// Halkaları içten dışa doğru tarıyor ve eldeki en iyi sonuç, bir
        /// sonraki halkanın olabilecek EN YAKIN mesafesinden küçükse erken
        /// çıkıyor — uzaktaki hücreleri boşuna gezmemek için.
        /// </summary>
        public float NearestDistance(Vector2 point, float maxSearch)
        {
            int maxRings = Mathf.CeilToInt(maxSearch / cellSize);
            int cx = CellOf(point.x), cy = CellOf(point.y);

            // KARESEL mesafe tutuluyor (karekök almak pahalı, karşılaştırma için
            // gereksiz). Erken çıkış eşiği de bu yüzden KARESİYLE karşılaştırılıyor —
            // ikisini karıştırmak sessizce yanlış sonuç verirdi.
            float bestSqr = float.MaxValue;

            for (int ring = 0; ring <= maxRings; ring++)
            {
                // Bir sonraki halkadaki en yakın nokta bile en az bu kadar uzakta
                // olabilir; elimizdeki bundan iyiyse aramayı bitir.
                float ringMinDistance = (ring - 1) * cellSize;
                if (ringMinDistance > 0f && bestSqr <= ringMinDistance * ringMinDistance) break;

                for (int x = cx - ring; x <= cx + ring; x++)
                    for (int y = cy - ring; y <= cy + ring; y++)
                    {
                        // Sadece halkanın KENARINDAKİ hücreler — içi zaten tarandı.
                        bool onEdge = Mathf.Abs(x - cx) == ring || Mathf.Abs(y - cy) == ring;
                        if (!onEdge) continue;

                        if (!cells.TryGetValue(Key(x, y), out List<Vector2> list)) continue;

                        foreach (Vector2 other in list)
                        {
                            float d = (other - point).sqrMagnitude;
                            if (d < bestSqr) bestSqr = d;
                        }
                    }
            }

            return bestSqr == float.MaxValue ? float.MaxValue : Mathf.Sqrt(bestSqr);
        }
    }
}
