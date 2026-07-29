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

        int newSeed = (int)(System.DateTime.Now.Ticks % int.MaxValue);
        syncedSeed = newSeed; 
        if (trackGenerator != null)
            trackGenerator.GenerateTrackWithSeed(newSeed);
    }

    private void OnSeedChanged(int oldSeed, int newSeed)
    {
        // Host modunda bu obje HEM server HEM client'tır — server zaten
        // OnStartServer()'da pisti senkron olarak üretti. Burada tekrar
        // üretirsek (host'un kendi SyncVar hook'u tetiklendiği için) pist
        // İKİNCİ KEZ üretilir: eski checkpoint objeleri yok edilip yenileri
        // oluşturulur, bu da CheckpointManager gibi ilk üretimi zaten
        // önbelleğe almış sistemlerde "hayalet" (destroyed) referanslara yol
        // açar. isServer kontrolü ile host'ta bu ikinci üretimi engelliyoruz
        // — sadece GERÇEK (server olmayan) client'lar burada üretim yapsın.
        if (isServer) return;

        if (trackGenerator != null)
            trackGenerator.GenerateTrackWithSeed(newSeed);
    }
}