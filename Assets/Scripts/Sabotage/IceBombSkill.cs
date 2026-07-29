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

    private CheckpointManager checkpointManager;
    private int selectedCheckpointIndex = -1;

    void Start()
    {
        checkpointManager = FindAnyObjectByType<CheckpointManager>();
    }

    [Server]
    public void SelectCheckpoint(int index)
    {
        if (checkpointManager == null || index < 0 || index >= checkpointManager.checkpoints.Count) return;
        selectedCheckpointIndex = index;
    }

    [Server]
    public void ActivateSkill()
    {
        if (selectedCheckpointIndex < 0 || checkpointManager == null) return;
        if (selectedCheckpointIndex >= checkpointManager.checkpoints.Count) return;

        if (launchPoint == null)
        {
            Debug.LogWarning("[IceBombSkill] Launch Point atanmamış! Inspector'dan fırlatma noktasını sürükle.");
            return;
        }

        Transform cp = checkpointManager.checkpoints[selectedCheckpointIndex];
        RpcLaunchIceBomb(launchPoint.position, cp.position, cp.rotation);
    }

    [ClientRpc]
    private void RpcLaunchIceBomb(Vector3 startPosition, Vector3 targetPosition, Quaternion targetRotation)
    {
        if (iceBombPrefab == null) return;

        GameObject bomb = Instantiate(iceBombPrefab, startPosition, targetRotation);
        IceBomb iceBomb = bomb.GetComponent<IceBomb>();

        if (iceBomb != null)
            iceBomb.Launch(startPosition, targetPosition, flightDuration, arcHeight);
    }
}
