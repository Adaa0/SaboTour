using UnityEngine;
using System.Collections;

public class IceBomb : MonoBehaviour
{
    public float delay = 1.5f;
    public GameObject icePatchPrefab;

    [Header("Flash Settings")]
    public Material normalMat;
    public Material flashMat;
    public float flashSpeed = 0.15f;

    [Header("Explosion Settings")]
    public float explosionRadius = 3f;
    public float explosionForce = 25000f;

    private Renderer rend;
    private bool flashing = true;

    void Start()
    {
        rend = GetComponent<Renderer>();
        rend.material = normalMat;

        StartCoroutine(FlashRoutine());
        Invoke(nameof(Explode), delay);
    }

    private IEnumerator FlashRoutine()
    {
        while (flashing)
        {
            rend.material = flashMat;
            yield return new WaitForSeconds(flashSpeed);

            rend.material = normalMat;
            yield return new WaitForSeconds(flashSpeed);
        }
    }

    void Explode()
    {
        flashing = false;

        // Buz alanı oluştur
        GameObject ice = Instantiate(icePatchPrefab, transform.position, Quaternion.identity);
        float s = Random.Range(5f, 10f);
        ice.transform.localScale = Vector3.one * s;
        ice.transform.position = new Vector3(transform.position.x, 0.02f, transform.position.z);

        // Patlama alanındaki tüm objeleri al
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);

        foreach (Collider hit in hits)
        {
            // Araba mı? (CarController varsa)
            CarController car = hit.GetComponentInParent<CarController>();
            Rigidbody rb = hit.attachedRigidbody;

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                // Şimdi itme kuvvetini uygula
                float dist = Vector3.Distance(transform.position, hit.transform.position);
                float t = Mathf.Clamp01(1f - (dist / explosionRadius));
                float finalForce = explosionForce * t;
                Vector3 dir = (hit.transform.position - transform.position).normalized;
                rb.AddForce(dir * finalForce, ForceMode.Impulse);
            }
        }

        Destroy(gameObject);
    }
}