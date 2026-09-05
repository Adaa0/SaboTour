using UnityEngine;
using Mirror;

/// <summary>
/// MOTOR ARIZASI TUZAĞI — sabotajcının 3. yeteneği.
///
/// ══ BU DOSYA ESKİ "DriftTrap"İN YERİNE GEÇTİ ══
/// Eski tasarım şöyleydi: checkpoint N'den geçen araç takibe alınıyor, N ile
/// N+1 ARASINDA ne kadar drift yaptığı ölçülüyor, N+1'e VARINCA ceza
/// uygulanıyordu. Üç ayrı sebepten çalışmadı:
///   1. Ceza, suçun işlendiği andan çok sonra geliyordu — oyuncu neden
///      yavaşladığını anlamıyor, sebep-sonuç bağı kopuyordu.
///   2. Görünmezdi. Buz bombası ve tavuk sürüsü ekranda apaçık dururken bu
///      yetenek ne yarışçı ne de SABOTAJCI tarafından fark ediliyordu —
///      yani butona basan oyuncunun da "yakaladım" anı yoktu.
///   3. Arcade bir yarış oyununda drift'i cezalandırmak, oyuncuya oyunun en
///      zevkli şeyini yasaklamak demekti.
///
/// YENİ TASARIM — KOŞULSUZ VE ANLIK:
/// Tuzak kurulur, o checkpoint'ten geçen İLK araç anında motor arızasına
/// girer. Drift ölçümü YOK, bekleme penceresi YOK, gecikme YOK. Tetikleyici
/// ile sonuç aynı karede. "Mayına bastın, patladı" mantığı.
///
/// İVME NEDEN ANİDEN DÜŞMÜYOR: motor gücü bir eğriyle iniyor ve yine bir
/// eğriyle toparlanıyor (bkz. CarController.ApplyEngineFailure). Anlık
/// kesme "ucuz"/bozuk hissettiriyordu; bu haliyle araç boğulup öksürerek
/// güç kaybediyor, sonra kendini toparlıyor.
///
/// ══ SAHNE KURULUMU (değişmedi) ══
/// Bu component Online Scene'de TEK bir GameObject üzerinde duruyor
/// (+ NetworkIdentity). Spawn edilen bir prefab DEĞİL. Sahneye yeni
/// eklenirse Ctrl+S şart (sceneId ataması için).
/// </summary>
public class EngineFailureTrap : NetworkBehaviour
{
    [Header("Tuzak")]
    [Tooltip("Tuzak kurulduktan sonra kaç saniye kurulu kalsın. Bu süre içinde kimse o checkpoint'ten geçmezse tuzak boşa gider. Uzun tutmak sabotajcı için daha az sinir bozucu — mayın gibi bekler.")]
    [SerializeField] private float armedWindowSeconds = 25f;

    [Header("Arıza Şiddeti")]
    [Tooltip("Arızanın en dip noktasında motor gücü bu orana düşer (1 = normal, 0.3 = gücün üçte biri). Aracı TAMAMEN durdurmuyoruz — durdurmak haksız hissettiriyor ve oyuncuyu ekrana bakıp beklemeye mahkûm ediyor.")]
    [Range(0.05f, 1f)][SerializeField] private float minAccelerationMultiplier = 0.3f;
    [Tooltip("Gücün dibe inmesi kaç saniye sürsün. 0 yaparsan anlık keser — 'ucuz' görünmemesi için kısa ama sıfırdan büyük bir değer bırak.")]
    [SerializeField] private float rampInSeconds = 0.35f;
    [Tooltip("Dipte kaç saniye kalsın — asıl 'stun' süresi bu.")]
    [SerializeField] private float holdSeconds = 1.4f;
    [Tooltip("Motorun normale dönmesi kaç saniye sürsün. Buranın uzun olması toparlanmayı hissettiriyor.")]
    [SerializeField] private float rampOutSeconds = 1f;
    [Tooltip("ASIL HİSSEDİLEN AYAR BU: arıza sırasında araca uygulanan, hıza orantılı fren gücü. 0 yaparsan tuzak sadece 'hızlanamıyorum' hissi verir ve zaten hızlı giden oyuncu HİÇBİR ŞEY fark etmez (ilk versiyonun sorunu buydu). Büyüttükçe araç daha sert boğulur; 3 civarı iyi bir başlangıç.")]
    [SerializeField] private float brakeStrength = 3f;

