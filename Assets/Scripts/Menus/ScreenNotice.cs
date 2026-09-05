using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EKRANIN ORTASINDA GEÇİCİ BİLGİ MESAJI GÖSTERİR.
///
/// KULLANIMI (kod):  ScreenNotice.Show("Bağlantı koptu");
///
/// Oyundaki ortadaki bildirimlerin HEPSİ buradan geçiyor: 3-2-1 geri
/// sayımı, "SON TUR!", "⚠ MOTOR ARIZASI", rol ipuçları, bağlantı uyarıları.
///
/// ─── GÖRÜNÜM ARTIK PREFABTA ───────────────────────────────────────────
/// Yazının fontu / boyutu / rengi / arka planı
/// `Assets/Resources/UI/ScreenNotice.prefab` içinde yaşıyor. Değiştirmek
/// için prefaba çift tıkla; bozulursa üst menüden
/// **SaboTour > UI Prefabları > Ekran Mesajı Prefabını Oluştur**.
///
/// Prefab bulunamazsa kod yine de basit bir yazı kuruyor (aşağıdaki
/// `BuildFallback`). Bu bilinçli bir güvenlik ağı: bu mesajların bir kısmı
/// "bağlantı koptu" gibi kritik bilgiler, prefab kaybolduğu için oyuncunun
/// sessizce hiçbir şey görmemesi kabul edilemez.
///
/// ─── NEDEN SAHNEYE DEĞİL, DontDestroyOnLoad'A KURULUYOR ───────────────
/// Bu mesajların çoğu SAHNE GEÇİŞİ SIRASINDA gösteriliyor — host oyundan
/// çıkınca Mirror Offline Scene'i yüklüyor ve Online Scene'deki her panel o
/// anda yok oluyor. DontDestroyOnLoad olan bir Canvas geçişte hayatta
/// kalıyor, yani oyuncu ana menüye düşerken mesajı okumaya devam ediyor.
/// </summary>
public class ScreenNotice : MonoBehaviour
{
    private static ScreenNotice instance;

    [Header("Prefab Referansları")]
    [Tooltip("Açılıp kapanan mesaj kabı (yazı + arka plan).")]
    public GameObject noticeRoot;

    [Tooltip("Mesajın yazıldığı metin kutusu.")]
    public TMP_Text noticeText;

    private Coroutine hideRoutine;

    /// <summary>
    /// Mesajı ekranın ortasında `seconds` saniye gösterir. Arka arkaya
    /// çağrılırsa yenisi eskisinin yerini alır (üst üste binmez).
    /// </summary>
    public static void Show(string message, float seconds = 4f)
    {
        if (string.IsNullOrEmpty(message)) return;

        EnsureInstance();
        if (instance != null) instance.ShowInternal(message, seconds);
    }

    /// <summary>Mesajı hemen gizler (ör. yeni bir oyuna girilince).</summary>
    public static void Hide()
    {
        if (instance != null && instance.noticeRoot != null)
            instance.noticeRoot.SetActive(false);
    }

    void Awake()
    {
        // Prefabtan gelen kopya kendi kendini kaydediyor. İkinci bir kopya
        // oluşursa (iki sahne birden yüklenirse) iki mesaj üst üste binerdi.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (noticeRoot != null) noticeRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>("UI/ScreenNotice");
        if (prefab != null)
        {
            // Awake içinde `instance` kendini atıyor, burada tekrar
            // atamaya gerek yok.
            Instantiate(prefab).name = "ScreenNotice (otomatik)";
            if (instance != null) return;
        }

        Debug.LogWarning("[ScreenNotice] Assets/Resources/UI/ScreenNotice.prefab bulunamadı — " +
                         "üst menüden 'SaboTour > UI Prefabları > Ekran Mesajı Prefabını Oluştur' " +
                         "çalıştır. Şimdilik basit bir yedek yazı kullanılıyor.");

        GameObject go = new GameObject("ScreenNotice (yedek)");
        instance = go.AddComponent<ScreenNotice>();
        instance.BuildFallback();
    }

    /// <summary>
    /// Prefab yokken kullanılan asgari kurulum. Güzel görünmesi hedef
    /// değil — mesajın OKUNABİLİR olması hedef.
    /// </summary>
    private void BuildFallback()
    {
        GameObject canvasObj = new GameObject("NoticeCanvas");
        canvasObj.transform.SetParent(transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        // 🚨 CanvasScaler'ı VARSAYILAN bırakmak (Constant Pixel Size) 2K
        // ekranda yazıyı fiziksel olarak küçültüyordu — yedek yolda bile
        // 1920×1080 referanslı ölçekleme kuruluyor.
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        noticeRoot = new GameObject("NoticeRoot", typeof(RectTransform));
        noticeRoot.transform.SetParent(canvasObj.transform, false);
        RectTransform rootRect = noticeRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        GameObject textObj = new GameObject("Message", typeof(RectTransform));
        textObj.transform.SetParent(noticeRoot.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 44f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        noticeText = text;

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(900f, 300f);

        noticeRoot.SetActive(false);
    }

    private void ShowInternal(string message, float seconds)
    {
        if (noticeText == null || noticeRoot == null) return;

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
