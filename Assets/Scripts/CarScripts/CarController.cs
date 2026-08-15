using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// MİMARİ NOT (Mirror Entegrasyonu):
///
/// Önceki tasarımda "SetNetworkInput" ile input relay planlanmıştı (server
/// tüm client'ların inputunu toplayıp herkese dağıtır, herkes aynı fiziği
/// hesaplar). Videodaki yaklaşımı öğrendikten sonra DAHA BASİT bir yola
/// geçiyoruz:
///
///   - Sadece OWNER (arabanın sahibi olan client) fiziği hesaplıyor.
///   - NetworkTransform component'i (Unity Inspector'da eklenecek) pozisyon/
///     rotasyonu otomatik olarak diğer client'lara yayıyor.
///   - Drift/grounded/hız gibi GÖRSEL state'ler (skid smoke, tekerlek dönüşü
///     gibi efektler için) SyncVar ile senkronize ediliyor, böylece remote
///     arabalar da doğru görsel efektleri gösteriyor.
///
/// Bu yaklaşım Rigidbody tabanlı arcade fizik için yaygın ve basit bir
/// çözüm. Rekabetçi/hile-korumalı bir oyun olsaydı server-authoritative
/// fizik tercih edilirdi, ama SaboTour bir party oyunu, bu basitlik yeterli.
///
/// UNITY INSPECTOR'DA YAPMAN GEREKENLER (kod değil, ayar):
/// 1. Araba prefabına "Network Identity" component'i ekle.
/// 2. Araba prefabına "Network Transform (Reliable)" ya da tercihen
///    "Network Transform Unreliable" ekle (video da bunu öneriyor —
///    hareket gibi sürekli değişen veri için Unreliable daha az gecikme
///    yaratıyor).
///    - Position ve Rotation senkronizasyonunu aç.
///    - "Sync Direction" alanını "Client To Server" yap (owner'ın fiziği
///      yetkili olsun diye).
/// 3. CarController component'inin kendisinde de (NetworkBehaviour olduğu
///    için) Inspector'da "Sync Direction: Client To Server" görünecek,
///    aynı şekilde ayarla — SyncVar'ları owner'ın yazabilmesi için gerekli.
/// </summary>
public class CarController : NetworkBehaviour
{
    #region 1. Referanslar

    [Header("Referanslar - Oyun nesneleri ve bileşenler")]
    [SerializeField] private Rigidbody carRB;
    [SerializeField] private Transform[] rayPoints;
    [SerializeField] private LayerMask drivable;
    [SerializeField] private Transform accelerationPoint;
    [SerializeField] private GameObject[] tires = new GameObject[4];
    [SerializeField] private GameObject[] frontTireParents = new GameObject[2];
    [SerializeField] private TrailRenderer[] skidMarks = new TrailRenderer[2];
    [SerializeField] private ParticleSystem[] skidSmokes = new ParticleSystem[2];

    #endregion

    #region 2. Multiplayer — Senkronize Görsel State
    // ─────────────────────────────────────────────────────────────────────
    // Bu değerler sadece OWNER tarafından her FixedUpdate'te güncelleniyor,
    // Mirror bunları otomatik olarak diğer client'lara (ve server'a) yayıyor.
    // Hook metodları, remote client'larda bu değerler değiştiğinde ilgili
    // private field'ı güncelliyor — böylece Visuals()/Vfx() gibi metodlar
    // owner'da da remote'da da AYNI KODLA çalışıyor, özel durum yazmaya
    // gerek kalmıyor.
    // ─────────────────────────────────────────────────────────────────────

    [SyncVar(hook = nameof(OnDriftingChanged))]
    private bool netIsDrifting;

    [SyncVar(hook = nameof(OnGroundedChanged))]
    private bool netIsGrounded;

    [SyncVar(hook = nameof(OnVelocityRatioChanged))]
    private float netVelocityRatio;

    // Vfx()'teki skid/smoke kontrolü currentCarLocalVelocity.x'e (yanal hız)
    // bakıyor, ama bu değer sadece owner'ın FixedUpdate'inde hesaplanıyordu
    // ve hiç senkronize edilmiyordu — bu yüzden BAŞKA client'ların arabasına
    // bakarken (spectator, kule, başka oyuncu) skidmark/smoke HİÇ görünmüyordu.
    [SyncVar(hook = nameof(OnLocalVelocityXChanged))]
    private float netLocalVelocityX;

    private void OnLocalVelocityXChanged(float oldValue, float newValue)
    {
        if (!isOwned) currentCarLocalVelocity.x = newValue;
    }

    // Skid/smoke gösterilip gösterilmeyeceği kararı (shouldShowEffects) eskiden
    // her Update()'te ham currentCarLocalVelocity.x/isDrifting değerlerinden
    // YENİDEN hesaplanıyordu. Sorun: düz giderken kısa bir el freni darbesi gibi
    // ÇOK KISA süren "true" anları, bir sonraki network sync tick'inden önce
    // tekrar "false"a dönebiliyordu — bu durumda o "true" anı ağa HİÇ
    // gönderilmeden kayboluyor, diğer client'lar hiç görmüyordu. Çözüm: kararı
    // owner'da FixedUpdate'te hesaplayıp en az skidEffectMinVisibleDuration kadar
    // AÇIK TUT (latch), sonra TEK bir senkronize bayrak olarak gönder — kısa
    // patlamalar bile artık ağa yetişecek kadar uzun sürüyor.
    [SyncVar(hook = nameof(OnShouldShowEffectsChanged))]
    private bool netShouldShowEffects;

    private void OnShouldShowEffectsChanged(bool oldValue, bool newValue)
    {
        if (!isOwned) shouldShowEffects = newValue;
    }

    [Tooltip("Skid/smoke efektinin en az bu kadar süre 'açık' kalması garanti edilir — kısa el freni darbelerinin network senkronuna yetişebilmesi için.")]
    [SerializeField] private float skidEffectMinVisibleDuration = 0.15f;
    private bool shouldShowEffects;
    private float skidEffectLatchTimer;

    // ─────────────────────────────────────────────────────────────────────
    // ARABA RENGİ
    //
    // 12 renklik sabit palet — LobbyPlayer, herkes hazır olduğunda bu
    // paletten (0-11 arası) rastgele, tekrarsız birer indeks dağıtıp
    // MyNetworkManager.SetColorAssignments() ile taşıyor. Araba spawn
    // olurken MyNetworkManager, SetColorIndex()'i çağırıp gerçek rengi
    // atıyor. netColorIndex SyncVar olduğu için bu, TÜM client'lara otomatik
    // yayılıyor — herkes herkesin arabasını doğru renkte görüyor.
    //
    // NEDEN MaterialPropertyBlock (ayrı .mat dosyası DEĞİL): her arabaya
    // farklı bir materyal asset'i vermek, GPU Instancing'i kırar (farklı
    // materyal = farklı batch). PropertyBlock ile renk değiştirmek,
    // instancing'i bozmadan per-obje renk uygulamanın standart yolu.
    // ─────────────────────────────────────────────────────────────────────

