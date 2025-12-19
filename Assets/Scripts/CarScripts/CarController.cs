using UnityEngine;

public class CarController : MonoBehaviour
{
    #region 1. Referanslar (Diğer Nesneler)

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

    #region 2. Süspansiyon Ayarları (Yere Basma)

    [Header("Süspansiyon Ayarları")]
    [SerializeField] private float springStiffness = 30000f;
    [SerializeField] private float damperStiffness = 3000f; 
    [SerializeField] private float restLength = 0.75f; 
    [SerializeField] private float springTravel = 0.1f;
    [SerializeField] private float wheelRadius = 0.6f; 
    
    private int[] wheelsIsGrounded = new int[4]; 
    private bool isGrounded = false;

    #endregion

    #region 3. Girdi Değişkenleri

    private float moveInput = 0; 
    private float steerInput = 0; 
    private bool isHandbrakePressed = false; 

    #endregion

    #region 4. Araba Ayarları (Hareket Fiziği)

    [Header("Araba Ayarları")]
    [SerializeField] private float acceleration = 25f; 
    [SerializeField] private float maxSpeed = 300f; 
    [SerializeField] private float deceleration = 10f; 
    [SerializeField] private float steerStrength = 30f; 
    [SerializeField] private AnimationCurve turningCurve; 
    [SerializeField] private float dragCoefficent = 0.8f; // DÜŞÜRÜLDÜ
    [SerializeField] private float brakingDeceleration = 150f; 
    [SerializeField] private float brakingDragCoefficent = 1f;
   
    private Vector3 currentCarLocalVelocity = Vector3.zero; 
    private float carVelocityRatio = 0;
    public float currentSpeed = 0f;

    [Header("Durma Ayarları")]
    [SerializeField] private float stopThreshold = 0.5f; 
    [SerializeField] private float autoStopForce = 5f;
    [SerializeField] private float minSpeedForMovement = 0.1f;
    [SerializeField] private float lowSpeedDragMultiplier = 2f; // DÜŞÜRÜLDÜ
    [SerializeField] private float lowSpeedThreshold = 20f;

    #endregion

    #region 5. Görsel Efekt Ayarları

    [Header("Görsel Efektler")]
    [SerializeField] private float tireRotSpeed = 3000f;
    [SerializeField] private float maxSteeringAngle = 30f; 
    [SerializeField] private float minSideSkidVelocity = 8f;

    #endregion

    #region 6. Diğer Fizik Ayarları

    [Header("Hava Sürtünmesi Ayarları")]
    [SerializeField] private float airDrag = 0.1f; // DÜŞÜRÜLDÜ
    [SerializeField] private float rollingResistance = 0.5f; // ÇOK DÜŞÜRÜLDÜ

    #endregion

    #region 7. El Freni Ayarları (Drift) - ARCADE TARZI

    [Header("El Freni Ayarları - Arcade Drift")]
    [SerializeField] private float driftGripReduction = 0.6f; // Arka tekerleklerin tutuş kaybı (0-1)
    [SerializeField] private float driftSteerBoost = 1.5f; // Drift sırasında direksiyon hassasiyeti çarpanı
    [SerializeField] private float driftMinSpeed = 15f; // Minimum drift hızı (km/h)
    [SerializeField] private float driftTransitionSpeed = 5f; // Drift geçiş hızı
    [SerializeField] private float driftSpeedLoss = 0.85f; // Drift sırasında hız kaybı (1 = kayıp yok, 0 = tam kayıp)
    
    private float currentDriftFactor = 0f; // 0-1 arası drift yoğunluğu
    private bool isDrifting = false;

    #endregion

    #region 8. Buz Mekaniği

    [Header("Buz / Kaygan Zemin Ayarları")]
    [SerializeField] private string iceTag = "Ice"; 
    [SerializeField] private float iceSidewaysDrag = 0.05f; 
    [SerializeField] private float iceSteeringChaosMultiplier = 1.5f;
    [SerializeField] private float iceAccelerationGrip = 0.3f;

    private bool isCarOnIce = false;
    private bool externalIceTrigger = false;
    #endregion

    #region Unity'nin Ana Fonksiyonları

    private void Awake()
    {
        if (carRB == null)
        {
            carRB = GetComponent<Rigidbody>();
        }
    }

