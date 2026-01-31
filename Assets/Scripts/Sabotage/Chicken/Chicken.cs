using UnityEngine;

public class Chicken : MonoBehaviour
{
    private Vector3 moveDir;
    private float speed;

    public void SetDirection(Vector3 dir, float spd)
    {
        moveDir = dir;
        speed = spd;
    }

    void Update()
    {
        transform.position += moveDir * speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        CarController car = other.GetComponent<CarController>();
        if (car != null)
        {
           // car.ApplySlow(0.7f, 0.5f); // %30 slow 0.5 sn
           // car.AddPenaltyTime(0.5f);  // +0.5s total time
        }
    }
}
