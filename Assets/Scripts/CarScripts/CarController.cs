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

    [Header("Süspansiyon Ayarları - Arabanın yere temasını ve zıplamasını ayarlar")]

    [SerializeField] private float springStiffness = 30000f;
    [SerializeField] private float damperStiffness = 3000f; 
    [SerializeField] private float restLength = 0.75f; 
    [SerializeField] private float springTravel = 0.1f;
    [SerializeField] private float wheelRadius = 0.6f; 
    
    private int[] wheelsIsGrounded = new int[4]; 
    private bool isGrounded = false;

    #endregion

    #region 3. Girdi Değişkenleri (Oyuncu Kontrolü)

    private float moveInput = 0; 
    private float steerInput = 0; 
    private bool isHandbrakePressed = false; // Cache için

    #endregion

    #region 4. Araba Ayarları (Hareket Fiziği)

    [Header("Araba Ayarları")]
    [SerializeField] private float acceleration = 25f; 
    [SerializeField] private float maxSpeed = 300f; 
    [SerializeField] private float deceleration = 10f; 
    [SerializeField] private float steerStrength = 45f; 
    [SerializeField] private AnimationCurve turningCurve; 
    [SerializeField] private float dragCoefficent = 2f; 
    [SerializeField] private float brakingDeceleration = 150f; 
    [SerializeField] private float brakingDragCoefficent = 1f;

    [Header("Motor Tork Ayarları")]
    [SerializeField] private AnimationCurve motorTorqueCurve;
    [SerializeField] private float torqueMultiplier = 1.5f;
   
    private Vector3 currentCarLocalVelocity = Vector3.zero; 
    private float carVelocityRatio = 0;
    public float currentSpeed = 0f;

    [Header("Durma Ayarları")]
    [SerializeField] private float stopThreshold = 0.5f; // Bu hızın altında araba tamamen durur
    [SerializeField] private float autoStopForce = 5f; // Otomatik durma kuvveti
    [SerializeField] private float minSpeedForMovement = 0.1f; // Minimum hareket hızı
    [SerializeField] private float lowSpeedDragMultiplier = 3f; // Düşük hızda ekstra yavaşlama çarpanı
    [SerializeField] private float lowSpeedThreshold = 20f; // Bu hızın altında ekstra yavaşlama aktif (km/h)

    #endregion

    #region 5. Görsel Efekt Ayarları

    [Header("Görsel Efektler")]
   
    [SerializeField] private float tireRotSpeed = 3000f;
    [SerializeField] private float maxSteeringAngle = 30f; 
    [SerializeField] private float minSideSkidVelocity = 8f;

    #endregion

    #region 6. Diğer Fizik Ayarları

    [Header("Hava Sürtünmesi Ayarları")]
    
    [SerializeField] private float airDrag = 0.2f; 
    [SerializeField] private float rollingResistance = 3f; 

    #endregion

    #region 7. El Freni Ayarları (Drift)

    [Header("El Freni Ayarları")]

    [SerializeField] private float handbrakeIntensity = 0.3f; 
    [SerializeField] private float handbrakeSidewaysDragReduction = 0.3f; 
    [SerializeField] private float handbrakeGripReduction = 0.3f; 
    [SerializeField] private float handbrakeMaxSlideSpeed = 50f; 
    [SerializeField] private float handbrakeDriftAssist = 1.2f; 
    [SerializeField] private float handbrakeSpeedMultiplier = 1.5f; 
    [SerializeField] private float handbrakeRecoverySpeed = 5f; 
    [SerializeField] private float handbrakeMinSpeed = 5f; 

   
    private float currentHandbrakeEffect = 0f; 
    private Vector3 handbrakeSlideDirection = Vector3.zero; 

    #endregion

    #region 8. BUZ MEKANİĞİ (GÜNCELLENDİ)
    
    [Header("Buz / Kaygan Zemin Ayarları")]
    [Tooltip("Buz zemini olarak algılanacak Tag adı")]
    [SerializeField] private string iceTag = "Ice"; 
    
    [Tooltip("Buzdayken yan sürtünme ne kadar düşecek? (Düşük değer = Sabun gibi kayma)")]
    [SerializeField] private float iceSidewaysDrag = 0.05f; 
    
    [Tooltip("Buzdayken direksiyon hakimiyeti ne kadar bozulacak? (1 = Normal, 2 = Aşırı Dönüş)")]
    [SerializeField] private float iceSteeringChaosMultiplier = 1.5f;

    [Tooltip("Buzdayken hızlanma ne kadar zorlaşacak? (Patinaj etkisi)")]
    [SerializeField] private float iceAccelerationGrip = 0.3f;

    private bool isCarOnIce = false;
    private bool externalIceTrigger = false; // IceSlide.cs tarafından tetiklenen değişken

    #endregion

    #region Unity'nin Ana Fonksiyonları

    private void Awake()
    {
        if (carRB == null)
        {
            carRB = GetComponent<Rigidbody>();
        }
        
        if (motorTorqueCurve == null || motorTorqueCurve.keys.Length == 0)
        {
            motorTorqueCurve = AnimationCurve.EaseInOut(0f, 1.5f, 1f, 0.3f);
        }
    }

    private void FixedUpdate()
    {
        GroundCheck();
        CalculateCarVelocity();
        Visuals();
        Suspension();
        Movement();
        ApplyDragAndResistance();
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
            SidewaysDrag();
            ApplyHandbrakeEffect();
        }
    }
    
    private void Acceleration()
    {
        currentSpeed = Mathf.Abs(currentCarLocalVelocity.z) * 3.6f;
        
        float speedRatio = currentSpeed / maxSpeed;
        float torqueFromCurve = motorTorqueCurve.Evaluate(speedRatio);
        
        float speedLimiter = 1f;
        if (currentSpeed >= maxSpeed * 0.95f)
        {
            float overspeedRatio = (currentSpeed - maxSpeed * 0.95f) / (maxSpeed * 0.05f);
            speedLimiter = Mathf.Pow(1f - Mathf.Clamp01(overspeedRatio), 3f);
        }
        
        if (currentSpeed >= maxSpeed)
        {
            speedLimiter = 0f;
        }

        // BUZ ETKİSİ: Eğer buzdaysak hızlanma kuvveti (grip) azalır, patinaj hissi verir
        float currentAcceleration = acceleration;
        if (isCarOnIce)
        {
            currentAcceleration *= iceAccelerationGrip;
        }
        
        float finalAcceleration = currentAcceleration * moveInput * torqueFromCurve * torqueMultiplier * speedLimiter;
        
        carRB.AddForceAtPosition(finalAcceleration * transform.forward, accelerationPoint.position, ForceMode.Acceleration);
    }
    
    private void Decelration()
    {
        if (Mathf.Abs(moveInput) < 0.1f || Mathf.Sign(moveInput) != Mathf.Sign(carVelocityRatio))
        {
            float decelPower = isHandbrakePressed ? brakingDeceleration : deceleration;
            
            // Düşük hızlarda yavaşlamayı artır
            if (currentSpeed < lowSpeedThreshold)
            {
                float lowSpeedBoost = 1f + (1f - currentSpeed / lowSpeedThreshold) * 2f;
                decelPower *= lowSpeedBoost;
            }

            // BUZ ETKİSİ: Buzdayken frenler çok daha az tutar
            if (isCarOnIce)
            {
                decelPower *= 0.2f; // Fren gücünü %80 azalt
            }
            
            Vector3 decelerationDirection = -transform.forward * Mathf.Sign(carVelocityRatio);
            carRB.AddForce(decelPower * Mathf.Abs(carVelocityRatio) * decelerationDirection, ForceMode.Acceleration);
        }
    }
    
    private void Turn()
    {
        float steeringMultiplier = 1f;
        
        if (isHandbrakePressed)
        {
            steeringMultiplier = 1f + (currentHandbrakeEffect * 0.5f);
        }

        // BUZ ETKİSİ: Buzdayken araba dönmeye çalışınca arkası daha kaotik savrulur
        if (isCarOnIce)
        {
            // Dönüş torkunu artırıyoruz (oversteer) ama kontrolsüz hissettiriyoruz
            steeringMultiplier *= iceSteeringChaosMultiplier;
        }

        carRB.AddRelativeTorque(steerStrength * steerInput * turningCurve.Evaluate(Mathf.Abs(carVelocityRatio)) *
         Mathf.Sign(carVelocityRatio) * steeringMultiplier * carRB.transform.up, ForceMode.Acceleration);
    }
    
    private void SidewaysDrag()
    {
        float currentSidewaySpeed = currentCarLocalVelocity.x;
        
        float dragCoefficient;

        // --- BUZ KONTROLÜ VE MANTIĞI ---
        if (isCarOnIce)
        {
            // Buzdaysak sürtünme dramatik şekilde düşer (sabun etkisi)
            dragCoefficient = iceSidewaysDrag;

            // El freni çekiliyse buzda sürtünme neredeyse sıfır olur
            if (isHandbrakePressed) dragCoefficient *= 0.5f;
        }
        else if (isHandbrakePressed)
        {
            // El freni: daha düşük drag = daha fazla kayma
            dragCoefficient = brakingDragCoefficent * handbrakeSidewaysDragReduction;
        }
        else if (Mathf.Abs(moveInput) < 0.1f || Mathf.Sign(moveInput) != Mathf.Sign(carVelocityRatio))
        {
            // Normal fren: standart drag
            dragCoefficient = brakingDragCoefficent;
        }
        else
        {
            // Normal sürüş: normal drag
            dragCoefficient = dragCoefficent;
        }
        
        float dragMagnitude = -currentSidewaySpeed * dragCoefficient;
        Vector3 dragForce = transform.right * dragMagnitude;

        carRB.AddForceAtPosition(dragForce, carRB.worldCenterOfMass, ForceMode.Acceleration);
    }
    
    private void ApplyHandbrakeEffect()
    {
        if (isHandbrakePressed && currentSpeed > handbrakeMinSpeed) 
        {
            float speedFactor = Mathf.Clamp01(Mathf.Abs(currentCarLocalVelocity.z) / maxSpeed);
            float intensity = handbrakeIntensity * speedFactor * handbrakeSpeedMultiplier;
            
            // Yandan kayma kuvvetini azalt (bu spin atmasını kolaylaştırır)
            Vector3 reducedSidewaysDrag = -currentCarLocalVelocity.x * handbrakeSidewaysDragReduction * transform.right;
            carRB.AddForce(reducedSidewaysDrag, ForceMode.Acceleration);

            // Kayma yönünü belirle
            if (handbrakeSlideDirection == Vector3.zero)
            {
                handbrakeSlideDirection = transform.right * Mathf.Sign(currentCarLocalVelocity.x);
            }
            else
            {
                handbrakeSlideDirection = Vector3.Lerp(handbrakeSlideDirection,
                    transform.right * Mathf.Sign(currentCarLocalVelocity.x), Time.fixedDeltaTime * 5f);
            }
            
            // Kayma kuvvetini uygula
            Vector3 handbrakeSlideForce = handbrakeSlideDirection * intensity * 50f;
            float currentSlideSpeed = Vector3.Dot(carRB.linearVelocity, handbrakeSlideDirection);

            if (Mathf.Abs(currentSlideSpeed) < handbrakeMaxSlideSpeed)
            {
                Vector3 rearWheelPosition = transform.position - transform.forward * 2f;
                carRB.AddForceAtPosition(handbrakeSlideForce, rearWheelPosition, ForceMode.Acceleration);
            }

            // Direksiyon varsa spin için tork ekle (artırıldı)
            if (steerInput != 0)
            {
                // Buzdaysa drift asisti daha da çılgın olur
                float assistMultiplier = isCarOnIce ? 2.5f : 1.0f;
                float driftTorque = steerInput * handbrakeDriftAssist * assistMultiplier * intensity * 100f;
                carRB.AddTorque(transform.up * driftTorque, ForceMode.Acceleration);
            }

            currentHandbrakeEffect = Mathf.Lerp(currentHandbrakeEffect, 1f, Time.fixedDeltaTime * 10f);
        }
        else
        {
            // Buzdaysa el freni etkisi daha yavaş kaybolur (kontrolü geri almak zordur)
            float recovery = isCarOnIce ? handbrakeRecoverySpeed * 0.3f : handbrakeRecoverySpeed;
            
            currentHandbrakeEffect = Mathf.Lerp(currentHandbrakeEffect, 0f, Time.fixedDeltaTime * recovery);
            handbrakeSlideDirection = Vector3.Lerp(handbrakeSlideDirection, Vector3.zero, Time.fixedDeltaTime * 3f);
        }
    }

    private void ApplyDragAndResistance()
    {
        if (isGrounded)
        {
            // Buzdaysa yuvarlanma direnci (sürtünme) çok azalır
            float currentRollingResistance = isCarOnIce ? rollingResistance * 0.2f : rollingResistance;

            float baseDrag = currentRollingResistance * Mathf.Abs(carVelocityRatio);
            
            // Düşük hızlarda ekstra yavaşlama (Buzda bu da azalır)
            if (currentSpeed < lowSpeedThreshold && Mathf.Abs(moveInput) < 0.1f)
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

    // YENİ FONKSİYON - Otomatik Durma
    private void CheckAndStop()
    {
        if (!isGrounded) return;

        // Buzdaysa durma eşiği daha düşük olur (kaymaya devam eder)
        float currentStopThreshold = isCarOnIce ? stopThreshold * 0.2f : stopThreshold;

        // Hız çok düşükse ve input yoksa
        if (currentSpeed < currentStopThreshold && Mathf.Abs(moveInput) < 0.1f)
        {
            // Hızı tamamen sıfırla
            if (carRB.linearVelocity.magnitude < minSpeedForMovement)
            {
                carRB.linearVelocity = Vector3.zero;
                carRB.angularVelocity = Vector3.zero;
            }
            else
            {
                // Yavaşça durdur
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
        
        // Buzda patinaj atıyorsa tekerlekler daha hızlı döner görsel olarak
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
        // Buz üzerinde duman çıkmaz veya çok az çıkar, ama kayma izi mantığı burada:
        // Buzda sürekli kaydığımız için eşiği düşürdük
        float skidThreshold = isCarOnIce ? 2f : minSideSkidVelocity;

        bool shouldShowEffects = isGrounded && 
                                 (Mathf.Abs(currentCarLocalVelocity.x) > skidThreshold || 
                                 (isHandbrakePressed && currentSpeed > 5f) ||
                                 (isCarOnIce && Mathf.Abs(steerInput) > 0.5f)) && // Buzda dönerken efekt ver
                                 carVelocityRatio > 0;
        
        ToggleSkidMarks(shouldShowEffects);
        ToggleSkidSmokes(shouldShowEffects);
    }

    private void ToggleSkidMarks(bool toggle)
    {
        foreach (var skidMark in skidMarks)
        {
            if (skidMark != null)
                skidMark.emitting = toggle;
        }
    }

    private void ToggleSkidSmokes(bool toggle)
    {
        foreach (var smoke in skidSmokes)
        {
            if (smoke != null)
            {
                if (toggle)
                {
                    if (!smoke.isPlaying)
                        smoke.Play();
                }
                else
                {
                    if (smoke.isPlaying)
                        smoke.Stop();
                }
            }
        }
    }

    private void SetTirePosition(GameObject tire, Vector3 targetPosition)
    {
        if (tire != null)
            tire.transform.position = targetPosition;
    }
    
    #endregion

    #region Araba Durum Kontrolleri

    private void GroundCheck()
    {
        int tempGroundedWheels = 0;

        for (int i = 0; i < wheelsIsGrounded.Length; i++)
        {
            tempGroundedWheels += wheelsIsGrounded[i];
        }

        if (tempGroundedWheels > 1)
        {
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
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
        int wheelsOnIceCount = 0; // Kaç tekerlek buzda sayacı

        for (int i = 0; i < rayPoints.Length; i++)
        {
            RaycastHit hit;
            float maxLength = restLength + springTravel;

            if (Physics.Raycast(rayPoints[i].position, -rayPoints[i].up, out hit, maxLength + wheelRadius, drivable))
            {
                wheelsIsGrounded[i] = 1;

                // --- BUZ TESPİTİ (TAG) ---
                if (hit.collider.CompareTag(iceTag))
                {
                    wheelsOnIceCount++;
                }

                float currentSpringLenght = hit.distance - wheelRadius;
                float springCompression = (restLength - currentSpringLenght) / springTravel;
                float springVelocity = Vector3.Dot(carRB.GetPointVelocity(rayPoints[i].position), rayPoints[i].up);
                float dampForce = damperStiffness * springVelocity;

                // El freni sadece arka tekerlekleri etkiler (i >= 2)
                float gripReduction = isHandbrakePressed && i >= 2 ?
                    Mathf.Lerp(1f, handbrakeGripReduction, currentHandbrakeEffect) : 1f;

                float currentSpringStiffness = springStiffness * gripReduction;
                float springForce = currentSpringStiffness * springCompression;

                float netForce = springForce - dampForce;

                carRB.AddForceAtPosition(netForce * rayPoints[i].up, rayPoints[i].position);

                SetTirePosition(tires[i], hit.point + rayPoints[i].up * wheelRadius);

                Debug.DrawLine(rayPoints[i].position, hit.point, Color.green);
            }
            else
            {
                wheelsIsGrounded[i] = 0;
                SetTirePosition(tires[i], rayPoints[i].position - rayPoints[i].up * maxLength);
                Debug.DrawLine(rayPoints[i].position, rayPoints[i].position + (wheelRadius + maxLength) * -rayPoints[i].up, Color.red);
            }
        }

        // Eğer en az 1 tekerlek buzdaysa VEYA dışarıdan (IceSlide.cs) buz tetiklendiyse
        isCarOnIce = (wheelsOnIceCount > 0) || externalIceTrigger;
    }
    
    #endregion
}