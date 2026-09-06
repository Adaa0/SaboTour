using UnityEngine;

// Sahneye (Online + Offline Scene) konan test/ayar paneli. Inspector'daki
// kaydırıcılar AudioBus'un statik kanallarını besliyor — Play modunda canlı,
// tek yerden ses dengesi ayarlamak için.
//
// master, SfxPlayer.MasterVolume (oyuncunun Ayarlar menüsü slider'ı) İLE
// AYNI ŞEY DEĞİL: bu geliştirici test ayarı, kaydedilmiyor. SfxPlayer.
// MasterVolume'a bilerek dokunulmuyor — dokunsaydı oyuncunun kendi seçimini
// her karede ezerdi.
public class AudioTuning : MonoBehaviour
{
    [Header("Kanal Seviyeleri (test ayarı, kaydedilmez)")]
    [Range(0f, 2f)] public float master = 1f;
    [Range(0f, 2f)] public float world = 1f;
    [Range(0f, 2f)] public float ui = 1f;
    [Range(0f, 2f)] public float engine = 1f;
    [Range(0f, 2f)] public float skid = 1f;
    [Range(0f, 2f)] public float music = 1f;

    void Update()
    {
        AudioBus.Master = master;
        AudioBus.World = world;
        AudioBus.Ui = ui;
        AudioBus.Engine = engine;
        AudioBus.Skid = skid;
        AudioBus.Music = music;
    }
}
