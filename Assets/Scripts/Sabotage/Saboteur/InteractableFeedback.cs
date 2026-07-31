using System.Collections;
using UnityEngine;

/// <summary>
/// Skil butonları, checkpoint marker'ları ve tetik butonu için ORTAK görsel
/// geri bildirim: tıklayınca içe çöküp geri kalkma animasyonu.
///
/// SaboteurInteraction tarafından RUNTIME'DA otomatik ekleniyor — hangi
/// objenin "FeedbackRoot"u (bkz. SkillSelectButton/TriggerButton/
/// MinimapCheckpointMarker.FeedbackRoot) tıklanırsa ona. Çok parçalı
/// butonlarda (kaide + kubbe gibi ayrı objeler) bu component TÜM parçaları
/// kapsayan üst objeye eklenir — böylece basma animasyonunda hepsi birlikte
/// küçülüp büyür (Unity'de child'lar parent'ın scale'ini otomatik miras alır).
/// </summary>
public class InteractableFeedback : MonoBehaviour
{
    [Header("Basma Animasyonu (tıklayınca)")]
    [Tooltip("1 = değişmez, 0.85 = tıklayınca %15 küçülür (içe çökme hissi).")]
    [SerializeField] private float pressScale = 0.85f;
    [SerializeField] private float pressDuration = 0.1f;

    private Vector3 baseScale;
    private Coroutine pressRoutine;

    void Awake()
    {
        baseScale = transform.localScale;
    }

    /// <summary>Tıklama başarıyla bir aksiyon tetiklediğinde çağrılır.</summary>
    public void PlayPress()
    {
        if (!isActiveAndEnabled) return;

        if (pressRoutine != null) StopCoroutine(pressRoutine);
        pressRoutine = StartCoroutine(PressRoutine());
    }

    private IEnumerator PressRoutine()
    {
        yield return ScaleOverTime(baseScale, baseScale * pressScale, pressDuration);
        yield return ScaleOverTime(baseScale * pressScale, baseScale, pressDuration);
        transform.localScale = baseScale;
        pressRoutine = null;
    }

    private IEnumerator ScaleOverTime(Vector3 from, Vector3 to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }
        transform.localScale = to;
    }
}
