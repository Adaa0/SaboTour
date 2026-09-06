/// <summary>
/// Bir skilin cooldown durumunun CLIENT'A yansıyan hâli.
///
/// NEDEN VAR: Cooldown kararı SERVER'da veriliyor (IceBombSkill/
/// ChickenFlockSkill/EngineFailureTrap içindeki `nextReadyTime`, `Time.time`
/// ile). Ama sabotajcının kule odasındaki buton ışığını çizebilmek için bu
/// bilginin SABOTAJCININ MAKİNESİNDE de olması gerekiyor — yoksa buton
/// hazır mı değil mi bilemez. O yüzden aynı bilgi ayrıca SyncVar ile
/// yayınlanıyor.
///
/// NEDEN TEK BİR STRUCT (iki ayrı SyncVar değil): Mirror, SyncVar'ları
/// tanımlanma sırasına göre TEK TEK deserialize ediyor. "Bitiş anı" ve
/// "toplam süre" ayrı iki alan olsaydı, arada birinin güncel diğerinin eski
/// olduğu bir kare oluşabilirdi. Podyum sisteminde birebir bu yaşandı
/// (raceEnded + saboteurWon → tek bir RaceOutcome enum'una birleştirildi,
/// bkz. CLAUDE.md). Aynı hatayı tekrarlamamak için ikisi tek alanda.
/// </summary>
public struct SkillCooldownState
{
    /// <summary>
    /// Cooldown'un biteceği an — `NetworkTime.time` cinsinden.
    ///
    /// ÖNEMLİ: `Time.time` DEĞİL. `Time.time` her makinede oyun açıldığı
    /// andan itibaren sayıyor, yani host'ta 300 iken client'ta 12 olabilir —
    /// server'ın gönderdiği bir `Time.time` değeri client'ta tamamen anlamsız
    /// olurdu. `NetworkTime.time` Mirror'ın tüm makinelerde hizaladığı ortak
    /// saat, bu yüzden karşıya geçen zaman değerleri hep bununla yazılmalı.
    /// </summary>
    public double endTime;

    /// <summary>
    /// Bu cooldown turunun toplam süresi (saniye). Butonun ışığının ne
    /// kadar dolduğunu hesaplamak için gerekiyor: dolum = 1 - (kalan/toplam).
    /// Sabit bir sayı değil — CheckpointCooldownManager.ScaleSkillCooldown()
    /// yarışçı sayısına göre her seferinde yeniden hesaplıyor.
    /// </summary>
    public float duration;
}
