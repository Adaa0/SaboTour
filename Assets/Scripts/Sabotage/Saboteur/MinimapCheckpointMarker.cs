using UnityEngine;

/// <summary>
/// Minimap üzerindeki her checkpoint işaretçisine MinimapController
/// tarafından runtime'da eklenir. Sadece hangi checkpoint'e denk geldiğini
/// taşır — SaboteurInteraction raycast ile bu bileşeni bulup checkpointIndex'i
/// okuyor.
/// </summary>
public class MinimapCheckpointMarker : MonoBehaviour
{
    public int checkpointIndex;

    [Tooltip("Marker birden fazla parçadan oluşuyorsa TÜM parçaları içeren " +
             "üst obje buraya sürüklenmeli. Boş bırakılırsa bu objenin " +
             "kendisi kullanılır.")]
    [SerializeField] private Transform visualRoot;

    /// <summary>Outline/basma animasyonunun uygulanacağı gerçek kök obje.</summary>
    public Transform FeedbackRoot => visualRoot != null ? visualRoot : transform;
}
