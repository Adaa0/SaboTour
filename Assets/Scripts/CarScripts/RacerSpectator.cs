using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// YARIŞI BİTİREN OYUNCU İÇİN İZLEYİCİ (SPECTATOR) MODU
///
/// SORUN: Podyum artık ancak TÜM yarışçılar bitirince açılıyor (bkz.
/// RacePodiumManager.HandleRacerFinished). Bu doğru bir karardı ama bir
/// boşluk bıraktı: ilk bitiren oyuncu pistte serbest kalıyordu — turu
/// sayılmıyor ama arabası hâlâ ortalıkta, hâlâ diğerlerinin önünü kesebiliyor
/// ve yapacak bir şeyi yok.
///
/// ÇÖZÜM: Bitiren yarışçının arabası HERKESİN ekranında görünmez/çarpışmasız
/// hale geliyor, kendisi de serbest uçan bir kameraya geçip diğerlerini
/// izliyor. Podyum açıldığı anda her şey geri normale dönüyor (araba tekrar
/// görünür oluyor ve podyum kolonuna ışınlanıyor).
///
/// ── NEDEN ARABA GERÇEKTEN "DESTROY" EDİLMİYOR ──
/// Araba objesi aynı zamanda PlayerRaceController'ı taşıyor: tur/checkpoint/
/// süre SyncVar'ları, leaderboard kaydı (AllPlayers listesi) ve podyum
/// sıralaması hep oradan okunuyor. Objeyi yok etmek (ya da SetActive(false)
/// yapmak — Mirror'da networked bir objeyi kapatmak sync'i bozar) bu bilgiyi
/// de yok ederdi. Bunun yerine SADECE görüntüsü + çarpışması + fiziği
/// kapatılıyor: oyuncular için araba "yok olmuş" görünüyor, ağ için hâlâ
/// orada.
///
/// ── HANGİ KOD NEREDE ÇALIŞIYOR (önemli ayrım) ──
/// Bu component HER arabada, HER client'ta çalışıyor:
///   • Arabayı gizleme/gösterme kısmı HERKESTE çalışır — yoksa sadece kendi
///     ekranında kaybolur, diğerleri hayalet arabayı görmeye devam ederdi.
///   • Serbest kamera kısmı SADECE arabanın sahibinde (IsNetworkOwned)
///     çalışır — imleç ve kamera makine başına tek bir şey.
///
/// ── DURUM NASIL ANLAŞILIYOR (SyncVar hook'u DEĞİL, her karede kontrol) ──
/// `PlayerRaceController.HasFinished` ve `RacePodiumManager.RaceInProgress`
/// her karede okunuyor. Bilerek hook kullanılmadı: bu projede daha önce
/// host'un (server+client aynı süreç) SyncVar hook'larını almadığı bir bug
/// yaşandı (bkz. CLAUDE.md — CarController'ın "donmuş araba" sorunu).
/// Karede iki bool okumanın maliyeti sıfıra yakın, karşılığında sağlamlık
/// garanti.
///
/// ── MANUEL ADIM (Unity'de senin yapman gerekiyor) ──
/// Bu component'i `Assets/Prefabs/Car.prefab` üzerine EKLE:
///   1. Project penceresinde Car.prefab'a çift tıkla (Prefab Mode açılır).
///   2. En ÜSTTEKİ KÖK objeyi seç (CarController'ın olduğu obje).
///      ⚠️ Dikkat: aşağıda [RequireComponent(typeof(CarController))] var —
///      yanlış bir objeye eklersen Unity oraya sessizce bir CarController DA
///      ekler ve anlamsız bir hata zinciri başlar (bu proje bunu bir kere
///      yaşadı, bkz. CLAUDE.md).
///   3. Add Component → "Racer Spectator" ara ve ekle.
///   4. Ctrl+S ile kaydet.
/// Başka hiçbir sahne ayarı gerekmiyor — kamera, sınırlar ve bilgi yazısı
/// çalışma anında kendiliğinden hazırlanıyor.
/// </summary>
[RequireComponent(typeof(CarController))]
public class RacerSpectator : MonoBehaviour
{
    [Header("Açık / Kapalı")]
    [Tooltip("Kapatırsan yarışı bitiren oyuncu eskisi gibi arabasıyla pistte kalmaya devam eder (hata ayıklama için işine yarayabilir).")]
    [SerializeField] private bool enableSpectatorMode = true;

