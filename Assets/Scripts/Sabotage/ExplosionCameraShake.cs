using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// BASİT PATLAMA KAMERA TİTREMESİ — Cinemachine'in kendi Impulse sistemi
/// yerine geçiyor.
///
/// NEDEN KENDİ SİSTEMİMİZ: Cinemachine Impulse'un "Explosion" sinyali tek
/// yönlü bir İTME üretiyor (kamera bir kere aşağı savrulup geri geliyor),
/// oysa istenen şey klasik bir TİTREME (kamera hızlıca rastgele oynayıp
/// sönümleniyor). Ayrıca Impulse zinciri (Source → Manager → Listener)
/// kurulum hatalarına çok açıktı ve sessizce çalışmıyordu. Bu script tek
/// parça, tamamen bizim kontrolümüzde.
///
/// NASIL ÇALIŞIYOR: Bu bir CinemachineExtension — yani bir CinemachineCamera
/// objesinin (CarCam) üzerine eklenir ve Cinemachine kamerayı hesapladıktan
/// SONRA (Finalize aşaması) sonuca bir kaydırma/döndürme ekler. Doğrudan
/// transform'a dokunmadığımız için Cinemachine ile çakışmıyor, birikme
/// (accumulation) sorunu da olmuyor.
///
/// "Trauma" mantığı: patlama yakınlığına göre 0-1 arası bir sarsıntı puanı
/// toplanıyor, her kare sönümleniyor. Titreme miktarı trauma'nın KARESİ ile
/// orantılı — böylece sönerken doğal bir şekilde yumuşayarak bitiyor,
/// aniden kesilmiyor.
///
/// KURULUM: Car.prefab içindeki CarCam objesine ekle (oradaki Cinemachine
/// Impulse Listener artık gereksiz, kaldırılabilir).
/// </summary>
public class ExplosionCameraShake : CinemachineExtension
{
    // Sahnedeki tüm aktif alıcılar. IceBomb patlarken hepsine tek tek
    // mesafeye göre sarsıntı dağıtıyor.
    private static readonly List<ExplosionCameraShake> Active = new();

    [Header("Titreme Şiddeti")]
    [Tooltip("Kameranın en fazla ne kadar KAYACAĞI (metre).")]
    [SerializeField] private float maxPositionShake = 0.5f;
    [Tooltip("Kameranın en fazla ne kadar DÖNECEĞİ (derece).")]
    [SerializeField] private float maxRotationShake = 2.5f;

    [Header("Titreme Karakteri")]
    [Tooltip("Titreme hızı. Büyük değer = daha sinirli/keskin titreme.")]
    [SerializeField] private float frequency = 22f;
    [Tooltip("Sarsıntının sönme hızı. 2 = tam şiddetli bir sarsıntı ~0.5 saniyede biter.")]
    [SerializeField] private float decayPerSecond = 2f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private float trauma;
    private float noiseSeed;

    protected override void Awake()
    {
        base.Awake();
        // Her kamera farklı bir noise başlangıcı kullansın ki birden fazla
        // araba varsa hepsi birebir aynı şekilde titremesin.
        noiseSeed = Random.value * 1000f;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!Active.Contains(this)) Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }

    /// <summary>
    /// Bir patlama olduğunda çağrılır. Menzil içindeki her kameraya,
    /// patlamaya ne kadar YAKINSA o kadar çok sarsıntı verir (mesafe
    /// azaldıkça kareli olarak artıyor — yani çok yakında belirgin şekilde
    /// daha sert).
    /// </summary>
    public static void ShakeAt(Vector3 worldPosition, float radius, float strength)
    {
        if (radius <= 0f) return;

        if (Active.Count == 0)
        {
            Debug.LogWarning("[ExplosionCameraShake] Sarsılacak KAMERA YOK. Ya CarCam objesine bu " +
                             "component eklenmemiş, ya da bu client'ta hiçbir CarCam aktif değil " +
                             "(ör. sabotajcının ekranı — sabotajcı kamerası Cinemachine değil).");
            return;
        }

        for (int i = 0; i < Active.Count; i++)
        {
            ExplosionCameraShake shake = Active[i];
            if (shake == null) continue;

            float distance = Vector3.Distance(shake.transform.position, worldPosition);

            // LİNEER azalma. DİKKAT: burada kare almak (falloff*falloff) ölümcül —
            // uzaktaki bir kamerada sarsıntıyı görünmez seviyeye indiriyor
            // (ör. 34m/40m'de 0.15 yerine 0.02'ye düşüyordu).
            float falloff = 1f - Mathf.Clamp01(distance / radius);
            if (falloff <= 0f) continue;

            shake.trauma = Mathf.Clamp01(shake.trauma + strength * falloff);

            if (shake.showDebugLogs)
                Debug.Log($"[ExplosionCameraShake] '{shake.name}' sarsıldı — mesafe {distance:F1}m / menzil {radius}m | " +
                          $"trauma {shake.trauma:F3} | tepe kayma ~{shake.maxPositionShake * shake.trauma:F2}m " +
                          $"(0.05'in altı gözle görülmez)");
        }
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        // Finalize = pipeline tamamen bittikten sonra, her kamera tipinde
        // garanti çalışan aşama.
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (trauma <= 0f) return;

        // Genlik doğrudan trauma ile orantılı. Burada da KARE ALINMIYOR —
        // zamanla sönümlenme (aşağıdaki decayPerSecond) zaten yumuşak bir
        // bitiş sağlıyor, ayrıca kare almak sarsıntıyı yok ediyordu.
        float amount = trauma;
        float t = Time.time * frequency + noiseSeed;

        // Perlin noise, rastgele sayılara göre daha akıcı/organik bir titreme
        // veriyor (rastgele olsaydı her kare zıplayıp cırtlak görünürdü).
        float nx = Mathf.PerlinNoise(t, 0f) * 2f - 1f;
        float ny = Mathf.PerlinNoise(0f, t) * 2f - 1f;
        float nz = Mathf.PerlinNoise(t, t) * 2f - 1f;

        // Kaydırma kameranın KENDİ eksenlerinde olsun (sağa-sola, yukarı-aşağı)
        Quaternion orientation = state.GetCorrectedOrientation();
        state.PositionCorrection += orientation * new Vector3(nx, ny, 0f) * (maxPositionShake * amount);
        state.OrientationCorrection *= Quaternion.Euler(
            new Vector3(ny, nx, nz) * (maxRotationShake * amount));

        if (deltaTime > 0f)
            trauma = Mathf.Max(0f, trauma - decayPerSecond * deltaTime);
    }
}
