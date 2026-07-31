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

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // Şu an hangi skil "arm" edilmiş — sadece local, server'a gitmiyor.
    private SkillType armedSkill = SkillType.IceBomb;

    // Son seçilen checkpoint — sadece geri bildirim/kontrol için tutuluyor,
    // asıl değer server tarafında her skilin kendi içinde duruyor.
    private int selectedCheckpoint = -1;

    private SaboteurController saboteurController;

    void Awake()
    {
        saboteurController = GetComponent<SaboteurController>();
    }

    void Update()
    {
        if (!isOwned) return;
        if (fpCam == null || Mouse.current == null) return;

        // İmleç ESC ile serbest bırakılmışsa (ya da bu kare içinde yeni
        // kilitlendiyse) tıklamalar skil tetiklemesin — o tık zaten imleci
        // geri kilitlemek için kullanılıyor.
        if (saboteurController != null && !saboteurController.CanInteract) return;

        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        if (!Physics.Raycast(fpCam.position, fpCam.forward, out RaycastHit hit, interactionRange))
            return;

        Collider hitCollider = hit.collider;

        if (hitCollider.TryGetComponent(out SkillSelectButton skillButton))
        {
            armedSkill = skillButton.skill;
            GetFeedback(hitCollider).PlayPress();
            if (showDebugLogs) Debug.Log($"[SaboteurInteraction] Skil seçildi: {armedSkill}");
            return;
        }

        if (hitCollider.TryGetComponent(out MinimapCheckpointMarker marker))
        {
            selectedCheckpoint = marker.checkpointIndex;
            GetFeedback(hitCollider).PlayPress();
            if (showDebugLogs) Debug.Log($"[SaboteurInteraction] Checkpoint seçildi: {selectedCheckpoint}");
            CmdSelectCheckpoint(marker.checkpointIndex);
            return;
        }

        if (hitCollider.TryGetComponent(out TriggerButton _))
        {
            // Checkpoint seçilmeden tetiklenirse server tarafında sessizce
            // hiçbir şey olmuyordu — sebebini görebilmek için uyarı veriyoruz.
            if (selectedCheckpoint < 0)
            {
                Debug.LogWarning("[SaboteurInteraction] Önce minimap'ten bir checkpoint seç, sonra tetikle!");
                return;
            }

            GetFeedback(hitCollider).PlayPress();
            if (showDebugLogs) Debug.Log($"[SaboteurInteraction] Tetikleniyor: {armedSkill} → checkpoint {selectedCheckpoint}");
            CmdActivateSkill(armedSkill);
        }
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
        DriftTrap driftTrap = FindAnyObjectByType<DriftTrap>();

        iceBomb?.SelectCheckpoint(index);
        chicken?.SelectCheckpoint(index);
        driftTrap?.SelectCheckpoint(index);

        if (iceBomb == null || chicken == null || driftTrap == null)
        {
            Debug.LogWarning($"[SaboteurInteraction] Skill component'i bulunamadı! " +
                             $"IceBomb={iceBomb != null}, ChickenFlock={chicken != null}, DriftTrap={driftTrap != null}");
        }

        TargetLog($"Checkpoint {index} seçildi.");
    }

    [Command]
    private void CmdActivateSkill(SkillType skill)
    {
        switch (skill)
        {
            case SkillType.IceBomb:
            {
                IceBombSkill s = FindAnyObjectByType<IceBombSkill>();
                if (s == null) { TargetLog("HATA: IceBombSkill sahnede bulunamadı!"); return; }
                s.ActivateSkill();
                break;
            }
            case SkillType.ChickenFlock:
            {
                ChickenFlockSkill s = FindAnyObjectByType<ChickenFlockSkill>();
                if (s == null) { TargetLog("HATA: ChickenFlockSkill sahnede bulunamadı!"); return; }
                s.ActivateSkill();
                break;
            }
            case SkillType.DriftTrap:
            {
                DriftTrap s = FindAnyObjectByType<DriftTrap>();
                if (s == null) { TargetLog("HATA: DriftTrap sahnede bulunamadı!"); return; }
                s.ActivateTrap();
                break;
            }
        }

        TargetLog($"{skill} AKTİF!");
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
