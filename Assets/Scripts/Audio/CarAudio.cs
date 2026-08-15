using UnityEngine;

/// <summary>
/// ARABANIN SESLERİ — motor, lastik cızırtısı, çarpma.
///
/// MANUEL ADIM: Bu component `Car.prefab`'ın KÖK objesine eklenmeli (yani
/// CarController ve Rigidbody'nin durduğu objeye — çarpışma olaylarını
/// (OnCollisionEnter) sadece Rigidbody'nin olduğu obje alır).
///
/// NEDEN AYRI BİR DOSYA (CarController'ın içine yazılmadı): CarController
/// zaten ~950 satır ve fizik/network mantığıyla dolu. Ses tamamen görsel-
/// işitsel bir katman, hiçbir oynanış kararını etkilemiyor — ayrı tutulunca
/// hem okuması kolay hem de sesle ilgili bir sorunda tek dosyaya bakman
/// yetiyor.
///
/// NETWORK NOTU: Bu script'in ağla HİÇBİR İŞİ YOK ve olması da gerekmiyor.
/// Okuduğu üç değer (SpeedRatio / IsSkidding / IsGroundedNow) CarController
/// tarafından zaten senkronize ediliyor, yani başka bir oyuncunun arabası
/// senin ekranında hızlanırken motor sesi de doğru şekilde yükseliyor.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarAudio : MonoBehaviour
{
    [Header("Motor Sesi")]
    [Tooltip("SÜREKLİ DÖNEN (loop) bir motor sesi dosyası. Sabit devirli, monoton bir 'vınn' olmalı — hızlanma/yavaşlama efekti perdeyi (pitch) değiştirerek KOD TARAFINDA yapılıyor. İçinde vites değişimi/egzoz patlaması olan hazır bir kayıt burada kötü duyulur.")]
    [SerializeField] private AudioClip engineLoop;
    [Tooltip("Araba dururken motorun perdesi (rölanti).")]
    [SerializeField] private float idlePitch = 0.75f;
    [Tooltip("Maksimum hızdaki perde. Fazla yüksek olursa ses ciyaklamaya başlar; 2.0-2.5 arası iyi çalışıyor.")]
    [SerializeField] private float maxPitch = 2.2f;
    [Tooltip("Araba DURURKEN motorun ses seviyesi. Rölanti sesi arka planda hafifçe duyulmalı, dikkat çekmemeli — bu yüzden bilerek çok kısık.")]
    [Range(0f, 1f)][SerializeField] private float idleVolume = 0.1f;
    [Tooltip("MAKSİMUM HIZDAKİ ses seviyesi. Hızlandıkça ses buraya doğru yükseliyor.")]
    [Range(0f, 1f)][SerializeField] private float maxVolume = 0.5f;
    [Tooltip("Sesin hıza göre yükselme eğrisi.\n\n1 = düz/lineer artış.\n2 = düşük hızlarda uzun süre kısık kalır, ancak yüksek hızda açılır (rölantinin baskın olmaması için önerilen).\n0.5 = daha ilk gazda hızla yükselir.")]
    [Range(0.25f, 4f)][SerializeField] private float volumeRamp = 2f;
    [Tooltip("Motorun TAM devirde sayılacağı hız (km/h). Bu hıza ulaşınca perde Max Pitch'e, ses Max Volume'a varmış olur. Arabanın gerçekte çıkabildiği en yüksek hızın biraz altına ayarla — yoksa tavan hiç görülmez.")]
    [SerializeField] private float speedForMaxPitch = 220f;
    [Tooltip("Perdenin VE ses seviyesinin hıza yetişme yumuşaklığı. Büyük değer = anında tepki, küçük = ağır/gecikmeli motor hissi.")]
    [SerializeField] private float pitchSmoothing = 6f;

    [Header("Lastik Cızırtısı (drift / kayma)")]
    [Tooltip("SÜREKLİ DÖNEN (loop) lastik kayma sesi. Duman ve lastik iziyle TAM AYNI anda başlayıp bitiyor (CarController.IsSkidding).")]
    [SerializeField] private AudioClip skidLoop;
    [SerializeField] private float skidVolume = 0.6f;
    [Tooltip("Cızırtının açılıp kapanma yumuşaklığı — anında kesilmesin, kısa bir fade ile sönsün diye.")]
    [SerializeField] private float skidFadeSpeed = 8f;

    [Header("Çarpışma Sesi")]
    [Tooltip("Çarpma sesleri. Birden fazla eklersen her çarpışmada rastgele biri seçilir (aynı sesi tekrar tekrar duymak çok yapay hissettiriyor).")]
    [SerializeField] private AudioClip[] crashClips;
    [Tooltip("Bu çarpma hızının (m/s) altındaki temaslar ses çıkarmaz — duvara sürtünürken sürekli çarpma sesi gelmesin diye.")]
    [SerializeField] private float minCrashSpeed = 6f;
    [Tooltip("Sert çarpışmanın 'tam ses' sayılacağı hız. Daha yavaş çarpmalar orantılı olarak daha kısık duyulur.")]
    [SerializeField] private float loudCrashSpeed = 22f;
    [SerializeField] private float crashVolume = 0.9f;
    [Tooltip("İki çarpma sesi arasındaki en kısa süre — tek bir çarpışmada onlarca temas noktası oluşabiliyor, hepsi ayrı ses çalarsa gürültü olur.")]
    [SerializeField] private float crashCooldown = 0.25f;
    [Tooltip("AÇIK olursa çarpma sesi SADECE kendi arabanda duyulur. Uzak arabaların çarpışması NetworkTransform ile sürüldükleri için bazen yanlış hız değerleri üretebiliyor — başkalarının çarpması kulağa tuhaf gelirse bunu aç.")]
    [SerializeField] private bool crashOnlyForOwnCar = false;

    [Header("Zıplama / Yere İniş")]
    [Tooltip("Araba havalanıp yere indiğinde çalar (tümsek/rampa hissi). Boş bırakılabilir.")]
    [SerializeField] private AudioClip landingClip;
    [SerializeField] private float landingVolume = 0.7f;
    [Tooltip("Araba en az BU KADAR süre havada kalmadıysa iniş sesi ÇALINMAZ.\n\nNEDEN GEREKLİ: CarController'ın 'yerde mi' kararı 4 tekerleğin süspansiyon raycast'ine bakıyor (2'den fazlası değiyorsa yerde sayılıyor). Normal sürüşte tümsekler, kenarlık (kerb) ve pist pürüzleri yüzünden bu değer saniyede birkaç kez bir açılıp bir kapanabiliyor — her seferinde iniş sesi çalarsa düz giderken sürekli bas gümlemesi duyulur. Bu eşik o titremeyi filtreliyor: gerçek bir sıçrama bundan uzun sürer, sahte temas kayıpları çok daha kısa.")]
    [SerializeField] private float minAirTimeForLanding = 0.35f;

    private CarController car;
    private AudioSource engineSource;
    private AudioSource skidSource;

    // Hem perde hem ses seviyesi AYNI yumuşatılmış hız değerinden hesaplanıyor.
    // Ayrı ayrı yumuşatsaydık ikisi farklı hızlarda değişip motor sesi
    // "kayık" duyulurdu (ses yükselirken perde geride kalır gibi).
    private float smoothedSpeedRatio;
    private float currentSkidVolume;
    private float lastCrashTime = -99f;
    private bool wasGrounded = true;
    private float airborneTime;

    void Awake()
    {
        car = GetComponent<CarController>();

        // İki SÜREKLİ ses (motor + cızırtı) havuzdan değil, arabanın kendi
        // üstündeki AudioSource'lardan çalıyor — çünkü loop'lu ve arabayla
        // birlikte HAREKET etmesi gerekiyor. SfxPlayer havuzu sadece tek
        // seferlik, sabit noktada çalan sesler için.
        engineSource = CreateLoopSource("EngineAudio", engineLoop, idleVolume);
        skidSource = CreateLoopSource("SkidAudio", skidLoop, 0f);

        smoothedSpeedRatio = 0f;
    }

    private AudioSource CreateLoopSource(string sourceName, AudioClip clip, float volume)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = volume;
        src.spatialBlend = 1f;                       // 3D — uzaktaki araba kısık duyulsun
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 6f;
        src.maxDistance = 120f;
        src.dopplerLevel = 0.3f;                     // hızla geçen araba hafif "vuuuş" yapsın

        // Klip atanmamışsa hiç başlatma — boş bir AudioSource'un Play()
        // edilmesi zararsız ama gereksiz.
        if (clip != null) src.Play();

        return src;
    }

    void Update()
    {
        UpdateEngine();
        UpdateSkid();
        UpdateLanding();
    }

    private void UpdateEngine()
    {
        if (engineSource == null || engineLoop == null) return;

        // Tek bir yumuşatılmış hız değeri (0 = duruyor, 1 = tam devir).
        //
        // DÜZELTME: burada eskiden car.SpeedRatio kullanılıyordu — ama o değer
        // birim karışıklığı yüzünden asla 1'e ulaşmıyor (300 km/h'de bile
        // ~0.28'de kalıyor, bkz. CarController.SpeedKmh açıklaması). Sonuç:
        // motor sesi perdesi Max Pitch'in ancak dörtte birine kadar
        // yükseliyor, araba hızlansa da ses neredeyse hiç değişmiyordu.
        float rawRatio = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, speedForMaxPitch));
        smoothedSpeedRatio = Mathf.Lerp(smoothedSpeedRatio, rawRatio, Time.deltaTime * pitchSmoothing);

        // PERDE (pitch): hızla birlikte lineer yükseliyor — motorun devri.
        engineSource.pitch = Mathf.Lerp(idlePitch, maxPitch, smoothedSpeedRatio);

        // SES SEVİYESİ: artık SABİT DEĞİL, hızla birlikte yükseliyor.
        // NEDEN: sabit seviyede rölanti sesi sürekli aynı yükseklikte
        // duyuluyor ve araba dururken bile kulakta baskın kalıyordu.
        // Gerçek bir motorda da boşta çalışan motor neredeyse duyulmaz,
        // asıl gürültü yük altında (gaza basınca) çıkar.
        //
        // volumeRamp üssü, düşük hızlarda sesin kısık kalma süresini
        // uzatıyor: 2 değerinde, hızın yarısında ses ancak dörtte bir
        // yükselmiş oluyor (0.5² = 0.25).
        float volumeT = Mathf.Pow(Mathf.Clamp01(smoothedSpeedRatio), volumeRamp);
        engineSource.volume = Mathf.Lerp(idleVolume, maxVolume, volumeT) * SfxPlayer.MasterVolume;
    }

    private void UpdateSkid()
    {
        if (skidSource == null || skidLoop == null) return;

        // Kayma kararını YENİDEN HESAPLAMIYORUZ — CarController'ın zaten
        // hesaplayıp senkronize ettiği (ve duman/lastik izini de tetikleyen)
        // sonucu kullanıyoruz. Böylece ses ile görsel efekt asla ayrışmıyor.
        float target = car.IsSkidding ? skidVolume : 0f;
        currentSkidVolume = Mathf.MoveTowards(currentSkidVolume, target, Time.deltaTime * skidFadeSpeed * skidVolume);

        skidSource.volume = currentSkidVolume * SfxPlayer.MasterVolume;

        // Tamamen sustuysa çalmayı durdur (boş yere bir ses kanalı işgal
        // etmesin), yeniden gerekince başlat.
        if (currentSkidVolume <= 0.001f)
        {
            if (skidSource.isPlaying) skidSource.Pause();
        }
        else if (!skidSource.isPlaying)
        {
            skidSource.UnPause();
            if (!skidSource.isPlaying) skidSource.Play();
        }
    }

    private void UpdateLanding()
    {
        bool grounded = car.IsGroundedNow;

        if (!grounded)
        {
            // Havada geçen süreyi biriktir.
            airborneTime += Time.deltaTime;
        }
        else
        {
            // Yere değdiği KARE (önceki kare havada, bu kare yerde) — ama
            // ses SADECE gerçekten havada kalmışsa çalıyor. Tümsekte
            // tekerlek birkaç kare kopmuşsa bu bir "iniş" sayılmıyor.
            if (!wasGrounded && airborneTime >= minAirTimeForLanding)
                SfxPlayer.PlayAt(landingClip, transform.position, landingVolume, 0.08f);

            airborneTime = 0f;
        }

        wasGrounded = grounded;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (crashClips == null || crashClips.Length == 0) return;
        if (crashOnlyForOwnCar && !car.isOwned) return;
        if (Time.time - lastCrashTime < crashCooldown) return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < minCrashSpeed) return;

        lastCrashTime = Time.time;

        // Ses şiddeti çarpma sertliğiyle orantılı — hafif sürtünme kısık,
        // duvara tam gaz girmek tam ses.
        float loudness = Mathf.InverseLerp(minCrashSpeed, loudCrashSpeed, impactSpeed);
        float volume = Mathf.Lerp(0.35f, 1f, loudness) * crashVolume;

        // Sesi temas NOKTASINDA çalıyoruz (arabanın merkezinde değil) —
        // yandan sıyırma ile burundan çarpma kulakta farklı yerden gelsin.
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

        SfxPlayer.PlayRandomAt(crashClips, point, volume, 0.08f);
    }
}
