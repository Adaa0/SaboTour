using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ANA MENÜ BUTONLARI KURUCU (tek seferlik Editor aracı)
///
/// Offline Scene'deki `LobbyCanvas > LobbyPanel` içine üç şey ekler:
///   • "Nasıl Oynanır" butonu
///   • "Geri Bildirim" butonu
///   • Bunların altında bir geri bildirim hatırlatma yazısı
/// ve hepsini `MainMenuButtons` component'ine bağlar.
///
/// ─── NEDEN AYRI PANEL ÜRETMİYOR ───────────────────────────────────────
/// Panellerin kendisi zaten `PauseMenu.prefab` içinde var ve o prefab her
/// sahnede otomatik yükleniyor. Buradaki butonlar sadece o panelleri
/// çağırıyor — aynı içeriğin ikinci bir kopyasını lobiye kurmak, ileride
/// metni iki ayrı yerde güncellemek demek olurdu.
///
/// ─── BUTONLAR NEDEN KOPYALANIYOR ──────────────────────────────────────
/// Sıfırdan buton kurmak yerine mevcut "Hazırım" butonu `Instantiate`
/// ediliyor: font, sprite, renk, boyut olduğu gibi korunuyor. (Aynı yöntem
/// PlaytestPanelBuilder'da da kullanıldı.)
///
/// 🚨 YERLEŞİM UYARISI: `LobbyPanel`de Layout Group YOK — butonlar elle
/// konumlandırılmış. Bu yüzden kopyalar kaynağın konumuna düşer ve üst üste
/// binerdi (PlaytestPanelBuilder'da birebir bu yaşandı: yeni butonlar
/// "Ayarlar"ın üstüne düşüp onu görünmez yaptı). Burada yeni butonlar,
/// mevcut sütunun ÖLÇÜLEN aralığıyla en alttaki butonun altına diziliyor.
/// Beğenmezsen Rect Tool ile elle taşıyabilirsin — kod bir daha karışmıyor.
/// </summary>
public static class MainMenuPanelBuilder
{
    private const string ScenePath = "Assets/Scenes/Mirror/Offline Scene.unity";

    private const string HowToPlayButtonName = "NasilOynanirButton";
    private const string FeedbackButtonName = "GeriBildirimButton";
    private const string ReminderName = "GeriBildirimHatirlatma";
    private const string QuickJoinButtonName = "HizliKatilButton";
    private const string NameFieldName = "IsimKutusu";

    // Sütunda iki buton bulunamazsa kullanılacak yedek aralık.
    private const float FallbackSpacing = 90f;

    [MenuItem("SaboTour/Ana Menüye Nasıl Oynanır + Geri Bildirim Ekle")]
    public static void Build() => Build(true);

    /// <summary>
    /// 🚨 `interactive = false` ile çağrılırsa HİÇ dialog açmaz.
    /// NEDEN: modal pencereler MCP/otomasyondan çağrıldığında Unity'yi
    /// kilitliyor — bu projede gerçekten yaşandı (bkz. CLAUDE.md).
    /// </summary>
    public static void Build(bool interactive)
    {
        if (!EnsureSceneOpen(interactive)) return;

        LobbyManager lobby = Object.FindAnyObjectByType<LobbyManager>();
        if (lobby == null || lobby.LobbyPanel == null)
        {
            Report(interactive, "Bulunamadı",
                   "Sahnede LobbyManager (ya da onun LobbyPanel alanı) yok.", true);
            return;
        }

        RectTransform panel = lobby.LobbyPanel.GetComponent<RectTransform>();

        Button source = lobby.ReadyButton;
        if (source == null) source = panel.GetComponentInChildren<Button>(true);

        if (source == null)
        {
            Report(interactive, "Bulunamadı",
                   "Kopyalanacak bir buton yok (LobbyManager > Ready Button boş).", true);
            return;
        }

        // ── Zaten eklenmiş mi? ──
        if (panel.Find(HowToPlayButtonName) != null || panel.Find(FeedbackButtonName) != null)
        {
            Report(interactive, "Zaten var",
                   "Ana menü butonları bu sahnede zaten mevcut.\n\n" +
                   "Yeniden üretmek istersen önce LobbyPanel içindeki " +
                   $"'{HowToPlayButtonName}', '{FeedbackButtonName}' ve '{ReminderName}' " +
                   "objelerini sil.", false);
            return;
        }

        MeasureColumn(panel, source, out float spacing, out float lowestY, out float columnX);

        Button howTo = CloneButton(source, panel, HowToPlayButtonName, "Nasıl Oynanır");
        Place(howTo.GetComponent<RectTransform>(), columnX, lowestY - spacing);

        Button feedback = CloneButton(source, panel, FeedbackButtonName, "Geri Bildirim");
        Place(feedback.GetComponent<RectTransform>(), columnX, lowestY - spacing * 2f);

        TMP_Text reminder = CreateReminder(panel, source,
                                           columnX, lowestY - spacing * 2f - 70f);

        // ── Component'i bağla ──
        MainMenuButtons links = lobby.GetComponent<MainMenuButtons>();
        if (links == null) links = lobby.gameObject.AddComponent<MainMenuButtons>();

        links.howToPlayButton = howTo;
        links.feedbackButton = feedback;
        links.reminderText = reminder;

        reminder.text = links.reminderBeforePlaying;

        EditorUtility.SetDirty(links);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Report(interactive, "Tamam",
               "Ana menüye 'Nasıl Oynanır' ve 'Geri Bildirim' butonları + hatırlatma " +
               "yazısı eklendi ve sahne kaydedildi.\n\n" +
               "Konumları beğenmezsen LobbyPanel içinden elle taşıyabilirsin.", false);
    }

