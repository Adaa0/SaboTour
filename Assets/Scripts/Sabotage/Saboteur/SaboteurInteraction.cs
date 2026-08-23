using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

/// <summary>
/// SABOTAJCI ETKİLEŞİM SİSTEMİ — SaboteurSkillInput.cs'in (klavye test
/// girişi) YERİNİ ALIYOR. Artık checkpoint/skil seçimi ve tetikleme,
/// minimap odasındaki fiziksel objelere BAKIP SOL TIKLAYARAK yapılıyor
/// (bkz. CLAUDE.md madde 6/11 — mouse + crosshair, klavye tuşu yok).
///
/// Sabotajcı FPS tarzı oynadığı için (Cursor.lockState = Locked, bkz.
/// SaboteurController.OnStartAuthority) imleç her zaman ekran ortasında —
/// crosshair = ekran merkezi, bu yüzden raycast doğrudan kameranın
/// merkezinden atılıyor.
///
/// DriftTrap/IceBombSkill/ChickenFlockSkill dosyalarında hiçbir değişiklik
/// yok — bu script sadece hangi skile hangi checkpoint index'inin
/// gönderileceğine ve ne zaman aktive edileceğine karar veriyor, asıl
/// server mantığı (SelectCheckpoint/ActivateTrap/ActivateSkill) aynen
/// duruyor.
/// </summary>
public class SaboteurInteraction : NetworkBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Raycast'in atılacağı kamera — SaboteurController'daki fpCam ile aynı obje.")]
    [SerializeField] private Transform fpCam;

    [Header("Etkileşim")]
    [Tooltip("Butonlara/checkpoint marker'lara ne kadar uzaktan tıklanabilsin (metre). Oda büyüdükçe bunu artır.")]
    [SerializeField] private float interactionRange = 8f;

    // ─── SESLER ──────────────────────────────────────────────────────────
    // Tıklama sesleri LOCAL çalıyor (network'e gitmiyor) — çünkü bunlar
    // sabotajcının kendi elinin altındaki fiziksel butonların sesi, odanın
    // dışından kimse duymuyor zaten. Yarışçılara ulaşması gereken sesler
    // (tuzak kuruldu, bomba fırlatıldı) ilgili skill dosyalarında ClientRpc
    // ile çalınıyor.
    //
    // 3D (PlayAt) tercih edildi, 2D değil: butonların odadaki konumu belli,
    // sağdaki butona basınca ses sağdan gelsin diye.
    [Header("Sesler (sadece sabotajcının kendi odasında duyulur)")]
    [Tooltip("3 skil butonundan birine tıklayınca çalan seçim sesi.")]
    [SerializeField] private AudioClip skillSelectClip;
    [Tooltip("Minimap'te bir checkpoint marker'ına tıklayınca çalan seçim sesi.")]
    [SerializeField] private AudioClip checkpointSelectClip;
    [Tooltip("Ortadaki büyük kırmızı tetik butonuna basınca çalan mekanik klik/kolu çekme sesi.")]
    [SerializeField] private AudioClip triggerClip;
    [Tooltip("Skil cooldown'daysa (henüz hazır değilse) çalan olumsuz 'çalışmadı' sesi. Oyuncunun neden bir şey olmadığını anlaması için ÖNEMLİ — şu an sadece Console'a yazı düşüyor, oyuncu hiçbir geri bildirim almıyor.")]
    [SerializeField] private AudioClip deniedClip;
    [Range(0f, 1f)][SerializeField] private float interactionVolume = 0.85f;

    [Header("Konsol Geri Bildirimi")]
    [Tooltip("KAPALI (varsayılan): cooldown'daki bir skil butonuna tıklanamaz — buton çökmez, " +
             "seçim değişmez, olumsuz ses çalar. Böylece 'sönük buton = ölü buton' kuralı net olur ve " +
             "SEÇİLİ skil HER ZAMAN ateşlenebilir olduğu için tetikte 'neden olmadı' sorusu kalkar.\n\n" +
             "AÇIK: cooldown'daki skil yine de seçilebilir (bir sonraki atışını önceden hazırlamak için). " +
             "Bu durumda tetiğe basınca 'henüz hazır değil' cevabı almaya devam edersin.")]
    [SerializeField] private bool allowArmingDuringCooldown = false;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // Şu an hangi skil "arm" edilmiş — sadece local, server'a gitmiyor.
    private SkillType armedSkill = SkillType.IceBomb;

    // ─── KONSOL GÖRSELLERİ İÇİN ÖNBELLEK ─────────────────────────────────
    // Skil scriptleri sahneye ELLE yerleştirilmiş NetworkIdentity'li objeler
    // (spawn edilmiyorlar), yani her client'ta baştan var olacaklar. Yine de
    // Start anında hazır olmayabilecekleri için tembel (lazy) aranıyorlar.
    private SkillSelectButton[] skillButtons;
    private IceBombSkill iceBombSkill;
    private ChickenFlockSkill chickenFlockSkill;
    private EngineFailureTrap engineFailureTrap;

    // Son seçilen checkpoint — sadece geri bildirim/kontrol için tutuluyor,
    // asıl değer server tarafında her skilin kendi içinde duruyor.
    private int selectedCheckpoint = -1;

    // Şu an minimap'te yeşille vurgulanan marker — yeni bir checkpoint
    // seçildiğinde bunun rengi geri (mora) alınıyor. Tamamen LOCAL/görsel,
    // network'e gitmiyor (MinimapController zaten local bir sistem).
    private MinimapCheckpointMarker selectedMarker;

    private SaboteurController saboteurController;

    void Awake()
    {
        saboteurController = GetComponent<SaboteurController>();
    }

    void Update()
    {
        if (!isOwned) return;
        if (fpCam == null) return;

        // İmleç ESC ile serbest bırakılmışsa (ya da bu kare içinde yeni
        // kilitlendiyse) tıklamalar skil tetiklemesin — o tık zaten imleci
        // geri kilitlemek için kullanılıyor.
        bool canInteract = saboteurController == null || saboteurController.CanInteract;

        // ─── HOVER RAYCAST (HER KARE) ────────────────────────────────────
        // Eskiden ışın SADECE tıklama anında atılıyordu. Hover takibi bir ara
        // eklenip beyaz outline denemesiyle birlikte kaldırılmıştı (bkz.
        // CLAUDE.md). Şimdi geri geldi çünkü kalan cooldown süresi "butona
        // BAKARKEN" gösteriliyor — bu bilgiye her karede ihtiyaç var.
        // Maliyeti TEK bir raycast ve sadece sabotajcının makinesinde
        // çalışıyor (8 Ağustos profilinde tüm fizik zaten 0.18ms).
        RaycastHit hit = default;
        bool hasHit = canInteract &&
                      Physics.Raycast(fpCam.position, fpCam.forward, out hit, interactionRange);

        UpdateConsoleVisuals(hasHit ? hit.collider : null);

        if (!canInteract || Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        if (!hasHit) return;

        Collider hitCollider = hit.collider;

        if (hitCollider.TryGetComponent(out SkillSelectButton skillButton))
        {
            // Cooldown'daki butonu SEÇTİRMİYORUZ (allowArmingDuringCooldown
            // kapalıysa). Basma animasyonu da bilerek oynatılmıyor — oyuncunun
            // "butona bastım ama çökmedi, demek ki ölü" diye anlaması için.
            // Sadece Console'a yazsaydık gerçek build'de Console olmadığı için
            // oyuncu hiçbir şey öğrenemezdi.
            if (!allowArmingDuringCooldown && GetSkillCooldownRemaining(skillButton.skill) > 0f)
            {
                SfxPlayer.PlayAt(deniedClip, hit.point, interactionVolume, 0f, 3f, 25f);
                if (showDebugLogs)
                    Debug.Log($"[SaboteurInteraction] {skillButton.skill} cooldown'da, seçilemedi.");
                return;
            }

            armedSkill = skillButton.skill;
            GetFeedback(hitCollider).PlayPress();
            SfxPlayer.PlayAt(skillSelectClip, hit.point, interactionVolume, 0.04f, 3f, 25f);
            if (showDebugLogs) Debug.Log($"[SaboteurInteraction] Skil seçildi: {armedSkill}");
            return;
        }

        if (hitCollider.TryGetComponent(out MinimapCheckpointMarker marker))
        {
            selectedCheckpoint = marker.checkpointIndex;
            GetFeedback(hitCollider).PlayPress();
            SfxPlayer.PlayAt(checkpointSelectClip, hit.point, interactionVolume, 0.04f, 3f, 25f);

            if (selectedMarker != null) selectedMarker.SetSelected(false);
            selectedMarker = marker;
            selectedMarker.SetSelected(true);

            if (showDebugLogs) Debug.Log($"[SaboteurInteraction] Checkpoint seçildi: {selectedCheckpoint}");
            CmdSelectCheckpoint(marker.checkpointIndex);
            return;
        }

        if (hitCollider.TryGetComponent(out TriggerButton _))
        {
            // Tetik butonunun KENDİ cooldown'u yok — "basınca bir şey olur mu"
            // durumu üç şeyin özeti: checkpoint seçili mi, o skil hazır mı,
            // o checkpoint hazır mı. Bu yüzden butona ayrı bir ışık KONMADI
            // (geliştirici kararı, 19 Ağustos): üç sebebin üçü de zaten odada
            // ayrı ayrı görünüyor — skil butonunun ışığı, minimap marker'ının
            // rengi, ve hiç kırmızı marker olmaması. Dördüncü bir gösterge
            // aynı bilgiyi tekrar söylemek olurdu.
            //
            // Ama basılamayacak durumdayken buton ÇÖKMÜYOR — skil butonlarıyla
            // aynı kural: tepki vermeyen buton "şu an olmaz" demektir.
            if (!IsTriggerReady(out string reason))
            {
                if (showDebugLogs) Debug.LogWarning($"[SaboteurInteraction] Tetiklenemedi — {reason}");
                SfxPlayer.PlayAt(deniedClip, hit.point, interactionVolume, 0f, 3f, 25f);
                return;
            }

            GetFeedback(hitCollider).PlayPress();
            SfxPlayer.PlayAt(triggerClip, hit.point, interactionVolume, 0.03f, 3f, 25f);
            if (showDebugLogs) Debug.Log($"[SaboteurInteraction] Tetikleniyor: {armedSkill} → checkpoint {selectedCheckpoint}");
            CmdActivateSkill(armedSkill);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  KONSOL GERİ BİLDİRİMİ — "oda konuşsun, HUD konuşmasın"
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Her karede 3 skil butonuna güncel durumunu bildirir. Butonlar bu
    /// bilgiyi nasıl göstereceğine kendileri karar veriyor (bkz.
    /// SkillSelectButton) — buradan sadece HAM DURUM gidiyor.
    ///
    /// Sadece sabotajcının kendi makinesinde çalışıyor (Update başındaki
    /// isOwned kontrolü) — kulede başka kimse olmadığı için butonların
    /// diğer oyuncularda güncellenmesine gerek yok, network mesajı da
    /// gerekmiyor.
    /// </summary>
    private void UpdateConsoleVisuals(Collider hovered)
    {
        if (skillButtons == null || skillButtons.Length == 0)
            skillButtons = FindObjectsByType<SkillSelectButton>(FindObjectsSortMode.None);

        SkillSelectButton hoveredButton = null;
        if (hovered != null) hovered.TryGetComponent(out hoveredButton);

        foreach (SkillSelectButton button in skillButtons)
        {
            if (button == null) continue;

            GetSkillCooldown(button.skill, out float remaining, out float total);
            button.UpdateVisualState(
                isArmed: button.skill == armedSkill,
                remaining: remaining,
                total: total,
                isHovered: button == hoveredButton,
                viewerTransform: fpCam);
        }
    }

    /// <summary>
    /// Bir skilin kalan/toplam cooldown'unu CLIENT tarafında okur.
    ///
    /// Bu değerler skil scriptlerindeki SyncVar'dan geliyor (bkz.
    /// SkillCooldownState.cs). SyncVar HOOK'u KULLANMIYORUZ, her karede
    /// doğrudan okuyoruz — bu projede host'un hook almadığı bir durum iki
    /// kere yaşandı (CarController "donmuş araba", RacerSpectator), iki
    /// float okumanın maliyeti de yok.
    /// </summary>
    private void GetSkillCooldown(SkillType skill, out float remaining, out float total)
    {
        remaining = 0f;
        total = 0f;

        switch (skill)
        {
            case SkillType.IceBomb:
                if (iceBombSkill == null) iceBombSkill = FindAnyObjectByType<IceBombSkill>();
                if (iceBombSkill != null)
                {
                    remaining = iceBombSkill.CooldownRemaining;
                    total = iceBombSkill.CooldownTotal;
                }
                break;

            case SkillType.ChickenFlock:
                if (chickenFlockSkill == null) chickenFlockSkill = FindAnyObjectByType<ChickenFlockSkill>();
                if (chickenFlockSkill != null)
                {
                    remaining = chickenFlockSkill.CooldownRemaining;
                    total = chickenFlockSkill.CooldownTotal;
                }
                break;

            case SkillType.EngineFailure:
                if (engineFailureTrap == null) engineFailureTrap = FindAnyObjectByType<EngineFailureTrap>();
                if (engineFailureTrap != null)
                {
                    remaining = engineFailureTrap.CooldownRemaining;
                    total = engineFailureTrap.CooldownTotal;
                }
                break;
        }
    }

    private float GetSkillCooldownRemaining(SkillType skill)
    {
        GetSkillCooldown(skill, out float remaining, out _);
        return remaining;
    }

    /// <summary>
    /// "Tetiğe şimdi bassam bir şey olur mu?" — server'daki üç kontrolün
    /// (ActivateSkill içindeki) client tarafındaki aynası. Server yine de
    /// kendi kontrolünü yapıyor, burası SADECE görsel/işitsel geri bildirim
    /// için: sunucudan cevap beklemeden butonun çöküp çökmeyeceğine karar
    /// verebilmek gerekiyor.
    ///
    /// Checkpoint'in cooldown'u için ekstra network mesajı GEREKMİYOR —
    /// CheckpointCooldownManager zaten RpcPlayCooldownVisual ile her client'ın
    /// minimap marker'ını boyuyor, biz de o marker'ın IsOnCooldown'unu okuyoruz.
    /// </summary>
    private bool IsTriggerReady(out string reason)
    {
        if (selectedCheckpoint < 0)
        {
            reason = "önce minimap'ten bir checkpoint seç";
            return false;
        }

        if (GetSkillCooldownRemaining(armedSkill) > 0f)
        {
            reason = $"{armedSkill} henüz hazır değil (buton sönük)";
            return false;
        }

        if (selectedMarker != null && selectedMarker.IsOnCooldown)
        {
            reason = $"checkpoint {selectedCheckpoint} az önce tuzaklandı (marker henüz yeşile dönmedi)";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// InteractableFeedback'i ÇARPILAN collider'ın kendi objesine değil, o
    /// objenin FeedbackRoot'una (bkz. SkillSelectButton/TriggerButton/
    /// MinimapCheckpointMarker) ekler. NEDEN: çok parçalı butonlarda (kaide +
    /// kubbe ayrı objeler) collider genelde tek bir parçanın üzerinde oluyor —
    /// feedback'i doğrudan oraya koysaydık sadece o parça küçülüp büyürdü,
    /// diğer parçalar yerinde kalırdı. FeedbackRoot, geliştiricinin Inspector'dan
    /// atadığı "tüm parçaları kapsayan üst obje" — atanmadıysa kendisi kullanılır.
    /// </summary>
    private static InteractableFeedback GetFeedback(Collider col)
    {
        Transform root = ResolveFeedbackRoot(col);

        InteractableFeedback feedback = root.GetComponent<InteractableFeedback>();
        if (feedback == null) feedback = root.gameObject.AddComponent<InteractableFeedback>();
        return feedback;
    }

    private static Transform ResolveFeedbackRoot(Collider col)
    {
        if (col.TryGetComponent(out SkillSelectButton skillBtn)) return skillBtn.FeedbackRoot;
        if (col.TryGetComponent(out TriggerButton triggerBtn)) return triggerBtn.FeedbackRoot;
        if (col.TryGetComponent(out MinimapCheckpointMarker marker)) return marker.FeedbackRoot;
        return col.transform;
    }

    /// <summary>
    /// Seçilen checkpoint ÜÇ SKİLE BİRDEN bildiriliyor (eski klavye testindeki
    /// davranışın aynısı). NEDEN: Sadece o an seçili skile bildirseydik,
    /// "önce checkpoint'e tıkla, sonra skil butonuna bas" sırasıyla oynayan
    /// biri için o skilin checkpoint'i hiç ayarlanmamış kalır ve tetikleme
    /// sessizce hiçbir şey yapmazdı. Bu şekilde tıklama sırası önemli değil.
    /// </summary>
    [Command]
    private void CmdSelectCheckpoint(int index)
    {
        IceBombSkill iceBomb = FindAnyObjectByType<IceBombSkill>();
        ChickenFlockSkill chicken = FindAnyObjectByType<ChickenFlockSkill>();
        EngineFailureTrap engineTrap = FindAnyObjectByType<EngineFailureTrap>();

        iceBomb?.SelectCheckpoint(index);
        chicken?.SelectCheckpoint(index);
        engineTrap?.SelectCheckpoint(index);

        if (iceBomb == null || chicken == null || engineTrap == null)
        {
            Debug.LogWarning($"[SaboteurInteraction] Skill component'i bulunamadı! " +
                             $"IceBomb={iceBomb != null}, ChickenFlock={chicken != null}, EngineFailure={engineTrap != null}");
        }

        TargetLog($"Checkpoint {index} seçildi.");
    }

    [Command]
    private void CmdActivateSkill(SkillType skill)
    {
        // Her skil metodu artık bool dönüyor — false ise ya skilin KENDİ
        // cooldown'u (skillCooldownSeconds) ya da hedef checkpoint'in ortak
        // cooldown'u (CheckpointCooldownManager) henüz dolmamış demektir.
        bool success;

        switch (skill)
        {
            case SkillType.IceBomb:
            {
                IceBombSkill s = FindAnyObjectByType<IceBombSkill>();
                if (s == null) { TargetLog("HATA: IceBombSkill sahnede bulunamadı!"); return; }
                success = s.ActivateSkill();
                break;
            }
            case SkillType.ChickenFlock:
            {
                ChickenFlockSkill s = FindAnyObjectByType<ChickenFlockSkill>();
                if (s == null) { TargetLog("HATA: ChickenFlockSkill sahnede bulunamadı!"); return; }
                success = s.ActivateSkill();
                break;
            }
            case SkillType.EngineFailure:
            {
                EngineFailureTrap s = FindAnyObjectByType<EngineFailureTrap>();
                if (s == null) { TargetLog("HATA: EngineFailureTrap sahnede bulunamadı!"); return; }
                success = s.ActivateTrap();
                break;
            }
            default:
                return;
        }

        TargetLog(success ? $"{skill} AKTİF!" : $"{skill} henüz hazır değil (cooldown'da).");

        // Başarısızlığın SESLİ geri bildirimi. Cooldown kararı SERVER'da
        // veriliyor (yukarıdaki ActivateSkill çağrıları), bu yüzden sabotajcı
        // butona basarken henüz hazır olup olmadığını bilmiyor — sonucu
        // öğrenmenin tek yolu server'ın cevabı. TargetRpc, Mirror'da sadece
        // bu objenin sahibi olan client'ta çalışır, yani sesi yalnızca
        // sabotajcı duyar.
        if (!success) TargetPlayDenied();
    }

    [TargetRpc]
    private void TargetPlayDenied()
    {
        SfxPlayer.PlayUI(deniedClip, interactionVolume);
    }

    /// <summary>
    /// Server'dan SADECE bu sabotajcının sahibi olan client'a geri bildirim.
    /// Henüz gerçek bir kule UI'ı olmadığı için Console'a yazıyor.
    /// </summary>
    [TargetRpc]
    private void TargetLog(string message)
    {
        Debug.Log($"[Sabotajcı] {message}");
    }
}
