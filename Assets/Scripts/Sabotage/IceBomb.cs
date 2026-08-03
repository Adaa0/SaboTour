using UnityEngine;
using System.Collections;

public class IceBomb : MonoBehaviour
{
    public float delay = 1.5f;
    public GameObject icePatchPrefab;

    [Header("Flash Settings")]
    [Tooltip("Yanıp sönecek objenin Renderer'ı — örneğin bombanın sadece iç kısmı. Boş bırakılırsa dış objede ve child'larda otomatik aranır.")]
    public Renderer flashTargetRenderer;
    public Material normalMat;
    public Material flashMat;
    public float flashSpeed = 0.15f;

    [Header("Explosion Settings")]
    public float explosionRadius = 3f;
    public float explosionForce = 25000f;

    [Header("Buz Alanı Ömrü")]
    [Tooltip("Yerdeki buz alanı (icePatchPrefab) patlamadan kaç saniye sonra yok olsun.")]
    public float icePatchLifetime = 5f;

    [Header("Kamera Sarsıntısı (bomba YERE DÜŞTÜĞÜ an)")]
    [Tooltip("Bu mesafeden daha uzaktaki kameralar hiç sarsılmaz. Yaklaştıkça sarsıntı artar.")]
    public float shakeRadius = 40f;
    [Tooltip("Tam merkezde hissedilecek sarsıntı şiddeti (0-1). 1 = mümkün olan en sert.")]
    [Range(0f, 1f)] public float shakeStrength = 0.7f;

    private Renderer rend;
    private bool flashing = true;
    private bool launched = false;

    void Start()
    {
        // Launch() ile fırlatılmadıysa (örneğin sahneye manuel yerleştirilip
        // tek başına test ediliyorsa) eski davranış: hemen yanıp sön + patla.
        if (!launched)
            BeginCountdown();
    }

    /// <summary>
    /// IceBombSkill (server) tarafından ClientRpc üzerinden HER client'ta
    /// çağrılır. Bombayı fırlatma noktasından hedefe deterministik bir
    /// parabolik eğriyle uçurur — Rigidbody fiziği DEĞİL, çünkü her
    /// client'ın aynı anda aynı sonucu görmesi lazım (bkz. CLAUDE.md madde
    /// 8 "Buz Bombası Fırlatma Animasyonu"). Uçuş bitince patlama geri
    /// sayımı BeginCountdown() ile başlar.
    /// </summary>
    public void Launch(Vector3 startPos, Vector3 endPos, float duration, float arcHeight)
    {
        launched = true;
        transform.position = startPos;
        StartCoroutine(FlightRoutine(startPos, endPos, duration, arcHeight));
    }

    private IEnumerator FlightRoutine(Vector3 start, Vector3 end, float duration, float arcHeight)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);

            Vector3 pos = Vector3.Lerp(start, end, u);
            pos.y += arcHeight * 4f * u * (1f - u); // parabol: u=0 ve u=1'de 0, u=0.5'te tepe noktası
            transform.position = pos;

            yield return null;
        }

        transform.position = end;

        // ÇARPMA ANI — sarsıntı burada, patlamada değil. Bomba yere değdiği
        // an hissediliyor; patlama (buz oluşumu) bundan `delay` saniye sonra.
        ExplosionCameraShake.ShakeAt(end, shakeRadius, shakeStrength);

        BeginCountdown();
    }

    private void BeginCountdown()
    {
        // ÖNEMLİ: Renderer bulunamasa bile Explode() MUTLAKA zamanlanmalı —
        // önceden rend.material ataması patlarsa bu metod tamamen duruyor ve
        // bomba hiç patlamıyordu (yanıp sönme ile patlama zamanlaması aynı
        // metodun içindeydi, biri çökünce diğeri de iptal oluyordu).
        rend = flashTargetRenderer != null ? flashTargetRenderer : GetComponentInChildren<Renderer>();

        if (rend == null)
            Debug.LogWarning("[IceBomb] Yanıp sönecek bir Renderer bulunamadı (Flash Target Renderer atanmamış ve child'larda da yok). Yanıp sönme atlanıyor, bomba yine de patlayacak.");
        else
        {
            rend.material = normalMat;
            StartCoroutine(FlashRoutine());
        }

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
        Destroy(ice, icePatchLifetime);

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
