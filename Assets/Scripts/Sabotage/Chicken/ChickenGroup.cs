using UnityEngine;
using System.Collections.Generic;

public class ChickenGroup : MonoBehaviour
{
    public List<Chicken> chickens; // child tavuklar
    public float moveSpeed = 6f;
    public float lifeTime = 3f;

    private Vector3 direction;
    private float timer;

    public void Init(Vector3 start, Vector3 end)
    {
        direction = (end - start).normalized;
        timer = lifeTime;

        foreach (var c in chickens)
        {
            c.SetDirection(direction, moveSpeed);
        }
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Destroy(gameObject);
        }
    }
}
