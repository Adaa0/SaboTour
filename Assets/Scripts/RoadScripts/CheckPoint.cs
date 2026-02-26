using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    public int checkpointIndex;
    public bool isFinishLine;

    private void OnTriggerEnter(Collider other)
    {
        GameObject root = other.transform.root.gameObject;

        if (root.CompareTag("Player") && root.TryGetComponent(out PlayerRaceController player))
        {
            player.ReachedCheckpoint(checkpointIndex, isFinishLine);

            // DriftTrap sistemini bilgilendir
            CarController car = root.GetComponent<CarController>();
            if (car != null)
            {
                DriftTrap driftTrap = FindAnyObjectByType<DriftTrap>();
                if (driftTrap != null)
                {
                    driftTrap.OnCarReachedCheckpoint(car, player, checkpointIndex);
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = isFinishLine ? Color.red : Color.green;
        Gizmos.DrawCube(transform.position, Vector3.one * 3f);
    }
}