    public static readonly Color[] ColorPalette =
    {
        new Color32(0xE6, 0x39, 0x46, 0xFF), // Kırmızı
        new Color32(0xF3, 0x72, 0x2C, 0xFF), // Kırmızı-Turuncu
        new Color32(0xF8, 0x96, 0x1E, 0xFF), // Turuncu
        new Color32(0xF9, 0xC7, 0x4F, 0xFF), // Sarı-Turuncu
        new Color32(0xB5, 0xE6, 0x55, 0xFF), // Sarı-Yeşil
        new Color32(0x43, 0xAA, 0x8B, 0xFF), // Yeşil
        new Color32(0x26, 0xC6, 0xDA, 0xFF), // Mavi-Yeşil
        new Color32(0x3A, 0x86, 0xFF, 0xFF), // Mavi
        new Color32(0x71, 0x59, 0xC1, 0xFF), // Mavi-Mor
        new Color32(0x9D, 0x4E, 0xDD, 0xFF), // Mor
        new Color32(0xE0, 0x21, 0x8A, 0xFF), // Kırmızı-Mor
        new Color32(0x1B, 0x1B, 0x1B, 0xFF), // Siyah
    };

    [Tooltip("Rengin uygulanacağı Renderer'lar (gövde + spoiler gibi ayrı parçalar buraya eklenir). Her Renderer'ın materyal slotları TEK TEK taranır, İSMİNDE 'CarBody' geçen slota renk uygulanır — aynı Renderer'daki BAŞKA bir materyal (ör. ayrı bir 'colormap' materyali) etkilenmez. ZORUNLU: boş bırakılırsa renk hiç uygulanmaz.")]
    [SerializeField] private Renderer[] paintableRenderers;

    [SyncVar(hook = nameof(OnColorIndexChanged))]
    private int netColorIndex = -1;

    /// <summary>Bu arabaya atanan renk indeksi (ColorPalette'e index) — minimap marker'ı gibi dış sistemler bunu okuyup kendi rengini eşleştirebilir.</summary>
    public int ColorIndex => netColorIndex;

    private static MaterialPropertyBlock carColorPropertyBlock;

    /// <summary>Server, spawn sırasında (MyNetworkManager) bu arabaya rengini atamak için çağırıyor.</summary>
    [Server]
    public void SetColorIndex(int index)
    {
        netColorIndex = index;
    }

    private void OnColorIndexChanged(int oldValue, int newValue)
    {
        ApplyCarColor(newValue);
    }

