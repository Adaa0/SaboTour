using UnityEngine;

public class IceBomb : MonoBehaviour
{
    public float delay = 1.5f;
    public GameObject icePatchPrefab;

    [Header("Flash Settings")]
    public Material normalMat;
    public Material flashMat;
    public float flashSpeed = 0.15f;

    private Renderer rend;
    private bool flashing = true;

    void Start()
    {
        rend = GetComponent<Renderer>();
        rend.material = normalMat;

        // Flash efektini başlat
        StartCoroutine(FlashRoutine());

        // Patlama zamanını ayarla
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
        flashing = false; // Yanıp sönmeyi durdur

        // Ice patch oluştur
        GameObject ice = Instantiate(icePatchPrefab, transform.position, Quaternion.identity);

        // Random scale & rotation
       float s = Random.Range(3f, 8f);
       ice.transform.localScale = new Vector3(s, s, s);
       ice.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
       ice.transform.position = new Vector3(ice.transform.position.x, 0.001f, ice.transform.position.z);

        Destroy(gameObject);
    }
}
