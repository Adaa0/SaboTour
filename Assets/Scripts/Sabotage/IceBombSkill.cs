using UnityEngine;
using Mirror;

/// <summary>
/// BUZ BOMBASI SKİLİ — DriftTrap.cs ile birebir aynı network deseni:
/// server-authoritative checkpoint seçimi + aktivasyon. Aynı DriftTrap
/// GameObject'i üzerine eklenmesi yeterli (zaten NetworkIdentity'si var,
/// ayrı bir obje/NetworkIdentity kurmaya gerek yok).
///
/// GEÇİCİ TEST GİRİŞİ: SaboteurSkillInput'taki F tuşu ile tetikleniyor
/// (bkz. CLAUDE.md). İleride minimap/harita butonuna bağlanacak.
///
/// Buz bombasının patlama fiziği (IceBomb.cs içindeki rb.AddForce) her
/// arabanın SAHİBİ olan client'ta doğru çalışması gerektiği için (bkz.
/// CarController "owner kendi fiziğini hesaplar") — bombayı server'da
/// networked bir obje olarak SPAWN ETMİYORUZ. Onun yerine [ClientRpc] ile
/// HER client'a "şurada bir buz bombası oluştur" deniyor, her client kendi
/// yerel (network'e bağlı olmayan) IceBomb kopyasını oluşturuyor — tıpkı
/// checkpoint/drift tetiklemelerinin zaten her client'ın kendi fizik
/// dünyasında ayrı ayrı çalışması gibi.
/// </summary>
public class IceBombSkill : NetworkBehaviour
{
    [SerializeField] private GameObject iceBombPrefab;

    [Header("Fırlatma")]
    [Tooltip("Bombanın fırlatılacağı nokta — kule tepesi. Henüz kule modeli yok, geçici bir GameObject ile yüksekliği ayarlanabilir.")]
    [SerializeField] private Transform launchPoint;
    [SerializeField] private float flightDuration = 1.2f;
    [Tooltip("Uçuş eğrisinin tepe noktası ne kadar yukarı çıksın (parabol yüksekliği).")]
    [SerializeField] private float arcHeight = 15f;
    [Tooltip("Bombanın düşeceği nokta, checkpoint'in konumundan kaç metre AŞAĞIDA olsun. " +
             "Checkpoint objeleri yolun 5m üstünde duruyor (bkz. TrackGenerator), bu yüzden " +
             "bomba doğrudan checkpoint'e gönderilirse havada patlıyordu. Bu değer arttıkça " +
             "bomba daha aşağıda, yere gömülü gibi patlar.")]
    [SerializeField] private float landingDepthOffset = 2.5f;

    [Header("Cooldown")]
    [Tooltip("Buz bombası ateşlendikten sonra bu skilin tekrar kullanılabilir olması için beklenmesi gereken süre.")]
    [SerializeField] private float skillCooldownSeconds = 20f;

    private CheckpointManager checkpointManager;
    private CheckpointCooldownManager checkpointCooldown;
    private int selectedCheckpointIndex = -1;
    private float nextReadyTime = 0f;

    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();
        checkpointCooldown = FindAnyObjectByType<CheckpointCooldownManager>();
    }

    [Server]
    public void SelectCheckpoint(int index)
    {
        if (checkpointManager == null || index < 0 || index >= checkpointManager.checkpoints.Count) return;
        selectedCheckpointIndex = index;
    }

    /// <summary>
    /// Başarılıysa true döner. false dönerse ya skilin kendi cooldown'u ya
    /// da hedef checkpoint'in ortak cooldown'u (CheckpointCooldownManager)
    /// henüz dolmamıştır — SaboteurInteraction bunu sabotajcıya bildiriyor.
    /// </summary>
    [Server]
    public bool ActivateSkill()
    {
        if (selectedCheckpointIndex < 0 || checkpointManager == null) return false;
        if (selectedCheckpointIndex >= checkpointManager.checkpoints.Count) return false;

        if (launchPoint == null)
        {
            Debug.LogWarning("[IceBombSkill] Launch Point atanmamış! Inspector'dan fırlatma noktasını sürükle.");
            return false;
        }

        if (Time.time < nextReadyTime) return false;
        if (checkpointCooldown != null && !checkpointCooldown.IsCheckpointReady(selectedCheckpointIndex)) return false;

        Transform cp = checkpointManager.checkpoints[selectedCheckpointIndex];

        // Hedefi checkpoint'in Y'sinden landingDepthOffset kadar AŞAĞI al —
        // böylece bomba uçuşun sonunda zaten yere gömülü konumda oluyor,
        // sonradan ayrıca "batma" animasyonu oynatmaya gerek kalmıyor.
        Vector3 landingPos = cp.position + Vector3.down * landingDepthOffset;
        RpcLaunchIceBomb(launchPoint.position, landingPos, cp.rotation);

        nextReadyTime = Time.time + (checkpointCooldown != null
            ? checkpointCooldown.ScaleSkillCooldown(skillCooldownSeconds)
            : skillCooldownSeconds);
        checkpointCooldown?.StartCooldown(selectedCheckpointIndex);
        return true;
    }

    [ClientRpc]
    private void RpcLaunchIceBomb(Vector3 startPosition, Vector3 targetPosition, Quaternion targetRotation)
    {
        if (iceBombPrefab == null)
        {
            Debug.LogWarning("[IceBombSkill TEŞHİS] iceBombPrefab ATANMAMIŞ — bomba oluşturulamadı.");
            return;
        }

        GameObject bomb = Instantiate(iceBombPrefab, startPosition, targetRotation);
        IceBomb iceBomb = bomb.GetComponent<IceBomb>();

        if (iceBomb != null)
            iceBomb.Launch(startPosition, targetPosition, flightDuration, arcHeight);
        else
            Debug.LogWarning("[IceBombSkill TEŞHİS] Oluşturulan bombanın KÖK objesinde IceBomb script'i YOK " +
                             "(muhtemelen bir child objede). Bu yüzden Launch() çağrılamadı, bomba uçmuyor " +
                             "ve yere inmediği için sarsıntı da tetiklenmiyor.");
    }
}
