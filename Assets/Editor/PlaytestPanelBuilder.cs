using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PLAYTEST PANELLERİ KURUCU (tek seferlik Editor aracı)
///
/// `Assets/Resources/UI/PauseMenu.prefab` içine iki panel ekler:
///   • Geri Bildirim  → oyuncu oyundan çıkmadan mesaj yazıp gönderir
///   • Nasıl Oynanır  → tek sayfa kontrol/kural özeti
/// ve ana menüye bu panelleri açan iki buton koyar.
///
/// ─── NEDEN PauseMenu.prefab'IN İÇİNE ──────────────────────────────────
/// O prefab `[RuntimeInitializeOnLoadMethod]` ile HER SAHNEDE otomatik
/// yükleniyor (DontDestroyOnLoad). Yani panelleri oraya koyunca ana menüde,
/// lobide, yarışın ortasında ve podyumda — her yerde ESC ile ulaşılabiliyor.
/// İnsanın "şunu bildireyim" dediği an genelde ana menüde değil, bir şey ters
/// gittiği andır; bu yüzden global olması önemli.
///
/// ─── NEDEN PREFABI SIFIRDAN ÜRETMİYOR ─────────────────────────────────
/// PauseMenuPrefabBuilder prefabı BAŞTAN kuruyor. Bu araç ise MEVCUT prefaba
/// EKLEME yapıyor — çünkü geliştirici o prefab üzerinde elle ayarlar yaptı
/// (slider atamaları, yerleşim). Baştan üretmek onları silerdi.
/// Araç aynı paneli iki kere eklemiyor: zaten varsa haber verip çıkıyor.
///
/// 🚨 KAYDETME YÖNTEMİ: `LoadPrefabContents` + `SaveAsPrefabAsset` +
/// `AssetDatabase.SaveAssets()` kullanılıyor. `EditPrefabContentsScope` bu
/// projede bir kere DENENDİ ve değişiklik SESSİZCE KAYBOLDU (bkz. CLAUDE.md,
/// Car.prefab syncDirection olayı). Kayıttan sonra ayrı bir okumayla
/// doğrulanması da o dersin parçası.
/// </summary>
public static class PlaytestPanelBuilder
{
    private const string PrefabPath = "Assets/Resources/UI/PauseMenu.prefab";

    [MenuItem("SaboTour/Playtest: Geri Bildirim + Nasıl Oynanır Panellerini Ekle")]
    public static void Build() => Build(true);

