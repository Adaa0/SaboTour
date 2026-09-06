using UnityEngine;
public class SaboteurSpawnPoint : MonoBehaviour
{
    void OnDrawGizmos() // editörde otomatik çalıştırılır scene view'de görsel işaretler çizer 
    {
        Gizmos.color = Color.cyan; // renk turkuaz 
        Gizmos.DrawWireSphere(transform.position, 0.5f); // objenin pozisyonunda 0.5 metre yarıçaplı kafes küre çizilir
        Gizmos.DrawRay(transform.position, transform.forward * 1.5f); // objenin baktığı yöne doğru bir çizgi çizilir 
    }
}
