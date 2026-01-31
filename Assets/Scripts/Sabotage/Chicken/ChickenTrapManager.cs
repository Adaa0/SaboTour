using UnityEngine;
using System.Collections.Generic;

public class ChickenTrapManager : MonoBehaviour
{
    public Transform truck;
    public List<Transform> idleSpots;
    public GameObject chickenGroupPrefab;

    public void ActivateChickenTrap(Transform trapPoint)
    {
        Transform bestSpot = GetClosestIdleSpot(trapPoint);
        if (bestSpot == null) return;

        Vector3 startPos = bestSpot.position;
        Vector3 endPos = truck.position;

        GameObject groupObj = Instantiate(chickenGroupPrefab, startPos, Quaternion.identity);
        ChickenGroup group = groupObj.GetComponent<ChickenGroup>();
        group.Init(startPos, endPos);
    }

    private Transform GetClosestIdleSpot(Transform trapPoint)
    {
        float minDist = float.MaxValue;
        Transform best = null;

        foreach (var spot in idleSpots)
        {
            float d = Vector3.Distance(trapPoint.position, spot.position);
            if (d < minDist)
            {
                minDist = d;
                best = spot;
            }
        }
        return best;
    }
}