    [Header("Uçuş Hızı")]
    [Tooltip("Normal uçuş hızı (metre/saniye).")]
    [SerializeField] private float moveSpeed = 30f;
    [Tooltip("Shift basılıyken uçuş hızı. Pist ~300m çapında olduğu için hızlı mod bir uçtan diğerine makul sürede gitmeyi sağlıyor.")]
    [SerializeField] private float fastMoveSpeed = 75f;
    [Tooltip("Space / Ctrl ile dikey hareket hızı.")]
    [SerializeField] private float verticalSpeed = 20f;
    [Tooltip("Hıza ulaşma/durma yumuşaklığı (saniye). 0 = anlık başlayıp anlık duran, sert his.")]
    [SerializeField] private float accelerationTime = 0.15f;
    [Tooltip("Fare tekerleği ile uçuş hızının kaç katına kadar inip çıkabileceği. 1 = tekerlek hız değiştirmez.")]
    [SerializeField] private float scrollSpeedRange = 4f;

    [Header("Bakış")]
    [Tooltip("Fare hassasiyeti burada DEĞİL — Ayarlar menüsünden (MouseSensitivitySettings) okunuyor, sabotajcı kamerasıyla aynı değeri kullanıyor.")]
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;
    [Tooltip("Kamera dönüşünün yumuşatılması — büyük değer = daha sert/anlık.")]
    [SerializeField] private float lookSmoothing = 25f;

    // ─────────────────────────────────────────────────────────────────────
    // SINIRLAR — "ne kadar ileri / ne kadar yukarı / ne kadar aşağı"
    //
    // NEDEN GEREKLİ: sınırsız bırakılırsa izleyici pistten kilometrelerce
    // uzaklaşıp haritanın bittiği yeri (prop'ların bittiği boşluk, skybox'ın
    // dibi) görür — oyunun "arkasını" göstermek en ucuz görünen şeydir.
    // Ayrıca çok yukarı çıkınca izlenecek bir şey kalmaz, çok aşağı inince
    // zeminin altına geçer.
    //
    // YATAY SINIR BİR DAİRE, kare değil: pist her seferinde rastgele
    // üretiliyor ama TrackGenerator onu dünya merkezine ortalıyor
    // (RecenterAroundOrigin), yani "pist merkezinden R metre" tanımı her
    // pistte anlamlı kalıyor.
    // ─────────────────────────────────────────────────────────────────────
    [Header("SINIR — Yatay (ne kadar ileri gidebilir)")]
    [Tooltip("AÇIK (önerilen): sınır yarıçapı her yarışta gerçek pistten hesaplanır — pistin merkezden en uzak noktası + aşağıdaki pay. Pist rastgele üretildiği için elle girilen sabit bir sayı bazı pistlerde dar, bazılarında gereksiz geniş kalırdı.")]
    [SerializeField] private bool autoBoundsFromTrack = true;
    [Tooltip("Pistin en dış noktasından kaç metre daha dışarı çıkılabilsin. Küçültürsen izleyici pistin üstüne yapışır, büyütürsen ormanda gezebilir.")]
    [SerializeField] private float horizontalPadding = 60f;
    [Tooltip("SADECE yukarıdaki kutu KAPALIYKEN kullanılır: pist merkezinden sabit yarıçap (metre).")]
    [SerializeField] private float manualHorizontalRadius = 300f;

    [Header("SINIR — Dikey (ne kadar yukarı / aşağı)")]
    [Tooltip("Pist seviyesinden en az bu kadar YUKARIDA kalır (metre). Çok küçük yaparsan kamera zemine/yola girer.")]
    [SerializeField] private float minHeight = 4f;
    [Tooltip("Pist seviyesinden en fazla bu kadar YUKARI çıkabilir (metre). Buradan tüm pist tepeden görünür; daha yükseği izlemeyi zorlaştırır.")]
    [SerializeField] private float maxHeight = 160f;

    [Header("Bilgilendirme")]
    [Tooltip("İzleyici moduna geçerken ekranda gösterilecek kontrol açıklaması. Boş bırakırsan hiçbir yazı çıkmaz.")]
    [TextArea(2, 5)]
    [SerializeField] private string enterMessage =
        "Yarışı bitirdin! Diğer yarışçılar bekleniyor.\n" +
        "İZLEYİCİ MODU — WASD: hareket · Fare: bak · Space/Ctrl: yüksel/alçal · Shift: hızlı";
    [SerializeField] private float enterMessageSeconds = 6f;

