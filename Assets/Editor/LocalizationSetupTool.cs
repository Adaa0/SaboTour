using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// DİL KURULUM ARACI — tek seferlik Editor işi, oyunun parçası DEĞİL.
///
/// İki iş yapıyor:
///  1. Ayarlar paneline DİL dropdown'ı ekliyor (SettingsMenuController'ın
///     languageDropdown alanına da bağlıyor).
///  2. Prefab ve sahnelerdeki SABİT Türkçe yazıları tarayıp, tanıdığı her
///     birine bir LocalizedText bileşeni ekleyip doğru anahtarı atıyor.
///
/// ── NEDEN PREFAB SIFIRDAN ÜRETİLMİYOR ──
/// PauseMenuPrefabBuilder.Build() prefabı BAŞTAN kuruyor — onu çağırsaydık
/// geliştiricinin elle yaptığı renk/konum/font ayarları ve slider atamaları
/// silinirdi. Bu araç PlaytestPanelBuilder'ın desenini izliyor: mevcut
/// prefabı AÇIP üzerine EKLİYOR, hiçbir şeyi yeniden kurmuyor.
///
/// İki kez çalıştırmak zararsız — zaten LocalizedText taşıyan bir yazıya
/// ikinci kez eklemiyor, zaten var olan dropdown'ı tekrar oluşturmuyor.
/// </summary>
public static class LocalizationSetupTool
{
    private const string PauseMenuPath = "Assets/Resources/UI/PauseMenu.prefab";
    private const string RaceHudPath = "Assets/Resources/UI/RaceHud.prefab";

    /// <summary>
    /// GÖRÜNEN YAZI → SÖZLÜK ANAHTARI eşleştirmesi.
    ///
    /// Eşleştirme yazının KENDİSİNE bakıyor, obje adına değil — obje adları
    /// bu projede tutarsız (HostButton, ReadyButton, NasilOynanirButton) ve
    /// bazıları elle yeniden adlandırılmış olabilir. Yazının kendisi ise
    /// ekranda ne göründüğünün kesin kaynağı.
    /// </summary>
    private static readonly Dictionary<string, string> TextToKey = new Dictionary<string, string>
    {
        // Ana menü / lobi
        { "Oyun Kur", "menu.host" },
        { "Hızlı Katıl", "menu.quickjoin" },
        { "Davet Et", "menu.invite" },
        { "Arkadaş Davet Et", "menu.invite" },
        { "Nasıl Oynanır", "menu.howtoplay" },
        { "Geri Bildirim", "menu.feedback" },
        { "Hazırım", "menu.ready" },
        { "Hazır Değilim", "menu.notready" },
        { "Yükleniyor...", "menu.loading" },
        { "Oyuncular yükleniyor...", "menu.loadingplayers" },

        // Duraklatma menüsü
        { "Devam Et", "pause.resume" },
        { "Ayarlar", "pause.settings" },
        { "Oyundan Ayrıl", "pause.leave" },
        { "Oyunu Kapat", "pause.quit" },
        { "Geri", "pause.back" },

        // Ayarlar panelindeki etiketler
        { "Kare Hızı", "set.fps" },
        { "FPS Sınırı", "set.fps" },
        { "Tam Ekran", "set.fullscreen" },
        { "Çözünürlük", "set.resolution" },
        { "Ses Seviyesi", "set.volume" },
        { "Fare Hassasiyeti", "set.sensitivity" },
        { "Fare Hassasiyeti (Sabotajcı)", "set.sensitivity" },

        // Yarış HUD
        { "Tekrar Oyna", "race.rematch" },
        { "Host yeni yarışı başlatabilir.", "race.hostrestarts" },

        // Nasıl Oynanır paneli
        { "NASIL OYNANIR", "howto.title" },

        // Geri bildirim paneli
        { "Gönder", "fb.send" },
        { "GERİ BİLDİRİM", "fb.paneltitle" },
        { "İsmin (isteğe bağlı)", "fb.nameLabel" },
        { "Buraya yaz…", "fb.messageplaceholder" },

        // Ana menü isim kutusu
        { "İsmini yaz...", "menu.namehint" },
    };

