using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// EKRANI KISA BİR BEYAZ FLAŞLA ÇAKTIRIYOR (Persona tarzı hover hissi,
/// buton üzerine gelince ~2 kare beyaz parlama).
///
/// Kalıcı bir sahne objesi / prefab GEREKMİYOR — `Trigger()` ilk
/// çağrıldığında kendi Canvas'ını ve tam ekran beyaz Image'ını runtime'da
/// kurup DontDestroyOnLoad yapıyor (ScreenNotice/SaboteurHud crosshair'deki
/// "prefab yoksa kendini kur" deseniyle aynı fikir — burada prefaba bile
/// gerek yok, tek bir düz beyaz dikdörtgen). Canvas'ın sortingOrder'ı çok
/// yüksek (30000) — hangi menü ekranı açık olursa olsun flaş her zaman en
/// üstte görünür.
/// </summary>
public static class MenuFlash
{
    private static Image flashImage;
    private static FlashRunner runner;
    private static Coroutine activeRoutine;

    public static void Trigger(float seconds = 0.04f)
    {
        EnsureBuilt();
        if (flashImage == null || runner == null) return;

        if (activeRoutine != null) runner.StopCoroutine(activeRoutine);
        activeRoutine = runner.StartCoroutine(FlashRoutine(seconds));
    }

    private static IEnumerator FlashRoutine(float seconds)
    {
        flashImage.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(seconds);
        flashImage.gameObject.SetActive(false);
        activeRoutine = null;
    }

    private static void EnsureBuilt()
    {
        if (flashImage != null) return;

        GameObject canvasObj = new GameObject("MenuFlashCanvas");
        Object.DontDestroyOnLoad(canvasObj);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000; // her menü/HUD katmanının üstünde

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Flaş tıklamayı/etkileşimi engellemesin diye raycaster tamamen kapalı.
        canvasObj.AddComponent<GraphicRaycaster>().enabled = false;

        GameObject imgObj = new GameObject("Flash");
        imgObj.transform.SetParent(canvasObj.transform, false);

        flashImage = imgObj.AddComponent<Image>();
        flashImage.color = Color.white;
        flashImage.raycastTarget = false;

        RectTransform rt = flashImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        imgObj.SetActive(false);

        runner = canvasObj.AddComponent<FlashRunner>();
    }

    // Coroutine'i çalıştıracak minik, boş bir MonoBehaviour — static sınıfın
    // kendisi Coroutine başlatamıyor, bunun için gerçek bir obje şart.
    private class FlashRunner : MonoBehaviour { }
}