    // ─── Çalışma anı durumu ──────────────────────────────────────────────

    private CarController car;
    private PlayerRaceController raceController;
    private CarCameraActivator camActivator;
    private RacePodiumManager podiumManager;
    private bool podiumSearched;

    private bool spectating;
    private bool cameraTaken;

    private Camera spectatorCamera;
    private CinemachineBrain brain;
    private bool brainWasEnabled;

    private float targetYaw, currentYaw;
    private float targetPitch, currentPitch;
    private Vector3 currentVelocity;
    private Vector3 velocitySmoothDamp;
    private float speedScale = 1f;

    // Sınırlar (yarışta bir kere hesaplanıp saklanıyor)
    private bool boundsResolved;
    private Vector3 boundsCenter;
    private float boundsRadius;
    private float boundsMinY, boundsMaxY;

    private float lastBoundaryNoticeTime = -99f;

    private void Awake()
    {
        car = GetComponent<CarController>();
        raceController = GetComponent<PlayerRaceController>();
    }

    private void Update()
    {
        if (!enableSpectatorMode || raceController == null) return;

        // Bitirdi mi VE yarış hâlâ sürüyor mu? İkincisi şart: podyum açılınca
        // araba geri görünür olup kolona ışınlanacak.
        bool shouldSpectate = raceController.HasFinished && RaceStillRunning();

        if (shouldSpectate != spectating)
        {
            if (shouldSpectate) EnterSpectatorMode();
            else ExitSpectatorMode();
        }

        if (!spectating || !cameraTaken) return;

        // Duraklatma menüsü açıkken imleç serbest — kamera dönmemeli, yoksa
        // menüde fare gezdirirken ekran savrulur (sabotajcıdaki ile aynı kural).
        if (PauseMenuController.IsOpen) return;

        HandleLook();
        HandleMove();
    }

    /// <summary>
    /// Podyum açıldığında (kazanan belli olunca) RaceInProgress false oluyor.
    /// Sahnede RacePodiumManager yoksa (ör. test sahnesi) yarış sürüyor kabul
    /// edilir — yanlış tarafa hata yapmak, izleyiciyi sebepsiz yere normal
    /// moda döndürmekten iyi.
    /// </summary>
    private bool RaceStillRunning()
    {
        if (podiumManager == null && !podiumSearched)
        {
            podiumManager = FindAnyObjectByType<RacePodiumManager>();
            podiumSearched = true;
        }

        return podiumManager == null || podiumManager.RaceInProgress;
    }

    // ─── Giriş / Çıkış ───────────────────────────────────────────────────

    private void EnterSpectatorMode()
    {
        spectating = true;

        // ── HER CLIENT'TA: araba ortadan kalksın ──
        car.SetHiddenForSpectator(true);

        // ── SADECE SAHİBİNDE: kamerayı devral ──
        if (!car.IsNetworkOwned) return;

        ResolveBounds();
        TakeOverCamera();

        if (!string.IsNullOrEmpty(enterMessage))
            ScreenNotice.Show(enterMessage, enterMessageSeconds);

        Debug.Log("[RacerSpectator] İzleyici modu açıldı — araba gizlendi, serbest kamera devrede.");
    }

    /// <summary>
    /// Normal duruma dön. İki yerden çağrılıyor: (1) buradaki her-kare
    /// kontrolü podyumun açıldığını görünce, (2) RacePodiumManager arabayı
    /// podyum kolonuna ışınlamadan HEMEN ÖNCE. İkincisi sıra garantisi için:
    /// araba gizli/kinematic haldeyken ışınlanırsa kolonun üstüne düşmez.
    /// Metod idempotent — iki kere çağrılması zararsız.
    /// </summary>
    public void ExitSpectatorMode()
    {
        if (!spectating) return;
        spectating = false;

        // SIRA ÖNEMLİ: önce gizlemeyi kaldır (Rigidbody kinematic'ten çıkar),
        // sonra dondur. Ters sırada olsaydı hız sıfırlama kinematic bir
        // gövdeye yazılırdı.
        car.SetHiddenForSpectator(false);

        // İzleyici modundan çıkışın TEK sebebi podyumun açılması. Arabayı
        // burada donduruyoruz çünkü sabotajcı kazandığı senaryoda podyum
        // arabaları kolona ışınlamıyor — dondurulmasaydı oyuncu, podyum
        // kamerasından izlerken görmediği arabasını WASD ile sürmeye devam
        // ederdi.
        car.FreezeForRaceEnd();

        ReleaseCamera();

        Debug.Log("[RacerSpectator] İzleyici modu kapandı — araba geri görünür.");
    }

