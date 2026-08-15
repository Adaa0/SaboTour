using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EKRANIN ORTASINDA GEÇİCİ BİLGİ MESAJI GÖSTERİR.
///
/// KULLANIMI (kod):  ScreenNotice.Show("Bağlantı koptu");
///
/// NEDEN RUNTIME'DA OLUŞTURULUYOR (sahnede hazır bir panel değil):
/// Bu mesajların çoğu SAHNE GEÇİŞİ SIRASINDA gösteriliyor — örneğin host
/// oyundan çıkınca Mirror otomatik olarak Offline Scene'i yüklüyor ve
/// Online Scene'deki her panel o anda yok oluyor. Runtime'da oluşturulup
/// DontDestroyOnLoad işaretlenen bir Canvas ise geçişte hayatta kalıyor,
/// yani oyuncu ana menüye düşerken mesajı okumaya devam edebiliyor.
///
/// Ayrıca bu sayede Inspector'da elle panel/Text kurmak gerekmiyor —
/// SaboteurController'ın crosshair'i ile aynı desen.
/// </summary>
public class ScreenNotice : MonoBehaviour
{
    private static ScreenNotice instance;

    private GameObject noticeRoot;
    private Text noticeText;
    private Coroutine hideRoutine;

    /// <summary>
    /// Mesajı ekranın ortasında `seconds` saniye gösterir. Arka arkaya
    /// çağrılırsa yenisi eskisinin yerini alır (üst üste binmez).
    /// </summary>
    public static void Show(string message, float seconds = 4f)
    {
        if (string.IsNullOrEmpty(message)) return;

        EnsureInstance();
        instance.ShowInternal(message, seconds);
    }

    /// <summary>Mesajı hemen gizler (ör. yeni bir oyuna girilince).</summary>
    public static void Hide()
    {
        if (instance != null && instance.noticeRoot != null)
            instance.noticeRoot.SetActive(false);
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;

        GameObject go = new GameObject("ScreenNotice (otomatik)");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ScreenNotice>();
        instance.Build();
    }

    private void Build()
    {
        noticeRoot = new GameObject("NoticeCanvas");
        noticeRoot.transform.SetParent(transform, false);

        Canvas canvas = noticeRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // crosshair (100) dahil her şeyin üstünde
        noticeRoot.AddComponent<CanvasScaler>();

        // Yarı saydam koyu zemin — yazının pist/gökyüzü üzerinde okunaklı
        // kalması için. Tam opak yapmadık, arkada ne olduğu görünsün.
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(noticeRoot.transform, false);
        Image bg = bgObj.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        bg.raycastTarget = false;
        RectTransform bgRect = bg.rectTransform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        GameObject textObj = new GameObject("Message");
        textObj.transform.SetParent(noticeRoot.transform, false);
        noticeText = textObj.AddComponent<Text>();
        noticeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        noticeText.fontSize = 32;
        noticeText.alignment = TextAnchor.MiddleCenter;
        noticeText.color = Color.white;
        noticeText.raycastTarget = false;

        RectTransform textRect = noticeText.rectTransform;
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(900f, 300f);

        noticeRoot.SetActive(false);
    }

    private void ShowInternal(string message, float seconds)
    {
        noticeText.text = message;
        noticeRoot.SetActive(true);

        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfter(seconds));
    }

    private IEnumerator HideAfter(float seconds)
    {
        // WaitForSecondsRealtime — normal WaitForSeconds, Time.timeScale 0
        // olduğunda hiç ilerlemiyor ve mesaj ekranda sonsuza kadar asılı
        // kalırdı (fotoğraf araçlarındaki FreezeFrame timeScale'i sıfırlıyor).
        yield return new WaitForSecondsRealtime(seconds);

        if (noticeRoot != null) noticeRoot.SetActive(false);
        hideRoutine = null;
    }
}