    /// <summary>
    /// Mevcut buton sütununu ölçer: hangi X'te duruyorlar, en alttaki nerede
    /// ve aralarındaki dikey boşluk ne kadar. Sabit sayı yazmak yerine
    /// ölçüyoruz ki geliştirici butonları taşımışsa yeni olanlar da ona uysun.
    /// </summary>
    private static void MeasureColumn(RectTransform panel, Button source,
                                      out float spacing, out float lowestY, out float columnX)
    {
        RectTransform sourceRect = source.GetComponent<RectTransform>();
        columnX = sourceRect.anchoredPosition.x;
        lowestY = sourceRect.anchoredPosition.y;
        spacing = FallbackSpacing;

        float nearestAbove = float.MaxValue;

        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            RectTransform rect = button.GetComponent<RectTransform>();

            // Sadece AYNI sütundaki butonlar sayılsın. Bu sahnede
            // "InviteButton" köşeye ayrı konmuş (x ≈ -634) — onu hesaba
            // katmak aralığı tamamen bozardı.
            if (Mathf.Abs(rect.anchoredPosition.x - columnX) > 40f) continue;

            float y = rect.anchoredPosition.y;

            if (y < lowestY) lowestY = y;

            // Kaynak butonun hemen ÜSTÜNDEKİ butonu bul → aradaki fark = aralık.
            if (y > sourceRect.anchoredPosition.y && y < nearestAbove)
                nearestAbove = y;
        }

