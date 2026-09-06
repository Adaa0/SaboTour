using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CİNEMACHINE OLMAYAN KAMERALAR İÇİN PATLAMA SARSINTISI.
///
/// ─── NEDEN AYRI BİR SİSTEM ────────────────────────────────────────────
/// `ExplosionCameraShake` bir `CinemachineExtension` — yani SADECE
/// Cinemachine'in sürdüğü kameralarda çalışıyor. Yarışçının kamerası öyle,
/// ama **sabotajcının FPCam'i düz bir Unity kamerası.** Sonuç: sabotajcı
/// kendi tetiklediği buz bombasının patlamasını bile hissetmiyordu —
/// butona basıyor, uzakta bir şey oluyor, ekranında hiçbir karşılık yok.
/// Oyundaki iki rolden birinin geri bildirimsiz kalması demekti.
///
/// ─── KAMERA TRANSFORM'UNU KİM YAZIYOR (çakışma önemli) ────────────────
/// `SaboteurController.HandleLook()` her karede `Update` içinde kameranın
/// `localEulerAngles`'ını KENDİ pitch değerinden yeniden yazıyor.
/// Bu yüzden:
///  • Sarsıntı `LateUpdate`'te uygulanıyor — yani look'tan SONRA.
///  • Rotasyon her karede sıfırdan ekleniyor (biriktirilmiyor); bir sonraki
///    Update look'u yeniden yazdığı için kayma (drift) imkânsız.
///  • Pozisyon, Start'ta yakalanan TABAN değerin üstüne uygulanıyor —
///    look pozisyona hiç dokunmuyor, o yüzden orası bize ait.
///
/// ─── KURULUM GEREKMİYOR ───────────────────────────────────────────────
/// `SaboteurController` bu bileşeni çalışma anında kendi kamerasına
/// ekliyor. Inspector'da hiçbir şey atamana gerek yok.
/// </summary>
[DisallowMultipleComponent]
public class SimpleCameraShake : MonoBehaviour
{
    [Tooltip("Trauma 1 iken kameranın en fazla kaç metre kayacağı.")]
    [SerializeField] private float maxPositionShake = 0.25f;

    [Tooltip("Trauma 1 iken kameranın en fazla kaç derece sallanacağı.")]
    [SerializeField] private float maxRotationShake = 2.5f;

    [Tooltip("Gürültünün hızı — büyük değer daha titrek, küçük değer daha savruk.")]
    [SerializeField] private float frequency = 22f;

    [Tooltip("Trauma saniyede ne kadar sönümlenecek. Büyük = daha kısa sarsıntı.")]
    [SerializeField] private float decayPerSecond = 1.6f;

    // Aktif tüm sarsıcılar. Patlama olduğunda hepsine mesafeye göre pay
    // dağıtılıyor — sahnede birden fazla kamera olabiliyor (izleyici modu,
    // podyum), hangisinin aktif olduğunu bilmek zorunda kalmıyoruz.
    private static readonly List<SimpleCameraShake> Active = new();

    private float trauma;
    private Vector3 basePosition;

    // Perlin gürültüsünü her eksende FARKLI bir yerden okumak için sabit
    // ofsetler. Aynı yerden okusaydık üç eksen aynı anda aynı yöne giderdi
    // ve sarsıntı "titreme" değil "kayma" gibi görünürdü.
    private float seedX, seedY, seedZ;

    void Awake()
    {
        basePosition = transform.localPosition;

        seedX = Random.value * 100f;
        seedY = Random.value * 100f;
        seedZ = Random.value * 100f;
    }

    void OnEnable() => Active.Add(this);

    void OnDisable()
    {
        Active.Remove(this);

        // Kapanırken kamerayı temiz bırak — yarım kalmış bir kayma
        // bir sonraki açılışta kalıcı görünürdü.
        transform.localPosition = basePosition;
        trauma = 0f;
    }

    /// <summary>
    /// Patlamayı mesafeye göre sarsıntıya çevirir. Kaç kameranın etkilendiğini
    /// döndürüyor — çağıran taraf "hiç kamera yok" uyarısını buna göre veriyor.
    ///
    /// FALLOFF LİNEER (kare DEĞİL): `ExplosionCameraShake`'te bir kere kare
    /// alınmıştı ve 34m/40m mesafede sarsıntıyı görünmez seviyeye (0.0005m)
    /// indirmişti. Aynı hatayı tekrarlamıyoruz.
    /// </summary>
    public static int ShakeAt(Vector3 worldPosition, float radius, float strength)
    {
        if (radius <= 0f) return 0;

        int affected = 0;

        for (int i = 0; i < Active.Count; i++)
        {
            SimpleCameraShake shake = Active[i];
            if (shake == null) continue;

            float distance = Vector3.Distance(shake.transform.position, worldPosition);
            float falloff = 1f - Mathf.Clamp01(distance / radius);
            if (falloff <= 0f) continue;

            shake.trauma = Mathf.Clamp01(shake.trauma + strength * falloff);
            affected++;
        }

        return affected;
    }

    void LateUpdate()
    {
        if (trauma <= 0.0001f)
        {
            // Sarsıntı bittiyse kamerayı tam olarak yerine oturt (yüzer
            // noktalı artıklar birikmesin).
            if (transform.localPosition != basePosition) transform.localPosition = basePosition;
            return;
        }

        trauma = Mathf.Max(0f, trauma - decayPerSecond * Time.deltaTime);

        // Trauma'nın KARESİ kullanılıyor: sarsıntı sonlara doğru hızla
        // sönsün, uzun bir "titreme kuyruğu" bırakmasın. (Falloff'ta kare
        // almak yanlıştı, burada doğru — biri mesafe, diğeri zaman.)
        float amount = trauma * trauma;
        float t = Time.time * frequency;

        Vector3 offset = new Vector3(
            (Mathf.PerlinNoise(seedX, t) - 0.5f) * 2f,
            (Mathf.PerlinNoise(seedY, t) - 0.5f) * 2f,
            (Mathf.PerlinNoise(seedZ, t) - 0.5f) * 2f);

        transform.localPosition = basePosition + offset * (maxPositionShake * amount);

        // Rotasyon TABANA DEĞİL, look'un bu karede bıraktığı değerin üstüne
        // ekleniyor. Biriktirmiyoruz: bir sonraki Update look'u sıfırdan
        // yazacağı için kayma olmuyor.
        transform.localRotation *= Quaternion.Euler(
            offset.y * maxRotationShake * amount,
            offset.x * maxRotationShake * amount,
            offset.z * maxRotationShake * amount);
    }
}