    /// <summary>
    /// 🚨 `interactive = false` ile çağrılırsa HİÇ dialog açmaz.
    /// NEDEN GEREKLİ: modal pencereler MCP/otomasyondan çağrıldığında Unity'yi
    /// kilitliyor — bu projede gerçekten yaşandı, RacerMinimapPrefabBuilder'a
    /// da aynı sebeple böyle bir aşırı yükleme eklenmişti (bkz. CLAUDE.md).
    /// </summary>
    public static void Build(bool interactive)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Report(interactive, "Bulunamadı",
                $"{PrefabPath} yok.\n\nÖnce SaboTour > Ayarlar Menüsü Prefabını Oluştur ile ana menüyü üret.", true);
            return;
        }

        try
        {
            PauseMenuController pm = root.GetComponentInChildren<PauseMenuController>(true);
            if (pm == null)
            {
                Report(interactive, "Hata", "Prefabda PauseMenuController yok.", true);
                return;
            }

            if (pm.feedbackMenu != null && pm.howToPlayPanel != null)
            {
                Report(interactive, "Zaten var",
                    "Paneller bu prefaba daha önce eklenmiş. Yeniden kurmak istiyorsan " +
                    "prefabdan 'FeedbackPanel' ve 'HowToPlayPanel' objelerini silip tekrar çalıştır.", false);
                return;
            }

            if (pm.mainButtonsPanel == null || pm.ayarlarButton == null)
            {
                Report(interactive, "Hata",
                    "Prefabda 'Main Buttons Panel' ya da 'Ayarlar Button' referansı boş. " +
                    "Önce onları bağla.", true);
                return;
            }

            // Panellerin konulacağı yer: ayarlar paneliyle AYNI ebeveyn, aynı
            // hizalama. Böylece geliştiricinin ayarlar paneli için yaptığı
            // yerleşim tercihleri yeni panellerde de geçerli oluyor.
            RectTransform reference = pm.settingsMenu != null
                ? pm.settingsMenu.GetComponent<RectTransform>()
                : pm.mainButtonsPanel.GetComponent<RectTransform>();

            Font font = FindFont(root);

            if (pm.howToPlayPanel == null) BuildHowToPlay(pm, reference, font);
            if (pm.feedbackMenu == null) BuildFeedback(pm, reference, font);

            EditorUtility.SetDirty(pm);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Verify(interactive);
    }

    /// <summary>Dialog yerine (otomasyonda) Console'a yazar.</summary>
    private static void Report(bool interactive, string title, string message, bool isError)
    {
        if (interactive) EditorUtility.DisplayDialog(title, message, "Tamam");
        else if (isError) Debug.LogError($"[PlaytestPanelBuilder] {title}: {message}");
        else Debug.LogWarning($"[PlaytestPanelBuilder] {title}: {message}");
    }

    /// <summary>
    /// Kaydın gerçekten tuttuğunu AYRI bir okumayla doğrular — "kaydedildi"
    /// logu tek başına yeterli kanıt değil (CLAUDE.md dersi).
    /// </summary>
    private static void Verify(bool interactive)
    {
        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        PauseMenuController pm = saved != null ? saved.GetComponentInChildren<PauseMenuController>(true) : null;

        bool ok = pm != null && pm.feedbackMenu != null && pm.howToPlayPanel != null
                  && pm.feedbackButton != null && pm.nasilOynanirButton != null;

        if (ok)
        {
            Debug.Log("[PlaytestPanelBuilder] ✅ Paneller eklendi ve doğrulandı. " +
                      "Prefabı açıp FeedbackPanel > Feedback Menu Controller > Webhook Url alanını doldurmayı unutma.");
            if (interactive)
            {
                EditorUtility.DisplayDialog("Tamam",
                    "Geri Bildirim ve Nasıl Oynanır panelleri eklendi.\n\n" +
                    "SIRADAKİ ADIM: Prefabı aç → FeedbackPanel → Feedback Menu Controller →\n" +
                    "• Form Url  (.../formResponse ile biten adres)\n" +
                    "• Name / Message / Context Entry Id  (entry.123456789 biçiminde)\n" +
                    "alanlarını doldur.", "Tamam");
                Selection.activeObject = saved;
            }
        }
        else
        {
            Debug.LogError("[PlaytestPanelBuilder] ❌ Kayıt tutmadı — referanslar boş görünüyor.");
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  NASIL OYNANIR
    // ═════════════════════════════════════════════════════════════════════

    private static void BuildHowToPlay(PauseMenuController pm, RectTransform reference, Font font)
    {
        GameObject panel = CreatePanel("HowToPlayPanel", reference, "NASIL OYNANIR", font, out RectTransform body);

        Text content = CreateText(body, "Content", HowToPlayText, font, 16, TextAnchor.UpperLeft);
        Stretch(content.rectTransform, 0f, 40f, 0f, 0f);

        Button geri = CloneButton(pm.ayarlarButton, panel.transform, "GeriButton", "Geri");
        PlaceBottom(geri.GetComponent<RectTransform>(), 0f);

        pm.howToPlayPanel = panel;
        pm.howToPlayGeriButton = geri;
        pm.nasilOynanirButton = CloneButton(pm.ayarlarButton, pm.mainButtonsPanel.transform,
                                            "NasilOynanirButton", "Nasıl Oynanır");

        panel.SetActive(false);
    }

    private const string HowToPlayText =
        "SaboTour asimetrik bir yarış oyunu: bir oyuncu SABOTAJCI, geri kalan herkes YARIŞÇI.\n\n" +
        "KAZANMA\n" +
        "• Yarışçılar turlarını bitirirse yarışçılar kazanır.\n" +
        "• Süre dolmadan kimse bitiremezse sabotajcı kazanır.\n\n" +
        "YARIŞÇI KONTROLLERİ\n" +
        "• W / S — gaz ve fren\n" +
        "• A / D — direksiyon\n" +
        "• Space — el freni (drift)\n" +
        "• ESC — menü\n" +
        "Pistten çok uzaklaşırsan ya da bir checkpoint'i atlarsan otomatik olarak geri ışınlanırsın.\n\n" +
        "SABOTAJCI KONTROLLERİ\n" +
        "• W A S D — yürü, Shift — koş, Space — zıpla\n" +
        "• Fare — bak, Sol tık — etkileşim\n" +
        "• ESC — imleci serbest bırak / menü\n\n" +
        "SABOTAJCI NE YAPAR\n" +
        "Kulenin içindeki masada pistin haritası var. Sırayla:\n" +
        "1. Haritadan bir checkpoint'e tıkla (seçilince kırmızı olur).\n" +
        "2. Bir skil butonuna bas (seçili buton basılı kalır).\n" +
        "3. Büyük kırmızı butona basıp tuzağı ateşle.\n\n" +
        "Buton basılı ve SÖNÜKSE o skil şarj oluyordur — üzerine bakınca kalan " +
        "saniye görünür. Yeşil marker hazır checkpoint, kırmızı marker seçili ya da " +
        "az önce tuzaklanmış demektir.\n\n" +
        "ÜÇ SKİL\n" +
        "• Buz Bombası (mavi) — checkpoint'e bomba düşer, araçları savurur ve yeri kayganlaştırır.\n" +
        "• Tavuk Sürüsü (sarı) — piste tavuklar salar, çarpan yavaşlar.\n" +
        "• Motor Arızası (turuncu) — o checkpoint'ten geçen İLK araç birkaç saniye güç kaybeder.";

    // ═════════════════════════════════════════════════════════════════════
    //  GERİ BİLDİRİM
    // ═════════════════════════════════════════════════════════════════════

    private static void BuildFeedback(PauseMenuController pm, RectTransform reference, Font font)
    {
        GameObject panel = CreatePanel("FeedbackPanel", reference, "GERİ BİLDİRİM", font, out RectTransform body);

        FeedbackMenuController fb = panel.AddComponent<FeedbackMenuController>();

        Text info = CreateText(body, "Info",
            "Ne beğendin, ne bozuk, ne eksik? Yazdıkların doğrudan geliştiriciye gider.\n" +
            "Pist numarası, rolün ve teknik bilgiler otomatik ekleniyor — yazmana gerek yok.",
            font, 13, TextAnchor.UpperLeft);
        Stretch(info.rectTransform, 0f, 40f, 0f, 0f);
        info.rectTransform.sizeDelta = new Vector2(info.rectTransform.sizeDelta.x, 44f);
        info.rectTransform.anchorMin = new Vector2(0f, 1f);
        info.rectTransform.anchorMax = new Vector2(1f, 1f);
        info.rectTransform.pivot = new Vector2(0.5f, 1f);
        info.rectTransform.anchoredPosition = new Vector2(0f, -44f);
        info.rectTransform.offsetMin = new Vector2(12f, info.rectTransform.offsetMin.y);
        info.rectTransform.offsetMax = new Vector2(-12f, info.rectTransform.offsetMax.y);

        InputField nameField = CreateInputField(body, "NameInput", "İsmin (isteğe bağlı)", font, false);
        AnchorTop(nameField.GetComponent<RectTransform>(), -96f, 34f);

        InputField messageField = CreateInputField(body, "MessageInput", "Buraya yaz…", font, true);
        AnchorTop(messageField.GetComponent<RectTransform>(), -138f, 150f);

        Text status = CreateText(body, "StatusText", "", font, 14, TextAnchor.MiddleCenter);
        AnchorTop(status.rectTransform, -296f, 26f);

        Button gonder = CloneButton(pm.ayarlarButton, panel.transform, "GonderButton", "Gönder");
        PlaceBottom(gonder.GetComponent<RectTransform>(), 44f);

        Button geri = CloneButton(pm.ayarlarButton, panel.transform, "GeriButton", "Geri");
        PlaceBottom(geri.GetComponent<RectTransform>(), 0f);

        fb.messageInput = messageField;
        fb.nameInput = nameField;
        fb.gonderButton = gonder;
        fb.geriButton = geri;
        fb.statusText = status;

        pm.feedbackMenu = fb;
        pm.feedbackButton = CloneButton(pm.ayarlarButton, pm.mainButtonsPanel.transform,
                                        "FeedbackButton", "Geri Bildirim");

        // Yeni butonları "Devam Et"in hemen altına al, sonra tüm sütunu
        // yeniden diz. Sıralama: Devam Et → Nasıl Oynanır → Geri Bildirim →
        // Ayarlar → Oyundan Ayrıl → Oyunu Kapat (yıkıcı olanlar en altta).
        if (pm.nasilOynanirButton != null) pm.nasilOynanirButton.transform.SetSiblingIndex(1);
        pm.feedbackButton.transform.SetSiblingIndex(2);
        LayoutMainButtons(pm.mainButtonsPanel);

        panel.SetActive(false);
    }

    /// <summary>
    /// Ana menü butonlarını tek bir dikey sütuna yeniden dizer.
    ///
    /// 🚨 NEDEN GEREKLİ (gerçekten yaşandı): Bu panelde LAYOUT GROUP YOK —
    /// butonlar elle `anchoredPosition` ile yerleştirilmiş (y = 135, 45, -45,
    /// -135). `Instantiate` kaynağın RectTransform'unu da kopyaladığı için
    /// yeni butonlar "Ayarlar"ın TAM ÜSTÜNE düştü ve onu görünmez yaptı.
    /// Buton kopyalarken pozisyonu ayrıca ayarlamak ŞART.
    ///
    /// Aralık mevcut butonlardan ölçülüyor — geliştirici prefabda aralığı
    /// değiştirdiyse yeni butonlar da ona uyuyor.
    /// </summary>
    private static void LayoutMainButtons(GameObject panel)
    {
        int count = panel.transform.childCount;
        if (count == 0) return;

        float spacing = 90f;
        if (count >= 3)
        {
            RectTransform a = panel.transform.GetChild(0) as RectTransform;
            RectTransform b = panel.transform.GetChild(1) as RectTransform;
            if (a != null && b != null)
            {
                float measured = Mathf.Abs(a.anchoredPosition.y - b.anchoredPosition.y);
                if (measured > 1f) spacing = measured;
            }
        }

        // Sütunu dikeyde ortala: en üstteki +yarım aralık, en alttaki -yarım.
        float top = (count - 1) * spacing * 0.5f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = panel.transform.GetChild(i) as RectTransform;
            if (rt == null) continue;

            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, top - i * spacing);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  UI YARDIMCILARI
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ayarlar paneliyle aynı yerde/boyutta bir panel kurar ve başlığını yazar.
    /// `body`, başlığın altındaki içerik alanı.
    /// </summary>
    private static GameObject CreatePanel(string name, RectTransform reference, string title,
                                          Font font, out RectTransform body)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(reference.parent, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = reference.anchorMin;
        rt.anchorMax = reference.anchorMax;
        rt.pivot = reference.pivot;
        rt.anchoredPosition = reference.anchoredPosition;
        rt.sizeDelta = reference.sizeDelta;
        rt.offsetMin = reference.offsetMin;
        rt.offsetMax = reference.offsetMax;

        Image bg = panel.GetComponent<Image>();
        Image referenceBg = reference.GetComponent<Image>();
        if (referenceBg != null)
        {
            bg.sprite = referenceBg.sprite;
            bg.color = referenceBg.color;
            bg.type = referenceBg.type;
        }
        else bg.color = new Color(0f, 0f, 0f, 0.85f);

        Text header = CreateText(rt, "Title", title, font, 22, TextAnchor.UpperCenter);
        AnchorTop(header.rectTransform, -8f, 32f);

        GameObject bodyObj = new GameObject("Body", typeof(RectTransform));
        bodyObj.transform.SetParent(panel.transform, false);
        body = bodyObj.GetComponent<RectTransform>();
        Stretch(body, 12f, 44f, 12f, 52f);

        return panel;
    }

    private static Text CreateText(Transform parent, string name, string content, Font font,
                                   int size, TextAnchor anchor)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);

        Text text = obj.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;

        Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
        return text;
    }

    /// <summary>
    /// Legacy InputField üç parçadan oluşuyor: arka plan (Image), yazının
    /// çizildiği Text ve boşken görünen placeholder Text. Elle kurmak gerekiyor.
    /// </summary>
    private static InputField CreateInputField(Transform parent, string name, string placeholder,
                                               Font font, bool multiline)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);

        Image bg = obj.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.12f);

        Text text = CreateText(obj.transform, "Text", "", font, 15, TextAnchor.UpperLeft);
        Stretch(text.rectTransform, 8f, 6f, 8f, 6f);

        Text hint = CreateText(obj.transform, "Placeholder", placeholder, font, 15, TextAnchor.UpperLeft);
        hint.color = new Color(1f, 1f, 1f, 0.4f);
        hint.fontStyle = FontStyle.Italic;
        Stretch(hint.rectTransform, 8f, 6f, 8f, 6f);

        InputField field = obj.AddComponent<InputField>();
        field.textComponent = text;
        field.placeholder = hint;
        field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        field.characterLimit = multiline ? 1200 : 40;
        field.targetGraphic = bg;

        return field;
    }

    /// <summary>
    /// Butonu KOPYALAYARAK üretiyoruz — böylece geliştiricinin ayarladığı
    /// font, sprite, renk ve boyut olduğu gibi korunuyor. Sıfırdan buton
    /// kurmak menünün geri kalanıyla uyumsuz görünürdü.
    /// </summary>
    private static Button CloneButton(Button source, Transform parent, string name, string label)
    {
        GameObject copy = Object.Instantiate(source.gameObject, parent);
        copy.name = name;

        Button button = copy.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();   // kaynaktan gelen olayları temizle

        Text text = copy.GetComponentInChildren<Text>(true);
        if (text != null) text.text = label;

        return button;
    }

    /// <summary>Prefabda zaten kullanılan fontu bulur — yenisini aramaktansa mevcut stile uy.</summary>
    private static Font FindFont(GameObject root)
    {
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
            if (text.font != null) return text.font;

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static void AnchorTop(RectTransform rt, float y, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(12f, 0f);
        rt.offsetMax = new Vector2(-12f, 0f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
    }

    private static void PlaceBottom(RectTransform rt, float y)
    {
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 8f + y);
    }
}
