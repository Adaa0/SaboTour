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

    // Buton NE KADAR basılı kalıyor: 0 = yukarıda, 1 = tam çökük.
    // Tıklama anındaki geçici basmadan farklı — süresiz, dışarıdan ayarlanıyor.
    // Ara değerler kullanılabiliyor çünkü skil butonu İKİ FARKLI sebeple
    // basılı kalabiliyor ve ikisi ayırt edilebilmeli:
    //   seçili (yarı basılı)  <  cooldown'da (tam basılı)
    private float heldAmount;

    /// <summary>Butonun şu an "dinlenme" ölçeği — basılı kalıyorsa çökük hâli.</summary>
    private Vector3 RestScale => Vector3.Lerp(baseScale, baseScale * pressScale, heldAmount);

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

    /// <summary>
    /// Butonu ne kadar basılı tutacağını ayarlar. 0 = yukarıda,
    /// 1 = tam çökük, aradaki değerler kısmi.
    ///
    /// SkillSelectButton bunu iki durumdan hangisinin geçerli olduğuna göre
    /// çağırıyor: skil SEÇİLİ ise yarı basılı (elinde ne olduğunu görürsün),
    /// COOLDOWN'da ise tam basılı (mekanik olarak kilitlenmiş gibi).
    /// Cooldown bitince yukarı kalkıyor.
    ///
    /// NEDEN SCALE (pozisyon değil): butonun hangi yöne "içeri" gittiği
    /// modelin kendi eksenlerine bağlı ve bu konsol FBX'i döndürülmüş
    /// olarak geliyor — localPosition'a "aşağı" yazmak butonu YAN TARAFA
    /// kaydırıyordu (19 Ağustos'ta bir kere denendi ve geri alındı).
    /// Ölçek küçültmek yönden tamamen bağımsız, her modelde doğru çalışıyor.
    /// </summary>
    public void SetHeldAmount(float amount)
    {
        amount = Mathf.Clamp01(amount);
        if (Mathf.Approximately(amount, heldAmount)) return;
        heldAmount = amount;

        if (!isActiveAndEnabled)
        {
            transform.localScale = RestScale;
            return;
        }

        if (pressRoutine != null) StopCoroutine(pressRoutine);
        pressRoutine = StartCoroutine(SettleRoutine());
    }

    private IEnumerator PressRoutine()
    {
        Vector3 pressed = baseScale * pressScale;
        yield return ScaleOverTime(transform.localScale, pressed, pressDuration);
        yield return ScaleOverTime(pressed, RestScale, pressDuration);
        transform.localScale = RestScale;
        pressRoutine = null;
    }

    /// <summary>Basılı kalma durumu değiştiğinde yumuşak geçiş.</summary>
    private IEnumerator SettleRoutine()
    {
        yield return ScaleOverTime(transform.localScale, RestScale, pressDuration);
        transform.localScale = RestScale;
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
