using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ARABAYI CHECKPOINT'E GERİ IŞINLAYAN KURTARMA SİSTEMİ (iki ayrı durum).
///
/// 1) PİSTTEN ÇOK UZAKLAŞMA — araba ormanda kaybolursa ya da buz bombasıyla
///    uzağa fırlatılırsa, EN SON GEÇTİĞİ checkpoint'e geri döner.
/// 2) CHECKPOINT ATLAMA — viraj kesip bir checkpoint'i ıskalarsa, ATLADIĞI
///    checkpoint'e geri döner.
///
/// Her iki durumda da oyuncunun ekranı siyaha kararır, araba ışınlanır,
/// ekran geri açılır.
///
/// ── SADECE KENDİ EKRANINDA ──
/// Bu script SADECE arabanın sahibi olan oyuncuda çalışır. Başka birinin
/// arabası pistten çıkarsa senin ekranın kararmaz. Işınlanan pozisyon zaten
/// NetworkTransform ile diğer oyunculara otomatik yayılıyor, bu yüzden ayrıca
/// bir ağ mesajı (Command/Rpc) göndermeye gerek yok.
///
/// ── ATLAMA NASIL YAKALANIYOR (ikinci collider'a GEREK YOK) ──
/// Her checkpoint'in bir "kapı düzlemi" var (checkpoint'in forward yönü pistin
/// gidiş yönüne bakıyor). Araba, gitmesi gereken checkpoint'in ÖNÜNDEN
/// ARKASINA geçtiği hâlde checkpoint kaydı gelmediyse, o checkpoint'i
/// ıskalamış demektir.
///
/// NEDEN "ANINDA" DEĞİL DE KISA BİR BEKLEME VAR (Confirm Delay): checkpoint'i
/// DÜZGÜN geçtiğinde de araba düzlemin arkasına geçiyor — ama bu bilginin
/// sunucuya gidip SyncVar ile geri dönmesi ~100-200ms sürüyor. Bekleme
/// olmasaydı dürüst geçişler bile "atladı" sayılırdı.
///
/// NEDEN BİR SONRAKİ CHECKPOINT'İ BEKLEMİYORUZ: atlamayı ancak oyuncu bir
/// sonraki checkpoint'e vardığında yakalasaydık, oyuncu kısayolun mesafesini
/// çoktan kazanmış olurdu — pist kendi üstüne kıvrıldığı yerlerde bu, kısayolu
/// kârlı hâle getirebilirdi. Düzlem kontrolü oyuncuyu daha ıskaladığı anda
/// yakaladığı için böyle bir açık kalmıyor.
///
/// ── INSPECTOR ALANLARI ──
///  • Max Distance From Track — yolun ORTA ÇİZGİSİNE olan mesafe bu değeri
///    (metre) aşarsa "pistten çıktı" sayılır. Yol 25m geniş, yani yolun kenarı
///    orta çizgiden ~12m uzakta; 60 vermek "yolun kenarından ~48m dışarıda" demek.
///  • Sustained Seconds — bu kadar saniye KESİNTİSİZ uzakta kalırsa tetiklenir.
///  • Check Interval — pist mesafesi kontrolünün kaç saniyede bir yapılacağı.
///    (Atlama kontrolü bundan bağımsız, her karede yapılıyor — tek bir çarpma
///    işlemi olduğu için maliyeti yok.)
///  • Gate Width / Gate Depth — checkpoint'in etrafındaki "bu checkpoint'e
///    yaklaşıyorum" penceresi. Pistin kendi üstüne kıvrıldığı yerlerde BAŞKA
///    bir bölümde giden arabanın yanlışlıkla yakalanmasını engelliyor.
///  • Skip Margin — düzlemin kaç metre arkasına geçilince "geçti" sayılacağı.
///  • Confirm Delay — düzlemi geçtikten sonra checkpoint kaydının gelmesi için
///    tanınan süre. Ağ gecikmesi yüksekse artır.
///  • Fade Duration / Hold Seconds — ekranın kararma süresi ve siyahken
///    beklenen ek süre (araba yere otursun diye).
///  • Spawn Height Offset — checkpoint objeleri yolun 5 metre ÜSTÜNDE duruyor,
///    bu yüzden varsayılan -3: araba yolun 2 metre üstünde doğuyor.
///
/// ── MANUEL ADIM (Unity'de senin yapman gerekiyor) ──
/// Bu component'i `Assets/Prefabs/Car.prefab` üzerine EKLE:
///   1. Project penceresinde Car.prefab'a çift tıkla (Prefab Mode açılır).
///   2. En üstteki kök objeyi seç (CarController'ın olduğu obje).
///   3. Add Component → "Checkpoint Recovery" ara ve ekle.
///   4. Ctrl+S ile kaydet.
/// Başka hiçbir sahne/prefab ayarı gerekmiyor — kararma perdesi (ScreenFader)
/// kendini runtime'da otomatik oluşturuyor, checkpoint prefabına dokunulmadı.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CheckpointRecovery : MonoBehaviour
{
    [Header("Pistten Uzaklaşma")]
    [Tooltip("Yolun orta çizgisine olan mesafe bu değeri (metre) aşarsa pistten çıkmış sayılır.")]
    [SerializeField] private float maxDistanceFromTrack = 60f;

    [Tooltip("Bu kadar saniye KESİNTİSİZ uzakta kalırsa kurtarma başlar. Anlık savrulmalarda tetiklenmesin diye.")]
    [SerializeField] private float sustainedSeconds = 2f;

    [Tooltip("Pist mesafesi kontrolünün kaç saniyede bir yapılacağı.")]
    [SerializeField] private float checkInterval = 0.5f;

    [Header("Checkpoint Atlama")]
    [Tooltip("Atlama tespitini tamamen kapatmak için işareti kaldır.")]
    [SerializeField] private bool detectSkippedCheckpoints = true;

    [Tooltip("Checkpoint'in sağına/soluna bu mesafeden (metre) daha uzaktaysa o checkpoint'e yaklaşıyor sayılmaz. Pistin kendi üstüne kıvrıldığı yerlerde yanlış tespiti önlüyor.")]
    [SerializeField] private float gateWidth = 60f;

    [Tooltip("Checkpoint'in önünde/arkasında bu mesafeden (metre) daha uzaktaysa o checkpoint'e yaklaşıyor sayılmaz.")]
    [SerializeField] private float gateDepth = 90f;

    [Tooltip("Checkpoint düzleminin kaç metre arkasına geçilince 'geçti' sayılacağı.")]
    [SerializeField] private float skipMargin = 8f;

    [Tooltip("Düzlemi geçtikten sonra checkpoint kaydının sunucudan dönmesi için tanınan süre. Ağ gecikmesi yüksekse artır.")]
    [SerializeField] private float confirmDelay = 1.5f;

    [Header("Kurtarma")]
    [Tooltip("Ekranın kararma (ve geri açılma) süresi.")]
    [SerializeField] private float fadeDuration = 0.4f;

    [Tooltip("Ekran tam siyahken beklenecek ek süre — araba yere otursun diye.")]
    [SerializeField] private float holdSeconds = 0.2f;

    [Tooltip("Checkpoint'ler yolun 5m üstünde duruyor. -3 = araba yolun 2m üstünde doğar.")]
    [SerializeField] private float spawnHeightOffset = -3f;

    private CarController car;
    private PlayerRaceController raceController;
    private TrackGenerator trackGenerator;
    private CheckpointManager checkpointManager;

    // Pistten uzaklaşma: kesintisiz "uzakta" geçirilen süre.
    private float awayTimer;

    // Atlama tespiti durumu.
    private int watchedIndex = -1;      // şu an hangi checkpoint'e yaklaşıyoruz
    private bool wasBeforeGate;         // düzlemin ÖN tarafında görüldü mü
    private float sinceCrossed = -1f;   // düzlemi geçtikten sonra geçen süre (-1 = henüz geçmedi)

    private bool recovering;
    private bool warnedMissingTrack;

    private void Awake()
    {
        car = GetComponent<CarController>();
        raceController = GetComponent<PlayerRaceController>();
    }

    private void Start()
    {
        StartCoroutine(OffTrackRoutine());
    }

    // ─── 1) Pistten Uzaklaşma ────────────────────────────────────────────
    private IEnumerator OffTrackRoutine()
    {
        var wait = new WaitForSeconds(checkInterval);

        while (true)
        {
            yield return wait;

            if (!ShouldMonitor() || !TryGetDistanceToTrack(out float distance) || distance <= maxDistanceFromTrack)
            {
                awayTimer = 0f;
                continue;
            }

            awayTimer += checkInterval;

            if (awayTimer >= sustainedSeconds)
            {
                awayTimer = 0f;
                // Son GEÇERLİ checkpoint'e dön (henüz geçmediği bir yere değil).
                TriggerRecovery(GetLastPassedIndex(), $"pistten uzaklaştı ({distance:F0}m)");
            }
        }
    }

    // ─── 2) Checkpoint Atlama ────────────────────────────────────────────
    private void Update()
    {
        if (!detectSkippedCheckpoints) return;

        if (!ShouldMonitor())
        {
            ResetGateState();
            return;
        }

        if (!TryGetCheckpoints(out List<Transform> checkpoints)) return;

        int expected = (GetLastPassedIndex() + 1) % checkpoints.Count;

        // Beklenen checkpoint değiştiyse (geçildi ya da yarış ilerledi) durumu sıfırla.
        if (expected != watchedIndex)
        {
            watchedIndex = expected;
            ResetGateState();
        }

        Transform gate = checkpoints[expected];
        if (gate == null) return;

        Vector3 toCar = transform.position - gate.position;
        float along = Vector3.Dot(toCar, gate.forward);                     // + = düzlemin arkasında
        float lateral = Vector3.ProjectOnPlane(toCar, gate.forward).magnitude;

        bool inWindow = lateral <= gateWidth && Mathf.Abs(along) <= gateDepth;

        if (sinceCrossed < 0f)
        {
            if (inWindow && along < 0f)
                wasBeforeGate = true;
            else if (wasBeforeGate && along > skipMargin)
                sinceCrossed = 0f;      // düzlemi geçti, kayıt gelecek mi diye bekliyoruz
            else if (!inWindow && along < 0f)
                wasBeforeGate = false;  // pencereden çıktı, baştan
        }
        else
        {
            sinceCrossed += Time.deltaTime;

            if (sinceCrossed >= confirmDelay)
            {
                ResetGateState();
                // Iskaladığı checkpoint'in TA KENDİSİNE dön — kısayolun
                // kazandırdığı mesafe böylece tamamen geri alınıyor.
                TriggerRecovery(expected, $"checkpoint {expected} atlandı");
            }
        }
    }

    private void ResetGateState()
    {
        wasBeforeGate = false;
        sinceCrossed = -1f;
    }

    // ─── Ortak ───────────────────────────────────────────────────────────

    /// <summary>
    /// Kurtarmanın çalışması için gereken TÜM koşullar. Yarış bitmişken
    /// (podyum ekranı) araba zaten kasten pistten uzağa ışınlanıyor — burada
    /// durdurulmazsa kurtarma onu sürekli piste geri çekerdi.
    /// </summary>
    private bool ShouldMonitor()
    {
        if (recovering) return false;

        // Mirror'ın isOwned'ı spawn anında hemen dolmuyor, o yüzden Start'ta
        // bir kere değil her kontrolde bakıyoruz. IsNetworkOwned kullanılıyor
        // (düz isOwned DEĞİL) — bu prefab network dışında da Instantiate
        // edilebiliyor ve orada isOwned NullReferenceException fırlatıyor.
        if (car == null || !car.IsNetworkOwned) return false;

        if (raceController != null && !raceController.isRacing) return false;

        return true;
    }

    /// <summary>
    /// En son GEÇİLEN checkpoint. Yarış başlamadan önce -1 dönüyor; bu bilinçli,
    /// çünkü beklenen checkpoint (-1 + 1) % n = 0 olarak doğru çıkıyor.
    /// </summary>
    private int GetLastPassedIndex()
    {
        if (raceController == null) return -1;
        return raceController.CurrentCheckpoint;
    }

    private bool TryGetCheckpoints(out List<Transform> checkpoints)
    {
        if (checkpointManager == null)
            checkpointManager = FindAnyObjectByType<CheckpointManager>();

        checkpoints = checkpointManager != null ? checkpointManager.checkpoints : null;
        return checkpoints != null && checkpoints.Count > 0;
    }

    /// <summary>
    /// Arabanın yolun ORTA ÇİZGİSİNE olan en kısa mesafesi.
    ///
    /// NEDEN CHECKPOINT'LERE DEĞİL YOLA BAKIYORUZ: checkpoint sayısı
    /// ayarlanabilir (checkpointsPerLap, 3-30 arası). 10 checkpoint'te bile
    /// aralarındaki mesafe ~90m oluyor, yani PİSTİN TAM ORTASINDA giden bir
    /// araba en yakın checkpoint'ten ~45m uzakta. Checkpoint sayısı düşürülürse
    /// bu mesafe iki katına çıkar ve pistte düzgün giden arabayı "kayboldu"
    /// sanıp ışınlardık. Yolun orta çizgisine olan mesafe checkpoint sayısından
    /// tamamen bağımsız, bu yüzden güvenli.
    /// </summary>
    private bool TryGetDistanceToTrack(out float distance)
    {
        distance = 0f;

        if (trackGenerator == null)
            trackGenerator = FindAnyObjectByType<TrackGenerator>();

        List<Vector3> points = trackGenerator != null ? trackGenerator.GetTrackPoints() : null;

        if (points == null || points.Count == 0)
        {
            if (!warnedMissingTrack)
            {
                warnedMissingTrack = true;
                Debug.LogWarning("[CheckpointRecovery] Pist noktaları okunamadı — kurtarma sistemi bekliyor.");
            }
            return false;
        }

        Vector3 carPos = transform.position;
        float closestSqr = float.MaxValue;

        // Karekök almadan karşılaştırıyoruz (sqrMagnitude) — birkaç bin nokta
        // üzerinde yarım saniyede bir dönüldüğü için maliyeti ihmal edilebilir.
        for (int i = 0; i < points.Count; i++)
        {
            float sqr = (points[i] - carPos).sqrMagnitude;
            if (sqr < closestSqr) closestSqr = sqr;
        }

        distance = Mathf.Sqrt(closestSqr);
        return true;
    }

    private void TriggerRecovery(int checkpointIndex, string reason)
    {
        if (recovering) return;
        if (!TryGetRespawnPose(checkpointIndex, out Vector3 position, out Quaternion rotation)) return;

        // Hangi sistemin tetiklediği loglanıyor — iki eşik (Max Distance From
        // Track / Confirm Delay) ayrı ayrı ayarlanacağı için hangisinin
        // devreye girdiğini görmek gerekiyor.
        Debug.Log($"[CheckpointRecovery] Kurtarma: {reason} → checkpoint {checkpointIndex}");

        recovering = true;
        StartCoroutine(RecoverRoutine(position, rotation));
    }

    private IEnumerator RecoverRoutine(Vector3 position, Quaternion rotation)
    {
        ScreenFader.PlaySequence(fadeDuration, holdSeconds, () =>
        {
            // Kararma sırasında yarış bitmiş olabilir (süre doldu / biri
            // bitirdi) — o durumda podyum ışınlaması devrede, araya girmemeliyiz.
            if (car == null || !car.IsNetworkOwned) return;
            if (raceController != null && !raceController.isRacing) return;

            car.TeleportTo(position, rotation);
        });

        // Perde tamamen açılana kadar yeni bir kurtarma başlamasın.
        yield return new WaitForSecondsRealtime(fadeDuration * 2f + holdSeconds);

        awayTimer = 0f;
        ResetGateState();
        recovering = false;
    }

    /// <summary>
    /// Hedef checkpoint'in konumu ve yolun gidiş yönü. Checkpoint'ler
    /// üretilirken forward'ları pistin gidiş yönüne bakacak şekilde
    /// döndürülüyor (bkz. TrackGenerator.GenerateCheckpoints), bu yüzden
    /// rotasyonu olduğu gibi kullanmak arabayı doğru yöne bakar hâlde bırakıyor.
    /// </summary>
    private bool TryGetRespawnPose(int index, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!TryGetCheckpoints(out List<Transform> checkpoints))
        {
            Debug.LogWarning("[CheckpointRecovery] CheckpointManager bulunamadı — ışınlama iptal.");
            return false;
        }

        // Yarış başlamadan CurrentCheckpoint -1 oluyor; o durumda başlangıç
        // çizgisi (0) mantıklı hedef.
        if (index < 0) index = 0;
        if (index >= checkpoints.Count) return false;

        Transform checkpoint = checkpoints[index];
        if (checkpoint == null) return false;

        position = checkpoint.position + Vector3.up * spawnHeightOffset;
        rotation = checkpoint.rotation;
        return true;
    }
}
