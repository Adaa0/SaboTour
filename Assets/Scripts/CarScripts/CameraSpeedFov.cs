using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// HIZLANDIKÇA KAMERANIN GÖRÜŞ AÇISINI (FOV) AÇAR.
///
/// MANUEL ADIM: Bu component `Car.prefab` içindeki **CarCam** objesine
/// eklenmeli (Cinemachine Camera'nın durduğu obje).
///
/// ── NEDEN İŞE YARIYOR ──
/// Hız hissi mutlak hızdan değil, GÖRSEL AKIŞTAN gelir: kenardaki nesneler
/// ekranı ne kadar hızlı terk ediyor. FOV açıldıkça çevredeki her şey
/// kenarlara doğru gerilip daha hızlı kayar, beyin bunu ivme olarak okur.
/// Yarış oyunlarının neredeyse tamamı bu numarayı kullanır — arabanın
/// gerçek hızını hiç değiştirmeden çok daha hızlı hissettirir.
///
/// Bu yüzden `Max Speed`'i büyütmekten çok daha iyi bir çözüm: maxSpeed
/// aynı zamanda carVelocityRatio'nun böleni olduğu için direksiyon
/// hissini de bozuyor (bkz. CarController). FOV ise fiziğe hiç dokunmuyor.
///
/// ── NETWORK ──
/// Gerek yok: CarCam zaten SADECE arabanın sahibinde aktif ediliyor
/// (CarCameraActivator.OnStartAuthority). Yani bu script hiçbir zaman
/// başka oyuncunun kamerasında çalışmıyor.
/// </summary>
[RequireComponent(typeof(CinemachineCamera))]
public class CameraSpeedFov : MonoBehaviour
{
    [Header("Görüş Açısı (FOV)")]
    [Tooltip("Araç DURURKEN kullanılacak görüş açısı. Cinemachine Camera > Lens > Field Of View'daki mevcut değerinle aynı olmalı, yoksa oyun başlar başlamaz kamera sıçrar.")]
    [SerializeField] private float baseFov = 60f;
    [Tooltip("Tam hızdaki görüş açısı. 75-80 arası belirgin ama abartısız. Çok yükseltirsen (90+) kenarlar balık gözü gibi eğrilmeye başlar ve mide bulandırır.")]
    [SerializeField] private float maxFov = 78f;
    [Tooltip("FOV'un tavana vurduğu hız (km/h). Arabanın gerçekte çıkabildiği hızın biraz ALTINA ayarla — yoksa maksimum açıyı hiç görmezsin.")]
    [SerializeField] private float speedForMaxFov = 200f;

    [Header("His Ayarları")]
    [Tooltip("FOV'un hıza yetişme yumuşaklığı. Düşük = ağır/sinematik, yüksek = anlık tepki. Çok yükseltme: her küçük hız dalgalanmasında ekran nefes alıp verir gibi olur, rahatsız eder.")]
    [SerializeField] private float smoothing = 2.5f;
    [Tooltip("Açılma eğrisi.\n\n1 = düz artış.\n1.5-2 = düşük hızlarda neredeyse hiç açılmaz, asıl etki yüksek hızda gelir (önerilen — şehir içi hızda ekranın gerilmesi yapay durur).")]
    [Range(0.5f, 4f)][SerializeField] private float fovCurvePower = 1.6f;

    private CinemachineCamera cam;
    private CarController car;
    private float currentFov;

    void Awake()
    {
        cam = GetComponent<CinemachineCamera>();

        // CarCam, araba kök objesinin ÇOCUĞU — CarController orada duruyor.
        car = GetComponentInParent<CarController>();

        if (car == null)
            Debug.LogWarning("[CameraSpeedFov] Üst objelerde CarController bulunamadı — FOV sabit kalacak. Bu script CarCam objesinde, arabanın altında olmalı.");

        currentFov = baseFov;
        ApplyFov(baseFov);
    }

    // LateUpdate DEĞİL Update: CinemachineBrain kendi hesabını LateUpdate'te
    // yapıyor ve lens değerini o sırada okuyor. LateUpdate'te yazsaydık
    // değişiklik bir kare geç uygulanırdı.
    void Update()
    {
        if (car == null) return;

        float t = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, speedForMaxFov));
        t = Mathf.Pow(t, fovCurvePower);

        float targetFov = Mathf.Lerp(baseFov, maxFov, t);

        currentFov = Mathf.Lerp(currentFov, targetFov, Time.deltaTime * smoothing);
        ApplyFov(currentFov);
    }

    private void ApplyFov(float fov)
    {
        // Lens bir struct — doğrudan `cam.Lens.FieldOfView = x` yazmak yerine
        // kopyala/değiştir/geri yaz deseni kullanılıyor (struct alanına
        // doğrudan yazmak Cinemachine sürümüne göre derlenmeyebiliyor).
        LensSettings lens = cam.Lens;
        lens.FieldOfView = fov;
        cam.Lens = lens;
    }
}