    private void ApplyCarColor(int index)
    {
        if (paintableRenderers == null) return;
        if (index < 0 || index >= ColorPalette.Length) return;

        carColorPropertyBlock ??= new MaterialPropertyBlock();
        Color c = ColorPalette[index];

        foreach (Renderer renderer in paintableRenderers)
        {
            if (renderer == null) continue;

            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material mat = materials[slot];
                if (mat == null) continue;
                // "(Instance)" gibi eklerle de eşleşsin diye Contains kullanılıyor.
                if (mat.name.IndexOf("CarBody", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // ÖNEMLİ: slot index'i BELİRTMEDEN SetPropertyBlock çağırmak
                // Renderer'daki TÜM materyal slotlarını etkiler — aynı Renderer
                // üzerinde CarBody dışında başka bir materyal (ör. colormap)
                // varsa o da yanlışlıkla boyanır. Bu yüzden slot'a özel
                // GetPropertyBlock/SetPropertyBlock kullanılıyor.
                renderer.GetPropertyBlock(carColorPropertyBlock, slot);
                carColorPropertyBlock.SetColor("_BaseColor", c);
                carColorPropertyBlock.SetColor("_Color", c);
                renderer.SetPropertyBlock(carColorPropertyBlock, slot);
            }
        }
    }

    private void OnDriftingChanged(bool oldValue, bool newValue)
    {
        if (!isOwned) isDrifting = newValue;
    }

    private void OnGroundedChanged(bool oldValue, bool newValue)
    {
        if (!isOwned) isGrounded = newValue;
    }

    private void OnVelocityRatioChanged(float oldValue, float newValue)
    {
        if (!isOwned) carVelocityRatio = newValue;
    }

    // ─────────────────────────────────────────────────────────────────────
    // TEKERLEK POZİSYONU SENKRONİZASYONU
    //
    // Suspension() sadece owner'da çalıştığı için tekerlekler remote'da
    // hiç raycast ile zemine oturtulmuyor, prefab'ın başlangıç konumunda
    // ("batık") kalıyor. Çözüm: owner'ın hesapladığı 4 tekerlek pozisyonunu
    // (LOCAL, yani araba gövdesine göre offset) senkronize ediyoruz.
    //
    // ÖNEMLİ VARSAYIM: tires[] objelerinin, araba gövdesinin (bu script'in
    // bağlı olduğu obje) CHILD'ı (alt objesi) olması gerekiyor. Eğer
    // değillerse localPosition anlamsız olur, world position senkronize
    // etmek gerekirdi (daha pahalı).
    // ─────────────────────────────────────────────────────────────────────

    [SyncVar(hook = nameof(OnTire0PosChanged))] private Vector3 netTire0LocalPos;
    [SyncVar(hook = nameof(OnTire1PosChanged))] private Vector3 netTire1LocalPos;
    [SyncVar(hook = nameof(OnTire2PosChanged))] private Vector3 netTire2LocalPos;
    [SyncVar(hook = nameof(OnTire3PosChanged))] private Vector3 netTire3LocalPos;

    private void OnTire0PosChanged(Vector3 oldValue, Vector3 newValue) => ApplyRemoteTirePosition(0, newValue);
    private void OnTire1PosChanged(Vector3 oldValue, Vector3 newValue) => ApplyRemoteTirePosition(1, newValue);
    private void OnTire2PosChanged(Vector3 oldValue, Vector3 newValue) => ApplyRemoteTirePosition(2, newValue);
    private void OnTire3PosChanged(Vector3 oldValue, Vector3 newValue) => ApplyRemoteTirePosition(3, newValue);

    private void ApplyRemoteTirePosition(int index, Vector3 localPos)
    {
        if (HasControl) return; // owner zaten kendi hesapladığı pozisyonu kullanıyor
        if (tires[index] != null)
            tires[index].transform.localPosition = localPos;
    }

    // ─────────────────────────────────────────────────────────────────────
    // DİREKSİYON GÖRSELİ SENKRONİZASYONU
    //
    // steerInput sadece owner'ın GetPlayerInput()'unda dolduruluyor. Bu
    // senkronize edilmezse, remote arabaların ön tekerlekleri asla dönmüş
    // görünmez (araba viraja girse bile tekerlek düz duruyormuş gibi
    // görünür) — küçük ama fark edilir bir görsel kusur.
    // ─────────────────────────────────────────────────────────────────────

    [SyncVar(hook = nameof(OnSteerInputChanged))]
    private float netSteerInput;

    private void OnSteerInputChanged(float oldValue, float newValue)
    {
        if (!isOwned) steerInput = newValue;
    }

    #endregion

    #region 3. Süspansiyon Ayarları

    [Header("Süspansiyon Ayarları")]
    [SerializeField] private float springStiffness = 30000f;
    [SerializeField] private float damperStiffness = 3000f;
    [SerializeField] private float restLength = 0.75f;
    [SerializeField] private float springTravel = 0.1f;
    [SerializeField] private float wheelRadius = 0.6f;

    private int[] wheelsIsGrounded = new int[4];
    private bool isGrounded = false;

    #endregion

    #region 4. Girdi Değişkenleri

    private float moveInput = 0;
    private float steerInput = 0;
    private bool isHandbrakePressed = false;

    #endregion

    #region 5. Araba Ayarları

    [Header("Araba Ayarları")]
    [SerializeField] private float acceleration = 25f;
    [SerializeField] private float maxSpeed = 300f;
    [SerializeField] private float deceleration = 10f;
    [SerializeField] private float steerStrength = 30f;
    [SerializeField] private AnimationCurve turningCurve;
    [SerializeField] private float dragCoefficent = 0.8f;
    [SerializeField] private float brakingDeceleration = 150f;
    [SerializeField] private float brakingDragCoefficent = 1f;

    private Vector3 currentCarLocalVelocity = Vector3.zero;
    private float carVelocityRatio = 0;
    public float currentSpeed = 0f;

    [Header("Durma Ayarları")]
    [SerializeField] private float stopThreshold = 0.5f;
    [SerializeField] private float autoStopForce = 5f;
    [SerializeField] private float minSpeedForMovement = 0.1f;
    [SerializeField] private float lowSpeedDragMultiplier = 2f;
    [SerializeField] private float lowSpeedThreshold = 20f;

    #endregion

    #region 6. Görsel Efekt Ayarları

    [Header("Görsel Efektler")]
    [SerializeField] private float tireRotSpeed = 3000f;
    [SerializeField] private float maxSteeringAngle = 30f;
    [SerializeField] private float minSideSkidVelocity = 8f;

    #endregion

    #region 7. Diğer Fizik Ayarları

    [Header("Hava Sürtünmesi Ayarları")]
    [SerializeField] private float airDrag = 0.1f;
    [SerializeField] private float rollingResistance = 0.5f;

    #endregion

    #region 8. El Freni Ayarları (Drift)

    [Header("El Freni Ayarları - Arcade Drift")]
    [Tooltip("Drift sırasında ARKA süspansiyon yaylarının ne kadar yumuşayacağı. DİKKAT: bu, yanlamasına kaymayı DEĞİL, arkanın ne kadar çöktüğünü ayarlıyor (kuvvet dikey uygulanıyor). Düşük = arka daha çok çöker, drift daha dramatik görünür. Kaymayı ayarlamak için Drift Sideways Grip'i kullan.")]
    [SerializeField] private float driftGripReduction = 0.6f;

    [Tooltip("Drift sırasında dönme kuvvetinin çarpanı — 'keskin dönüş' ayarı. Yükselt = araç daha hızlı döner. Çok yükseltirsen araç kendi etrafında fırıl fırıl döner.")]
    [SerializeField] private float driftSteerBoost = 1.5f;

    [Tooltip("Drift'in devreye GİREBİLMESİ için gereken en düşük hız (km/h). Bunun altındayken Space'e basmak drift başlatmaz, sadece normal fren yapar.")]
    [SerializeField] private float driftMinSpeed = 15f;

    [Tooltip("Drift BAŞLADIKTAN sonra, hız Drift Min Speed'in bu oranının altına düşene kadar drift bozulmaz (histerezis).\n\nNEDEN VAR: girme ve çıkma eşiği aynı olsaydı, sürekli daire çizerken araç eşiğin tam üstünde salınırdı — drift kapanır, tutuş aniden artar, araç hızlanır, drift tekrar açılır. Saniyede birkaç kez tekrarlayan bu açılıp kapanma 'takıla takıla dönme' hissi veriyordu.\n\n0.6 = 15 km/h'de drifte girer, 9 km/h'nin altına düşene kadar driftte kalır.\n1 yaparsan histerezis kapanır (eski davranış).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float driftExitSpeedFactor = 0.6f;

    [Tooltip("Drift'e girip çıkma hızı. Yükselt = Space'e basar basmaz tepki verir (daha 'kontrollü' hissettirir), düşür = yumuşak/gecikmeli geçiş.")]
    [SerializeField] private float driftTransitionSpeed = 5f;

    [Tooltip("⚠️ TERS ÇALIŞIR: bu, hızın KORUNAN oranı. 0.85 = hızın %85'i korunur, %15'i kaybedilir. DAHA ÇOK yavaşlaması için bu değeri DÜŞÜR (ör. 0.70 = iki kat yavaşlama). NOT: bu fren sadece gaza basılıyken uygulanıyor.")]
    [SerializeField] private float driftSpeedLoss = 0.85f;

    [Tooltip("Drift sırasında yanal tutuşun çarpanı — ASIL 'kayma' ayarı budur.\n\nNormal sürüşte yanal sürtünme Drag Coefficent (0.8) kadar. Space'e basınca o değer bu çarpanla azaltılıyor, araç yana kaymaya başlıyor.\n\nDÜŞÜR (0.5) = daha çok kayar, savrulur.\nYÜKSELT (0.85) = az kayar, raya oturmuş gibi döner.\n\n0.7 = eskiden koda gömülü olan varsayılan değer (yani 0.7'de davranış hiç değişmez).")]
    [SerializeField] private float driftSidewaysGrip = 0.7f;

    private float currentDriftFactor = 0f;
    private bool isDrifting = false;

    #endregion

    #region 9. Buz Mekaniği

    [Header("Buz / Kaygan Zemin Ayarları")]
    [SerializeField] private string iceTag = "Ice";
    [SerializeField] private float iceSidewaysDrag = 0.05f;
    [SerializeField] private float iceSteeringChaosMultiplier = 1.5f;
    [SerializeField] private float iceAccelerationGrip = 0.3f;

    private bool isCarOnIce = false;
    private bool externalIceTrigger = false;

    #endregion

    #region 9b. Kenarlık / Çim Sürtünmesi

    [Header("Kenarlık (Curb) / Çim Sürtünmesi")]
    [Tooltip("Buz ile AYNI desen (iceTag) — bu tag'e sahip zeminde araç daha az ivmelenir, sanki sürtünme artmış gibi hissettirir. Ice'ten farkı: kayganlaştırmıyor, sadece YAVAŞLATIYOR.")]
    [SerializeField] private string curbTag = "Curb";
    [Tooltip("1 = normal, 0.75 = kenarlıkta ivmenin %75'i (hafif direnç hissi).")]
    [Range(0.1f, 1f)][SerializeField] private float curbAccelerationGrip = 0.75f;

    [SerializeField] private string grassTag = "Grass";
    [Tooltip("Çimde ivmenin ne kadarı korunur (1 = ivme hiç düşmez). YÜKSEK TUT — asıl yavaşlatmayı aşağıdaki SÜRTÜNME yapıyor. İkisi birden sert olursa araç çimde çakılıp kalıyor, piste dönemiyor.")]
    [Range(0.1f, 1f)][SerializeField] private float grassAccelerationGrip = 0.85f;

    [Tooltip("Çimde hıza ORANTILI sürtünme — gaza basmasan bile momentumu yiyor. Hıza orantılı olduğu için sabit bir ceza değil: ne kadar hızlıysan o kadar çok kaybediyorsun, yani gittikçe yavaşlıyorsun. 0 = sürtünme yok.")]
    [SerializeField] private float grassDrag = 1.5f;

    [Tooltip("Çim sürtünmesinin KESİLDİĞİ hız (km/h). Bunun altına inince sürtünme artık uygulanmıyor — araç çimde sürüne sürüne durmuyor, bu hız civarında dengeye oturuyor.")]
    [SerializeField] private float grassDragFloorKmh = 45f;

    private bool isCarOnCurb = false;
    private bool isCarOnGrass = false;

    #endregion

    #region 9c. Drift Tuzağı — Geçici Yavaşlatma

    // DriftTrap.cs artık süre cezası yerine burayı kullanıyor (bkz.
    // PlayerRaceController.ApplyDriftSlowdown). Coroutine YOK — ice
    // pattern'iyle aynı mantık: her karede "süresi doldu mu" diye
    // Time.time karşılaştırılıyor, obje yok olursa/sahne değişirse
    // Stop edilmesi gereken bir coroutine kalmıyor.
    private float trapSlowdownMultiplier = 1f;
    private float trapSlowdownEndTime = -1f;

    /// <summary>
    /// DriftTrap tuzağına yakalanınca (PlayerRaceController.ApplyDriftSlowdown
    /// üzerinden, sadece bu arabanın SAHİBİ olan client'ta) çağrılır. Aracın
    /// ivmesini bir süreliğine kısıyor — sert bir fren DEĞİL, "motor gücü
    /// azaldı" hissi (aniden durursa haksız/sinir bozucu hissettirirdi).
    /// </summary>
    public void ApplyTrapSlowdown(float accelerationMultiplier, float duration)
    {
        trapSlowdownMultiplier = Mathf.Clamp01(accelerationMultiplier);
        trapSlowdownEndTime = Time.time + duration;
    }

    private float CurrentTrapSlowdownMultiplier =>
        Time.time < trapSlowdownEndTime ? trapSlowdownMultiplier : 1f;

    #endregion

    #region 10. Gaz Kesildiğinde Otomatik Yavaşlama

    [Header("Gaz Kesildiğinde Otomatik Yavaşlama")]
    [SerializeField] private float coastingBrakeStrength = 35f;
    [SerializeField] private float minSpeedForCoastingBrake = 5f;

    #endregion

    #region Fotoğraf / Capsule Sahnesi Modu

    [Header("Fotoğraf Sahnesi (GEÇİCİ ARAÇ)")]
    [Tooltip("SADECE ekran görüntüsü/capsule çekim sahnesinde aç.\n\n" +
             "Normalde araba SADECE 'isOwned' (bu araba bu oyuncunun) olduğunda " +
             "hareket eder — ağ oturumu yoksa isOwned hep false kalır ve araba " +
             "sahneye atıldığında hiç kıpırdamaz. Bu kutu işaretliyken araba " +
             "kendini sahibi sayar, yani NetworkManager/lobi/host olmadan, " +
             "sıradan bir sahnede WASD ile sürebilirsin.\n\n" +
             "GERÇEK OYUN SAHNELERİNDE KAPALI KALMALI — açık kalırsa her " +
             "client kendi ekranındaki TÜM arabaları sürmeye çalışır.")]
    [SerializeField] private bool photoStudioMode = false;

    /// <summary>
    /// Bu arabanın girdisini bu makine mi işliyor? Normalde Mirror'ın
    /// 'isOwned' değeri, fotoğraf sahnesinde ise elle açılan bayrak.
    ///
    /// NEDEN netIdentity NULL KONTROLÜ VAR: Mirror'da `isOwned` doğrudan
    /// `netIdentity.isOwned` okuyor (bkz. NetworkBehaviour.cs:68). Bu prefab
    /// network DIŞINDA da Instantiate edilebiliyor — minimap araba marker'ı,
    /// fotoğraf sahnesi gibi. O kopyalarda NetworkIdentity olmayınca `isOwned`
    /// her karede NullReferenceException fırlatıyordu (Console'u dolduran,
    /// gerçek hataları görünmez yapan bir gürültü). Network'e kayıtlı olmayan
    /// bir kopya hiçbir zaman "kontrol bende" diyemez, o yüzden false dönmek
    /// hem güvenli hem doğru.
    /// </summary>
    public bool IsNetworkOwned => netIdentity != null && isOwned;

    private bool HasControl => photoStudioMode || IsNetworkOwned;

    /// <summary>
    /// CarCameraFollow bunu okuyup takip kamerasını ağ olmadan açabiliyor.
    /// </summary>
    public bool PhotoStudioMode => photoStudioMode;

    #endregion

    #region Yarış Sonu / Podyum

    // Yarış bitip podyum sahnesine geçilince true olur — fizik/input tamamen
    // durur. Sadece OWNER'ın kendi instance'ında anlamlı; remote client'lar
    // zaten NetworkTransform ile senkronize pozisyonu okuyor, owner durunca
    // onlar da otomatik duruyor.
    private bool raceEndedFrozen = false;

    /// <summary>
    /// RacePodiumManager, YARIŞ BİTTİĞİNDE arabayı podyum kolonundaki spawn
    /// noktasına ışınlamak için çağırır. Sadece kontrolü olan (owner) client
    /// çağırmalı — Sync Direction Client To Server olduğu için pozisyonu
    /// yetkili şekilde yazabilen taraf odur.
    /// </summary>
    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        if (!HasControl || carRB == null)
        {
            Debug.LogWarning($"[CarController] TeleportTo çağrıldı ama HasControl={HasControl}, carRB={(carRB != null)} — ışınlama İPTAL.");
            return;
        }

        // Işınlanmadan ÖNCE efekt üretimini kes: skidmark/duman hâlâ açıkken
        // yer değiştirirsek eski konumla yeni konum arasına iz/partikül basılır.
        ToggleSkidMarks(false);
        ToggleSkidSmokes(false);

        carRB.linearVelocity = Vector3.zero;
        carRB.angularVelocity = Vector3.zero;
        carRB.position = position;
        carRB.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);

        // Konum değiştikten SONRA temizle. TrailRenderer noktalarını DÜNYA
        // uzayında tuttuğu için, emitting'i kapatmak eski izin silinmesine
        // yetmiyor — hâlâ duran noktalarla yeni konum arasına bir şerit
        // gerilir. Clear() o birikmiş noktaları atıyor, iz yeni konumdan
        // sıfırdan başlıyor. Sıra önemli: önce taşı, sonra temizle.
        ClearEffectTrails();

        Debug.Log($"[CarController] {name} ışınlandı -> {position}");
    }

    /// <summary>
    /// Birikmiş skidmark noktalarını ve havada duran duman partiküllerini siler.
    /// Işınlanma sonrası "eski yerden yeni yere uzanan çizgi" artefaktını
    /// önlemek için kullanılıyor.
    /// </summary>
    private void ClearEffectTrails()
    {
        foreach (var skidMark in skidMarks)
        {
            if (skidMark != null) skidMark.Clear();
        }

        foreach (var smoke in skidSmokes)
        {
            // true = alt (child) particle system'ler de temizlensin.
            if (smoke != null) smoke.Clear(true);
        }
    }

    /// <summary>
    /// Podyuma ışınlandıktan sonra input/özel fizik hesaplamasını (Suspension,
    /// Movement vb.) tamamen durdurur — AMA Rigidbody'yi kinematic YAPMIYOR.
    /// Spawn noktası kolonun biraz üstünde olduğu için, isKinematic false
    /// kaldığında Unity'nin normal fizik motoru yerçekimiyle arabayı doğal
    /// şekilde kolonun üstüne düşürüp oturtuyor (kolonun bir Collider'ı
    /// olması ŞART, yoksa araba sonsuza dek düşer).
    /// </summary>
    public void FreezeForRaceEnd()
    {
        raceEndedFrozen = true;

        // Düşüşü yarış anındaki eski hızla değil sıfırdan başlat — yoksa
        // araba kolona garip bir açıyla/hızla çarpabilir.
        if (carRB != null)
        {
            carRB.linearVelocity = Vector3.zero;
            carRB.angularVelocity = Vector3.zero;
        }

        // Teker/duman/skidmark görselleri artık HİÇ güncellenmeyecek (bkz.
        // Update() içindeki raceEndedFrozen erken çıkışı) — o yüzden burada
        // son bir kere elle kapatıyoruz, yoksa tam o anda açık kalmış bir
        // duman/iz sonsuza kadar ekranda asılı kalır.
        ToggleSkidMarks(false);
        ToggleSkidSmokes(false);
    }

    #endregion

    #region İzleyici (Spectator) Modu — Arabayı Gizleme

    // ─────────────────────────────────────────────────────────────────────
    // Yarışı bitiren oyuncunun arabası, podyum açılana kadar ortadan
    // kalkıyor (bkz. RacerSpectator.cs). Obje YOK EDİLMİYOR — üzerindeki
    // PlayerRaceController leaderboard/podyum sıralaması için hâlâ gerekli.
    // Bunun yerine görüntüsü, çarpışması, fiziği ve sesi kapatılıyor.
    //
    // GERİ AÇARKEN NEDEN LİSTE TUTUYORUZ: prefabda BAŞTAN kapalı duran
    // renderer/collider'lar olabilir (kapalı bir efekt, kullanılmayan bir
    // parça). "Hepsini aç" deseydik onları da yanlışlıkla açardık. Sadece
    // BİZİM kapattıklarımızı geri açıyoruz.
    // ─────────────────────────────────────────────────────────────────────

    private bool hiddenForSpectator;
    private readonly List<Renderer> spectatorHiddenRenderers = new();
    private readonly List<Collider> spectatorDisabledColliders = new();

    /// <summary>Araba şu an izleyici modu yüzünden gizli mi?</summary>
    public bool HiddenForSpectator => hiddenForSpectator;

    /// <summary>
    /// RacerSpectator çağırır — HER client'ta (sadece owner'da değil), yoksa
    /// araba sadece kendi ekranında kaybolur, diğerleri hayalet bir araba
    /// görmeye devam ederdi.
    /// </summary>
    public void SetHiddenForSpectator(bool hidden)
    {
        if (hidden == hiddenForSpectator) return;
        hiddenForSpectator = hidden;

        if (hidden)
        {
            // Işınlanmadaki ile aynı sıra/sebep: önce efekt üretimini kes,
            // sonra birikmiş iz/partikülleri temizle — yoksa araba görünmez
            // olduktan sonra havada asılı kalmış bir duman/iz kalırdı.
            ToggleSkidMarks(false);
            ToggleSkidSmokes(false);
            ClearEffectTrails();

            spectatorHiddenRenderers.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                spectatorHiddenRenderers.Add(r);
            }

            spectatorDisabledColliders.Clear();
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
            {
                if (c == null || !c.enabled) continue;
                c.enabled = false;
                spectatorDisabledColliders.Add(c);
            }

            // isKinematic: görünmez araba yerçekimiyle haritanın altına
            // düşmesin ya da bir yamaçtan kaymasın diye tamamen dondurulmuş
            // olmalı. Collider'ları zaten kapattığımız için kimseye "duvar"
            // etkisi yapmıyor (podyumdaki dondurmadan farkı bu — orada
            // arabanın kolona DÜŞMESİ gerektiği için kinematic yapılmıyor).
            if (carRB != null)
            {
                carRB.linearVelocity = Vector3.zero;
                carRB.angularVelocity = Vector3.zero;
                carRB.isKinematic = true;
            }

            // Motor/kayma sesi kesilsin — görünmeyen bir arabadan gelen
            // motor sesi diğer oyuncuların kafasını karıştırırdı.
            CarAudio audio = GetComponent<CarAudio>();
            if (audio != null) audio.enabled = false;

            foreach (AudioSource src in GetComponentsInChildren<AudioSource>(true))
            {
                if (src != null) src.Stop();
            }
        }
        else
        {
            foreach (Renderer r in spectatorHiddenRenderers)
            {
                if (r != null) r.enabled = true;
            }
            spectatorHiddenRenderers.Clear();

            foreach (Collider c in spectatorDisabledColliders)
            {
                if (c != null) c.enabled = true;
            }
            spectatorDisabledColliders.Clear();

            if (carRB != null) carRB.isKinematic = false;

            // CarAudio BİLEREK geri açılmıyor: bu noktadan sonraki tek durum
            // podyum ve orada araba zaten donmuş halde duruyor (motor sesi
            // olmaması doğru davranış).
        }
    }

