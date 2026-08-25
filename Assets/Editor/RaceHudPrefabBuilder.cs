using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// YARIŞ HUD PREFABI KURUCU (tek seferlik Editor aracı)
///
/// `Assets/Resources/UI/RaceHud.prefab` üretiyor:
///   • Ekranın ÜSTÜNDE ince bir süre çubuğu (azalarak boşalıyor)
///   • Çubuğun altında mm:ss yazısı
///   • Yarış bitince çıkan "Tekrar Oyna" butonu (sadece host'ta aktif)
///
/// `PauseMenuPrefabBuilder` ve `RacerMinimapPrefabBuilder` ile AYNI desen:
/// UI kodda kurulmuyor, prefabta yaşıyor — geliştirici çift tıklayıp normal
/// bir sahne gibi düzenleyebiliyor (renk, font, boyut, konum).
///
/// 🚨 `Build(bool interactive)` aşırı yüklemesi var: `false` ile çağrılırsa
/// hiç dialog açmıyor. Modal pencere MCP/otomasyondan çağrılınca Unity'yi
/// kilitliyor — bu projede gerçekten yaşandı.
/// </summary>
public static class RaceHudPrefabBuilder
{
    private const string FolderPath = "Assets/Resources/UI";
    private const string PrefabPath = FolderPath + "/RaceHud.prefab";

    [MenuItem("SaboTour/Yarış HUD Prefabını Oluştur")]
    public static void Build() => Build(true);

    public static void Build(bool interactive)
    {
        if (File.Exists(PrefabPath) && interactive)
        {
            bool overwrite = EditorUtility.DisplayDialog("Zaten var",
                $"{PrefabPath} zaten mevcut.\n\nÜzerine yazılsın mı? Prefab üzerinde " +
                "yaptığın elle düzenlemeler (renk, konum, font) KAYBOLUR.",
                "Üzerine yaz", "Vazgeç");

            if (!overwrite) return;
        }

        Directory.CreateDirectory(FolderPath);

        GameObject root = new GameObject("RaceHud",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(RaceHud));

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Yükleme ekranının (500) ALTINDA, oyun HUD'ının (0) üstünde.
        canvas.sortingOrder = 100;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RaceHud hud = root.GetComponent<RaceHud>();
        TMP_FontAsset font = FindFont();

        BuildBar(root.transform, hud, font);
        BuildRematch(root.transform, hud, font);
        BuildLeaderboard(root.transform, hud, font);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[RaceHud] Prefab üretildi: {PrefabPath}");

        if (interactive)
        {
            EditorUtility.DisplayDialog("Tamam",
                "Yarış HUD'ı oluşturuldu.\n\n" +
                "• Üstte ince süre çubuğu — azalıyor, son 60sn sarı, son 30sn kırmızı + nabız\n" +
                "• Yarış bitince 'Tekrar Oyna' (sadece host'ta basılabilir)\n\n" +
                "Sahneye elle bir şey eklemene gerek yok, oyun açılışında kendi yükleniyor. " +
                "Rengini/konumunu değiştirmek için prefaba çift tıkla.",
                "Tamam");
        }
    }

    /// <summary>
    /// Süre çubuğu: ekranın en üstüne yapışık, ince bir şerit.
    /// Arkada koyu bir zemin, önünde `Filled` tipinde dolan/boşalan bir Image.
    /// </summary>
    private static void BuildBar(Transform parent, RaceHud hud, TMP_FontAsset font)
    {
        GameObject barRoot = CreateRect("TimeBar", parent);
        RectTransform barRect = barRoot.GetComponent<RectTransform>();

        // Ekranın üst kenarına tam genişlikte yapıştır.
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.anchoredPosition = Vector2.zero;
        barRect.sizeDelta = new Vector2(0f, 10f);

        // Arka zemin — çubuk boşaldıkça ne kadarının gittiği görünsün.
        GameObject bg = CreateRect("Background", barRoot.transform);
        Stretch(bg.GetComponent<RectTransform>());
        Image bgImage = bg.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.55f);
        bgImage.raycastTarget = false;

