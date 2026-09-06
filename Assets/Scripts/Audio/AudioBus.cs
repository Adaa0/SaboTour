// Tüm ses kanallarının seviyesini TEK YERDEN tutan statik sınıf. AudioTuning
// component'i (sahneye konan, Inspector kaydırıcılı) bu alanları besliyor;
// Play modunda canlı değişiyor. Kategori bazlı ses ayarı için tek nokta —
// prefab prefab dolaşıp her klibi tek tek ayarlamak yerine buradan.
//
// SfxPlayer.MasterVolume (oyuncunun Ayarlar menüsü slider'ı) İLE
// KARIŞTIRILMAMALI — bu ayrı bir kademe, buradaki Master'a dokunmuyoruz.
public static class AudioBus
{
    public static float World = 1f;
    public static float Ui = 1f;
    public static float Engine = 1f;
    public static float Skid = 1f;
    public static float Music = 1f;
    public static float Master = 1f;

    /// <summary>Dünya sesleri (patlama, tavuk, ayak, çarpma, tuzak, kule butonları) için nihai çarpan.</summary>
    public static float WorldFinal => World * Master;

    /// <summary>UI sesleri (checkpoint, tur, zafer/yenilgi, lobi) için nihai çarpan.</summary>
    public static float UiFinal => Ui * Master;
}
