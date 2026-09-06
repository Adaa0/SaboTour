using System.Collections;
using UnityEngine;
public class InteractableFeedback : MonoBehaviour
{
    [Header("Basma Animasyonu (tıklayınca)")]
    [SerializeField] private float pressScale = 0.85f; //ne kadar küçüleceğine karar verir
    [SerializeField] private float pressDuration = 0.1f;

    private Vector3 baseScale; //orjinal boyutu
    private Coroutine pressRoutine; //şuan oynayan animasyonu takip eder
    private float heldAmount; //ne kadar basılı tutulduğunu gösterir

    private Vector3 RestScale => Vector3.Lerp(baseScale, baseScale * pressScale, heldAmount); //restscale'i heldAmounta göre hesaplar

    void Awake() //kod uyandığında baseScaleyi normal scale olarak alır 
    {
        baseScale = transform.localScale;
    }

    public void PlayPress()
    {
        if (!isActiveAndEnabled) return;

        if (pressRoutine != null) StopCoroutine(pressRoutine);
        pressRoutine = StartCoroutine(PressRoutine());
    }
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
