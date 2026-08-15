using UnityEngine;
public class TriggerButton : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;

    public Transform FeedbackRoot => visualRoot != null ? visualRoot : transform;
}