    [Header("Cooldown")]
    [Tooltip("Tuzak kurulduktan sonra bu yeteneğin tekrar kullanılabilmesi için geçmesi gereken süre. Yarışçı sayısına göre CheckpointCooldownManager tarafından ölçekleniyor.")]
    [SerializeField] private float skillCooldownSeconds = 15f;

    [Header("Checkpoint Seçim Göstergesi")]
    [Tooltip("Seçilen checkpoint'in üzerinde beliren ok (Assets/Prefabs/Ok.prefab).")]
    [SerializeField] private GameObject checkpointArrowPrefab;
    [SerializeField] private float arrowHeightOffset = 3f;

    [Header("Sesler")]
    [Tooltip("Tuzak KURULURKEN hedef checkpoint'te çalar. Herkes duyar — yaklaşan yarışçı 'burada bir şey oldu' diye tedirgin olsun diye.")]
    [SerializeField] private AudioClip trapArmedClip;
    [Range(0f, 1f)][SerializeField] private float trapArmedVolume = 0.9f;
    [Tooltip("Motor arızası PATLADIĞI anda kurbanın konumunda çalar. Öksüren/boğulan motor, kıvılcım, metal sesi gibi bir şey olmalı.")]
    [SerializeField] private AudioClip engineFailureClip;
    [Range(0f, 1f)][SerializeField] private float engineFailureVolume = 1f;

    [Header("Kamera Sarsıntısı")]
    [Tooltip("Arıza anında bu yarıçap içindeki kameralar sarsılır — kurban dışındaki yakın yarışçılar da hisseder.")]
    [SerializeField] private float shakeRadius = 45f;
    [SerializeField] private float shakeStrength = 0.55f;

    [Header("Ekran Yazıları")]
    [Tooltip("AÇIK (önerilen): yazılar Loc.cs sözlüğünden, her oyuncunun KENDİ " +
             "dilinde gelir. KAPALI: aşağıdaki iki alan aynen kullanılır (tek " +
             "dilde). Sahnede kayıtlı eski yazıları geri istersen kapat.")]
    [SerializeField] private bool useLocalizedMessages = true;
    [Tooltip("Arızayı YİYEN yarışçının ekranında görünür. Sadece 'Use Localized Messages' KAPALIYKEN kullanılır.")]
    [SerializeField] private string victimMessage = "⚠ MOTOR ARIZASI!\nGüç kesildi";
    [Tooltip("SABOTAJCININ ekranında görünür — butona bastığının karşılığını görmesi için. {0} yerine yakalanan oyuncunun adı yazılır. Sadece 'Use Localized Messages' KAPALIYKEN kullanılır.")]
    [SerializeField] private string saboteurMessage = "Motor arızası tetiklendi!\n{0} yakalandı";
    [SerializeField] private float noticeSeconds = 2.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // ─── Durum (sadece server'da anlamlı) ────────────────────────────────
    private int trapCheckpointIndex = -1;
    private bool trapArmed = false;
    private float trapArmedTime = 0f;
    private int selectedCheckpointIndex = -1;
    private float nextReadyTime = 0f;

    private CheckpointManager checkpointManager;
    private CheckpointCooldownManager checkpointCooldown;

    // ─── COOLDOWN'UN SABOTAJCI EKRANINA YANSIMASI ────────────────────────
    // nextReadyTime yukarıda SERVER'ın kendi kararı (Time.time ile). Aşağıdaki
    // SyncVar ise AYNI bilginin client'a geçen kopyası — sabotajcının kule
    // odasındaki buton ışığı bunu okuyup "hazır mı, ne kadar kaldı" diye
    // çiziyor. Detaylı gerekçe: SkillCooldownState.cs.
    [SyncVar] private SkillCooldownState cooldownState;

