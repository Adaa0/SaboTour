using UnityEngine;

public class IceBomb : MonoBehaviour
{
    public float delay = 1.5f;
    public GameObject icePatchPrefab;

    [Header("Flash Settings")]
    public Material normalMat;
    public Material flashMat;
    public float flashSpeed = 0.15f;

    [Header("Explosion Settings")]
    public float explosionRadius = 100f;
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

    private System.Collections.IEnumerator FlashRoutine()
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

        GameObject ice = Instantiate(icePatchPrefab, transform.position, Quaternion.identity);

        float s = Random.Range(50f, 60f);
        ice.transform.localScale = new Vector3(s, s, 0.0001f);
        ice.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ice.transform.position = new Vector3(ice.transform.position.x, 0.02f, ice.transform.position.z);

        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);

        foreach (Collider hit in hits)
        {
            Rigidbody rb = hit.attachedRigidbody;
            if (rb != null)
            {
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
