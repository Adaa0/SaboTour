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
        if (isOwned) return; // owner zaten kendi hesapladığı pozisyonu kullanıyor
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

    #region Unity Ana Fonksiyonları

    private void Awake()
    {
        if (carRB == null)
            carRB = GetComponent<Rigidbody>();
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
        if (!isOwned) return;

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

        // GEÇİCİ TEŞHİS LOGU — sorunun tam olarak nerede koptuğunu görmek için.
        // Sadece belirgin bir şekilde dönerken/driftteyken loglanıyor (spam olmasın).
        if (Mathf.Abs(steerInput) > 0.3f || isDrifting)
        {
            Debug.Log($"[CarController-DEBUG][YAZAN] netId={netId} isServer={isServer} isClient={isClient} " +
                      $"steerInput={steerInput:F2}→netSteerInput={netSteerInput:F2} isDrifting={isDrifting}");
        }
    }

    private void Update()
    {
        if (isOwned)
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

            // GEÇİCİ TEŞHİS LOGU — bu makine (host ya da başka client) bu
            // arabanın senkronize verisini okurken ne görüyor.
            if (Mathf.Abs(netSteerInput) > 0.3f || netIsDrifting)
            {
                Debug.Log($"[CarController-DEBUG][OKUYAN] netId={netId} isServer={isServer} isClient={isClient} " +
                          $"netSteerInput={netSteerInput:F2}→steerInput={steerInput:F2} netIsDrifting={netIsDrifting}");
            }
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
        float skidThreshold = isCarOnIce ? 2f : minSideSkidVelocity;
        bool shouldShowEffects = isGrounded &&
                                 (Mathf.Abs(currentCarLocalVelocity.x) > skidThreshold ||
                                 (isDrifting && currentSpeed > 5f) ||
                                 (isCarOnIce && Mathf.Abs(steerInput) > 0.5f)) &&
                                 carVelocityRatio > 0;

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
        foreach (var smoke in skidSmokes)
        {
            if (smoke != null)
            {
                if (toggle) { if (!smoke.isPlaying) smoke.Play(); }
                else { if (smoke.isPlaying) smoke.Stop(); }
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