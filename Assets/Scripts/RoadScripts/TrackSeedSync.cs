using UnityEngine;
using Mirror;

public class TrackSeedSync : NetworkBehaviour
{
    [SerializeField] private TrackGenerator trackGenerator;

    [SyncVar(hook = nameof(OnSeedChanged))]
    private int syncedSeed;
    public override void OnStartServer()
    {
        base.OnStartServer();

        // 🚨 SABİT SEED DESTEĞİ (performans ölçümü / hata ayıklama için).
        // TrackGenerator'daki `useSavedSeedOnStart` işaretliyse rastgele seed
        // ÜRETİLMİYOR, Inspector'daki kayıtlı seed kullanılıyor — böylece her
        // yarışta BİREBİR aynı pist çıkıyor ve ölçümler kıyaslanabiliyor.
        //
        // Bu kontrol olmadan o bayrak multiplayer'da HİÇBİR İŞE YARAMIYORDU:
        // TrackGenerator.Start() kayıtlı seed'le pisti üretiyor, hemen ardından
        // buradaki rastgele seed onu eziyordu. Bayrağı işaretleyen kişi pistin
        // neden hâlâ değiştiğini anlayamıyordu.
        bool useFixed = trackGenerator != null && trackGenerator.useSavedSeedOnStart;

        int newSeed = useFixed
            ? trackGenerator.seed
            : (int)(System.DateTime.Now.Ticks % int.MaxValue);

        syncedSeed = newSeed;
        GenerateIfNeeded(newSeed);
    }

    private void OnSeedChanged(int oldSeed, int newSeed)
    {
        if (isServer) return;

        GenerateIfNeeded(newSeed);
    }

    /// <summary>
    /// Pist zaten AYNI seed ile üretilmişse tekrar üretmiyor.
    ///
    /// NEDEN: `useSavedSeedOnStart` açıkken `TrackGenerator.Start()` pisti
    /// kayıtlı seed'le bir kez kuruyor; buraya gelindiğinde seed aynı olduğu
    /// için ikinci kurulum gereksiz. Pist üretimi ~9 ms ama ona bağlı prop
    /// serpiştirme BİNLERCE obje yaratıyor — iki kez yapmak yarış başında
    /// belirgin bir takılma demek.
    ///
    /// Rastgele seed durumunda seed'ler tutmayacağı için davranış değişmiyor.
    /// </summary>
    private void GenerateIfNeeded(int seedValue)
    {
        if (trackGenerator == null) return;

        bool alreadyBuilt = trackGenerator.seed == seedValue
                            && trackGenerator.GetTrackPoints() != null
                            && trackGenerator.GetTrackPoints().Count > 2;

        if (alreadyBuilt) return;

        trackGenerator.GenerateTrackWithSeed(seedValue);
    }
}