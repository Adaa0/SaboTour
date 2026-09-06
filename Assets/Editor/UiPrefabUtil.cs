using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI PREFAB KURUCULARININ ORTAK YARDIMCILARI (sadece Editor).
///
/// `SaboteurHudPrefabBuilder`, `ScreenNoticePrefabBuilder` ve
/// `UiResolutionFixer` bunu kullanıyor. Eskiden her builder kendi
/// `CreateRect` / `Stretch` / `FindFont` kopyasını taşıyordu; üç dosyada
/// aynı kodu tutmak yerine tek yere alındı.
///
/// 🚨 EN ÖNEMLİ PARÇA: <see cref="ApplyStandardScaler"/>.
/// Bu projede "1080p'de güzel, 2K'da kayık" şikayetinin sebebi
/// CanvasScaler'ın **Constant Pixel Size** modunda kalmasıydı. O modda
/// UI, ekran büyüdükçe piksel olarak AYNI kalıyor:
///   • yazılar fiziksel olarak küçülüyor,
///   • ekranın ORTASINA sabitlenip büyük bir piksel kaydırmasıyla
///     yerleştirilmiş her şey (ör. sıralama tablosu) ekran büyüdükçe
///     ortaya doğru kayıyor.
/// Doğru mod **Scale With Screen Size** — 1920×1080 referansla, yani
/// "1080p'de nasıl duruyorsa her çözünürlükte öyle dursun".
/// </summary>
public static class UiPrefabUtil
{
    /// <summary>Tüm UI'ın tasarlandığı referans çözünürlük.</summary>
    public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    /// <summary>
    /// Canvas'ı projenin standart ölçekleme ayarına getirir.
    ///
    /// `matchWidthOrHeight = 0.5` NEDEN: 0 = sadece genişliğe göre ölçekle,
    /// 1 = sadece yüksekliğe göre. 16:9 ekranlarda üçü de aynı sonucu verir,
    /// ama 16:10 / 21:9 / ultrawide ekranlarda 0 seçilirse dikey yerleşim
    /// ekrandan taşar. 0.5 ikisinin ortasını alıp her en-boy oranında makul
    /// kalıyor.
    /// </summary>
    /// <returns>Bir şey değiştiyse true (rapor için).</returns>
    public static bool ApplyStandardScaler(CanvasScaler scaler)
    {
        if (scaler == null) return false;

        bool changed = scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize
                       || scaler.referenceResolution != ReferenceResolution
                       || !Mathf.Approximately(scaler.matchWidthOrHeight, 0.5f)
                       || scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return changed;
    }

    /// <summary>
    /// Ekran üstü (Screen Space - Overlay) bir Canvas kökü kurar ve
    /// standart ölçeklemeyi uygular.
    /// </summary>
    /// <param name="raycaster">
    /// UI tıklanabilir olacaksa true. Crosshair/bilgi yazısı gibi sadece
    /// GÖRÜNEN şeylerde false bırak — GraphicRaycaster her tıklamada bu
    /// canvas'ı da tarar, tıklanmayacak bir UI için gereksiz iş.
    /// </param>
    public static GameObject CreateOverlayCanvas(string name, int sortingOrder, bool raycaster)
    {
        GameObject root = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        ApplyStandardScaler(root.GetComponent<CanvasScaler>());

        if (raycaster) root.AddComponent<GraphicRaycaster>();

        return root;
    }

    public static GameObject CreateRect(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    /// <summary>Objeyi ebeveyninin tamamına yayar (dört kenara yapıştırır).</summary>
    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Objeyi ekranın bir KÖŞESİNE sabitler.
    ///
    /// 🚨 BU, ÇÖZÜNÜRLÜK SORUNUNUN İKİNCİ YARISI. Bir şeyi "sol üstte"
    /// göstermenin iki yolu var:
    ///   ❌ merkeze sabitleyip (-800, 400) gibi büyük bir kaydırma vermek —
    ///      ekran büyüyünce merkez kayar, obje ortaya doğru sürüklenir,
    ///   ✅ doğrudan sol üst köşeye sabitleyip küçük bir kenar boşluğu
    ///      vermek — ekran ne olursa olsun köşede kalır.
    /// Sıralama tablosu tam olarak birinci yoldan yapılmıştı.
    /// </summary>
    /// <param name="anchor">(0,1) sol üst, (1,1) sağ üst, (0,0) sol alt, (1,0) sağ alt.</param>
    /// <param name="margin">Köşeden içeri boşluk (piksel, 1080p referansında).</param>
    public static void AnchorToCorner(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 margin)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size;

        // Kenar boşluğu her zaman ekranın İÇİNE doğru olmalı: sağ kenarda
        // eksi X, üst kenarda eksi Y.
        rect.anchoredPosition = new Vector2(
            anchor.x > 0.5f ? -margin.x : margin.x,
            anchor.y > 0.5f ? -margin.y : margin.y);
    }

    /// <summary>
    /// Projede zaten kullanılan TMP fontunu bulur.
    ///
    /// NEDEN: TMP'nin varsayılan fontu Türkçe karakterleri (ğ ş ı İ ç ö ü)
    /// taşımayabiliyor — menüde çalıştığı görülmüş fontu kullanmak güvenli.
    /// </summary>
    public static TMP_FontAsset FindFont()
    {
        GameObject pauseMenu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/PauseMenu.prefab");
        if (pauseMenu != null)
        {
            foreach (TMP_Text text in pauseMenu.GetComponentsInChildren<TMP_Text>(true))
                if (text.font != null) return text.font;
        }

        return TMP_Settings.defaultFontAsset;
    }
}
