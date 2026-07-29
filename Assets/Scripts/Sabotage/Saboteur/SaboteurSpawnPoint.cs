using UnityEngine;

/// <summary>
/// Online Scene içinde, sabotajcının kulede spawn olacağı noktayı işaretler.
/// MyNetworkManager bunu FindAnyObjectByType ile bulup sabotajcıyı burada
/// spawn ediyor. Boş bir GameObject'e eklenip kule içindeki uygun konuma
/// yerleştirilmesi yeterli — görsel bir işlevi yok, sadece pozisyon/rotasyon
/// referansı.
/// </summary>
public class SaboteurSpawnPoint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawRay(transform.position, transform.forward * 1.5f);
    }
}