    private void FixedUpdate()
    {
        GroundCheck();
        CalculateCarVelocity();
        Visuals();
        Suspension();
        Movement(); // İlk önce hareket kuvvetleri uygulanır
        ApplyDragAndResistance(); // Sonra sürtünme
        CheckAndStop(); 
    }
    
    private void Update()
    {
        GetPlayerInput();
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
            ArcadeDrift(); // YENİ ARCADE DRİFT SİSTEMİ
            SidewaysDrag();
        }
    }
    
    private void Acceleration()
    {
        currentSpeed = Mathf.Abs(currentCarLocalVelocity.z) * 3.6f;
        
        // Hız limitleyici - sadece max speed'e çok yaklaşınca devreye girer
        float speedLimiter = 1f;
        if (currentSpeed >= maxSpeed * 0.98f) // 0.95'ten 0.98'e çıkarıldı
        {
            float overspeedRatio = (currentSpeed - maxSpeed * 0.98f) / (maxSpeed * 0.02f);
            speedLimiter = Mathf.Pow(1f - Mathf.Clamp01(overspeedRatio), 2f); // 3'ten 2'ye düşürüldü
        }
        
        if (currentSpeed >= maxSpeed)
        {
            speedLimiter = 0f;
        }

        float currentAcceleration = acceleration;
        if (isCarOnIce)
        {
            currentAcceleration *= iceAccelerationGrip;
        }
        
        // Gaz pedalına basılıyorsa ivmelendir
        if (Mathf.Abs(moveInput) > 0.01f)
        {
            float finalAcceleration = currentAcceleration * moveInput * speedLimiter;
            carRB.AddForceAtPosition(finalAcceleration * transform.forward, accelerationPoint.position, ForceMode.Acceleration);
        }
    }
    
    private void Decelration()
    {
        // Sadece şu durumlarda aktif frenleme yap:
        // 1. Gaz verilmiyor VE hız çok düşük
        // 2. Ters yöne gidiliyor
        
        bool isReversing = Mathf.Abs(moveInput) > 0.1f && Mathf.Sign(moveInput) != Mathf.Sign(carVelocityRatio);
        bool isLowSpeedNoInput = Mathf.Abs(moveInput) < 0.01f && currentSpeed < lowSpeedThreshold;
        
        if (isReversing)
        {
            // Ters yöne gidiliyorsa güçlü frenleme
            float decelPower = brakingDeceleration;
            if (isCarOnIce) decelPower *= 0.2f;
            
            Vector3 decelerationDirection = -transform.forward * Mathf.Sign(carVelocityRatio);
            carRB.AddForce(decelPower * Mathf.Abs(carVelocityRatio) * decelerationDirection, ForceMode.Acceleration);
        }
        else if (isLowSpeedNoInput)
        {
            // Düşük hızda gaz verilmiyorsa hafif frenleme
            float decelPower = isHandbrakePressed ? brakingDeceleration : deceleration * 0.5f; // Yarıya düşürüldü
            
            float lowSpeedBoost = 1f + (1f - currentSpeed / lowSpeedThreshold);
            decelPower *= lowSpeedBoost;

            if (isCarOnIce) decelPower *= 0.2f;
            
            Vector3 decelerationDirection = -transform.forward * Mathf.Sign(carVelocityRatio);
            carRB.AddForce(decelPower * Mathf.Abs(carVelocityRatio) * decelerationDirection, ForceMode.Acceleration);
        }
    }
    
    private void Turn()
    {
        // Sadece drift sırasında hafif direksiyon boost
        float steeringMultiplier = isDrifting ? driftSteerBoost : 1f;

        if (isCarOnIce)
        {
            steeringMultiplier *= iceSteeringChaosMultiplier;
        }

        carRB.AddRelativeTorque(steerStrength * steerInput * turningCurve.Evaluate(Mathf.Abs(carVelocityRatio)) *
         Mathf.Sign(carVelocityRatio) * steeringMultiplier * carRB.transform.up, ForceMode.Acceleration);
    }
    
    private void SidewaysDrag()
    {
        float currentSidewaySpeed = currentCarLocalVelocity.x;
        float dragCoefficient;

        if (isCarOnIce)
        {
            dragCoefficient = iceSidewaysDrag;
        }
        else if (isDrifting)
        {
            // Drift sırasında yan tutuş azalır - ama çok hafif
            dragCoefficient = dragCoefficent * 0.7f;
        }
        else if (Mathf.Abs(moveInput) < 0.1f || Mathf.Sign(moveInput) != Mathf.Sign(carVelocityRatio))
        {
            dragCoefficient = brakingDragCoefficent;
        }
        else
        {
            dragCoefficient = dragCoefficent;
        }
        
        float dragMagnitude = -currentSidewaySpeed * dragCoefficient;
        Vector3 dragForce = transform.right * dragMagnitude;

        carRB.AddForceAtPosition(dragForce, carRB.worldCenterOfMass, ForceMode.Acceleration);
    }
    
    private void ArcadeDrift()
    {
        // Drift şartı: Space basılı VE yeterli hız VAR
        bool wantsToDrift = isHandbrakePressed && currentSpeed > driftMinSpeed;
        
        // Drift faktörünü yumuşak geçişle güncelle
        float targetDrift = wantsToDrift ? 1f : 0f;
        currentDriftFactor = Mathf.Lerp(currentDriftFactor, targetDrift, Time.fixedDeltaTime * driftTransitionSpeed);
        
        isDrifting = currentDriftFactor > 0.05f;
        
        // Drift aktifse hafif hız kaybı uygula
        if (isDrifting && Mathf.Abs(moveInput) > 0.1f)
        {
            // Drift sırasında ileri momentum azalt (hız patlamasını önler)
            Vector3 forwardVelocity = transform.forward * currentCarLocalVelocity.z;
            Vector3 speedReduction = -forwardVelocity * (1f - driftSpeedLoss) * currentDriftFactor;
            carRB.AddForce(speedReduction, ForceMode.Acceleration);
        }
    }

    private void ApplyDragAndResistance()
    {
        if (isGrounded)
        {
            // Gaz veriliyorsa sürtünmeyi azalt
            float resistanceFactor = Mathf.Abs(moveInput) > 0.1f ? 0.2f : 1f;
            
            // Drift sırasında sürtünmeyi biraz artır (hız patlamasını önlemek için)
            if (isDrifting)
            {
                resistanceFactor += 0.3f * currentDriftFactor;
            }
            
            float currentRollingResistance = isCarOnIce ? rollingResistance * 0.2f : rollingResistance;
            float baseDrag = currentRollingResistance * resistanceFactor * Mathf.Abs(carVelocityRatio);
            
            // Düşük hızda VE gaz verilmiyorsa ek sürtünme
            if (currentSpeed < lowSpeedThreshold && Mathf.Abs(moveInput) < 0.01f)
            {
                float lowSpeedFactor = 1f - (currentSpeed / lowSpeedThreshold);
                float currentLowSpeedDrag = isCarOnIce ? lowSpeedDragMultiplier * 0.1f : lowSpeedDragMultiplier;
                baseDrag += currentLowSpeedDrag * lowSpeedFactor;
            }
            
            carRB.AddForce(-carRB.linearVelocity * baseDrag, ForceMode.Acceleration);
        }
        else
        {
            carRB.AddForce(-carRB.linearVelocity * airDrag, ForceMode.Acceleration);
        }
    }

    private void CheckAndStop()
    {
        if (!isGrounded) return;

        float currentStopThreshold = isCarOnIce ? stopThreshold * 0.2f : stopThreshold;

        if (currentSpeed < currentStopThreshold && Mathf.Abs(moveInput) < 0.1f)
        {
            if (carRB.linearVelocity.magnitude < minSpeedForMovement)
            {
                carRB.linearVelocity = Vector3.zero;
                carRB.angularVelocity = Vector3.zero;
            }
            else
            {
                float stopForce = isCarOnIce ? autoStopForce * 0.2f : autoStopForce;
                carRB.AddForce(-carRB.linearVelocity * stopForce, ForceMode.Acceleration);
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
        if(isCarOnIce && Mathf.Abs(moveInput) > 0.1f) effectiveTireRotSpeed *= 2f;

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
                                 (isDrifting && currentSpeed > 5f) || // Handbrake yerine isDrifting
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

    #region Süspansiyon Fonksiyonu

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

                // Drift sırasında arka tekerleklerin tutuşu azalır
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
}