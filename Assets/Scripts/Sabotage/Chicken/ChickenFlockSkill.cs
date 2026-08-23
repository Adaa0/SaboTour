using UnityEngine;
using Mirror;

/// <summary>
/// TAVUK SÜRÜSÜ SKİLİ — DriftTrap/IceBombSkill ile aynı desen. Asıl tavuk
/// spawn mantığı zaten ChickenFlockManager.cs'de hazırdı (network'ten önce
/// yazılmıştı), burada sadece server-authoritative seçim + her client'ı
/// aynı anda tetikleyen bir [ClientRpc] köprüsü var.
///
/// Tavuklar (Chicken.cs) networked DEĞİL — her client kendi kopyasını
/// yerel olarak oluşturuyor (IceBombSkill'deki gerekçenin aynısı: araba
/// çarpışma/yavaşlatma fiziği owner'ın kendi client'ında doğru çalışmalı).
/// Bu yüzden farklı client'larda tavukların TAM pozisyonu birebir aynı
/// olmayabilir (her biri kendi rastgele spawn noktalarını hesaplıyor) —
/// geçici test için kabul edilebilir, ileride gerekirse seed senkronizasyonu
/// eklenebilir (TrackSeedSync'teki gibi).
/// </summary>
public class ChickenFlockSkill : NetworkBehaviour
{
    [Header("Cooldown")]
    [Tooltip("Tavuk sürüsü ateşlendikten sonra bu skilin tekrar kullanılabilir olması için beklenmesi gereken süre.")]
    [SerializeField] private float skillCooldownSeconds = 15f;

    private ChickenFlockManager flockManager;
    private CheckpointCooldownManager checkpointCooldown;
    private int selectedCheckpointIndex = -1;
    private float nextReadyTime = 0f;

    // ─── COOLDOWN'UN SABOTAJCI EKRANINA YANSIMASI ────────────────────────
    // nextReadyTime yukarıda SERVER'ın kendi kararı (Time.time ile). Aşağıdaki
    // SyncVar ise AYNI bilginin client'a geçen kopyası — sabotajcının kule
    // odasındaki buton ışığı bunu okuyup "hazır mı, ne kadar kaldı" diye
    // çiziyor. Detaylı gerekçe: SkillCooldownState.cs.
    [SyncVar] private SkillCooldownState cooldownState;

    /// <summary>Kalan cooldown (saniye). 0 ise skil hazır. HER client okuyabilir.</summary>
    public float CooldownRemaining => Mathf.Max(0f, (float)(cooldownState.endTime - NetworkTime.time));

    /// <summary>Bu cooldown turunun toplam süresi — ışığın dolum oranı için.</summary>
    public float CooldownTotal => cooldownState.duration;

    void Start()
    {
        flockManager = FindAnyObjectByType<ChickenFlockManager>();
        checkpointCooldown = FindAnyObjectByType<CheckpointCooldownManager>();
    }

    [Server]
    public void SelectCheckpoint(int index)
    {
        selectedCheckpointIndex = index;
    }

    /// <summary>
    /// Başarılıysa true döner. false dönerse ya skilin kendi cooldown'u ya
    /// da hedef checkpoint'in ortak cooldown'u (CheckpointCooldownManager)
    /// henüz dolmamıştır.
    /// </summary>
    [Server]
    public bool ActivateSkill()
    {
        if (selectedCheckpointIndex < 0) return false;
        if (Time.time < nextReadyTime) return false;
        if (checkpointCooldown != null && !checkpointCooldown.IsCheckpointReady(selectedCheckpointIndex)) return false;

        RpcSpawnFlock(selectedCheckpointIndex);

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

        checkpointCooldown?.StartCooldown(selectedCheckpointIndex);
        return true;
    }

    [ClientRpc]
    private void RpcSpawnFlock(int index)
    {
        if (flockManager == null)
            flockManager = FindAnyObjectByType<ChickenFlockManager>();

        flockManager?.SpawnChickenFlockAtCheckpoint(index);
    }
}
