using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Checkpoint BAZLI ortak cooldown. Hangi skil (Buz Bombası/Tavuk Sürüsü/
/// Drift Trap) ateşlenirse ateşlensin, o checkpoint bir süre (checkpointCooldownSeconds)
/// TÜM skiller için tekrar hedef alınamaz hale gelir — bu, DriftTrap.cs gibi
/// TEK bir NetworkIdentity'si olan, sahneye elle yerleştirilmiş bir obje
/// (server-authoritative, sceneId için Ctrl+S gerekiyor, bkz. CLAUDE.md).
///
/// Skill'lerin KENDİ cooldown'u (IceBombSkill/ChickenFlockSkill/DriftTrap
/// içindeki skillCooldownSeconds) bundan AYRI — o, "bu skili art arda kaç
/// saniyede bir kullanabilirim" sorusuna cevap veriyor. Bu sınıf ise "bu
/// checkpoint'e (hangi skille olursa olsun) az önce tuzak kuruldu, biraz
/// bekle" sorusuna cevap veriyor. İkisi birlikte kontrol ediliyor.
///
/// GÖRSEL GERİ BİLDİRİM: cooldown başlayınca RpcPlayCooldownVisual ile TÜM
/// client'lara haber veriliyor, her client kendi MinimapController'ını bulup
/// ilgili checkpoint marker'ının rengini kırmızıdan yeşile doğru
/// (MinimapCheckpointMarker.PlayCooldown) kaydırıyor. Süre bitince marker
/// yeşile ulaşır — checkpoint'in yeniden hedef alınabilir olduğunun görsel
/// işareti budur.
/// </summary>
public class CheckpointCooldownManager : NetworkBehaviour
{
    [Tooltip("Bir checkpoint'e HERHANGİ bir skil ateşlendikten sonra, o checkpoint'in tekrar hedef alınabilmesi için beklenmesi gereken süre. " +
             "Bu değer yarışçı sayısıyla DEĞİŞMİYOR — yarışçılar için 'burası az önce tuzaklandı, bir süre güvenli' garantisi bu.")]
    [SerializeField] private float checkpointCooldownSeconds = 15f;

    [Header("Skill Cooldown'unun Yarışçı Sayısına Göre Ölçeklenmesi")]
    [Tooltip("Her EK yarışçı için skill cooldown'u bu oranla çarpılır. " +
             "Formül: taban × oran^(yarışçı-1). 0.65 → 5 yarışçıda kabaca beşte bire iner.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float cooldownRatioPerRacer = 0.65f;
    [Tooltip("Skill cooldown'u kaç yarışçı olursa olsun bunun altına inmez — sabotajcının " +
             "her atışının hâlâ bir KARAR olması için. Saf spam'i engelliyor.")]
    [SerializeField] private float minimumSkillCooldown = 3f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // Server-only: checkpointIndex -> cooldown'un biteceği Time.time değeri.
    private readonly Dictionary<int, float> cooldownEndTimes = new Dictionary<int, float>();

    /// <summary>
    /// Bir skilin TABAN cooldown'unu, o yarıştaki yarışçı sayısına göre
    /// ölçekler. Üç skil de (IceBomb/ChickenFlock/DriftTrap) kendi taban
    /// değerini buraya veriyor — böylece oran ve alt sınır TEK yerden
    /// ayarlanıyor, üç ayrı component'te tekrar tekrar girmeye gerek yok.
    ///
    /// NEDEN ÖLÇEKLİYORUZ: 1 sabotajcı 5 yarışçıyla uğraşırken aynı
    /// cooldown'la yetişemez. NEDEN ALT SINIR VAR: cooldown çok kısalırsa
    /// "hangi skili nereye?" kararı ortadan kalkıp saf spam'e dönüşüyor.
    /// </summary>
    public float ScaleSkillCooldown(float baseCooldown)
    {
        int racers = 1;
        if (NetworkManager.singleton is MyNetworkManager netManager)
            racers = Mathf.Max(1, netManager.RacerCount);

        float scaled = baseCooldown * Mathf.Pow(cooldownRatioPerRacer, racers - 1);
        return Mathf.Max(minimumSkillCooldown, scaled);
    }

    [Server]
    public bool IsCheckpointReady(int checkpointIndex)
    {
        if (!cooldownEndTimes.TryGetValue(checkpointIndex, out float endTime))
            return true;

        return Time.time >= endTime;
    }

    /// <summary>
    /// Bir skil bu checkpoint'te BAŞARIYLA ateşlendiğinde çağrılır (skill
    /// kendi cooldown/checkpoint hazır mı kontrolünü yaptıktan SONRA).
    /// </summary>
    [Server]
    public void StartCooldown(int checkpointIndex)
    {
        cooldownEndTimes[checkpointIndex] = Time.time + checkpointCooldownSeconds;

        if (showDebugLogs)
            Debug.Log($"[CheckpointCooldownManager] Checkpoint {checkpointIndex} {checkpointCooldownSeconds}s cooldown'a girdi.");

        RpcPlayCooldownVisual(checkpointIndex, checkpointCooldownSeconds);
    }

    [ClientRpc]
    private void RpcPlayCooldownVisual(int checkpointIndex, float duration)
    {
        MinimapController minimap = FindAnyObjectByType<MinimapController>();
        minimap?.PlayCheckpointCooldownVisual(checkpointIndex, duration);
    }
}
