using UnityEngine;

/// <summary>
/// Basit tavuk prefab script'i
/// Şimdilik sadece spawn olmasını sağlıyor
/// İleride animasyon, hareket vs. eklenebilir
/// </summary>
public class Chicken : MonoBehaviour
{
    [Header("Görsel")]
    [SerializeField] private GameObject visualModel;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    void Start()
    {
        if (showDebugInfo)
        {
            Debug.Log($"{gameObject.name} spawn oldu: {transform.position}");
        }

        // İleride buraya:
        // - Rastgele animasyon başlatma
        // - Ses efektleri
        // - Idle hareket (ileri-geri dolaşma)
        // eklenebilir
    }

    void OnDrawGizmos()
    {
        // Tavuk konumunu göster
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }

    // İleride araba çarptığında çağrılacak fonksiyon
    public void OnHitByCar()
    {
        // Tavuğu uçur, animasyon oynat, vs.
        Debug.Log($"{gameObject.name} arabaya çarptı!");
        
        // Örnek: Tavuğu havaya fırlat
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddForce(Vector3.up * 5f + Random.insideUnitSphere * 2f, ForceMode.Impulse);
        }
    }
}