    /// <summary>
    /// Uzun metinler yukarıdaki tabloya konmuyor — birebir eşleşme gerektiren
    /// bir sözlükte çok satırlı bir paragrafın tek bir karakteri bile
    /// değiştiğinde eşleşme sessizce kaybolurdu. Onun yerine OBJE ADIYLA
    /// eşleştiriliyor: "HowToPlayPanel altındaki Content" gibi.
    /// Biçim: "ÜstObjeAdı/ObjeAdı" → anahtar.
    /// </summary>
    private static readonly Dictionary<string, string> PathToKey = new Dictionary<string, string>
    {
        { "Body/Content", "howto.content" },  // Nasıl Oynanır metninin gövdesi
        { "Body/Info", "fb.info" },           // Geri bildirim panelinin açıklaması
    };

    [MenuItem("SaboTour/Dil/Dil Desteğini Kur (dropdown + çeviri etiketleri)")]
    public static void SetupAll() => SetupAll(true);

    /// <summary>
    /// interactive = false ise HİÇ dialog açmıyor.
    /// NEDEN: modal bir pencere, araç MCP/otomasyondan çağrıldığında Unity'yi
    /// kilitliyor — bu projede gerçekten yaşandı (bkz. RacerMinimapPrefabBuilder
    /// ve PlaytestPanelBuilder'daki aynı desen).
    /// </summary>
    public static void SetupAll(bool interactive)
    {
        int dropdownAdded = AddLanguageDropdown();
        int pausePrefab = TagPrefab(PauseMenuPath);
        int raceHudPrefab = TagPrefab(RaceHudPath);
        int scene = TagOpenScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary =
            $"Dil dropdown'ı: {(dropdownAdded == 1 ? "eklendi" : dropdownAdded == 0 ? "zaten vardı" : "EKLENEMEDİ")}\n" +
            $"PauseMenu.prefab: {pausePrefab} yazı etiketlendi\n" +
            $"RaceHud.prefab: {raceHudPrefab} yazı etiketlendi\n" +
            $"Açık sahne: {scene} yazı etiketlendi\n\n" +
            "NOT: Açık sahne için Ctrl+S ile KAYDETMEYİ unutma.\n" +
            "Ana menü yazıları için Offline Scene açıkken tekrar çalıştır.";

        Debug.Log("[Dil Kurulumu]\n" + summary);
        if (interactive)
            EditorUtility.DisplayDialog("Dil Desteği Kuruldu", summary, "Tamam");
    }

