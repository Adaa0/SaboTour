using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EKRANI SİYAHA KARARTIP GERİ AÇAR (fade).
///
/// KULLANIMI (kod):
///   ScreenFader.PlaySequence(0.4f, 0.2f, () => { /* ekran tam siyahken çalışır */ });
///
/// ── NEDEN AYRI/KALICI BİR OBJE ──
/// Kararma dizisi (karart → bir şey yap → aç) kendi objesinin üstünde
/// çalışıyor, onu BAŞLATAN objenin değil. Sebep: kurtarmayı başlatan araba
/// o sırada yok olabilir (bağlantı kopması, sahne geçişi) — coroutine araba
/// üzerinde çalışsaydı tam o anda kesilir ve oyuncunun ekranı SONSUZA KADAR
/// siyah kalırdı. Burada dizi her hâlükârda sonuna kadar gidip ekranı açıyor.
///
/// Sahnede elle bir Canvas kurmaya GEREK YOK — ScreenNotice.cs ile aynı
/// desen, kendi Canvas'ını runtime'da oluşturup DontDestroyOnLoad yapıyor.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    private static ScreenFader instance;

    private Image overlay;
    private Coroutine runningSequence;

    /// <summary>Şu an bir kararma/açılma dizisi oynuyor mu?</summary>
    public static bool IsPlaying => instance != null && instance.runningSequence != null;

    /// <summary>
    /// Ekranı karartır, tam siyahken <paramref name="onFullyBlack"/> çağırır,
    /// <paramref name="holdSeconds"/> bekler, sonra ekranı geri açar.
    /// </summary>
    /// <param name="fadeDuration">Kararma ve açılmanın her birinin süresi (saniye).</param>
    /// <param name="holdSeconds">Ekran tam siyahken beklenecek ek süre.</param>
    /// <param name="onFullyBlack">Ekran tamamen siyah olduğu anda çalışacak iş (ışınlama gibi).</param>
    public static void PlaySequence(float fadeDuration, float holdSeconds, System.Action onFullyBlack)
    {
        EnsureInstance();

        // Üst üste binen istekleri yok say — ikinci bir dizi araya girerse
        // ilkinin "ekranı geri aç" adımı iptal olur ve ekran siyah kalır.
        if (instance.runningSequence != null) return;

        instance.runningSequence = instance.StartCoroutine(
            instance.SequenceRoutine(fadeDuration, holdSeconds, onFullyBlack));
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;

        GameObject go = new GameObject("ScreenFader (otomatik)");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ScreenFader>();
        instance.Build();
    }

    private void Build()
    {
        GameObject canvasObj = new GameObject("FadeCanvas");
        canvasObj.transform.SetParent(transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // ScreenNotice (500) ve pause menüsünün (400) ÜSTÜNDE: kararma
        // gerçekten her şeyi örtmeli, yoksa siyah perdenin arkasından HUD
        // yazıları görünmeye devam eder.
        canvas.sortingOrder = 600;
        canvasObj.AddComponent<CanvasScaler>();

        GameObject overlayObj = new GameObject("Black");
        overlayObj.transform.SetParent(canvasObj.transform, false);
        overlay = overlayObj.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0f);
        // raycastTarget kapalı ŞART: açık kalsa görünmez bile olsa tüm
        // ekranı kaplayan bu Image, altındaki menü butonlarının tıklanmasını
        // engellerdi.
        overlay.raycastTarget = false;

        RectTransform rect = overlay.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private IEnumerator SequenceRoutine(float fadeDuration, float holdSeconds, System.Action onFullyBlack)
    {
        yield return FadeTo(1f, fadeDuration);

        // Çağıran obje bu arada yok olmuş olabilir — hata fırlatıp diziyi
        // yarıda kesmesin, ekranın geri açılması her koşulda garanti olsun.
        try { onFullyBlack?.Invoke(); }
        catch (System.Exception e) { Debug.LogError($"[ScreenFader] Kararma sırasındaki iş hata verdi: {e}"); }

        if (holdSeconds > 0f)
            yield return new WaitForSecondsRealtime(holdSeconds);

        yield return FadeTo(0f, fadeDuration);

        runningSequence = null;
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        float startAlpha = overlay.color.a;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Ölçeklenmemiş zaman: projedeki fotoğraf araçları (FreezeFrame)
            // Time.timeScale'i sıfırlayabiliyor, o durumda normal deltaTime
            // hiç ilerlemez ve ekran siyah takılı kalırdı.
            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, t));
            yield return null;
        }

        SetAlpha(targetAlpha);
    }

    private void SetAlpha(float alpha)
    {
        Color c = overlay.color;
        c.a = alpha;
        overlay.color = c;
    }
}
