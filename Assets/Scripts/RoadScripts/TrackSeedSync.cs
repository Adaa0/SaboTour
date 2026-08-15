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
        if (isServer) return;

        if (trackGenerator != null)
            trackGenerator.GenerateTrackWithSeed(newSeed);
    }
}