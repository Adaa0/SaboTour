using UnityEngine;
using Mirror;

public class Checkpoint : MonoBehaviour
{
    public int checkpointIndex;
    public bool isFinishLine;

    [Header("Görsel (Bayrak)")]
    public GameObject normalFlagVisual;
    public GameObject finishFlagVisual;

    private void Start()
    {
        RefreshVisual();
    }

    public void RefreshVisual()
    {
        if (normalFlagVisual != null) normalFlagVisual.SetActive(!isFinishLine);
        if (finishFlagVisual != null) finishFlagVisual.SetActive(isFinishLine);
    }

    private void OnTriggerEnter(Collider other)
    {
        GameObject root = other.transform.root.gameObject;

        if (root.CompareTag("Player") && root.TryGetComponent(out PlayerRaceController player))
        {
            if (player.isOwned)
                player.CmdReachedCheckpoint(checkpointIndex, isFinishLine);

            if (NetworkServer.active)
            {
                CarController car = root.GetComponent<CarController>();
                if (car != null)
                {
                    EngineFailureTrap engineTrap = FindAnyObjectByType<EngineFailureTrap>();
                    if (engineTrap != null)
                    {
                        engineTrap.OnCarReachedCheckpoint(car, player, checkpointIndex);
                    }
                }
            }
        }
    }
}