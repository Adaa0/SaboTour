using UnityEngine;

/// <summary>
/// Minimap yanına elle yerleştirilen 3 fiziksel butondan (Buz Bombası,
/// Tavuk Sürüsü, Drift Trap) her birine eklenir. SaboteurInteraction bu
/// bileşeni raycast ile bulup hangi skilin "arm" edildiğini belirliyor.
/// </summary>
public class SkillSelectButton : MonoBehaviour
{
    public SkillType skill;

    [Tooltip("Buton birden fazla parçadan oluşuyorsa (ör. kaide + kubbe ayrı " +
             "objeler), TÜM parçaları içeren üst obje buraya sürüklenmeli — " +
             "outline ve basma animasyonu buradan aşağıya uygulanır. Boş " +
             "bırakılırsa sadece bu objenin kendisi kullanılır.")]
    [SerializeField] private Transform visualRoot;

    /// <summary>Outline/basma animasyonunun uygulanacağı gerçek kök obje.</summary>
    public Transform FeedbackRoot => visualRoot != null ? visualRoot : transform;
}