    #endregion

    #region Unity Ana Fonksiyonları

    private void Awake()
    {
        if (carRB == null)
            carRB = GetComponent<Rigidbody>();

        // Fotoğraf modunda ağ hiç başlamıyor, yani OnStartAuthority() de hiç
        // çağrılmıyor — yerçekimini normalde orada açıyorduk. Burada elle
        // açmazsak araba havada asılı kalır.
        if (photoStudioMode && carRB != null)
            carRB.useGravity = true;
    }

    /// <summary>
    /// Mirror callback — obje HERHANGİ bir client'ta spawn olduğunda çağrılır
    /// (hem owner'da hem remote'da).
    ///
    /// NEDEN GEREKLİ: Remote arabalarda Suspension()/Movement() çalışmıyor
    /// (isOwned kontrolü yüzünden), ama Rigidbody hâlâ aktif olduğu için
    /// Unity'nin fizik motoru yerçekimini uygulamaya devam ediyor — hiçbir
    /// kuvvet buna karşı koymadığından araba yavaşça yere batıyor.
    ///
    /// NOT: isKinematic = true DENENMİŞTİ ama bu arabayı "duvar" gibi
    /// yapıyor — kinematic objeler collision'dan etkilenmiyor, sadece
    /// karşı tarafı itiyor, kendisi hiç tepki vermiyor. Bunun yerine
    /// SADECE yerçekimini kapatıyoruz: araba hâlâ normal dinamik bir
    /// Rigidbody (çarpışmalarda gerçekçi tepki veriyor, itilebiliyor)
    /// ama üzerine yerçekimi etki etmediği için batmıyor. NetworkTransform
    /// zaten periyodik olarak doğru pozisyonu yeniden yazdığı için, itilen
    /// araba kısa sürede owner'ın gerçek konumuna kendini toparlıyor —
    /// arcade oyun için hoş bir "esneme" hissi, duvar hissi değil.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        if (carRB != null)
            carRB.useGravity = false;

