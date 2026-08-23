public enum SkillType
{
    IceBomb,
    ChickenFlock,
    // ESKİ ADI: DriftTrap. Yetenek yeniden tasarlandı (drift ölçüp gecikmeli
    // ceza vermek yerine, checkpoint'ten geçen ilk aracı anında arızalandırıyor
    // — bkz. EngineFailureTrap.cs). Enum SIRASI bilerek korundu: Unity enum
    // alanlarını sayı olarak saklıyor, sıra değişseydi sahnedeki skil
    // butonlarının Inspector'da seçili değerleri kayardı.
    EngineFailure
}
