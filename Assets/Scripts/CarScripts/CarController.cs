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
    [SerializeField] private AnimationCurve motorTorqueCurve; // X: 0-1 (hız oranı), Y: tork çarpanı (örn: 0.2 hızda 1.5x güç)
    [SerializeField] private float torqueMultiplier = 1.5f; // Motor tork çarpanı
   
    private Vector3 currentCarLocalVelocity = Vector3.zero; 
    private float carVelocityRatio = 0;
    public float currentSpeed = 0f; // km/h cinsinden gerçek hız

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
    [SerializeField] private float handbrakeSidewaysDragReduction = 0.1f; 
    [SerializeField] private float handbrakeGripReduction = 0.5f; 
    [SerializeField] private float handbrakeMaxSlideSpeed = 30f; 
    [SerializeField] private float handbrakeDriftAssist = 0.8f; 
    [SerializeField] private float handbrakeSpeedMultiplier = 2f; 
    [SerializeField] private float handbrakeRecoverySpeed = 5f; 

   
    private float currentHandbrakeEffect = 0f; 
    private Vector3 handbrakeSlideDirection = Vector3.zero; 

    #endregion

    #region Unity'nin Ana Fonksiyonları

    private void Awake()
    {
        if (carRB == null)
        {
            carRB = GetComponent<Rigidbody>();
        }
        
        // Eğer motor tork eğrisi atanmamışsa varsayılan bir eğri oluştur
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
        // Gerçek hızı hesapla (km/h)
        currentSpeed = Mathf.Abs(currentCarLocalVelocity.z) * 3.6f;
        
        // Hız oranını hesapla (0-1 arası)
        float speedRatio = currentSpeed / maxSpeed;
        
        // Motor tork eğrisinden güç çarpanını al
        // Düşük hızda yüksek tork, yüksek hızda düşük tork
        float torqueFromCurve = motorTorqueCurve.Evaluate(speedRatio);
        
        // Max speed sınırlaması - hıza yaklaştıkça güç exponansiyel azalır
        float speedLimiter = 1f;
        if (currentSpeed >= maxSpeed * 0.95f)
        {
            // Max speed'in %95'inden sonra güç hızla düşer
            float overspeedRatio = (currentSpeed - maxSpeed * 0.95f) / (maxSpeed * 0.05f);
            speedLimiter = Mathf.Pow(1f - Mathf.Clamp01(overspeedRatio), 3f);
        }
        
        // Eğer max speed'i aştıysak hiç güç uygulama
        if (currentSpeed >= maxSpeed)
        {
            speedLimiter = 0f;
        }
        
        // Final güç hesaplaması: base acceleration * input * tork eğrisi * tork çarpanı * hız limiti
        float finalAcceleration = acceleration * moveInput * torqueFromCurve * torqueMultiplier * speedLimiter;
        
        carRB.AddForceAtPosition(finalAcceleration * transform.forward, accelerationPoint.position, ForceMode.Acceleration);
    }
    
    private void Decelration()
    {
        if (Mathf.Abs(moveInput) < 0.1f || Mathf.Sign(moveInput) != Mathf.Sign(carVelocityRatio))
        {
            float decelPower = Input.GetKey(KeyCode.Space) ? brakingDeceleration : deceleration;
            Vector3 decelerationDirection = -transform.forward * Mathf.Sign(carVelocityRatio);
            carRB.AddForce(decelPower * Mathf.Abs(carVelocityRatio) * decelerationDirection, ForceMode.Acceleration);
        }
    }
    
    private void Turn()
    {
        float steeringMultiplier = 1f;
        if (Input.GetKey(KeyCode.Space))
        {
            steeringMultiplier = 1f + (currentHandbrakeEffect * 0.5f);
        }
        carRB.AddRelativeTorque(steerStrength * steerInput * turningCurve.Evaluate(Mathf.Abs(carVelocityRatio)) *
         Mathf.Sign(carVelocityRatio) * steeringMultiplier * carRB.transform.up, ForceMode.Acceleration);
    }
    
    private void SidewaysDrag()
    {
        float currentSidewaySpeed = currentCarLocalVelocity.x;
        float dragCoefficient = Input.GetKey(KeyCode.Space) ?
        brakingDragCoefficent * handbrakeSidewaysDragReduction * currentHandbrakeEffect :
         dragCoefficent;
        float dragMagnitude = -currentSidewaySpeed * dragCoefficient;
        Vector3 dragForce = transform.right * dragMagnitude;

        carRB.AddForceAtPosition(dragForce, carRB.worldCenterOfMass, ForceMode.Acceleration);
    }
    
    private void ApplyHandbrakeEffect()
    {
        if (Input.GetKey(KeyCode.Space))
        {
            float speedFactor = Mathf.Clamp01(Mathf.Abs(currentCarLocalVelocity.z) / maxSpeed);
            float intensity = handbrakeIntensity * speedFactor * handbrakeSpeedMultiplier;
            Vector3 reducedSidewaysDrag = -currentCarLocalVelocity.x * handbrakeSidewaysDragReduction * transform.right;
            carRB.AddForce(reducedSidewaysDrag, ForceMode.Acceleration);

            if (handbrakeSlideDirection == Vector3.zero)
            {
                handbrakeSlideDirection = transform.right * Mathf.Sign(currentCarLocalVelocity.x);
            }
            else
            {
                handbrakeSlideDirection = Vector3.Lerp(handbrakeSlideDirection,
                    transform.right * Mathf.Sign(currentCarLocalVelocity.x), Time.fixedDeltaTime * 5f);
            }
            Vector3 handbrakeSlideForce = handbrakeSlideDirection * intensity * 50f;
            float currentSlideSpeed = Vector3.Dot(carRB.linearVelocity, handbrakeSlideDirection);

            if (Mathf.Abs(currentSlideSpeed) < handbrakeMaxSlideSpeed)
            {
                Vector3 rearWheelPosition = transform.position - transform.forward * 2f;
                carRB.AddForceAtPosition(handbrakeSlideForce, rearWheelPosition, ForceMode.Acceleration);
            }

            if (steerInput != 0)
            {
                float driftTorque = steerInput * handbrakeDriftAssist * intensity * 100f;
                carRB.AddTorque(transform.up * driftTorque, ForceMode.Acceleration);
            }

            currentHandbrakeEffect = Mathf.Lerp(currentHandbrakeEffect, 1f, Time.fixedDeltaTime * 10f);
        }
        else
        {
            currentHandbrakeEffect = Mathf.Lerp(currentHandbrakeEffect, 0f, Time.fixedDeltaTime * handbrakeRecoverySpeed);
            handbrakeSlideDirection = Vector3.Lerp(handbrakeSlideDirection, Vector3.zero, Time.fixedDeltaTime * 3f);
        }
    }

    private void ApplyDragAndResistance()
    {
        if (isGrounded)
        {
            carRB.AddForce(-carRB.linearVelocity * rollingResistance * Mathf.Abs(carVelocityRatio), ForceMode.Acceleration);
        }
        else
        {
            carRB.AddForce(-carRB.linearVelocity * airDrag, ForceMode.Acceleration);
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
        bool shouldShowEffects = isGrounded && Mathf.Abs(currentCarLocalVelocity.x) > minSideSkidVelocity && carVelocityRatio > 0;
        ToggleSkidMarks(shouldShowEffects);
        ToggleSkidSmokes(shouldShowEffects);
    }

    private void ToggleSkidMarks(bool toggle)
    {
        foreach (var skidMark in skidMarks)
        {
            skidMark.emitting = toggle;
        }
    }

    private void ToggleSkidSmokes(bool toggle)
    {
        foreach (var smoke in skidSmokes)
        {
            if (toggle)
            {
                smoke.Play();
            }
            else
            {
                smoke.Stop();
            }
        }
    }

    private void SetTirePosition(GameObject tire, Vector3 targetPosition)
    {
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
        for (int i = 0; i < rayPoints.Length; i++)
        {
            RaycastHit hit;
            float maxLength = restLength + springTravel;

            if (Physics.Raycast(rayPoints[i].position, -rayPoints[i].up, out hit, maxLength + wheelRadius, drivable))
            {
                wheelsIsGrounded[i] = 1;

                float currentSpringLenght = hit.distance - wheelRadius;

                float springCompression = (restLength - currentSpringLenght) / springTravel;

                float springVelocity = Vector3.Dot(carRB.GetPointVelocity(rayPoints[i].position), rayPoints[i].up);

                float dampForce = damperStiffness * springVelocity;

                float gripReduction = Input.GetKey(KeyCode.Space) && i >= 2 ?
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
    }
    
    #endregion
}