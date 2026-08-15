using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

/// <summary>
/// TEK SEFERLİK ARAÇ — "Ayarlar/Pause" menüsünün Prefab'ını oluşturur.
///
/// NEDEN VAR: Menü eskiden tamamen KOD içinde (runtime'da GameObject'ler
/// yaratılarak) kuruluyordu — bunun sonucu: Scene view'da görünmüyordu,
/// bir sprite/font değiştirmek için kod yazmak (ya da Claude'a sormak)
/// gerekiyordu. Bu araç, AYNI menüyü NORMAL bir Unity Prefab'ı olarak
/// (Editor'ün "GameObject > UI > Button/Dropdown/..." menüsünün ürettiğiyle
/// BİREBİR AYNI yapıda) bir kere oluşturuyor. Ondan sonra:
///   - Assets/Resources/UI/PauseMenu.prefab dosyasına çift tıkla,
///   - Scene view'da normal bir sahne gibi düzenle (Rect Tool ile taşı/
///     boyutlandır, Image component'lerine sprite sürükle, Text'lerin
///     Font alanına kendi fontunu sürükle, renkleri Inspector'dan değiştir),
///   - Kaydet (Ctrl+S, prefab modundayken).
/// Kod tarafına BİR DAHA HİÇ DOKUNMAN GEREKMİYOR — PauseMenuController/
/// SettingsMenuController artık sadece bu prefab'taki referansları okuyor.
///
/// ── KULLANIMI ──
/// Unity Editor'de üst menüden: SaboTour > Ayarlar Menüsü Prefabını Oluştur.
/// Zaten dosya varsa üzerine yazmadan önce sorar (yaptığın özelleştirmeleri
/// KAYBETMEMEK için — bir daha çalıştırmana normalde hiç gerek yok, sadece
/// prefab'ı yanlışlıkla silersen/bozarsan yeniden oluşturmak için burada).
/// </summary>
public static class PauseMenuPrefabBuilder
{
    private const string FolderPath = "Assets/Resources/UI";
    private const string PrefabPath = FolderPath + "/PauseMenu.prefab";