        // Host, kendi arabası dışındaki arabalara baktığında SyncVar hook'ları
        // (deserialize'a bağlı oldukları için) tetiklenmeyebiliyor — bu proje
        // içinde daha önce de yaşanan bir durum. Rengin spawn anında kesin
        // uygulanması için OnStartClient'ta da elle çağırıyoruz.
        ApplyCarColor(netColorIndex);
    }

    /// <summary>
    /// Mirror callback — SADECE bu arabanın sahibi olan client'ta çağrılır.
    /// Burada gerçek yerçekimini geri açıyoruz, çünkü owner'ın süspansiyon
    /// sistemi (Suspension()) yerçekimine karşı kuvvet uygulayarak dengeyi
    /// sağlıyor — bu etkileşim sadece owner'da gerçekleşmeli.
    /// </summary>
    public override void OnStartAuthority()
    {
        base.OnStartAuthority();

        if (carRB != null)
            carRB.useGravity = true;
    }

    private void FixedUpdate()
    {
        // ─────────────────────────────────────────────────────────────
        // SADECE OWNER FİZİĞİ HESAPLIYOR. Remote arabalar için pozisyon
        // zaten NetworkTransform tarafından otomatik güncelleniyor,
        // burada tekrar fizik hesaplamaya gerek yok (hatta hesaplarsak
        // NetworkTransform'un yazdığı pozisyonla çakışıp titremeye
        // sebep olur).
        // ─────────────────────────────────────────────────────────────
        // hiddenForSpectator: yarışı bitirip izleyici moduna geçen oyuncunun
        // arabası görünmez/çarpışmasız halde duruyor — fiziği hesaplamaya
        // devam etmesi hem gereksiz hem tehlikeli (görünmez araba yamaçtan
        // kayıp bambaşka bir yere gidebilirdi).
        if (!HasControl || raceEndedFrozen || hiddenForSpectator) return;

        GroundCheck();
        CalculateCarVelocity();
        Suspension();
        Movement();
        ApplyDragAndResistance();
        CheckAndStop();

        // Tekerlek pozisyonlarını ve direksiyon inputunu remote client'lara
        // yaymak için senkronize et.
        if (tires[0] != null) netTire0LocalPos = tires[0].transform.localPosition;
        if (tires[1] != null) netTire1LocalPos = tires[1].transform.localPosition;
        if (tires[2] != null) netTire2LocalPos = tires[2].transform.localPosition;
        if (tires[3] != null) netTire3LocalPos = tires[3].transform.localPosition;
        netSteerInput = steerInput;

        // Görsel state'i diğer client'lara yaymak için senkronize et.
        // (Sync Direction: Client To Server ayarlandığı için owner
        // bunları doğrudan yazabiliyor.)
        netIsDrifting = isDrifting;
        netIsGrounded = isGrounded;
        netVelocityRatio = carVelocityRatio;
        netLocalVelocityX = currentCarLocalVelocity.x;

        // shouldShowEffects kararı burada (owner'ın FixedUpdate'inde) hesaplanıp
        // latch'leniyor — Vfx() artık bunu yeniden hesaplamıyor, doğrudan bu
        // (senkronize edilmiş) sonucu kullanıyor.
        float skidThreshold = isCarOnIce ? 2f : minSideSkidVelocity;
        bool rawShouldShowEffects = isGrounded &&
                                    (Mathf.Abs(currentCarLocalVelocity.x) > skidThreshold ||
                                    (isDrifting && currentSpeed > 5f) ||
                                    (isCarOnIce && Mathf.Abs(steerInput) > 0.5f)) &&
                                    carVelocityRatio > 0;

        skidEffectLatchTimer = rawShouldShowEffects
            ? skidEffectMinVisibleDuration
            : Mathf.Max(0f, skidEffectLatchTimer - Time.fixedDeltaTime);

        shouldShowEffects = skidEffectLatchTimer > 0f;
        netShouldShowEffects = shouldShowEffects;
    }

    private void Update()
    {
        // Visuals() bilerek ÇAĞRILMIYOR — TireVisuals() carVelocityRatio'nun
        // SON değerine göre tekerleği döndürmeye devam ederdi (araba durduktan
        // sonra bile tekerler dönüyor gibi görünmesinin sebebi buydu).
        // Donma anında tekerlek/direksiyon açısı ne haldeyse öyle kalsın diye
        // bu fonksiyona hiç girmiyoruz.
        //
        // hiddenForSpectator aynı sebeple burada: araba görünmezken tekerlek/
        // duman görsellerini güncellemenin bir anlamı yok, ayrıca izleyici
        // modundaki oyuncunun WASD'si arabayı sürmeye devam etmemeli.
        if (raceEndedFrozen || hiddenForSpectator) return;

        if (HasControl)
        {
            GetPlayerInput();
        }
        else
        {
            // ÖNEMLİ: SyncVar hook'larına GÜVENMİYORUZ — host kendisi sunucu
            // olduğu için (host = server + client aynı süreçte), host'un
            // yerel görünümü network'ten "deserialize" ederek veri almıyor,
            // hook'lar da SADECE deserialize anında tetikleniyor. Yani host,
            // BAŞKA bir client'ın arabasına baktığında hook'lar hiç çalışmıyor
            // — ham SyncVar değeri (netSteerInput vb.) doğru gelse bile, onu
            // görsel alanlara kopyalayan hook atlanıyor, araba "donmuş" görünüyor.
            //
            // Çözüm: hook'u beklemeden, HER KAREDE ham senkronize değerleri
            // doğrudan buradan kopyalıyoruz — host olsun client olsun,
            // davranış artık tutarlı.
            steerInput = netSteerInput;
            isDrifting = netIsDrifting;
            isGrounded = netIsGrounded;
            carVelocityRatio = netVelocityRatio;
            currentCarLocalVelocity.x = netLocalVelocityX;

            ApplyRemoteTirePosition(0, netTire0LocalPos);
            ApplyRemoteTirePosition(1, netTire1LocalPos);
            ApplyRemoteTirePosition(2, netTire2LocalPos);
            ApplyRemoteTirePosition(3, netTire3LocalPos);
        }

        // Visuals HERKESTE çalışıyor — owner'da taze hesaplanan, remote'da
        // yukarıda kopyalanan değerlerle aynı kod çalışıyor.
        Visuals();
    }

    #endregion

    #region Girdi Alma Fonksiyonu

    private void GetPlayerInput()
    {
        moveInput = Input.GetAxis("Vertical");
        steerInput = Input.GetAxis("Horizontal");
        isHandbrakePressed = Input.GetKey(KeyCode.Space);
    }

    #endregion

    #region Hareket Fonksiyonları

    private void Movement()
    {
        if (isGrounded)
        {
            Acceleration();
            Decelration();
            Turn();
            ArcadeDrift();
            SidewaysDrag();
        }
    }

    private void Acceleration()
    {
        currentSpeed = Mathf.Abs(currentCarLocalVelocity.z) * 3.6f;

        float speedLimiter = 1f;
        if (currentSpeed >= maxSpeed * 0.98f)
        {
            float overspeedRatio = (currentSpeed - maxSpeed * 0.98f) / (maxSpeed * 0.02f);
            speedLimiter = Mathf.Pow(1f - Mathf.Clamp01(overspeedRatio), 2f);
        }

        if (currentSpeed >= maxSpeed)
            speedLimiter = 0f;

        float currentAcceleration = acceleration;

        // Buz/kenarlık/çim BİRBİRİNİ dışlıyor (bkz. Suspension() — tekerlek
        // aynı anda ikisine birden basamaz), o yüzden else-if zinciri yeterli.
        if (isCarOnIce)
            currentAcceleration *= iceAccelerationGrip;
        else if (isCarOnGrass)
            currentAcceleration *= grassAccelerationGrip;
        else if (isCarOnCurb)
            currentAcceleration *= curbAccelerationGrip;

        // Drift tuzağı cezası — yüzeyden BAĞIMSIZ, üstüne çarpan olarak biniyor.
        currentAcceleration *= CurrentTrapSlowdownMultiplier;

        if (Mathf.Abs(moveInput) > 0.01f)
        {
            float finalAcceleration = currentAcceleration * moveInput * speedLimiter;
            carRB.AddForceAtPosition(finalAcceleration * transform.forward, accelerationPoint.position, ForceMode.Acceleration);
        }
    }

    private void Decelration()
    {
        bool isReversing = Mathf.Abs(moveInput) > 0.1f && Mathf.Sign(moveInput) != Mathf.Sign(carVelocityRatio);
        bool isLowSpeedNoInput = Mathf.Abs(moveInput) < 0.01f && currentSpeed < lowSpeedThreshold;

        if (isReversing)
        {
            float decelPower = brakingDeceleration;
            if (isCarOnIce) decelPower *= 0.1f;

            Vector3 decelerationDirection = -transform.forward * Mathf.Sign(carVelocityRatio);
            carRB.AddForce(decelPower * Mathf.Abs(carVelocityRatio) * decelerationDirection, ForceMode.Acceleration);
        }
        else if (isLowSpeedNoInput && !isCarOnIce)
        {
            float decelPower = isHandbrakePressed ? brakingDeceleration : deceleration * 0.5f;
            float lowSpeedBoost = 1f + (1f - currentSpeed / lowSpeedThreshold);
            decelPower *= lowSpeedBoost;

            Vector3 decelerationDirection = -transform.forward * Mathf.Sign(carVelocityRatio);
            carRB.AddForce(decelPower * Mathf.Abs(carVelocityRatio) * decelerationDirection, ForceMode.Acceleration);
        }
        else if (Mathf.Abs(moveInput) < 0.01f && currentSpeed > minSpeedForCoastingBrake && !isCarOnIce)
        {
            float brakePower = coastingBrakeStrength;
            Vector3 brakeDirection = -transform.forward * Mathf.Sign(carVelocityRatio);
            carRB.AddForce(brakePower * brakeDirection, ForceMode.Acceleration);
        }
    }

    private void Turn()
    {
        float steeringMultiplier = isDrifting ? driftSteerBoost : 1f;
        if (isCarOnIce)
            steeringMultiplier *= iceSteeringChaosMultiplier;

        carRB.AddRelativeTorque(steerStrength * steerInput * turningCurve.Evaluate(Mathf.Abs(carVelocityRatio)) *
            Mathf.Sign(carVelocityRatio) * steeringMultiplier * carRB.transform.up, ForceMode.Acceleration);
    }

    private void SidewaysDrag()
    {
        float currentSidewaySpeed = currentCarLocalVelocity.x;
        float dragCoefficient;

        if (isCarOnIce)
            dragCoefficient = iceSidewaysDrag;
        else if (isDrifting)
            dragCoefficient = dragCoefficent * driftSidewaysGrip;
        else if (Mathf.Abs(moveInput) < 0.1f || Mathf.Sign(moveInput) != Mathf.Sign(carVelocityRatio))
            dragCoefficient = brakingDragCoefficent;
        else
            dragCoefficient = dragCoefficent;

        float dragMagnitude = -currentSidewaySpeed * dragCoefficient;
        Vector3 dragForce = transform.right * dragMagnitude;
        carRB.AddForceAtPosition(dragForce, carRB.worldCenterOfMass, ForceMode.Acceleration);
    }

    private void ArcadeDrift()
    {
        // HİSTEREZİS: drifte girmek için driftMinSpeed gerekiyor, ama ZATEN
        // driftteyken çıkmak için daha düşük bir hıza inmek gerekiyor. Tek
        // eşik olsaydı araç sınırda salınır, yanal tutuş sürekli 0.56 ↔ 0.8
        // arasında zıplar ve sürekli daire çizerken "takıla takıla" dönerdi.
        float requiredSpeed = isDrifting ? driftMinSpeed * driftExitSpeedFactor : driftMinSpeed;

        bool wantsToDrift = isHandbrakePressed && currentSpeed > requiredSpeed;
        float targetDrift = wantsToDrift ? 1f : 0f;
        currentDriftFactor = Mathf.Lerp(currentDriftFactor, targetDrift, Time.fixedDeltaTime * driftTransitionSpeed);
        isDrifting = currentDriftFactor > 0.05f;

        if (isDrifting && Mathf.Abs(moveInput) > 0.1f)
        {
            Vector3 forwardVelocity = transform.forward * currentCarLocalVelocity.z;
            Vector3 speedReduction = -forwardVelocity * (1f - driftSpeedLoss) * currentDriftFactor;
            carRB.AddForce(speedReduction, ForceMode.Acceleration);
        }
    }

    private void ApplyDragAndResistance()
    {
        if (isGrounded)
        {
            if (isCarOnIce)
            {
                carRB.AddForce(-carRB.linearVelocity * (airDrag * 0.2f), ForceMode.Acceleration);
            }
            else
            {
                float resistanceFactor = Mathf.Abs(moveInput) > 0.1f ? 0.2f : 1f;
                if (isDrifting)
                    resistanceFactor += 0.3f * currentDriftFactor;

                float currentRollingResistance = rollingResistance;
                float baseDrag = currentRollingResistance * resistanceFactor * Mathf.Abs(carVelocityRatio);

                if (currentSpeed < lowSpeedThreshold && Mathf.Abs(moveInput) < 0.01f)
                {
                    float lowSpeedFactor = 1f - (currentSpeed / lowSpeedThreshold);
                    float currentLowSpeedDrag = lowSpeedDragMultiplier;
                    baseDrag += currentLowSpeedDrag * lowSpeedFactor;
                }

                // ÇİM SÜRTÜNMESİ: aşağıdaki AddForce zaten kuvveti hıza
                // (linearVelocity) çarptığı için bu sabit bir fren değil —
                // hızlıyken çok, yavaşlarken az yavaşlatıyor, yani araç
                // gittikçe yavaşlıyor. ALT SINIR şart: olmasaydı sürtünme
                // aracı çimde sürüne sürüne durdurur, oyuncu piste geri
                // dönemezdi. Bu sınırın altında sürtünme kesiliyor ve araç
                // o hız civarında dengeye oturuyor.
                if (isCarOnGrass && currentSpeed > grassDragFloorKmh)
                    baseDrag += grassDrag;

                carRB.AddForce(-carRB.linearVelocity * baseDrag, ForceMode.Acceleration);
            }
        }
        else
        {
            carRB.AddForce(-carRB.linearVelocity * airDrag, ForceMode.Acceleration);
        }
    }

    private void CheckAndStop()
    {
        if (!isGrounded) return;
        if (isCarOnIce) return;

        if (currentSpeed < stopThreshold && Mathf.Abs(moveInput) < 0.1f)
        {
            if (carRB.linearVelocity.magnitude < minSpeedForMovement)
            {
                carRB.linearVelocity = Vector3.zero;
                carRB.angularVelocity = Vector3.zero;
            }
            else
            {
                carRB.AddForce(-carRB.linearVelocity * autoStopForce, ForceMode.Acceleration);
            }
        }
    }

    #endregion

    #region Görsel Fonksiyonlar

    private void Visuals()
    {
        TireVisuals();
        Vfx();
    }

    private float[] currentSteerAngles = new float[2];

    private void TireVisuals()
    {
        float effectiveTireRotSpeed = tireRotSpeed;
        if (isCarOnIce && Mathf.Abs(moveInput) > 0.1f) effectiveTireRotSpeed *= 2f;

        float targetSteerAngle = maxSteeringAngle * steerInput;

        for (int i = 0; i < 2; i++)
        {
            tires[i].transform.Rotate(Vector3.right, effectiveTireRotSpeed * carVelocityRatio * Time.deltaTime, Space.Self);
            currentSteerAngles[i] = Mathf.Lerp(currentSteerAngles[i], targetSteerAngle, Time.deltaTime * 8f);
            frontTireParents[i].transform.localEulerAngles = new Vector3(
                frontTireParents[i].transform.localEulerAngles.x,
                currentSteerAngles[i],
                frontTireParents[i].transform.localEulerAngles.z
            );
        }

        for (int i = 2; i < 4; i++)
        {
            tires[i].transform.Rotate(Vector3.right, effectiveTireRotSpeed * carVelocityRatio * Time.deltaTime, Space.Self);
        }
    }

    private void Vfx()
    {
        // Karar (latch'li) dünden beri FixedUpdate'te hesaplanıyor — bkz.
        // shouldShowEffects ve netShouldShowEffects. Owner için orada
        // güncelleniyor, remote client'lar için OnShouldShowEffectsChanged
        // hook'u ile geliyor. İkisi de AYNI ANDA, aynı değerle tetikleniyor.
        //
        // NOT: Bugün burada denenen ekstra "duman gecikmesi" / "patlama
        // (Emit)" / "kapanma toleransı" mantığı GERİ ALINDI — asıl sebep
        // particle'ın Max Particles (300) sınırına takılması + Start
        // Lifetime'ın SABİT olması (aynı anda üretilen parçacıklar aynı
        // anda ölüyor) olduğu ortaya çıktı. Gerçek çözüm particle
        // ayarlarında (Max Particles artırmak / Start Lifetime'ı rastgele
        // bir aralığa çevirmek), tetikleme kodunda değil.
        ToggleSkidMarks(shouldShowEffects);
        ToggleSkidSmokes(shouldShowEffects);
    }

    private void ToggleSkidMarks(bool toggle)
    {
        foreach (var skidMark in skidMarks)
        {
            if (skidMark != null) skidMark.emitting = toggle;
        }
    }

    private void ToggleSkidSmokes(bool toggle)
    {
        // isPlaying DEĞİL isEmitting kontrol ediliyor — isPlaying, Stop()
        // çağrılsa bile ortada henüz ölmemiş (fade-out'taki) eski partiküller
        // varken true dönmeye devam ediyor. Bu yüzden yeni bir drift, eski
        // dumanın kuyruğu tamamen sönene kadar Play() ile hiç yeniden
        // tetiklenmiyordu. isEmitting, Stop() çağrılır çağrılmaz anında false
        // oluyor — istediğimiz davranış bu.
        foreach (var smoke in skidSmokes)
        {
            if (smoke != null)
            {
                if (toggle) { if (!smoke.isEmitting) smoke.Play(); }
                else { if (smoke.isEmitting) smoke.Stop(); }
            }
        }
    }

    private void SetTirePosition(GameObject tire, Vector3 targetPosition)
    {
        if (tire != null) tire.transform.position = targetPosition;
    }

    #endregion

    #region Araba Durum Kontrolleri

    private void GroundCheck()
    {
        int tempGroundedWheels = 0;
        for (int i = 0; i < wheelsIsGrounded.Length; i++) tempGroundedWheels += wheelsIsGrounded[i];
        isGrounded = tempGroundedWheels > 1;
    }

    private void CalculateCarVelocity()
    {
        currentCarLocalVelocity = transform.InverseTransformDirection(carRB.linearVelocity);
        carVelocityRatio = currentCarLocalVelocity.z / maxSpeed;
    }

    #endregion

    #region Süspansiyon

    private void Suspension()
    {
        int wheelsOnIceCount = 0;
        int wheelsOnCurbCount = 0;
        int wheelsOnGrassCount = 0;

        for (int i = 0; i < rayPoints.Length; i++)
        {
            RaycastHit hit;
            float maxLength = restLength + springTravel;

            if (Physics.Raycast(rayPoints[i].position, -rayPoints[i].up, out hit, maxLength + wheelRadius, drivable))
            {
                wheelsIsGrounded[i] = 1;

                // Bir collider'ın tag'i tek olduğu için bu üçü doğal olarak
                // birbirini dışlıyor — aynı tekerlek aynı anda hem Ice hem
                // Curb hem Grass sayılamaz.
                if (hit.collider.CompareTag(iceTag)) wheelsOnIceCount++;
                else if (hit.collider.CompareTag(grassTag)) wheelsOnGrassCount++;
                else if (hit.collider.CompareTag(curbTag)) wheelsOnCurbCount++;

                float currentSpringLenght = hit.distance - wheelRadius;
                float springCompression = (restLength - currentSpringLenght) / springTravel;
                float springVelocity = Vector3.Dot(carRB.GetPointVelocity(rayPoints[i].position), rayPoints[i].up);
                float dampForce = damperStiffness * springVelocity;

                float gripReduction = (isDrifting && i >= 2) ?
                    Mathf.Lerp(1f, driftGripReduction, currentDriftFactor) : 1f;

                float currentSpringStiffness = springStiffness * gripReduction;
                float springForce = currentSpringStiffness * springCompression;
                float netForce = springForce - dampForce;

                carRB.AddForceAtPosition(netForce * rayPoints[i].up, rayPoints[i].position);
                SetTirePosition(tires[i], hit.point + rayPoints[i].up * wheelRadius);
            }
            else
            {
                wheelsIsGrounded[i] = 0;
                SetTirePosition(tires[i], rayPoints[i].position - rayPoints[i].up * maxLength);
            }
        }

        isCarOnIce = (wheelsOnIceCount > 0) || externalIceTrigger;
        isCarOnGrass = wheelsOnGrassCount > 0;
        isCarOnCurb = wheelsOnCurbCount > 0;
    }

    #endregion

    #region Public API — DriftTrap ve diğer sistemler için

    /// <summary>
    /// Aracın şu an drift atıp atmadığını döner. Owner'da doğrudan taze
    /// değer, remote'da SyncVar hook'undan gelen senkronize değer —
    /// DriftTrap.cs (server'da çalışıyor) bunu güvenle her iki durumda da
    /// kullanabilir.
    /// </summary>
    public bool IsDrifting()
    {
        return isDrifting;
    }

    // ─────────────────────────────────────────────────────────────────────
    // SES SİSTEMİ İÇİN OKUMA ERİŞİMLERİ (CarAudio.cs kullanıyor)
    //
    // ÖNEMLİ: Üçü de HEM owner'da HEM remote'ta doğru değeri veriyor —
    // dayandıkları alanlar (carVelocityRatio, shouldShowEffects, isGrounded)
    // zaten SyncVar ile senkronize edilip Update()'te remote arabalara
    // kopyalanıyor (bkz. Update() içindeki "else" bloğu). Bu sayede motor
    // sesi ve lastik cızırtısı BAŞKA oyuncuların arabalarında da doğru
    // çalışıyor, ekstra network mesajı gerekmiyor.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aracın KM/H cinsinden hızı — HUD, motor sesi ve hıza bağlı FOV için
    /// kullanılması gereken değer budur.
    ///
    /// ⚠️ NEDEN SpeedRatio DEĞİL DE BU: `carVelocityRatio` isminin aksine
    /// 0-1 aralığında DEĞİL. Hesabı `localVelocity.z / maxSpeed` ve burada
    /// birimler karışıyor — pay METRE/SANİYE, payda ise KM/H (maxSpeed 300
    /// bir km/h değeri, hız sınırı `currentSpeed >= maxSpeed` şeklinde
    /// km/h ile karşılaştırılıyor). Sonuç: araç tam 300 km/h'deyken bile
    /// carVelocityRatio ancak ~0.28 oluyor, asla 1'e ulaşmıyor.
    ///
    /// Bu oranı doğrudan "yüzde kaç hızlıyız" diye kullanan her şey (motor
    /// sesi perdesi, FOV) hep %28'de takılı kalırdı. Bu property birimi
    /// düzeltip gerçek km/h veriyor: ratio × maxSpeed = m/s, ×3.6 = km/h.
    ///
    /// carVelocityRatio senkronize edildiği için bu değer hem owner'da hem
    /// remote arabalarda doğru çalışıyor.
    /// </summary>
    public float SpeedKmh => Mathf.Abs(carVelocityRatio) * maxSpeed * 3.6f;

    /// <summary>
    /// Ham hız oranı (0 = duruyor). ⚠️ 1'e ULAŞMAZ — bkz. SpeedKmh
    /// açıklaması. Yeni kodda SpeedKmh kullan, bu sadece geriye dönük
    /// uyumluluk için duruyor.
    /// </summary>
    public float SpeedRatio => Mathf.Clamp01(Mathf.Abs(carVelocityRatio));

    /// <summary>Şu an lastik izi/duman efekti gösteriliyor mu — lastik cızırtısı sesi bununla aynı anda çalıyor.</summary>
    public bool IsSkidding => shouldShowEffects;

    /// <summary>Araba yerde mi (havadayken motor sesi boşta gaz gibi yükselmesin diye).</summary>
    public bool IsGroundedNow => isGrounded;

    #endregion
}