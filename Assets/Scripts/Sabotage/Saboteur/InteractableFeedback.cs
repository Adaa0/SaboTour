using System.Collections;
using UnityEngine;
public class InteractableFeedback : MonoBehaviour
{
    [Header("Basma Animasyonu (tıklayınca)")]
    [Tooltip("1 = değişmez, 0.85 = tıklayınca %15 küçülür (içe çökme hissi).")]
    [SerializeField] private float pressScale = 0.85f;
    [SerializeField] private float pressDuration = 0.1f;

    private Vector3 baseScale;
    private Coroutine pressRoutine;
    private float heldAmount;

    private Vector3 RestScale => Vector3.Lerp(baseScale, baseScale * pressScale, heldAmount);

    void Awake()
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
