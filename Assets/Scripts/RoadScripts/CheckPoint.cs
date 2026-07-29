using UnityEngine;
using Mirror;

public class Checkpoint : MonoBehaviour
{
    public int checkpointIndex;
    public bool isFinishLine;

    private void OnTriggerEnter(Collider other)
    {
        GameObject root = other.transform.root.gameObject;

        if (root.CompareTag("Player") && root.TryGetComponent(out PlayerRaceController player))
        {
            // Checkpoint tetiklemesi HER client'ın kendi fizik dünyasında
            // olur (remote arabaların collider'ı da var), bu yüzden sadece
            // aracın SAHİBİ Command gönderir — server'da tek sefer işlenir.
            if (player.isOwned)
                player.CmdReachedCheckpoint(checkpointIndex, isFinishLine);

            // DriftTrap sistemini bilgilendir — SADECE SERVER'da. OnTriggerEnter
            // her client'ın kendi fizik dünyasında ayrı ayrı tetikleniyor ama
            // DriftTrap artık server-authoritative bir NetworkBehaviour; her
            // client burada çağırırsa server'da olmayan, senkronize olmayan
            // kopyalar (trapActive, trackedCars) oluşurdu.
            if (NetworkServer.active)
            {
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
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = isFinishLine ? Color.red : Color.green;
        Gizmos.DrawCube(transform.position, Vector3.one * 3f);
    }
}