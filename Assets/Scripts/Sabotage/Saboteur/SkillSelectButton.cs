using UnityEngine;
public class SkillSelectButton : MonoBehaviour
{
    public SkillType skill;
    [SerializeField] private Transform visualRoot;
    public Transform FeedbackRoot
        {
            get { return visualRoot != null ? visualRoot : transform; } 
        }
}
