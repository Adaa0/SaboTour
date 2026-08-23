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

    [Header("Fırlayan Araç — Prop Çarpışması")]
    [Tooltip("Patlamayla fırlayan araç, bu süre boyunca ağaç/kaya collider'larını " +
             "yok sayar — havada ilk ağaca takılıp durmasın diye. Bu süre dolduktan " +
             "SONRA araç yere değer değmez normale döner.")]
    public float propIgnoreMinSeconds = 1.5f;

    [Tooltip("Araç yere hiç inmezse (bir yere sıkışırsa) en geç bu kadar sonra prop " +
             "çarpışması geri açılır — güvenlik ağı.")]
    public float propIgnoreMaxSeconds = 8f;

    [Header("Buz Alanı Ömrü")]
    [Tooltip("Yerdeki buz alanı (icePatchPrefab) patlamadan kaç saniye sonra yok olsun.")]
    public float icePatchLifetime = 5f;

    // ─── SESLER ──────────────────────────────────────────────────────────
    // Bu script HER CLIENT'ta ayrı ayrı çalışıyor (bomba networked bir obje
    // DEĞİL, IceBombSkill'in [ClientRpc]'si herkeste yerel bir kopya
    // oluşturuyor — bkz. IceBombSkill.cs). Yani buradaki sesler otomatik
    // olarak herkeste, doğru anda ve doğru konumda çalıyor; ekstra bir
    // network mesajı GEREKMİYOR.
    //
    // ÖNEMLİ: Sesler SfxPlayer üzerinden çalınıyor, bombanın kendi
    // AudioSource'undan DEĞİL — çünkü Explode() bombayı yok ediyor ve yok
    // olan bir objenin AudioSource'u sesi ortasında keser (patlama sesinin
    // hiç duyulmaması demek).
    [Header("Sesler")]
    [Tooltip("Bomba kuleden fırlatıldığı anda, KULENİN TEPESİNDE çalar (fırlatma/mancınık sesi).")]
    public AudioClip launchClip;
    [Tooltip("Bomba yere çarptığı anda çalar. Kamera sarsıntısıyla TAM AYNI an — sert bir 'güm' olmalı.")]
    public AudioClip impactClip;
    [Tooltip("Patlamadan önceki yanıp sönme sırasında her yanışta çalan bip sesi (bomba geri sayımı). Boş bırakılabilir.")]
    public AudioClip beepClip;
    [Tooltip("Patlama + buz alanının oluştuğu an.")]
    public AudioClip explosionClip;
    [Range(0f, 1f)] public float sfxVolume = 1f;

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

        // Fırlatma sesi kulenin tepesinde (startPos) çalıyor — sabotajcı
        // kendi attığını yakından duyuyor, pistteki yarışçılar ise uzaktan
        // kısık bir sesle "bir şey fırlatıldı" uyarısı alıyor.
        SfxPlayer.PlayAt(launchClip, startPos, sfxVolume, 0.06f);

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
        SfxPlayer.PlayAt(impactClip, end, sfxVolume, 0.05f);

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
            // Bip, görsel yanıp sönmeyle AYNI karede — ışık ve ses birlikte
            // gelince geri sayım hissi çok daha güçlü oluyor.
            SfxPlayer.PlayAt(beepClip, transform.position, sfxVolume * 0.6f, 0f, 5f, 45f);
            yield return new WaitForSeconds(flashSpeed);

            rend.material = normalMat;
            yield return new WaitForSeconds(flashSpeed);
        }
    }

    void Explode()
    {
        flashing = false;

        // Patlama sesi EN BAŞTA — altındaki kod bu objeyi Destroy ediyor,
        // ama SfxPlayer sesi bombadan bağımsız bir kaynaktan çaldığı için
        // obje yok olsa bile ses sonuna kadar duyuluyor.
        SfxPlayer.PlayAt(explosionClip, transform.position, sfxVolume, 0.05f, 12f, 140f);

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

                // Fırlayan araç, piste yakın propların (ağaç/kaya) collider'larını
                // bir süreliğine yok saysın — yoksa havadayken ilk ağaca takılıp
                // duruyor ve patlamanın bütün etkisi kayboluyor.
                // Araç yere indiği anda (ya da en geç maxSeconds sonra) normale
                // dönüyor, bkz. CarController.IgnorePropCollisions.
                if (car != null)
                    car.IgnorePropCollisions(propIgnoreMinSeconds, propIgnoreMaxSeconds);
            }
        }

        Destroy(gameObject);
    }
}