    // ─── Kamera devralma ─────────────────────────────────────────────────

    /// <summary>
    /// Sahnedeki asıl kamerayı (Main Camera) doğrudan sürüyoruz; YENİ bir
    /// kamera oluşturmuyoruz. NEDEN: AudioListener, post-process ayarları ve
    /// "MainCamera" tag'i o objede duruyor — başka bir kamera açsaydık sesi
    /// ve `Camera.main`'e bakan diğer scriptleri (RacePodiumManager,
    /// SaboteurController) bozardık.
    ///
    /// Cinemachine'in kameraya yazmasını durdurmak için CinemachineBrain
    /// geçici olarak kapatılıyor — yoksa her karede bizim yazdığımız
    /// pozisyonun üstüne kendi hesabını yazardı.
    /// </summary>
    private void TakeOverCamera()
    {
        if (camActivator == null) camActivator = GetComponent<CarCameraActivator>();
        if (camActivator != null) camActivator.SetCarCamActive(false);

        spectatorCamera = Camera.main;
        if (spectatorCamera == null)
        {
            Debug.LogWarning("[RacerSpectator] Camera.main bulunamadı — izleyici kamerası açılamadı. Sahnede 'MainCamera' tag'li aktif bir kamera olmalı.");
            return;
        }

        brain = spectatorCamera.GetComponent<CinemachineBrain>();
        if (brain != null)
        {
            brainWasEnabled = brain.enabled;
            brain.enabled = false;
        }

        // Geçiş yumuşak olsun diye kamerayı olduğu yerde bırakıyoruz; sadece
        // açıları mevcut bakıştan devralıyoruz ki ilk karede ekran zıplamasın.
        Vector3 euler = spectatorCamera.transform.eulerAngles;
        currentYaw = targetYaw = euler.y;
        currentPitch = targetPitch = NormalizePitch(euler.x);

        currentVelocity = Vector3.zero;
        velocitySmoothDamp = Vector3.zero;
        speedScale = 1f;

        // Araba yol seviyesindeydi, minimum yükseklik sınırının altında
        // kalmış olabilir — ilk karede sınırın içine çekiyoruz.
        spectatorCamera.transform.position = ClampToBounds(spectatorCamera.transform.position, out _);

        cameraTaken = true;
    }

    private void ReleaseCamera()
    {
        if (camActivator == null) camActivator = GetComponent<CarCameraActivator>();

        // CarCam'i BİLEREK geri açmıyoruz: bu noktadan sonra tek çıkış podyum
        // ve RacePodiumManager zaten podyum kamerasına geçerken CarCam'i
        // kapatıyor. Burada açsaydık iki kamera bir kare boyunca yarışırdı.
        if (brain != null)
        {
            brain.enabled = brainWasEnabled;
            brain = null;
        }

        spectatorCamera = null;
        cameraTaken = false;
    }

    private void OnDisable()
    {
        // Araba yok olurken / sahne değişirken Cinemachine'i kapalı bırakma —
        // yoksa bir sonraki yarışta hiçbir kamera çalışmaz.
        if (cameraTaken) ReleaseCamera();
    }

    // ─── Kontroller ──────────────────────────────────────────────────────

    private void HandleLook()
    {
        if (Mouse.current == null || spectatorCamera == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue();

        float sensitivity = MouseSensitivitySettings.Raw;
        targetYaw += delta.x * sensitivity;
        targetPitch = Mathf.Clamp(targetPitch - delta.y * sensitivity, minPitch, maxPitch);

        float t = 1f - Mathf.Exp(-lookSmoothing * Time.deltaTime);
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, t);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, t);

