using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ARABANIN SESLERİ — motor, lastik cızırtısı, çarpma.
///
/// MANUEL ADIM: Bu component `Car.prefab`'ın KÖK objesine eklenmeli (yani
/// CarController ve Rigidbody'nin durduğu objeye — çarpışma olaylarını
/// (OnCollisionEnter) sadece Rigidbody'nin olduğu obje alır).
///
/// NEDEN AYRI BİR DOSYA (CarController'ın içine yazılmadı): CarController
/// zaten ~950 satır ve fizik/network mantığıyla dolu. Ses tamamen görsel-
/// işitsel bir katman, hiçbir oynanış kararını etkilemiyor — ayrı tutulunca
/// hem okuması kolay hem de sesle ilgili bir sorunda tek dosyaya bakman
/// yetiyor.
///
/// NETWORK NOTU: Bu script'in ağla HİÇBİR İŞİ YOK ve olması da gerekmiyor.
/// Okuduğu üç değer (SpeedRatio / IsSkidding / IsGroundedNow) CarController
/// tarafından zaten senkronize ediliyor, yani başka bir oyuncunun arabası
/// senin ekranında hızlanırken motor sesi de doğru şekilde yükseliyor.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarAudio : MonoBehaviour
{
    [Header("Motor Sesi — Devir Bantları")]
    [Tooltip("i6 / Rotary tarzı motor paketlerinden gelen LOOP klipleri. Hız arttıkça " +
             "komşu bantlar arası CROSSFADE yapılıyor — tek klibi perde kaydırmaktan çok " +
             "daha gerçekçi (\"1. viteste kalıyor\" hissini çözer).\n\n" +
             "En az `Idle Clip` + 1-2 bant koy. Atamadığın bant sessiz; hiçbiri yoksa " +
             "motor tamamen sessiz (oyun bozulmaz).\n\n" +
             "i6 paketi eşleşmesi: idle→Idle, low_on→Low, med_on→Med, high_on→High.")]
    [SerializeField] private AudioClip idleClip;
    [SerializeField] private AudioClip lowClip;
    [SerializeField] private AudioClip medClip;
    [SerializeField] private AudioClip highClip;

    [Tooltip("low / med kliplerinin DEVİR EKSENİNDE nereye oturduğu (0 = rölanti, 1 = tam devir). " +
             "idle her zaman 0, high her zaman 1. Kulakla ayarla: bantlar arası geçiş pürüzsüz mü, " +
             "bir bant çok baskın mı.")]
    [Range(0.05f, 0.5f)][SerializeField] private float lowRpm = 0.26f;
    [Range(0.35f, 0.9f)][SerializeField] private float medRpm = 0.54f;

    [Tooltip("🚨 'low/med bantlarını hiç duymuyorum, direkt high'a atlıyor' AYARI BUDUR.\n\n" +
             "Devir eğrisi. 1 = düz (hız ↔ devir doğrusal). >1 = düşük hızlar daha GENİŞ bir " +
             "devir aralığına yayılır, yani low/med bantları çok daha uzun duyulur ve high " +
             "sadece GERÇEK son hıza yakınken baskın olur.\n\n" +
             "SEBEP: arcade araba zamanının %80'ini yüksek hızda geçiriyor; düz eğride bu " +
             "sürekli 'high' demek. 1.5 iyi başlangıç, 2'ye kadar çıkabilirsin.")]
    [Range(1f, 3f)][SerializeField] private float rpmCurve = 1.6f;

    [Tooltip("Bantların İÇİNDE uygulanan hafif perde kayması (0 = rölanti, 1 = tam devir). " +
             "Bantlar arası basamağı yumuşatıyor — dar tut, asıl iş crossfade'de. " +
             "1.15 üstü 'ciyaklama' riski; high klibi zaten agresifse bunu düşür.")]
    [SerializeField] private float engineMinPitch = 0.95f;
    [SerializeField] private float engineMaxPitch = 1.08f;

    [Tooltip("Araba DURURKEN motor ses seviyesi (rölanti arka planda, dikkat çekmesin diye kısık).")]
    [Range(0f, 1f)][SerializeField] private float idleVolume = 0.06f;
    [Tooltip("MAKSİMUM HIZDAKİ ses seviyesi.")]
    [Range(0f, 1f)][SerializeField] private float maxVolume = 0.28f;
    [Tooltip("Sesin hıza göre yükselme eğrisi. 1 = düz, 2 = düşük hızda uzun süre kısık (rölanti baskın olmasın diye önerilen), 0.5 = ilk gazda hızla açılır.")]
    [Range(0.25f, 4f)][SerializeField] private float volumeRamp = 2f;
    [Tooltip("Motorun TAM devir (rpm=1) sayılacağı hız (km/h). Arabanın azamisi ~220, yarışta " +
             "sürekli ~200'de gidiyor. Bu değeri azaminin ÜSTÜNE koy (230-240) — yoksa cruise " +
             "hızında rpm sürekli 1'e yapışıp 'high' bandında sıkışıyorsun. 230'da cruise ≈ " +
             "med/high karışımı, high sadece gerçek son hızda baskın.")]
    [SerializeField] private float speedForMaxPitch = 242f;
    [Tooltip("Devirin hıza yetişme yumuşaklığı. Büyük = anında/keskin geçiş, küçük = bantlar " +
             "arası geçiş daha yavaş/yumuşak. 'Geçişler çok hızlı' diyorsan bunu düşür.")]
    [SerializeField] private float pitchSmoothing = 4f;

    [Tooltip("Bu hızın (km/h) ÜZERİNDE araç 'hareket ediyor/yük altında' sayılıyor ve motor " +
             "en az `Low` bandına çıkıyor — `Idle` sadece araç GERÇEKTEN dururken çalar.\n\n" +
             "NEDEN: keskin driftte ileri hız (SpeedKmh) düşüyor ama araç hâlâ hızlı kayıyor; " +
             "bu eşik + drift/kayma kontrolü o an idle'a düşmeyi engelliyor (drift sırasında " +
             "devir `Med` bandına çekiliyor).")]
    [SerializeField] private float idleReleaseSpeedKmh = 6f;

    [Header("Sanal Vites (Mario Kart tarzı — opsiyonel)")]
    [Tooltip("KAPALI = bantlar hız boyunca sürekli birbirine karışır (yumuşak ama " +
             "timbre 'sürünür').\n\n" +
             "AÇIK = her bant BİR VİTES. low = 1. vites, med = 2., high = 3. Aktif " +
             "vitesin klibi kendi hız aralığında perde olarak tırmanır; sınıra " +
             "gelince bir sonraki bant DİP perdeden girer (= yukarı vites hissi). " +
             "Mario Kart bunu yapıyor: az sayıda örnek, her biri bir vites, vites " +
             "SESİ yok — geçiş zaten örnek + perde sıçramasından anlaşılıyor.")]
    [SerializeField] private bool useGearShifts = false;
    [Tooltip("Vites İÇİNDE perde ne kadar tırmanır. Yumuşak moddaki dar aralığın " +
             "aksine burada perde ASIL işi yapıyor, geniş tut. 0.82 → 1.35 iyi.")]
    [SerializeField] private float gearMinPitch = 0.82f;
    [SerializeField] private float gearMaxPitch = 1.35f;
    [Tooltip("Vitesin son yüzde kaçında bir sonraki bant devreye girip crossfade " +
             "olur. Küçük (0.1) = keskin/ani shift, büyük (0.25) = yumuşak geçiş.")]
    [Range(0.05f, 0.4f)][SerializeField] private float gearBlend = 0.15f;
    [Tooltip("🚨 'Bir hızda takılınca sürekli vites atıp indiriyor' AYARI BUDUR " +
             "(histerezis). Yukarı vites tam sınırda atılıyor; AŞAĞI vites ise " +
             "ancak bu kadar (vitesin oranı) altına DÜŞÜNCE atılıyor. 0.2 = bir " +
             "sonraki sınırın %20 altına inmeden geri dönmez. Büyük = daha kararlı " +
             "ama geç indirir.")]
    [Range(0f, 0.5f)][SerializeField] private float gearHysteresis = 0.2f;
    [Tooltip("İki vites değişimi arasındaki en kısa süre (sn). Sert hızlanmada " +
             "art arda 'tık tık tık' vites sesini engeller. 0.25 iyi.")]
    [SerializeField] private float gearShiftCooldown = 0.25f;

    [Header("Lastik Cızırtısı (drift / kayma)")]
    [Tooltip("SÜREKLİ DÖNEN (loop) lastik kayma sesi. Duman ve lastik iziyle TAM AYNI anda başlayıp bitiyor (CarController.IsSkidding).")]
    [SerializeField] private AudioClip skidLoop;
    [SerializeField] private float skidVolume = 0.6f;
    [Tooltip("Cızırtının açılıp kapanma yumuşaklığı — anında kesilmesin, kısa bir fade ile sönsün diye.")]
    [SerializeField] private float skidFadeSpeed = 8f;

    [Header("Çarpışma Sesi")]
    [Tooltip("Çarpma sesleri (metal/duvar). Birden fazla eklersen her çarpışmada rastgele biri seçilir (aynı sesi tekrar tekrar duymak çok yapay hissettiriyor).")]
    [SerializeField] private AudioClip[] crashClips;
    [Tooltip("AĞACA / KAYAYA / ÇALIYA çarpınca çalar (odunsu/donuk ses). Boş bırakılırsa prop çarpmasında da normal `crashClips` çalar. Prop katmanları: Prop / PropSmall / PropFar / PropTree.")]
    [SerializeField] private AudioClip[] propHitClips;
    [Tooltip("Bu çarpma hızının (m/s) altındaki temaslar ses çıkarmaz — duvara sürtünürken sürekli çarpma sesi gelmesin diye.")]
    [SerializeField] private float minCrashSpeed = 6f;
    [Tooltip("Sert çarpışmanın 'tam ses' sayılacağı hız. Daha yavaş çarpmalar orantılı olarak daha kısık duyulur.")]
    [SerializeField] private float loudCrashSpeed = 22f;
    [SerializeField] private float crashVolume = 0.9f;
    [Tooltip("İki çarpma sesi arasındaki en kısa süre — tek bir çarpışmada onlarca temas noktası oluşabiliyor, hepsi ayrı ses çalarsa gürültü olur.")]
    [SerializeField] private float crashCooldown = 0.25f;
    [Tooltip("AÇIK olursa çarpma sesi SADECE kendi arabanda duyulur. Uzak arabaların çarpışması NetworkTransform ile sürüldükleri için bazen yanlış hız değerleri üretebiliyor — başkalarının çarpması kulağa tuhaf gelirse bunu aç.")]
    [SerializeField] private bool crashOnlyForOwnCar = false;

    [Header("Yarış Bitince (Podyum)")]
    [Tooltip("Yarış bitip podyum açılınca motor/cızırtı sesinin kaç kat hızla " +
             "sönümleneceği. 2 = yaklaşık yarım saniyede tamamen susar. Sert bir " +
             "kesme yerine kısa bir sönümleme, podyum fanfarının üstüne binmesin diye.")]
    [SerializeField] private float raceEndFadeSpeed = 2f;

    [Header("Zıplama / Yere İniş")]
    [Tooltip("Araba havalanıp yere indiğinde çalar (tümsek/rampa hissi). Boş bırakılabilir.")]
    [SerializeField] private AudioClip landingClip;
    [SerializeField] private float landingVolume = 0.7f;
    [Tooltip("Araba en az BU KADAR süre havada kalmadıysa iniş sesi ÇALINMAZ.\n\nNEDEN GEREKLİ: CarController'ın 'yerde mi' kararı 4 tekerleğin süspansiyon raycast'ine bakıyor (2'den fazlası değiyorsa yerde sayılıyor). Normal sürüşte tümsekler, kenarlık (kerb) ve pist pürüzleri yüzünden bu değer saniyede birkaç kez bir açılıp bir kapanabiliyor — her seferinde iniş sesi çalarsa düz giderken sürekli bas gümlemesi duyulur. Bu eşik o titremeyi filtreliyor: gerçek bir sıçrama bundan uzun sürer, sahte temas kayıpları çok daha kısa.")]
    [SerializeField] private float minAirTimeForLanding = 0.35f;

    private CarController car;

    // Motor artık TEK kaynak değil — devir bantları (idle/low/med/high). Her
    // kare rpm'e göre komşu iki bant arası crossfade yapılıyor.
    private struct EngineBand { public float center; public AudioSource src; }
    private readonly List<EngineBand> engineBands = new();

    private AudioSource skidSource;

    // Hem perde hem ses seviyesi AYNI yumuşatılmış hız değerinden hesaplanıyor.
    // Ayrı ayrı yumuşatsaydık ikisi farklı hızlarda değişip motor sesi
    // "kayık" duyulurdu (ses yükselirken perde geride kalır gibi).
    private float smoothedSpeedRatio;
    private int currentGear;          // sanal vites: histerezisli aktif vites
    private float lastShiftTime = -99f;
    private int previousGear;         // vites değişimini yakalamak için
    private int lastBlendTarget = -1; // crossfade'e giren bandı bir kez baştan başlatmak için
    private float currentSkidVolume;
    private float lastCrashTime = -99f;
    private bool wasGrounded = true;
    private float airborneTime;
    private int propLayerBits;        // Prop / PropSmall / PropFar / PropTree katmanlarının bit maskesi

    void Awake()
    {
        car = GetComponent<CarController>();

        // Ağaç/kaya çarpması ile duvar çarpmasını ayırmak için prop
        // katmanlarının maskesi. NameToLayer bilinmeyen isimde -1 döner,
        // o bit yok sayılıyor (1 << -1 tanımsız olduğu için elde ediyoruz).
        propLayerBits = 0;
        foreach (string layerName in new[] { "Prop", "PropSmall", "PropFar", "PropTree" })
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) propLayerBits |= 1 << layer;
        }

        // İki SÜREKLİ ses (motor + cızırtı) havuzdan değil, arabanın kendi
        // üstündeki AudioSource'lardan çalıyor — çünkü loop'lu ve arabayla
        // birlikte HAREKET etmesi gerekiyor. SfxPlayer havuzu sadece tek
        // seferlik, sabit noktada çalan sesler için.
        // Devir bantlarını kur — atanmayan klip atlanıyor. En az 1 tane varsa
        // motor çalışır; hiçbiri yoksa engineBands boş, UpdateEngine sessizce döner.
        AddEngineBand(idleClip, 0f);
        AddEngineBand(lowClip, Mathf.Clamp01(lowRpm));
        AddEngineBand(medClip, Mathf.Clamp01(medRpm));
        AddEngineBand(highClip, 1f);
        engineBands.Sort((a, b) => a.center.CompareTo(b.center));

        skidSource = CreateLoopSource("SkidAudio", skidLoop, 0f);

        smoothedSpeedRatio = 0f;
        currentGear = 0;
        previousGear = 0;
        lastBlendTarget = -1;
        lastShiftTime = -99f;
    }

    private void AddEngineBand(AudioClip clip, float center)
    {
        if (clip == null) return;
        AudioSource src = CreateLoopSource($"Engine_{center:F2}", clip, 0f);
        engineBands.Add(new EngineBand { center = center, src = src });
    }

    private AudioSource CreateLoopSource(string sourceName, AudioClip clip, float volume)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = volume;
        src.spatialBlend = 1f;                       // 3D — uzaktaki araba kısık duyulsun
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 6f;
        src.maxDistance = 120f;
        src.dopplerLevel = 0.3f;                     // hızla geçen araba hafif "vuuuş" yapsın

        // Klip atanmamışsa hiç başlatma — boş bir AudioSource'un Play()
        // edilmesi zararsız ama gereksiz.
        if (clip != null) src.Play();

        return src;
    }

    void Update()
    {
        // ── PODYUMDA ARABA SUSAR ──────────────────────────────────────────
        // Yarış bitince araçlar podyuma ışınlanıyor ve CarController.
        // FreezeForRaceEnd() fiziği durduruyor. Ama bu script sadece
        // car.SpeedKmh'e bakıyordu; hız 0 olsa bile `idleVolume` rölanti sesi
        // çalmaya devam ediyor ve kazananın fanfarının altında egzoz uğuldadığı
        // için podyum ucuz duyuluyordu.
        //
        // NEDEN HER KAREDE OKUNUYOR (FreezeForRaceEnd'den bir çağrı DEĞİL):
        // podyum sonucu bir SyncVar ve bu projede "hook her makinede tetiklenir"
        // varsayımı birkaç kez yanlış çıktı (host hook almıyor). İki bool
        // okumanın maliyeti yok, RacerSpectator da aynı gerekçeyle böyle yazıldı.
        //
        // İzleyici modunda zaten susuyor (SetHiddenForSpectator bu component'i
        // kapatıyor) — podyum yolu o kancayı kullanmıyordu, açık kalan buydu.
        if (RaceIsOver())
        {
            FadeOutForRaceEnd();
            return;
        }

        UpdateEngine();
        UpdateSkid();
        UpdateLanding();
    }

    /// <summary>
    /// Sahnede RacePodiumManager YOKSA (fotoğraf sahnesi, minimap araba
    /// marker'ı, izole testler) false döner — o kopyalarda ses normal çalışır.
    /// </summary>
    private static bool RaceIsOver()
        => RacePodiumManager.Instance != null && RacePodiumManager.Instance.RaceOver;

    private void FadeOutForRaceEnd()
    {
        float step = Time.deltaTime * Mathf.Max(0.01f, raceEndFadeSpeed);

        foreach (EngineBand band in engineBands)
        {
            if (band.src == null) continue;
            band.src.volume = Mathf.MoveTowards(band.src.volume, 0f, step);
            if (band.src.volume <= 0.001f && band.src.isPlaying) band.src.Pause();
        }

        if (skidSource != null)
        {
            currentSkidVolume = Mathf.MoveTowards(currentSkidVolume, 0f, step);
            skidSource.volume = currentSkidVolume;
            if (currentSkidVolume <= 0.001f && skidSource.isPlaying) skidSource.Pause();
        }

        // Araç podyum kolonunun üstüne DÜŞEREK oturuyor; bu düşüş "havada
        // kaldı" sayılıp iniş sesi tetiklemesin diye sayacı sıfır tutuyoruz.
        airborneTime = 0f;
        wasGrounded = true;
    }

    private void UpdateEngine()
    {
        if (engineBands.Count == 0) return;

        // Devir hedefi (0 = duruyor, 1 = tam devir). car.SpeedRatio DEĞİL —
        // o birim karışıklığı yüzünden asla 1'e ulaşmıyor (bkz. CarController).
        float rawRatio = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, speedForMaxPitch));

        // YÜK TABANI: araç hareket ediyorsa idle bandını bırak. Keskin driftte
        // ileri hız düşse de motor devri düşmesin — o an drift/kayma varsa devri
        // `Med` bandına çek. Taban, eğri UYGULANMADAN önceki orana çevriliyor
        // (Pow'un tersi) ki bant konumu tam istenen yere otursun. Sonra hepsi
        // birlikte yumuşatılıyor → tık/zıplama yok.
        float targetRatio = rawRatio;
        if (car.SpeedKmh > idleReleaseSpeedKmh)
        {
            float floorRpm = (car.IsSkidding || car.IsDrifting()) ? medRpm : lowRpm;
            float floorRatio = Mathf.Pow(Mathf.Clamp01(floorRpm), 1f / Mathf.Max(0.01f, rpmCurve));
            targetRatio = Mathf.Max(targetRatio, floorRatio);
        }

        smoothedSpeedRatio = Mathf.Lerp(smoothedSpeedRatio, targetRatio, Time.deltaTime * pitchSmoothing);
        float speedRatio = Mathf.Clamp01(smoothedSpeedRatio);

        // rpmCurve SADECE bant konumunu şekillendiriyor (volume/pitch DEĞİL) —
        // düşük hızları geniş bir devir aralığına yayıyor. Arcade araba zamanının
        // çoğunu yüksek hızda geçirdiği için, düz eğride motor sürekli "high"
        // bandında sıkışıyor, low/med hiç duyulmuyor.
        float rpm = Mathf.Pow(speedRatio, rpmCurve);

        // Toplam ses seviyesi HAM hız oranından (eğrisiz) — yoksa çift eğri
        // yüzünden düşük hızda motor iyice sessiz kalırdı. Vites modunda da
        // seviye vitesten BAĞIMSIZ (her shift'te "motor stop etti" olmasın).
        float engineVol = Mathf.Lerp(idleVolume, maxVolume, Mathf.Pow(speedRatio, volumeRamp))
                          * AudioBus.Engine * AudioBus.Master * SfxPlayer.MasterVolume;

        if (useGearShifts)
        {
            UpdateEngineGeared(speedRatio, engineVol);
            return;
        }

        float pitch = Mathf.Lerp(engineMinPitch, engineMaxPitch, speedRatio);

        // Tek bant varsa direkt çal (crossfade için komşu yok).
        if (engineBands.Count == 1)
        {
            engineBands[0].src.volume = engineVol;
            engineBands[0].src.pitch = pitch;
            return;
        }

        // rpm'in düştüğü aralığın iki ucunu bul, aralarında crossfade.
        // Bantlar center'a göre sıralı (Awake'te Sort). Aynı anda en fazla
        // İKİ bant sesli — geçiş pürüzsüz, artefakt yok.
        int hi = engineBands.Count - 1;
        for (int i = 0; i < engineBands.Count; i++)
        {
            if (rpm <= engineBands[i].center) { hi = i; break; }
        }
        int lo = Mathf.Max(0, hi - 1);
        float t = (hi == lo) ? 0f
                 : Mathf.InverseLerp(engineBands[lo].center, engineBands[hi].center, rpm);
        // Geçişin ORTASINI yumuşat — düz (lineer) t, iki bandın tam yarı yarıya
        // karıştığı anda "cırt" gibi bir sınır hissi veriyordu.
        t = Mathf.SmoothStep(0f, 1f, t);

        for (int i = 0; i < engineBands.Count; i++)
        {
            // EŞİT GÜÇ (equal-power) crossfade: lineer (1-t / t) ağırlıkta
            // iki klip ilişkisiz olduğu için t=0.5'te toplam ses ~3 dB
            // DÜŞÜYOR — her geçişte duyulan "dalgalanma" buydu. cos/sin
            // ağırlıkta ağırlıkların KARELERİ toplamı hep 1, ses sabit kalıyor.
            float w = (i == lo) ? Mathf.Cos(t * 0.5f * Mathf.PI)
                    : (i == hi) ? Mathf.Sin(t * 0.5f * Mathf.PI)
                    : 0f;
            engineBands[i].src.volume = w * engineVol;
            engineBands[i].src.pitch = pitch;
        }
    }

    // SANAL VİTES (Mario Kart deseni). idle olmayan her bant = BİR VİTES.
    // `rpmCurve` ile büzülmüş hız oranı, vites sayısına bölünüyor: aktif vitesin
    // klibi kendi diliminde `gearMinPitch → gearMaxPitch` tırmanıyor, dilimin
    // son `gearBlend` kısmında bir sonraki bant DİP perdeden (gearMinPitch)
    // crossfade ile giriyor → kulakta "yukarı vites attı". Vites SESİ yok.
    private void UpdateEngineGeared(float speedRatio, float engineVol)
    {
        // Bantlar Awake'te center'a göre sıralı. center≈0 olan idle bandını ayır.
        int first = (engineBands.Count > 1 && engineBands[0].center <= 0.0001f) ? 1 : 0;
        int gearCount = engineBands.Count - first;

        // Her kare tüm motor kaynaklarını sıfırla, sonra aktif olan(lar)ı yaz.
        for (int i = 0; i < engineBands.Count; i++) engineBands[i].src.volume = 0f;

        // idle bandı: sadece araç (neredeyse) dururken duyulur.
        if (first == 1)
        {
            float idleW = 1f - Mathf.Clamp01(car.SpeedKmh / Mathf.Max(0.1f, idleReleaseSpeedKmh));
            engineBands[0].src.volume = idleW * engineVol;
            engineBands[0].src.pitch = gearMinPitch;
        }

        if (gearCount <= 0) return;
        if (gearCount == 1)
        {
            engineBands[first].src.volume = engineVol;
            engineBands[first].src.pitch = Mathf.Lerp(gearMinPitch, gearMaxPitch, speedRatio);
            return;
        }

        float climb = Mathf.Pow(speedRatio, rpmCurve);                 // 0-1, büzülmüş
        float gf = Mathf.Clamp(climb * gearCount, 0f, gearCount - 0.0001f);

        // HİSTEREZİS: vites artık `gf`'in floor'u DEĞİL, takip edilen bir durum.
        // Yukarı vites tam sınırda; aşağı vites ancak `gearHysteresis` kadar
        // altına düşünce → sınırda seğirse bile vites atıp inmez. + cooldown.
        currentGear = Mathf.Clamp(currentGear, 0, gearCount - 1);
        if (Time.time - lastShiftTime >= gearShiftCooldown)
        {
            if (currentGear < gearCount - 1 && gf >= currentGear + 1f)
            { currentGear++; lastShiftTime = Time.time; }
            else if (currentGear > 0 && gf <= currentGear - gearHysteresis)
            { currentGear--; lastShiftTime = Time.time; }
        }
        int g = currentGear;
        float within = Mathf.Clamp01(gf - g);                         // redline'da 1'de tutar

        // Vitesin son gearBlend kısmında bir sonraki bant devreye girer.
        float blend = (g < gearCount - 1 && within > 1f - gearBlend)
                      ? (within - (1f - gearBlend)) / gearBlend
                      : 0f;
        float wCur  = Mathf.Cos(blend * 0.5f * Mathf.PI);
        float wNext = Mathf.Sin(blend * 0.5f * Mathf.PI);

        engineBands[first + g].src.volume = wCur * engineVol;
        engineBands[first + g].src.pitch  = Mathf.Lerp(gearMinPitch, gearMaxPitch, within);

        if (blend > 0f)
        {
            engineBands[first + g + 1].src.volume = wNext * engineVol;
            engineBands[first + g + 1].src.pitch  = gearMinPitch;     // yeni vites dipten başlar
        }

        // KLİP FAZI: yeni giren vites klibi ORTASINDAN değil BAŞINDAN girsin.
        // Loop'lar sürekli sessizce dönüyor; devreye girerken rastgele bir
        // yerdeler. Crossfade'e giren bandı BİR KEZ baştan başlatıyoruz
        // (her karede değil — yoksa titreme olur). Aşağı viteste yeni aktif
        // olan bandı da shift anında başa alıyoruz.
        int incoming = (blend > 0f) ? first + g + 1 : -1;
        if (incoming >= 0 && incoming != lastBlendTarget)
        {
            engineBands[incoming].src.time = 0f;
            lastBlendTarget = incoming;
        }
        else if (incoming < 0)
        {
            lastBlendTarget = -1;
        }
        if (currentGear < previousGear)                    // aşağı vites
            engineBands[first + currentGear].src.time = 0f;
        previousGear = currentGear;
    }

    private void UpdateSkid()
    {
        if (skidSource == null || skidLoop == null) return;

        // Kayma kararını YENİDEN HESAPLAMIYORUZ — CarController'ın zaten
        // hesaplayıp senkronize ettiği (ve duman/lastik izini de tetikleyen)
        // sonucu kullanıyoruz. Böylece ses ile görsel efekt asla ayrışmıyor.
        float target = car.IsSkidding ? skidVolume : 0f;
        currentSkidVolume = Mathf.MoveTowards(currentSkidVolume, target, Time.deltaTime * skidFadeSpeed * skidVolume);

        skidSource.volume = currentSkidVolume * AudioBus.Skid * AudioBus.Master * SfxPlayer.MasterVolume;

        // Tamamen sustuysa çalmayı durdur (boş yere bir ses kanalı işgal
        // etmesin), yeniden gerekince başlat.
        if (currentSkidVolume <= 0.001f)
        {
            if (skidSource.isPlaying) skidSource.Pause();
        }
        else if (!skidSource.isPlaying)
        {
            skidSource.UnPause();
            if (!skidSource.isPlaying) skidSource.Play();
        }
    }

    private void UpdateLanding()
    {
        bool grounded = car.IsGroundedNow;

        if (!grounded)
        {
            // Havada geçen süreyi biriktir.
            airborneTime += Time.deltaTime;
        }
        else
        {
            // Yere değdiği KARE (önceki kare havada, bu kare yerde) — ama
            // ses SADECE gerçekten havada kalmışsa çalıyor. Tümsekte
            // tekerlek birkaç kare kopmuşsa bu bir "iniş" sayılmıyor.
            if (!wasGrounded && airborneTime >= minAirTimeForLanding)
                SfxPlayer.PlayAt(landingClip, transform.position, landingVolume, 0.08f);

            airborneTime = 0f;
        }

        wasGrounded = grounded;
    }

    void OnCollisionEnter(Collision collision)
    {
        // Ağaç/kaya prop'una mı çarptık, yoksa duvara/başka arabaya mı?
        int otherLayer = collision.collider != null ? collision.collider.gameObject.layer : collision.gameObject.layer;
        bool hitProp = (propLayerBits & (1 << otherLayer)) != 0;
        AudioClip[] clips = (hitProp && propHitClips != null && propHitClips.Length > 0)
                            ? propHitClips
                            : crashClips;
        if (clips == null || clips.Length == 0) return;
        // Podyumda araçlar kolonların üstüne düşüp birbirine/kolona değiyor —
        // zafer anında çarpma sesleri patlamasın.
        if (RaceIsOver()) return;
        // `car.isOwned` DEĞİL: Mirror'da `isOwned => netIdentity.isOwned` ve
        // araba prefabı network DIŞINDA da Instantiate ediliyor (minimap araba
        // marker'ı, fotoğraf sahnesi). O kopyalarda NetworkIdentity yok, yani
        // düz `isOwned` NullReferenceException atıyor. `IsNetworkOwned` bu
        // kontrolü kendi içinde yapıyor.
        if (crashOnlyForOwnCar && !car.IsNetworkOwned) return;
        if (Time.time - lastCrashTime < crashCooldown) return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < minCrashSpeed) return;

        lastCrashTime = Time.time;

        // Ses şiddeti çarpma sertliğiyle orantılı — hafif sürtünme kısık,
        // duvara tam gaz girmek tam ses.
        float loudness = Mathf.InverseLerp(minCrashSpeed, loudCrashSpeed, impactSpeed);
        float volume = Mathf.Lerp(0.35f, 1f, loudness) * crashVolume;

        // Sesi temas NOKTASINDA çalıyoruz (arabanın merkezinde değil) —
        // yandan sıyırma ile burundan çarpma kulakta farklı yerden gelsin.
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

        SfxPlayer.PlayRandomAt(clips, point, volume, 0.08f);
    }
}
