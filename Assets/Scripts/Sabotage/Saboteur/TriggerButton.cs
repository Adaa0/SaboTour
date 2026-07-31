using UnityEngine;

/// <summary>
/// Minimap ortasındaki büyük kırmızı tetikleme butonu — işaretleyici bileşen.
/// SaboteurInteraction raycast ile bu bileşeni bulunca o an arm edilmiş
/// skili aktive ediyor.
/// </summary>
public class TriggerButton : MonoBehaviour
{
    [Tooltip("Buton birden fazla parçadan oluşuyorsa (ör. kaide + kırmızı " +
             "kubbe ayrı objeler), TÜM parçaları içeren üst obje buraya " +
             "sürüklenmeli — outline ve basma animasyonu buradan aşağıya " +
             "uygulanır. Boş bırakılırsa sadece bu objenin kendisi kullanılır.")]
    [SerializeField] private Transform visualRoot;

    /// <summary>Outline/basma animasyonunun uygulanacağı gerçek kök obje.</summary>
    public Transform FeedbackRoot => visualRoot != null ? visualRoot : transform;
}