        spectatorCamera.transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
    }

    private void HandleMove()
    {
        if (Keyboard.current == null || spectatorCamera == null) return;

        // Fare tekerleği ile hız çarpanı — uzağı taramak ile bir arabayı
        // yakından takip etmek çok farklı hızlar istiyor.
        if (Mouse.current != null && scrollSpeedRange > 1f)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                speedScale = Mathf.Clamp(speedScale * (scroll > 0f ? 1.15f : 1f / 1.15f),
                                         1f / scrollSpeedRange, scrollSpeedRange);
            }
        }

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        float vertical = 0f;
        if (Keyboard.current.spaceKey.isPressed) vertical += 1f;
        if (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed) vertical -= 1f;

        bool fast = Keyboard.current.leftShiftKey.isPressed;
        float speed = (fast ? fastMoveSpeed : moveSpeed) * speedScale;

        Transform cam = spectatorCamera.transform;
        Vector3 target = (cam.right * input.x + cam.forward * input.y) * speed
                         + Vector3.up * (vertical * verticalSpeed * speedScale);

        currentVelocity = accelerationTime <= 0f
            ? target
            : Vector3.SmoothDamp(currentVelocity, target, ref velocitySmoothDamp, accelerationTime);

        Vector3 next = cam.position + currentVelocity * Time.deltaTime;
        next = ClampToBounds(next, out bool wasClamped);
        cam.position = next;

        if (wasClamped) NotifyBoundary();
    }

    // ─── Sınırlar ────────────────────────────────────────────────────────

    /// <summary>
    /// İzleyici alanını belirler. Yatayda pistin gerçek noktalarından
    /// (TrackGenerator.GetTrackPoints — minimap ve checkpoint kurtarma
    /// sisteminin de kullandığı aynı liste) bir merkez + yarıçap çıkarıyoruz.
    /// Dikeyde ise pistin ortalama yüksekliğini "yer seviyesi" kabul edip
    /// min/max yüksekliği ona göre ölçüyoruz — böylece pist Y=0'da olmasa
    /// bile sınırlar doğru yerde kalır.
    /// </summary>
    private void ResolveBounds()
    {
        if (boundsResolved) return;

        boundsCenter = Vector3.zero;
        boundsRadius = manualHorizontalRadius;
        float groundY = 0f;

        if (autoBoundsFromTrack)
        {
            TrackGenerator generator = FindAnyObjectByType<TrackGenerator>();
            List<Vector3> points = generator != null ? generator.GetTrackPoints() : null;

            if (points != null && points.Count > 0)
            {
                Vector3 min = points[0];
                Vector3 max = points[0];
                foreach (Vector3 p in points)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }

                boundsCenter = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
                groundY = (min.y + max.y) * 0.5f;

                float farthest = 0f;
                foreach (Vector3 p in points)
                {
                    float d = new Vector2(p.x - boundsCenter.x, p.z - boundsCenter.z).magnitude;
                    if (d > farthest) farthest = d;
                }

                boundsRadius = farthest + horizontalPadding;
            }
            else
            {
                Debug.LogWarning("[RacerSpectator] Pist noktaları okunamadı — izleyici sınırı için elle girilen yarıçap kullanılıyor.");
            }
        }

        boundsMinY = groundY + minHeight;
        boundsMaxY = groundY + Mathf.Max(maxHeight, minHeight + 1f);

        boundsResolved = true;
        Debug.Log($"[RacerSpectator] İzleyici sınırları: merkez={boundsCenter}, yarıçap={boundsRadius:F0}m, yükseklik={boundsMinY:F0}m..{boundsMaxY:F0}m");
    }

    private Vector3 ClampToBounds(Vector3 position, out bool clamped)
    {
        clamped = false;

        Vector2 planar = new Vector2(position.x - boundsCenter.x, position.z - boundsCenter.z);
        if (planar.sqrMagnitude > boundsRadius * boundsRadius)
        {
            planar = planar.normalized * boundsRadius;
            position.x = boundsCenter.x + planar.x;
            position.z = boundsCenter.z + planar.y;
            clamped = true;
        }

        float clampedY = Mathf.Clamp(position.y, boundsMinY, boundsMaxY);
        if (!Mathf.Approximately(clampedY, position.y))
        {
            position.y = clampedY;
            clamped = true;
        }

        return position;
    }

    /// <summary>
    /// Sınıra dayanınca oyuncu neden durduğunu anlamalı — ama her karede
    /// yazı basmak ekranı kilitler, bu yüzden seyrek (10 saniyede bir).
    /// </summary>
    private void NotifyBoundary()
    {
        if (Time.time - lastBoundaryNoticeTime < 10f) return;
        lastBoundaryNoticeTime = Time.time;
        ScreenNotice.Show(Loc.T("warn.spectatorbound"), 2f);
    }

    private static float NormalizePitch(float eulerX) => eulerX > 180f ? eulerX - 360f : eulerX;
}
