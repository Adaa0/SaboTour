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
    [SerializeField] private float driftGripReduction = 0.6f;
    [SerializeField] private float driftSteerBoost = 1.5f;
    [SerializeField] private float driftMinSpeed = 15f;
    [SerializeField] private float driftTransitionSpeed = 5f;
    [SerializeField] private float driftSpeedLoss = 0.85f;

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
    /// </summary>
    private bool HasControl => photoStudioMode || isOwned;

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
        if (!HasControl || carRB == null) return;

        carRB.linearVelocity = Vector3.zero;
        carRB.angularVelocity = Vector3.zero;
        carRB.position = position;
        carRB.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
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
        ToggleSkidSmokes(false, false);
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
        if (!HasControl || raceEndedFrozen) return;

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
        if (raceEndedFrozen) return;

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
        if (isCarOnIce)
            currentAcceleration *= iceAccelerationGrip;

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
            dragCoefficient = dragCoefficent * 0.7f;
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
        bool wantsToDrift = isHandbrakePressed && currentSpeed > driftMinSpeed;
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
        // Karar (latch'li) artık FixedUpdate'te hesaplanıyor — bkz. shouldShowEffects
        // ve netShouldShowEffects. Owner için orada güncelleniyor, remote client'lar
        // için OnShouldShowEffectsChanged hook'u ile geliyor. İkisi de AYNI ANDA
        // tetikleniyor — kısa el freni darbelerinin bile garanti görünmesi
        // gerektiği için (bkz. skidEffectMinVisibleDuration) burada bir "duman
        // gecikmesi" YOK: kısa bir gecikme denendi ama darbe süresi gecikmeden
        // kısa kaldığında dumanın HİÇ tetiklenmemesine sebep oluyordu (bug,
        // geri alındı). Doğal "duman biraz sonra yükseliyor" hissi istenirse
        // bunu particle'ın KENDİ Size/Opacity over Lifetime eğrisiyle (aşağıda
        // açıklandı) yapmak gerekiyor, açma/kapama zamanlamasıyla değil.
        // Efekt YENİ başladıysa (önceki karede kapalıydı, şimdi açık) burada
        // yakalanıyor — ToggleSkidSmokes bunu, Rate Over Time'ın şansına
        // bırakmadan garanti bir parçacık patlaması üretmek için kullanıyor.
        bool justStarted = shouldShowEffects && !wasShowingEffects;
        wasShowingEffects = shouldShowEffects;

        ToggleSkidMarks(shouldShowEffects);

        // Duman, shouldShowEffects false OLDUKTAN SONRA da bir süre daha
        // "açık" tutuluyor (Stop() hemen çağrılmıyor). NEDEN: sürekli bir
        // drift sırasında ham koşul (currentCarLocalVelocity.x eşiği vb.)
        // çok kısa anlarla false'a düşüp tekrar true olabiliyor — her
        // false'ta Stop() çağrılırsa duman görünür şekilde "kesilip" tekrar
        // patlıyormuş gibi hissettiriyordu (skidmark'ta bu sorun yok çünkü
        // TrailRenderer durunca bile eski iz duruyor, duman gerçekten
        // kesiliyor). Bu tolerans SADECE ne zaman Stop() çağrılacağını
        // geciktiriyor, ne zaman patlama (Emit) tetikleneceğini DEĞİL —
        // justStarted hâlâ ham shouldShowEffects geçişinden hesaplanıyor.
        smokeStopHoldTimer = shouldShowEffects
            ? smokeStopHoldDuration
            : Mathf.Max(0f, smokeStopHoldTimer - Time.deltaTime);

        bool smokeShouldPlay = shouldShowEffects || smokeStopHoldTimer > 0f;
        ToggleSkidSmokes(smokeShouldPlay, justStarted);
    }

    private bool wasShowingEffects;
    [Tooltip("Efekt başladığı AN, Rate Over Time'ı beklemeden garanti üretilecek parçacık sayısı — kısa el freni darbelerinde bile duman şansa bırakılmadan görünsün diye.")]
    [SerializeField] private int smokeBurstCount = 10;

    private float smokeStopHoldTimer;
    [Tooltip("shouldShowEffects false olduktan sonra duman Stop() çağrılmadan önce beklenecek ekstra süre — sürekli drift içindeki çok kısa doğal kesintilerde dumanın görünür şekilde kesilip yeniden patlamasını önler.")]
    [SerializeField] private float smokeStopHoldDuration = 0.3f;

    private void ToggleSkidMarks(bool toggle)
    {
        foreach (var skidMark in skidMarks)
        {
            if (skidMark != null) skidMark.emitting = toggle;
        }
    }

    private void ToggleSkidSmokes(bool toggle, bool burstOnStart)
    {
        foreach (var smoke in skidSmokes)
        {
            if (smoke == null) continue;

            if (toggle)
            {
                if (!smoke.isPlaying) smoke.Play();
                // Rate Over Time'ın "şansına" bırakmadan, efekt başladığı anda
                // birkaç parçacığı doğrudan üretiyoruz — çok kısa açık kalma
                // pencerelerinde bile (örn. skidEffectMinVisibleDuration kadar)
                // en az bu kadar parçacık GARANTİ görünür.
                if (burstOnStart) smoke.Emit(smokeBurstCount);
            }
            else
            {
                if (smoke.isPlaying) smoke.Stop();
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

        for (int i = 0; i < rayPoints.Length; i++)
        {
            RaycastHit hit;
            float maxLength = restLength + springTravel;

            if (Physics.Raycast(rayPoints[i].position, -rayPoints[i].up, out hit, maxLength + wheelRadius, drivable))
            {
                wheelsIsGrounded[i] = 1;
                if (hit.collider.CompareTag(iceTag)) wheelsOnIceCount++;

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

    #endregion
}