using UnityEngine;
using Mirror;

public class Checkpoint : MonoBehaviour
{
    public int checkpointIndex;
    public bool isFinishLine;

    [Header("Görsel (Bayrak)")]
    [Tooltip("Normal checkpoint'lerde gösterilecek bayrak (yeşil).")]
    public GameObject normalFlagVisual;
    [Tooltip("Başlangıç/bitiş checkpoint'inde (isFinishLine) gösterilecek bayrak (damalı).")]
    public GameObject finishFlagVisual;

    private void Start()
    {
        RefreshVisual();
    }

    /// <summary>
    /// isFinishLine'a göre doğru bayrağı açıp diğerini kapatır. TrackGenerator
    /// checkpoint'i üretip isFinishLine'ı ATADIKTAN HEMEN SONRA bunu çağırıyor
    /// (Instantiate anındaki Awake'te isFinishLine henüz set edilmemiş olurdu).
    /// Sahneye elle yerleştirilmiş checkpoint'ler için de Start() zaten çağırıyor.
    /// </summary>
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

    private void OnDrawGizmos()
    {
        Gizmos.color = isFinishLine ? Color.red : Color.green;
        Gizmos.DrawCube(transform.position, Vector3.one);
    }
}