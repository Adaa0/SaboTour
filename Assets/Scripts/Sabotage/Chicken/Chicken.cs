using UnityEngine;

/// <summary>
/// Tavuk AI - 0,0,0 noktasına koşar ve araba ile çarpışınca yok olur
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Chicken : MonoBehaviour
{
    [Header("Hareket Ayarları")]
    [SerializeField] private float maxMoveSpeed = 100f; // Spawn olunca başlangıç hızı
    [SerializeField] private float minMoveSpeed = 40f;  // Checkpoint'i geçerken minimum hız
    [SerializeField] private float rotationSpeed = 5f;
    [SerializeField] private float slowDownStartDistance = 150f; // Bu mesafeden önce yavaşlamaya başlar
    [SerializeField] private float slowDownEndDistance = 20f;   // Bu mesafede minimum hıza ulaşır
    [SerializeField] private float destroyDistance = 2f; // 0,0,0'a bu kadar yaklaşınca yok olsun
    [SerializeField] private float speedSmoothness = 5f; // Hız değişim yumuşaklığı (yüksek = daha hızlı tepki)

    [Header("Araba Çarpışması")]
    [SerializeField] private float carSpeedReduction = 0.4f; // %40 = 0.4
    [SerializeField] private string carTag = "Player";
    [SerializeField] private float destroyDelay = 0f; // Anında yok olsun

    [Header("Görsel")]
    [SerializeField] private GameObject visualModel;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = false;
    [SerializeField] private bool showDebugLogs = false; // Hız değişimini logla

    private Rigidbody rb;
    private Vector3 targetPosition = Vector3.zero; // 0,0,0
    private Transform nearestCheckpoint;
    private bool isMoving = true;
    private bool isDestroyed = false;
    private float currentSpeed; // Mevcut hız
    private float targetSpeed;  // Hedef hız

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        // Rigidbody ayarları
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        // Yol üzerindeki checkpoint'i bul
        FindCheckpointOnPath();

        // Başlangıç hızı maksimum
        currentSpeed = maxMoveSpeed;
        targetSpeed = maxMoveSpeed;
    }

    /// <summary>
    /// Tavuğun 0,0,0'a giderken geçeceği checkpoint'i bul
    /// </summary>
    void FindCheckpointOnPath()
    {
        CheckpointManager manager = FindAnyObjectByType<CheckpointManager>();
        
        if (manager == null || manager.checkpoints == null || manager.checkpoints.Count == 0)
        {
            Debug.LogWarning($"{gameObject.name} - CheckpointManager veya checkpoint'ler bulunamadı!");
            return;
        }

        Vector3 chickenPos = transform.position;
        Vector3 targetPos = Vector3.zero;
        Vector3 pathDirection = (targetPos - chickenPos).normalized;

        float bestDot = -1f; // En önde olan checkpoint'i bulmak için
        Transform bestCheckpoint = null;
        
        foreach (Transform checkpoint in manager.checkpoints)
        {
            if (checkpoint == null) continue;

            Vector3 toCheckpoint = (checkpoint.position - chickenPos).normalized;
            
            // Checkpoint tavuğun önünde mi? (dot > 0 ise önünde)
            float dot = Vector3.Dot(pathDirection, toCheckpoint);
            
            if (dot > 0.3f) // Önünde olan checkpoint'ler (30 derece açı toleransı)
            {
                // Yola ne kadar yakın?
                float distanceToPath = Vector3.Cross(pathDirection, toCheckpoint).magnitude;
                
                // Hem önde hem de yola yakın olanı seç
                // Dot değeri yüksek olana öncelik ver (daha önde olanı al)
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestCheckpoint = checkpoint;
                }
            }
        }

        nearestCheckpoint = bestCheckpoint;

        if (nearestCheckpoint != null)
        {
            Debug.Log($"{gameObject.name} - Yol üzerindeki checkpoint: {nearestCheckpoint.name}");
        }
        else
        {
            Debug.LogWarning($"{gameObject.name} - Yol üzerinde checkpoint bulunamadı!");
        }
    }

    void Update()
    {
        if (isDestroyed || !isMoving) return;

        CalculateTargetSpeed();
        SmoothSpeedTransition();
        MoveTowardsTarget();
        RotateTowardsTarget();
        CheckIfReachedTarget();
    }

    /// <summary>
    /// Hedef hızı yumuşak bir şekilde mevcut hıza uygula
    /// </summary>
    void SmoothSpeedTransition()
    {
        // Lerp ile yumuşak geçiş (speedSmoothness ne kadar düşükse o kadar yumuşak)
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * speedSmoothness);
    }

    /// <summary>
    /// Checkpoint'e olan mesafeye göre hedef hızı hesapla
    /// Checkpoint'e yaklaşırken yavaşla, geçerken minimum hızda ol
    /// </summary>
    void CalculateTargetSpeed()
    {
        if (nearestCheckpoint == null)
        {
            targetSpeed = maxMoveSpeed;
            
            if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                Debug.LogWarning($"{gameObject.name}: Checkpoint bulunamadı!");
            }
            return;
        }

        // Checkpoint'e olan mesafe
        float distanceToCheckpoint = Vector3.Distance(transform.position, nearestCheckpoint.position);

        // Yavaşlama başlangıç mesafesinden uzaktaysa maksimum hız
        if (distanceToCheckpoint >= slowDownStartDistance)
        {
            targetSpeed = maxMoveSpeed;
        }
        // Yavaşlama bitiş mesafesinden yakınsa minimum hız
        else if (distanceToCheckpoint <= slowDownEndDistance)
        {
            targetSpeed = minMoveSpeed;
        }
        // Arada bir yerdeyse yumuşak geçiş (Smoothstep için)
        else
        {
            // slowDownStartDistance ve slowDownEndDistance arasında interpolasyon
            float t = (distanceToCheckpoint - slowDownEndDistance) / (slowDownStartDistance - slowDownEndDistance);
            
            // Smoothstep ile daha yumuşak geçiş (normal Lerp yerine)
            t = t * t * (3f - 2f * t); // Smoothstep formula
            
            targetSpeed = Mathf.Lerp(minMoveSpeed, maxMoveSpeed, t);
        }

        // Debug log (her saniyede bir)
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"{gameObject.name}: Mesafe={distanceToCheckpoint:F1}m | TargetSpeed={targetSpeed:F1} | CurrentSpeed={currentSpeed:F1} | Checkpoint={nearestCheckpoint.name}");
        }
    }

    /// <summary>
    /// 0,0,0 noktasına doğru hareket et
    /// </summary>
    void MoveTowardsTarget()
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0; // Sadece yatay hareket

        // Rigidbody ile hareket (fizik tabanlı)
        if (rb != null)
        {
            Vector3 moveVelocity = direction * currentSpeed;
            moveVelocity.y = rb.linearVelocity.y; // Y hızını koru (yerçekimi için)
            rb.linearVelocity = moveVelocity;
        }
        else
        {
            // Rigidbody yoksa transform ile hareket
            transform.position += direction * currentSpeed * Time.deltaTime;
        }
    }

    /// <summary>
    /// Hareket yönüne doğru dön
    /// </summary>
    void RotateTowardsTarget()
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// 0,0,0'a ulaştı mı kontrol et - ulaştıysa yok ol
    /// </summary>
    void CheckIfReachedTarget()
    {
        float distance = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), targetPosition);

        if (distance <= destroyDistance)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Araba ile çarpışma
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        if (isDestroyed) return;

        // Araba mı çarptı?
        GameObject hitObject = collision.gameObject;
        
        // Root object'i kontrol et (araba child object'lerden biri de çarpabilir)
        if (hitObject.CompareTag(carTag) || hitObject.transform.root.CompareTag(carTag))
        {
            OnHitByCar(hitObject);
        }
    }

    /// <summary>
    /// Araba çarpınca çağrılır
    /// </summary>
    void OnHitByCar(GameObject car)
    {
        if (isDestroyed) return;
        isDestroyed = true;

        // Arabayı yavaşlat
        SlowDownCar(car);

        // Tavuğu yok et
        if (destroyDelay > 0)
        {
            Destroy(gameObject, destroyDelay);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Arabayı yavaşlat
    /// </summary>
    void SlowDownCar(GameObject car)
    {
        // Root object'ten CarController'ı bul
        CarController carController = car.GetComponentInParent<CarController>();
        
        if (carController == null)
        {
            carController = car.GetComponent<CarController>();
        }

        if (carController == null)
        {
            carController = car.transform.root.GetComponent<CarController>();
        }

        if (carController != null)
        {
            // Rigidbody'yi yavaşlat
            Rigidbody carRB = car.GetComponentInParent<Rigidbody>();
            if (carRB == null)
            {
                carRB = car.transform.root.GetComponent<Rigidbody>();
            }

            if (carRB != null)
            {
                carRB.linearVelocity *= (1f - carSpeedReduction);
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // Tavuk konumu
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);

        // Hedefe çizgi
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, targetPosition);

        // 0,0,0'a ulaşma mesafesi
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(targetPosition, destroyDistance);

        // En yakın checkpoint ve yavaşlama bölgesi
        if (nearestCheckpoint != null)
        {
            // Checkpoint'e çizgi
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, nearestCheckpoint.position);

            // Yavaşlama başlangıç bölgesi
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // Sarı yarı saydam
            Gizmos.DrawWireSphere(nearestCheckpoint.position, slowDownStartDistance);

            // Yavaşlama bitiş bölgesi (minimum hız)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f); // Turuncu daha koyu
            Gizmos.DrawWireSphere(nearestCheckpoint.position, slowDownEndDistance);
        }
    }
}