    [MenuItem("SaboTour/Ayarlar Menüsü Prefabını Oluştur")]
    public static void Build()
    {
        if (File.Exists(PrefabPath))
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Prefab zaten var",
                $"{PrefabPath} zaten mevcut. Üzerine yazmak, yaptığın TÜM görsel " +
                "özelleştirmeleri (sprite/font/renk/pozisyon) SİLER. Emin misin?",
                "Üzerine Yaz", "İptal");
            if (!overwrite) return;
        }

        if (!Directory.Exists(FolderPath))
            Directory.CreateDirectory(FolderPath);

        var resources = new DefaultControls.Resources();

        // ── Kök obje: her zaman aktif kalıyor (ESC'yi dinlemeye devam edebilsin),
        //    PauseMenuController bunun üzerinde. ──────────────────────────────
        GameObject rootGo = new GameObject("PauseMenuRoot");
        PauseMenuController controller = rootGo.AddComponent<PauseMenuController>();

        // ── Canvas: gerçek gizlenen/gösterilen panel BU. ─────────────────────
        GameObject canvasGo = new GameObject("PauseCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(rootGo.transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400; // crosshair'in (100) üstünde, ScreenNotice'in (500) altında
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject bgGo = CreateStretchedChild(canvasGo, "Background", typeof(Image));
        Image bg = bgGo.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);

        // ── Ana buton paneli ──────────────────────────────────────────────────
        GameObject mainPanel = CreateStretchedChild(canvasGo, "MainButtonsPanel");

        Button devamEt = CreateButton(mainPanel, resources, "Devam Et", 135f);
        Button ayarlar = CreateButton(mainPanel, resources, "Ayarlar", 45f);
        Button oyundanAyril = CreateButton(mainPanel, resources, "Oyundan Ayrıl", -45f);
        Button oyunuKapat = CreateButton(mainPanel, resources, "Oyunu Kapat", -135f);

        // ── Ayarlar paneli ────────────────────────────────────────────────────
        GameObject settingsPanel = CreateStretchedChild(canvasGo, "SettingsPanel");
        SettingsMenuController settingsMenu = settingsPanel.AddComponent<SettingsMenuController>();

        CreateLabel(settingsPanel, "Ayarlar", 300f, 32);

        CreateLabel(settingsPanel, "Kare Hızı", 250f, 20);
        Dropdown fpsDropdown = CreateDropdown(settingsPanel, resources, 210f,
            new[] { "VSync", "60 FPS", "120 FPS", "Sınırsız" });

        Toggle fullscreenToggle = CreateToggle(settingsPanel, resources, "Tam Ekran", 120f);

        CreateLabel(settingsPanel, "Çözünürlük", 70f, 20);
        Dropdown resolutionDropdown = CreateDropdown(settingsPanel, resources, 30f,
            new[] { "(oyun açılınca dolduruluyor)" });

        CreateLabel(settingsPanel, "Ses Seviyesi", -30f, 20);
        Slider volumeSlider = CreateSlider(settingsPanel, resources, -70f);

        CreateLabel(settingsPanel, "Fare Hassasiyeti (Sabotajcı)", -130f, 20);
        Slider sensitivitySlider = CreateSlider(settingsPanel, resources, -170f);

        Button geri = CreateButton(settingsPanel, resources, "Geri", -270f);

        // ── Referansları bağla (Inspector'da elle sürüklemene gerek kalmasın diye) ──
        controller.panelRoot = canvasGo;
        controller.mainButtonsPanel = mainPanel;
        controller.settingsMenu = settingsMenu;
        controller.devamEtButton = devamEt;
        controller.ayarlarButton = ayarlar;
        controller.oyundanAyrilButton = oyundanAyril;
        controller.oyunuKapatButton = oyunuKapat;

        settingsMenu.fpsDropdown = fpsDropdown;
        settingsMenu.fullscreenToggle = fullscreenToggle;
        settingsMenu.resolutionDropdown = resolutionDropdown;
        settingsMenu.volumeSlider = volumeSlider;
        settingsMenu.sensitivitySlider = sensitivitySlider;
        settingsMenu.geriButton = geri;

        settingsPanel.SetActive(false);

        PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
        Object.DestroyImmediate(rootGo);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Hazır",
            $"{PrefabPath} oluşturuldu.\n\nŞimdi çift tıklayıp Prefab Mode'da " +
            "açabilir, sprite/font/renk/pozisyon değiştirebilirsin. Play'e " +
            "bastığında değişikliklerin otomatik uygulanır.", "Tamam");

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    // ─── Yardımcılar ─────────────────────────────────────────────────────────

    private static GameObject CreateStretchedChild(GameObject parent, string name, params System.Type[] extraComponents)
    {
        GameObject go = new GameObject(name, PrependRectTransform(extraComponents));
        go.transform.SetParent(parent.transform, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    private static System.Type[] PrependRectTransform(System.Type[] extra)
    {
        var result = new System.Type[extra.Length + 1];
        result[0] = typeof(RectTransform);
        extra.CopyTo(result, 1);
        return result;
    }

    private static void CreateLabel(GameObject parent, string text, float yOffset, int fontSize)
    {
        GameObject go = new GameObject("Label_" + text, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);

        Text t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.text = text;
        t.raycastTarget = false;

        RectTransform rect = t.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(500f, 50f);
    }

    private static Button CreateButton(GameObject parent, DefaultControls.Resources resources, string label, float yOffset)
    {
        GameObject go = DefaultControls.CreateButton(resources);
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(320f, 70f);

        Text text = go.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = label;
            text.fontSize = 24;
        }

        return go.GetComponent<Button>();
    }

    private static Dropdown CreateDropdown(GameObject parent, DefaultControls.Resources resources, float yOffset, string[] options)
    {
        GameObject go = DefaultControls.CreateDropdown(resources);
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(320f, 45f);

        Dropdown dropdown = go.GetComponent<Dropdown>();
        dropdown.ClearOptions();
        var optionList = new System.Collections.Generic.List<Dropdown.OptionData>();
        foreach (string opt in options) optionList.Add(new Dropdown.OptionData(opt));
        dropdown.AddOptions(optionList);
        return dropdown;
    }

    private static Toggle CreateToggle(GameObject parent, DefaultControls.Resources resources, string label, float yOffset)
    {
        GameObject go = DefaultControls.CreateToggle(resources);
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(220f, 40f);

        Text text = go.GetComponentInChildren<Text>();
        if (text != null) text.text = label;

        return go.GetComponent<Toggle>();
    }

    private static Slider CreateSlider(GameObject parent, DefaultControls.Resources resources, float yOffset)
    {
        GameObject go = DefaultControls.CreateSlider(resources);
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(320f, 20f);

        Slider slider = go.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        return slider;
    }
}
