using UnityEngine;

/// <summary>
/// Tavuk AI - 0,0,0 noktasına koşar ve araba ile çarpışınca yok olur
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Chicken : MonoBehaviour
{
    [Header("Hareket Ayarları")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotationSpeed = 5f;
    [SerializeField] private float stopDistance = 2f; // 0,0,0'a ne kadar yaklaşınca dursun

    [Header("Araba Çarpışması")]
    [SerializeField] private float carSpeedReduction = 0.4f; // %40 = 0.4
    [SerializeField] private string carTag = "Player";
    [SerializeField] private float destroyDelay = 0f; // Anında yok olsun

    [Header("Görsel")]
    [SerializeField] private GameObject visualModel;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showDebugGizmos = true;

    private Rigidbody rb;
    private Vector3 targetPosition = Vector3.zero; // 0,0,0
    private bool isMoving = true;
    private bool isDestroyed = false;

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

        if (showDebugInfo)
        {
            Debug.Log($"{gameObject.name} spawn oldu: {transform.position} → Hedef: 0,0,0");
        }
    }

    void Update()
    {
        if (isDestroyed || !isMoving) return;

        MoveTowardsTarget();
        RotateTowardsTarget();
        CheckIfReachedTarget();
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
            Vector3 moveVelocity = direction * moveSpeed;
            moveVelocity.y = rb.linearVelocity.y; // Y hızını koru (yerçekimi için)
            rb.linearVelocity = moveVelocity;
        }
        else
        {
            // Rigidbody yoksa transform ile hareket
            transform.position += direction * moveSpeed * Time.deltaTime;
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
    /// Hedefe ulaştı mı kontrol et
    /// </summary>
    void CheckIfReachedTarget()
    {
        float distance = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), targetPosition);

        if (distance <= stopDistance)
        {
            isMoving = false;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
            }

            if (showDebugInfo)
            {
                Debug.Log($"{gameObject.name} hedefe ulaştı!");
            }
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

        if (showDebugInfo)
        {
            Debug.Log($"🐔 {gameObject.name} araba tarafından çarpıldı!");
        }

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
            float currentSpeed = carController.currentSpeed;
            float newSpeed = currentSpeed * (1f - carSpeedReduction);
            
            if (showDebugInfo)
            {
                Debug.Log($"🚗 Araba hızı: {currentSpeed:F1} km/h → {newSpeed:F1} km/h (%{carSpeedReduction * 100} azaldı)");
            }

            // Rigidbody'yi yavaşlat
            Rigidbody carRB = car.GetComponentInParent<Rigidbody>();
            if (carRB == null)
            {
                carRB = car.transform.root.GetComponent<Rigidbody>();
            }

            if (carRB != null)
            {
                carRB.linearVelocity *= (1f - carSpeedReduction);
                
                if (showDebugInfo)
                {
                    Debug.Log($"✅ Araba yavaşlatıldı!");
                }
            }
        }
        else
        {
            Debug.LogWarning($"⚠️ CarController bulunamadı: {car.name}");
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

        // Stop mesafesi
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(targetPosition, stopDistance);
    }
}