        // Dolan kısım — düz bir dikdörtgen.
        //
        // 🚨 `Image.Type.Filled` + `fillAmount` KULLANILMIYOR: fillAmount
        // yalnızca Image'ın bir SPRITE'ı varsa çalışıyor, sprite'sız Image'da
        // tamamen yok sayılıyor (çubuk hiç azalmıyor, sadece rengi değişiyor —
        // bu gerçekten yaşandı). `RaceHud` bunun yerine RectTransform'un
        // `anchorMax.x`'ini kısıyor; sprite'a hiç ihtiyaç duymuyor.
        GameObject fill = CreateRect("Fill", barRoot.transform);
        Stretch(fill.GetComponent<RectTransform>());
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.35f, 0.85f, 0.55f);
        fillImage.raycastTarget = false;

        // Sayısal okuma — çubuk "ne kadar kaldı"yı hissettiriyor, yazı
        // "tam olarak ne kadar"ı söylüyor. İkisi farklı soruya cevap veriyor.
        GameObject textObj = CreateRect("TimeText", barRoot.transform);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0f);
        textRect.anchorMax = new Vector2(0.5f, 0f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = new Vector2(0f, -4f);
        textRect.sizeDelta = new Vector2(180f, 34f);

        TextMeshProUGUI timeText = textObj.AddComponent<TextMeshProUGUI>();
        timeText.text = "5:00";
        timeText.fontSize = 24f;
        timeText.alignment = TextAlignmentOptions.Center;
        timeText.color = Color.white;
        timeText.raycastTarget = false;
        if (font != null) timeText.font = font;

        hud.barRoot = barRoot;
        hud.barFill = fillImage;
        hud.timeText = timeText;
    }

    /// <summary>
    /// "Tekrar Oyna" — yarış bitince ekranın altında çıkıyor.
    /// Podyum kamerası devredeyken görünüyor, yani oyuncunun manzarasını
    /// kapatmıyor.
    /// </summary>
    private static void BuildRematch(Transform parent, RaceHud hud, TMP_FontAsset font)
    {
        GameObject rematchRoot = CreateRect("Rematch", parent);
        RectTransform rootRect = rematchRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 60f);
        rootRect.sizeDelta = new Vector2(520f, 110f);

        // Buton
        GameObject buttonObj = CreateRect("RematchButton", rematchRoot.transform);
        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 1f);
        buttonRect.anchorMax = new Vector2(0.5f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(320f, 64f);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.12f, 0.14f, 0.18f, 0.92f);

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        GameObject label = CreateRect("Label", buttonObj.transform);
        Stretch(label.GetComponent<RectTransform>());
        TextMeshProUGUI labelText = label.AddComponent<TextMeshProUGUI>();
        labelText.text = "Tekrar Oyna";
        labelText.fontSize = 28f;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = Color.white;
        labelText.raycastTarget = false;
        if (font != null) labelText.font = font;

        // Client'lara "neden bende buton yok" sorusunu doğurtmayan satır.
        GameObject infoObj = CreateRect("InfoText", rematchRoot.transform);
        RectTransform infoRect = infoObj.GetComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0.5f, 0f);
        infoRect.anchorMax = new Vector2(0.5f, 0f);
        infoRect.pivot = new Vector2(0.5f, 0f);
        infoRect.anchoredPosition = Vector2.zero;
        infoRect.sizeDelta = new Vector2(520f, 34f);

        TextMeshProUGUI infoText = infoObj.AddComponent<TextMeshProUGUI>();
        infoText.text = "Host yeni yarışı başlatabilir.";
        infoText.fontSize = 20f;
        infoText.alignment = TextAlignmentOptions.Center;
        infoText.color = new Color(1f, 1f, 1f, 0.75f);
        infoText.raycastTarget = false;
        if (font != null) infoText.font = font;

        hud.rematchRoot = rematchRoot;
        hud.rematchButton = button;
        hud.rematchInfoText = infoText;

        rematchRoot.SetActive(false);
    }

    /// <summary>
    /// MEVCUT prefaba sıralama tablosunu EKLER (yoksa).
    ///
    /// 🚨 NEDEN AYRI BİR YOL: `Build()` prefabı sıfırdan üretiyor, yani
    /// geliştiricinin prefab üzerinde elle yaptığı her şeyi (renk, konum,
    /// font) siler. Tablo sonradan eklenen bir parça olduğu için mevcut
    /// prefabı bozmadan içine yerleştirilmesi gerekiyor.
    /// `PlaytestPanelBuilder`'ın PauseMenu'ye panel eklerken kullandığı
    /// desenin aynısı.
    /// </summary>
    /// <returns>Gerçekten eklendiyse true.</returns>
    public static bool EnsureLeaderboard()
    {
        if (!File.Exists(PrefabPath))
        {
            Debug.LogWarning($"[RaceHud] {PrefabPath} yok — önce " +
                             "'SaboTour > Yarış HUD Prefabını Oluştur' çalıştır.");
            return false;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            RaceHud hud = contents.GetComponent<RaceHud>();
            if (hud == null)
            {
                Debug.LogWarning("[RaceHud] Prefabın kökünde RaceHud bileşeni yok, atlandı.");
                return false;
            }

            // Zaten varsa dokunma — araç birden çok kez çalıştırılabilsin.
            foreach (TMP_Text existing in contents.GetComponentsInChildren<TMP_Text>(true))
            {
                if (existing.gameObject.name == "LeaderboardText")
                {
                    if (hud.leaderboardText == null)
                    {
                        hud.leaderboardText = existing;
                        PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                        AssetDatabase.SaveAssets();
                        Debug.Log("[RaceHud] Sıralama tablosu referansı yeniden bağlandı.");
                        return true;
                    }
                    return false;
                }
            }

            BuildLeaderboard(contents.transform, hud, FindFont());

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[RaceHud] Sıralama tablosu prefaba eklendi (sol üst köşe).");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// SIRALAMA TABLOSU — sol üst köşe.
    ///
    /// ─── NEDEN ARTIK SAHNEDE DEĞİL, BURADA ────────────────────────────
    /// Eskiden Online Scene'in içinde tek başına bir `LeaderboardText`
    /// objesiydi ve iki ayrı sebepten çözünürlük değişince kayıyordu:
    ///
    ///   1. O sahnenin Canvas'ı **Constant Pixel Size** modundaydı — UI
    ///      ekran büyüdükçe piksel olarak aynı kalıyor, yani fiziksel
    ///      olarak küçülüyordu.
    ///   2. Kutu ekranın MERKEZİNE sabitlenip (-800, +400) kaydırılmıştı.
    ///      Merkez, ekran genişledikçe sağa kayıyor; kutu da onunla birlikte
    ///      ortaya doğru sürükleniyordu. 1080p'de sol kenardan %8 içerideydi,
    ///      1440p'de %19 — "2K'da ortaya kayık" şikayeti tam olarak buydu.
    ///
    /// Şimdi doğrudan SOL ÜST KÖŞEYE sabitli (anchor 0,1) ve 1920×1080
    /// referanslı ölçekleme kullanan bu prefabın içinde — her çözünürlükte
    /// köşede, aynı oranda duruyor.
    ///
    /// ─── KUTU BOYUTU ──────────────────────────────────────────────────
    /// Sahnedeki eski kutu 200×50 px ve font 36'ydı: 36 puntoluk bir satır
    /// ~40 px, yani o kutuya TEK satır sığıyordu — başlık + 3 yarışçı =
    /// 4 satır kutunun dışına taşıp üst üste binmiş gibi görünüyordu
    /// (CLAUDE.md'de "yazı bozuk" olarak kayıtlı). Burada 520×230 ve font 22.
    /// </summary>
    private static void BuildLeaderboard(Transform parent, RaceHud hud, TMP_FontAsset font)
    {
        // 🚨 İSİM ÖNEMLİ: `RaceLeaderboard`, referansı boş bulursa
        // `GameObject.Find("LeaderboardText")` ile arıyor. Yeniden
        // adlandırırsan o yedek yol kopar.
        GameObject obj = CreateRect("LeaderboardText", parent);
        RectTransform rect = obj.GetComponent<RectTransform>();

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(520f, 230f);

        // Üst kenardaki süre çubuğunun (10 px) altında kalsın diye Y payı
        // biraz fazla.
        rect.anchoredPosition = new Vector2(28f, -28f);

        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.text = "";
        text.fontSize = 22f;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = Color.white;
        text.raycastTarget = false;

        // Satır kaydırma KAPALI — kutu darken TMP satırı ortadan bölüp
        // süreyi ("0:11") alt satıra atıyordu. Kırpma işini RaceLeaderboard
        // `maxNameLength` ile kontrollü yapıyor.
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;

        if (font != null) text.font = font;

        hud.leaderboardText = text;
    }

    // ─── Yardımcılar ────────────────────────────────────────────────────

    private static GameObject CreateRect(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Projede zaten kullanılan TMP fontunu bulur — varsayılan TMP fontu
    /// Türkçe karakterleri (ğ ş ı İ ç ö ü) taşımayabiliyor, menüde çalışan
    /// fontu kullanmak daha güvenli.
    /// </summary>
    private static TMP_FontAsset FindFont()
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