        if (nearestAbove < float.MaxValue)
            spacing = Mathf.Abs(nearestAbove - sourceRect.anchoredPosition.y);
    }

    /// <summary>
    /// Butonu KOPYALAYARAK üretir — geliştiricinin ayarladığı font, sprite,
    /// renk ve boyut korunsun diye. Etiket hem TMP hem legacy Text için
    /// yazılıyor (lobi TMP kullanıyor ama prefab değişirse diye).
    /// </summary>
    private static Button CloneButton(Button source, Transform parent, string name, string label)
    {
        GameObject copy = Object.Instantiate(source.gameObject, parent);
        copy.name = name;
        copy.SetActive(true);

        Button button = copy.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();   // kaynaktan gelen olayları temizle

        TMP_Text tmp = copy.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) tmp.text = label;

        Text legacy = copy.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = label;

        return button;
    }

    /// <summary>
    /// Hatırlatma yazısı. Stili (font/renk) butonun kendi etiketinden
    /// alınıyor ki menüyle uyumlu görünsün.
    /// </summary>
    private static TMP_Text CreateReminder(RectTransform panel, Button styleSource,
                                           float x, float y)
    {
        GameObject obj = new GameObject(ReminderName, typeof(RectTransform));
        obj.transform.SetParent(panel, false);

        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 20f;
        text.color = new Color(1f, 0.92f, 0.55f);   // menüden ayrışsın diye hafif sarı

        TMP_Text style = styleSource.GetComponentInChildren<TMP_Text>(true);
        if (style != null && style.font != null) text.font = style.font;

        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(600f, 60f);
        Place(rect, x, y);

        return text;
    }

    private static void Place(RectTransform rect, float x, float y)
    {
        rect.anchoredPosition = new Vector2(x, y);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  HIZLI KATIL BUTONU
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ana menüye "Hızlı Katıl" butonu ekler ve `SteamLobbyManager`'ın
    /// `quickJoinButton` alanına bağlar.
    ///
    /// AYRI BİR MENÜ KOMUTU: Yukarıdaki araç zaten çalıştırıldıysa "zaten
    /// var" deyip çıkıyor; bu butonu ona eklemek, aracı bir daha
    /// çalıştırılamaz hâle getirirdi.
    ///
    /// 🚨 BUTON LobbyPanel'DE, COMPONENT NetworkManager'DA: `SteamLobbyManager`
    /// NetworkManager objesinde duruyor, buton ise lobi canvas'ında. Alan
    /// `[SerializeField] private` olduğu için `SerializedObject` ile
    /// yazılıyor — sırf Editor bağlayabilsin diye alanı public yapmaya
    /// gerek yok.
    /// </summary>
    [MenuItem("SaboTour/Ana Menüye Hızlı Katıl Butonu Ekle")]
    public static void BuildQuickJoin() => BuildQuickJoin(true);

    public static void BuildQuickJoin(bool interactive)
    {
        if (!EnsureSceneOpen(interactive)) return;

        LobbyManager lobby = Object.FindAnyObjectByType<LobbyManager>();
        if (lobby == null || lobby.LobbyPanel == null)
        {
            Report(interactive, "Bulunamadı",
                   "Sahnede LobbyManager (ya da onun LobbyPanel alanı) yok.", true);
            return;
        }

        SteamLobbyManager steam = Object.FindAnyObjectByType<SteamLobbyManager>(FindObjectsInactive.Include);
        if (steam == null)
        {
            Report(interactive, "Bulunamadı",
                   "Sahnede SteamLobbyManager yok — normalde NetworkManager objesinde olmalı.", true);
            return;
        }

        RectTransform panel = lobby.LobbyPanel.GetComponent<RectTransform>();

        Button source = lobby.ReadyButton;
        if (source == null) source = panel.GetComponentInChildren<Button>(true);

        if (source == null)
        {
            Report(interactive, "Bulunamadı", "Kopyalanacak bir buton yok.", true);
            return;
        }

        Transform existing = panel.Find(QuickJoinButtonName);
        Button quickJoin;

        if (existing != null)
        {
            // Buton duruyor ama bağlantısı kopmuş olabilir (script yeniden
            // derlendiğinde ya da elle silinip eklendiğinde) — yenisini
            // üretmek yerine mevcut olanı yeniden bağlıyoruz.
            quickJoin = existing.GetComponent<Button>();
        }
        else
        {
            MeasureColumn(panel, source, out float spacing, out float lowestY, out float columnX);

            quickJoin = CloneButton(source, panel, QuickJoinButtonName, "Hızlı Katıl");
            Place(quickJoin.GetComponent<RectTransform>(), columnX, lowestY - spacing);
        }

        SerializedObject so = new SerializedObject(steam);
        SerializedProperty prop = so.FindProperty("quickJoinButton");

        if (prop == null)
        {
            Report(interactive, "Hata",
                   "SteamLobbyManager'da 'quickJoinButton' alanı bulunamadı — script güncel mi?", true);
            return;
        }

        prop.objectReferenceValue = quickJoin;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Report(interactive, "Tamam",
               "Ana menüye 'Hızlı Katıl' butonu eklendi ve SteamLobbyManager'a bağlandı.\n\n" +
               "Bu buton yer olan bir PUBLIC oyun arar; bulamazsa kendisi public bir oyun " +
               "kurup bekler. 'Oyun Kur' değişmedi — o hâlâ sadece arkadaşlara açık.", false);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  İSİM KUTUSU
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ana menüye oyuncunun adını yazdığı kutuyu ekler ve `PlayerNameField`
    /// component'ine bağlar.
    ///
    /// Kutu, buton sütununun EN ÜSTÜNE konuyor — okuma sırası "önce adını
    /// yaz, sonra oyuna gir" olsun diye.
    ///
    /// `TMP_DefaultControls.CreateInputField` kullanılıyor: TMP_InputField
    /// elle kurulacak olsa viewport + text + placeholder + mask'ı ayrı ayrı
    /// bağlamak gerekirdi. Unity'nin kendi menüsünün kullandığı fabrika
    /// metodu aynısını doğru şekilde kuruyor.
    /// </summary>
    [MenuItem("SaboTour/Ana Menüye İsim Kutusu Ekle")]
    public static void BuildNameField() => BuildNameField(true);

    public static void BuildNameField(bool interactive)
    {
        if (!EnsureSceneOpen(interactive)) return;

        LobbyManager lobby = Object.FindAnyObjectByType<LobbyManager>();
        if (lobby == null || lobby.LobbyPanel == null)
        {
            Report(interactive, "Bulunamadı",
                   "Sahnede LobbyManager (ya da onun LobbyPanel alanı) yok.", true);
            return;
        }

        RectTransform panel = lobby.LobbyPanel.GetComponent<RectTransform>();

        Button source = lobby.ReadyButton;
        if (source == null) source = panel.GetComponentInChildren<Button>(true);

        Transform existing = panel.Find(NameFieldName);
        TMP_InputField field;

        if (existing != null)
        {
            field = existing.GetComponent<TMP_InputField>();
        }
        else
        {
            GameObject obj = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            obj.name = NameFieldName;
            obj.transform.SetParent(panel, false);

            field = obj.GetComponent<TMP_InputField>();

            StyleNameField(field, source, panel, out Vector2 position);
            Place(field.GetComponent<RectTransform>(), position.x, position.y);
        }

        if (field == null)
        {
            Report(interactive, "Hata", "İsim kutusu oluşturulamadı.", true);
            return;
        }

        field.characterLimit = PlayerNameSettings.MaxLength;
        field.lineType = TMP_InputField.LineType.SingleLine;

        PlayerNameField link = lobby.GetComponent<PlayerNameField>();
        if (link == null) link = lobby.gameObject.AddComponent<PlayerNameField>();
        link.input = field;

        EditorUtility.SetDirty(link);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Report(interactive, "Tamam",
               $"Ana menüye isim kutusu eklendi (en fazla {PlayerNameSettings.MaxLength} karakter).\n\n" +
               "Oyuncu adını buraya yazıyor; boş bırakırsa Steam profil adı kullanılıyor.\n\n" +
               "Konumunu beğenmezsen LobbyPanel içinden elle taşıyabilirsin.", false);
    }

    /// <summary>
    /// Kutuyu menünün geri kalanına benzetir ve sütunun en üstüne yerleştirir.
    /// Font, mevcut bir TMP yazısından alınıyor — varsayılan TMP fontu Türkçe
    /// karakterleri taşımayabilir, menüde zaten çalışan fontu kullanmak daha
    /// güvenli.
    /// </summary>
    private static void StyleNameField(TMP_InputField field, Button styleSource,
                                       RectTransform panel, out Vector2 position)
    {
        RectTransform rect = field.GetComponent<RectTransform>();
        rect.sizeDelta = styleSource != null
            ? styleSource.GetComponent<RectTransform>().sizeDelta
            : new Vector2(300f, 60f);

        TMP_Text sourceText = panel.GetComponentInChildren<TMP_Text>(true);

        if (field.textComponent != null && sourceText != null && sourceText.font != null)
            field.textComponent.font = sourceText.font;

        if (field.placeholder is TMP_Text placeholder)
        {
            placeholder.text = "İsmini yaz...";
            if (sourceText != null && sourceText.font != null) placeholder.font = sourceText.font;
        }

        // Sütunun EN ÜSTÜ: en yüksek butonun bir aralık üstü.
        float columnX = 0f;
        float highestY = float.MinValue;
        float spacing = FallbackSpacing;

        if (styleSource != null)
        {
            RectTransform sourceRect = styleSource.GetComponent<RectTransform>();
            columnX = sourceRect.anchoredPosition.x;

            MeasureColumn(panel, styleSource, out spacing, out _, out columnX);

            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            {
                RectTransform r = button.GetComponent<RectTransform>();
                if (Mathf.Abs(r.anchoredPosition.x - columnX) > 40f) continue;

                if (r.anchoredPosition.y > highestY) highestY = r.anchoredPosition.y;
            }
        }

        if (highestY <= float.MinValue) highestY = 0f;

        position = new Vector2(columnX, highestY + spacing);
    }

    /// <summary>
    /// Offline Scene açık değilse (izinle) açar. Açık sahnede kaydedilmemiş
    /// değişiklik olabilir, o yüzden ZORLA açmıyoruz.
    /// </summary>
    private static bool EnsureSceneOpen(bool interactive)
    {
        if (EditorSceneManager.GetActiveScene().path == ScenePath) return true;

        if (!interactive)
        {
            Debug.LogError($"[MainMenuPanelBuilder] Önce '{ScenePath}' sahnesini aç.");
            return false;
        }

        if (!EditorUtility.DisplayDialog("Sahne açık değil",
                "Bu araç Offline Scene üzerinde çalışıyor.\n\n" +
                "Açık sahneyi kaydedip Offline Scene'e geçmemi ister misin?",
                "Evet, aç", "Vazgeç"))
            return false;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    private static void Report(bool interactive, string title, string message, bool isError)
    {
        if (isError) Debug.LogError($"[MainMenuPanelBuilder] {message}");
        else Debug.Log($"[MainMenuPanelBuilder] {message}");

        if (interactive) EditorUtility.DisplayDialog(title, message, "Tamam");
    }
}