    [MenuItem("SaboTour/Dil/Eksik Çevirileri Tara (rapor)")]
    public static void ReportUntagged()
    {
        var found = new List<string>();

        ScanPrefabForReport(PauseMenuPath, found);
        ScanPrefabForReport(RaceHudPath, found);

        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
            CollectUntagged(root.transform, $"[Sahne: {active.name}]", found);

        if (found.Count == 0)
        {
            Debug.Log("[Dil Taraması] Etiketlenmemiş Türkçe yazı bulunamadı.");
            return;
        }

        Debug.LogWarning($"[Dil Taraması] {found.Count} yazı hâlâ çevrilmiyor:\n  " +
                         string.Join("\n  ", found) +
                         "\n\nBunlar için Loc.cs'e anahtar ekleyip, yazının objesine " +
                         "elle Localized Text bileşeni ekle (ya da bu aracın TextToKey " +
                         "tablosuna satır ekleyip tekrar çalıştır).");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  1) DİL DROPDOWN'I
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>1 = eklendi, 0 = zaten vardı, -1 = eklenemedi.</summary>
    private static int AddLanguageDropdown()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PauseMenuPath);
        if (root == null)
        {
            Debug.LogError($"[Dil Kurulumu] {PauseMenuPath} açılamadı. Önce " +
                           "'SaboTour > Ayarlar Menüsü Prefabını Oluştur' çalıştır.");
            return -1;
        }

        try
        {
            SettingsMenuController settings = root.GetComponentInChildren<SettingsMenuController>(true);
            if (settings == null)
            {
                Debug.LogError("[Dil Kurulumu] Prefabta SettingsMenuController bulunamadı.");
                return -1;
            }

            if (settings.languageDropdown != null) return 0; // zaten kurulu

            // Ayarlar panelinin en üstüne koyuyoruz: dil, diğer ayarları
            // OKUYABİLMEK için gereken ayar — listenin altında olsaydı
            // yanlış dile düşen oyuncunun onu bulması daha zor olurdu.
            GameObject panel = settings.gameObject;
            var resources = new DefaultControls.Resources();

            CreateLabel(panel, "Dil", 345f, 20);

            GameObject go = DefaultControls.CreateDropdown(resources);
            go.name = "LanguageDropdown";
            go.transform.SetParent(panel.transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 310f);
            rt.sizeDelta = new Vector2(300f, 32f);

            Dropdown dropdown = go.GetComponent<Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(GameLanguage.DisplayNames));

            settings.languageDropdown = dropdown;
            EditorUtility.SetDirty(settings);

            PrefabUtility.SaveAsPrefabAsset(root, PauseMenuPath);
            return 1;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 🚨 DEĞERLER TAHMİNLE DEĞİL, DİĞER AYAR ETİKETLERİYLE (Ses Seviyesi,
    /// Çözünürlük, Tam Ekran) BİREBİR EŞLEŞECEK ŞEKİLDE seçildi — geliştirici
    /// önce ilk sürümü "diğerleri gibi büyük değil, okunmuyor" diye
    /// düzeltilmesini istedi. Sebep: bu etiket sabit fontSize=24 + BestFit
    /// KAPALI + küçük bir kutuyla (300×30) kuruluyordu; diğer etiketler ise
    /// fontSize=20 ama BestFit AÇIK (min 10 / max 40) + geniş bir kutuyla
    /// (500×50) — Best Fit açıkken gerçek çizilen punto kutunun boyutuna göre
    /// büyüyor, yani "aynı fontSize" görsel olarak "aynı boyut" ANLAMINA
    /// gelmiyor. Buradaki değerler PauseMenuPrefabBuilder.CreateLabel'daki
    /// diğer çağrılarla (CreateLabel(settingsPanel, "Ses Seviyesi", -30f, 20))
    /// aynı görünüm verecek şekilde ayarlandı.
    /// </summary>
    private static void CreateLabel(GameObject parent, string text, float y, int fontSize)
    {
        GameObject go = new GameObject(text + "Label", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);

        Text label = go.AddComponent<Text>();
        label.text = text;
        // Arial.ttf DEĞİL: Unity 2022'den beri geçersiz ve hata fırlatıyor.
        // PauseMenuPrefabBuilder da bunu kullanıyor, aynı kaynağı kullanmak
        // etiketin diğer ayar etiketleriyle aynı görünmesini de sağlıyor.
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        // Diğer ayar etiketleriyle AYNI davranış: Best Fit açık, kutu
        // büyüdükçe/küçüldükçe yazı da onunla orantılı büyür/küçülür.
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 10;
        label.resizeTextMaxSize = 40;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        // Diğer etiketlerle AYNI kutu boyutu (500×50) — 300×30'luk eski kutu
        // Best Fit'i de açsak küçük kalırdı, gerçek fark BURADAYDI.
        rt.sizeDelta = new Vector2(500f, 50f);

        // Etiketin kendisi de çevrilsin.
        go.AddComponent<LocalizedText>().key = "set.language";
    }

    // ─────────────────────────────────────────────────────────────────────
    //  2) ÇEVİRİ ETİKETLERİ
    // ─────────────────────────────────────────────────────────────────────

    private static int TagPrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogWarning($"[Dil Kurulumu] {path} bulunamadı, atlandı.");
            return 0;
        }

        try
        {
            int count = TagRecursive(root.transform);
            if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            return count;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int TagOpenScene()
    {
        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid()) return 0;

        int count = 0;
        foreach (GameObject go in active.GetRootGameObjects())
            count += TagRecursive(go.transform);

        if (count > 0) EditorSceneManager.MarkSceneDirty(active);
        return count;
    }

    private static int TagRecursive(Transform t)
    {
        int count = 0;

        string text = ReadText(t.gameObject);
        if (!string.IsNullOrEmpty(text) && t.GetComponent<LocalizedText>() == null)
        {
            string key = null;

            // Önce obje yoluna bak (uzun paragraflar için), sonra yazının
            // kendisine. Yol eşleşmesi önce geliyor çünkü daha kesin.
            string path = (t.parent != null ? t.parent.name + "/" : "") + t.gameObject.name;
            if (!PathToKey.TryGetValue(path, out key))
                TextToKey.TryGetValue(text.Trim(), out key);

            if (!string.IsNullOrEmpty(key))
            {
                LocalizedText loc = t.gameObject.AddComponent<LocalizedText>();
                loc.key = key;
                EditorUtility.SetDirty(t.gameObject);
                count++;
            }
        }

        for (int i = 0; i < t.childCount; i++)
            count += TagRecursive(t.GetChild(i));

        return count;
    }

    /// <summary>Objedeki yazıyı okur — TMP ve legacy Text karışık kullanılıyor.</summary>
    private static string ReadText(GameObject go)
    {
        TMP_Text tmp = go.GetComponent<TMP_Text>();
        if (tmp != null) return tmp.text;

        Text legacy = go.GetComponent<Text>();
        return legacy != null ? legacy.text : null;
    }

    // ─── Rapor yardımcıları ──────────────────────────────────────────────

    private static void ScanPrefabForReport(string path, List<string> found)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null) return;

        try { CollectUntagged(root.transform, $"[{System.IO.Path.GetFileName(path)}]", found); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>
    /// BİLEREK çevrilmeyen yazılar — raporda çıkmamalılar, yoksa her taramada
    /// "3 yazı çevrilmiyor" uyarısı görünür ve gerçek eksikler bu gürültünün
    /// içinde kaybolur.
    /// </summary>
    private static bool IsIntentionallyUntranslated(GameObject go, string text)
    {
        // Dil dropdown'ının seçenekleri: her dil KENDİ adıyla yazılı olmalı.
        if (go.transform.parent != null && go.transform.parent.name == "LanguageDropdown") return true;
        foreach (string name in GameLanguage.DisplayNames)
            if (text.Trim() == name) return true;

        // Çözünürlük listesinin yer tutucusu — oyun açılınca kod dolduruyor.
        if (text.Contains("oyun açılınca dolduruluyor")) return true;

        // Kodla yazılan yazılar: metni duruma göre değiştikleri için
        // LocalizedText taşımıyorlar, çevirileri kendi script'lerinde.
        // (MainMenuButtons.RefreshReminder → menu.reminder.before/after)
        if (go.name == "GeriBildirimHatirlatma") return true;

        return false;
    }

    /// <summary>
    /// Türkçe karakter taşıyan ama LocalizedText'i olmayan yazıları topluyor.
    /// Türkçe karaktere bakmak kaba bir ölçüt ama işe yarıyor: çevrilmemiş
    /// bir Türkçe cümlenin içinde neredeyse her zaman en az bir tane var.
    /// </summary>
    private static void CollectUntagged(Transform t, string context, List<string> found)
    {
        string text = ReadText(t.gameObject);

        if (!string.IsNullOrWhiteSpace(text)
            && t.GetComponent<LocalizedText>() == null
            && ContainsTurkishChars(text)
            && !IsIntentionallyUntranslated(t.gameObject, text))
        {
            string preview = text.Length > 45 ? text.Substring(0, 45) + "…" : text;
            found.Add($"{context} {t.name}: \"{preview.Replace("\n", " ")}\"");
        }

        for (int i = 0; i < t.childCount; i++)
            CollectUntagged(t.GetChild(i), context, found);
    }

    private static bool ContainsTurkishChars(string s)
    {
        foreach (char c in s)
            if ("çğıöşüÇĞİÖŞÜ".IndexOf(c) >= 0) return true;
        return false;
    }
}