    /// <summary>Kalan cooldown (saniye). 0 ise yetenek hazır. HER client okuyabilir.</summary>
    public float CooldownRemaining => Mathf.Max(0f, (float)(cooldownState.endTime - NetworkTime.time));

    /// <summary>Bu cooldown turunun toplam süresi — ışığın dolum oranı için.</summary>
    public float CooldownTotal => cooldownState.duration;

    // Ok göstergesi HER client'ta ayrı tutulur (ClientRpc ile senkronize) —
    // networked bir obje değil, sadece görsel işaretçi.
    private GameObject arrowInstance;

    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();
        checkpointCooldown = FindAnyObjectByType<CheckpointCooldownManager>();

        if (checkpointManager == null)
            Debug.LogWarning("[EngineFailureTrap] CheckpointManager bulunamadı!");
        else if (showDebugLogs)
            Debug.Log("[EngineFailureTrap] Hazır.");
    }

    void Update()
    {
        // Server-authoritative: tuzak durumu sadece server'da geçerli.
        if (!isServer || !trapArmed) return;

        if (Time.time - trapArmedTime > armedWindowSeconds)
        {
            trapArmed = false;
            if (showDebugLogs) Debug.Log("[EngineFailureTrap] Kimse gelmedi, tuzak söndü.");
        }
    }

    #region INPUT — SaboteurInteraction'dan [Command] üzerinden geliyor

    /// <summary>Sabotajcı minimap'te bir checkpoint'e tıkladı.</summary>
    [Server]
    public void SelectCheckpoint(int index)
    {
        if (checkpointManager == null || index < 0 || index >= checkpointManager.checkpoints.Count)
        {
            if (showDebugLogs) Debug.LogWarning($"[EngineFailureTrap] Checkpoint {index} mevcut değil.");
            return;
        }

        selectedCheckpointIndex = index;
        if (showDebugLogs) Debug.Log($"[EngineFailureTrap] Checkpoint {index} seçildi.");

        RpcShowCheckpointArrow(index);
    }

    /// <summary>
    /// Sabotajcı kırmızı tetik butonuna bastı. Başarılıysa true döner;
    /// false dönerse ya yeteneğin kendi cooldown'u ya da hedef checkpoint'in
    /// ortak cooldown'u dolmamıştır.
    /// </summary>
    [Server]
    public bool ActivateTrap()
    {
        if (selectedCheckpointIndex < 0)
        {
            if (showDebugLogs) Debug.LogWarning("[EngineFailureTrap] Önce bir checkpoint seç!");
            return false;
        }
        if (checkpointManager == null || checkpointManager.checkpoints.Count == 0) return false;
        if (Time.time < nextReadyTime) return false;
        if (checkpointCooldown != null && !checkpointCooldown.IsCheckpointReady(selectedCheckpointIndex)) return false;

        trapCheckpointIndex = selectedCheckpointIndex;
        trapArmed = true;
        trapArmedTime = Time.time;

        RpcClearCheckpointArrow();
        RpcPlayTrapArmedSound(trapCheckpointIndex);

        if (showDebugLogs)
            Debug.Log($"[EngineFailureTrap] ⚠️ Tuzak kuruldu → CP {trapCheckpointIndex}. " +
                      $"Oradan geçen İLK araç arızalanacak ({armedWindowSeconds}s içinde).");

        float cooldown = checkpointCooldown != null
            ? checkpointCooldown.ScaleSkillCooldown(skillCooldownSeconds)
            : skillCooldownSeconds;

        nextReadyTime = Time.time + cooldown;

        // Aynı bilgiyi client'a da yolla (buton ışığı için). Zaman NetworkTime
        // ile — Time.time client'ta anlamsız olurdu, bkz. SkillCooldownState.cs.
        cooldownState = new SkillCooldownState
        {
            endTime = NetworkTime.time + cooldown,
            duration = cooldown
        };

        checkpointCooldown?.StartCooldown(trapCheckpointIndex);

        return true;
    }

    [ClientRpc]
    private void RpcShowCheckpointArrow(int index)
    {
        if (checkpointArrowPrefab == null || checkpointManager == null) return;
        if (index < 0 || index >= checkpointManager.checkpoints.Count) return;

        Transform cp = checkpointManager.checkpoints[index];
        Vector3 pos = cp.position + Vector3.up * arrowHeightOffset;

        if (arrowInstance == null)
            arrowInstance = Instantiate(checkpointArrowPrefab, pos, cp.rotation);
        else
        {
            arrowInstance.transform.SetPositionAndRotation(pos, cp.rotation);
            arrowInstance.SetActive(true);
        }
    }

    [ClientRpc]
    private void RpcClearCheckpointArrow()
    {
        if (arrowInstance != null) arrowInstance.SetActive(false);
    }

    /// <summary>
    /// Kurulma sesi HER client'ta, hedef checkpoint'in konumunda çalar.
    ///
    /// NEDEN AYRI BİR ClientRpc: ActivateTrap [Server] — yani o kod sadece
    /// server makinesinde çalışıyor. Sesi orada çalsaydık yalnızca host
    /// duyardı, gerçek client'lar hiçbir şey duymazdı.
    /// </summary>
    [ClientRpc]
    private void RpcPlayTrapArmedSound(int index)
    {
        if (checkpointManager == null || index < 0 || index >= checkpointManager.checkpoints.Count) return;

        Transform cp = checkpointManager.checkpoints[index];
        if (cp != null)
            SfxPlayer.PlayAt(trapArmedClip, cp.position, trapArmedVolume, 0.05f, 12f, 150f);
    }

    #endregion

    #region SERVER — Tetikleme

    /// <summary>
    /// Checkpoint.cs çağırıyor (bir araç checkpoint'e ulaştığında, sadece
    /// server'da). Tuzaklı checkpoint'e İLK giren aracı anında arızalandırır.
    ///
    /// TEK KULLANIMLIK OLMASI BİLİNÇLİ: tuzak patladıktan sonra kapanıyor,
    /// yani arkadan gelenler serbest geçiyor. Eski tasarımda bu bir BUG'dı
    /// (ceza uygulanınca tuzak herkes için kapanıyordu ama tasarım "herkesi
    /// yakala" olduğu için tutarsızdı); yeni tasarımda kasıtlı bir kural —
    /// sabotajcı "kimi yakalayacağım" zamanlamasını düşünmek zorunda.
    /// </summary>
    [Server]
    public void OnCarReachedCheckpoint(CarController car, PlayerRaceController raceController, int checkpointIndex)
    {
        if (!trapArmed || car == null || raceController == null) return;
        if (checkpointIndex != trapCheckpointIndex) return;
        if (Time.time - trapArmedTime > armedWindowSeconds) { trapArmed = false; return; }

        trapArmed = false; // tek kullanımlık — patladı

        Vector3 hitPosition = car.transform.position;

        // 1) HERKES: ses + kamera sarsıntısı (mesafeye göre azalıyor, yani
        //    yakındaki diğer yarışçılar da olayı hissediyor).
        RpcPlayFailureEffects(hitPosition);

        // 2) KURBAN: motor gücü düşüşü + fren + ekran yazısı.
        TargetVictimFeedback(raceController.connectionToClient,
                             minAccelerationMultiplier, rampInSeconds, holdSeconds, rampOutSeconds,
                             brakeStrength);

        // 3) SABOTAJCI: butona bastığının karşılığını görsün.
        NotifySaboteur(raceController.PlayerLabel);

        if (showDebugLogs)
            Debug.Log($"[EngineFailureTrap] 💥 {raceController.PlayerLabel} CP {checkpointIndex}'te arızalandı " +
                      $"(güç ×{minAccelerationMultiplier}, toplam {rampInSeconds + holdSeconds + rampOutSeconds:F1}s).");
    }

    /// <summary>
    /// Sabotajcının bağlantısını bulup ona bildirim gönderir.
    ///
    /// NEDEN GEREKLİ: konsey değerlendirmesinde çıkan en önemli tespit —
    /// sabotajcı da bir oyuncu. Butona basıp ekranda hiçbir şey olmuyorsa
    /// onun da "yakaladım" anı olmuyor. Eski tasarımın iki tarafı birden
    /// ıskalamasının sebebi buydu.
    /// </summary>
    [Server]
    private void NotifySaboteur(string victimLabel)
    {
        SaboteurController saboteur = FindAnyObjectByType<SaboteurController>();
        if (saboteur == null || saboteur.connectionToClient == null) return;

        // 🚨 DİL: mesaj artık SUNUCUDA formatlanmıyor, sadece yakalanan
        // oyuncunun ADI gönderiliyor — cümleyi sabotajcının kendi makinesi
        // kuruyor. Önceden burada string.Format yapılıyordu ve sunucunun dili
        // kazanıyordu: Türkçe oynayan bir host'un yanındaki İngilizce oynayan
        // sabotajcı, ayarını İngilizce yapmış olmasına rağmen Türkçe uyarı
        // görürdü. Aynı kural her TargetRpc/ClientRpc için geçerli — ağdan
        // HAZIR CÜMLE değil, VERİ geçir; cümleyi alıcı kursun.
        if (!string.IsNullOrEmpty(victimLabel))
            TargetSaboteurFeedback(saboteur.connectionToClient, victimLabel);
    }

    #endregion

    #region CLIENT — Geri bildirim

    /// <summary>Ses + kamera sarsıntısı: HERKESTE çalışır, mesafeye göre zayıflar.</summary>
    [ClientRpc]
    private void RpcPlayFailureEffects(Vector3 position)
    {
        SfxPlayer.PlayAt(engineFailureClip, position, engineFailureVolume, 0.06f, 10f, 120f);
        ExplosionCameraShake.ShakeAt(position, shakeRadius, shakeStrength);
    }

    /// <summary>
    /// SADECE arızayı yiyen yarışçıda çalışır (Mirror'da TargetRpc, hedef
    /// bağlantıya özel). Motor gücü kısıtlaması burada uygulanıyor.
    /// </summary>
    [TargetRpc]
    private void TargetVictimFeedback(NetworkConnection target, float min, float rampIn, float hold, float rampOut, float brake)
    {
        NetworkIdentity local = NetworkClient.localPlayer;

        if (local != null && local.TryGetComponent(out CarController car))
        {
            car.ApplyEngineFailure(min, rampIn, hold, rampOut, brake);
        }
        else
        {
            Debug.LogWarning("[EngineFailureTrap] Kurbanın CarController'ı bulunamadı — " +
                             "arıza uygulanamadı (ekran yazısı yine de gösterilecek).");
        }

        // Bu bir TargetRpc, yani KURBANIN kendi makinesinde çalışıyor —
        // çeviri burada yapıldığı için doğru dile düşüyor.
        if (useLocalizedMessages)
            ScreenNotice.Show(Loc.T("warn.enginefailure"), noticeSeconds);
        else if (!string.IsNullOrEmpty(victimMessage))
            ScreenNotice.Show(victimMessage, noticeSeconds);
    }

    /// <summary>
    /// Sadece sabotajcının ekranında çalışır. Parametre HAZIR CÜMLE değil,
    /// yakalanan oyuncunun ADI — cümle burada, sabotajcının kendi dilinde
    /// kuruluyor (bkz. NotifySaboteur'daki açıklama).
    /// </summary>
    [TargetRpc]
    private void TargetSaboteurFeedback(NetworkConnection target, string victimLabel)
    {
        string message = useLocalizedMessages
            ? Loc.T("warn.caught", victimLabel)
            : string.Format(saboteurMessage, victimLabel);

        ScreenNotice.Show(message, noticeSeconds);
    }

    #endregion

    #region Gizmos

    void OnDrawGizmos()
    {
        if (!trapArmed || checkpointManager == null) return;
        if (trapCheckpointIndex < 0 || trapCheckpointIndex >= checkpointManager.checkpoints.Count) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(checkpointManager.checkpoints[trapCheckpointIndex].position, 5f);
    }

    #endregion
}
