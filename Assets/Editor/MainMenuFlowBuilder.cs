using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Ana menüyü İKİ EKRANA böler: ANA EKRAN (OYNA / Nasıl Oynanır / Ayarlar /
// Geri Bildirim / Çıkış) ve ODA EKRANI (isim + Oyun Kur / Hızlı Katıl /
// Davet Et / Hazırım / oyuncu listesi).
//
// 🚨 MEVCUT BUTONLAR SİLİNMİYOR, SADECE TAŞINIYOR. Hepsi çalışma anında
// koddan bağlanıyor (SteamLobbyManager.WireButtons, MainMenuButtons,
// LobbyManager) ve o bağlantılar Inspector REFERANSI üzerinden — yani
// hiyerarşide yer değiştirmek onları bozmuyor. Yeniden kursaydık hepsini
// yeniden bağlamak gerekirdi.
//
// 🚨 SAHNEYİ KAYDETMİYOR. Beğenmezsen sahneyi kaydetmeden yeniden aç.
public static class MainMenuFlowBuilder
{
    const string MainScreenName = "MainScreen";
    const string RoomScreenName = "RoomScreen";

    // Hangi mevcut obje hangi ekrana gidiyor.
    static readonly string[] ToRoom =
    {
        "IsimKutusu", "HostButton", "HizliKatilButton",
        "InviteButton", "ReadyButton", "PlayerListText"
    };
    static readonly string[] ToMain =
    {
        "NasilOynanirButton", "GeriBildirimButton", "GeriBildirimHatirlatma"
    };

    [MenuItem("SaboTour/Menü Stili/Ana Menüyü İki Ekrana Böl", false, 110)]
    public static void Build() => Build(true);

    public static void Build(bool interactive)
    {
        var canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                           .FirstOrDefault(c => c.name == "LobbyCanvas");
        if (canvas == null)
        {
            Report(interactive, "'LobbyCanvas' bulunamadı. Önce Offline Scene'i aç.", true);
            return;
        }

        var panel = canvas.transform.Find("LobbyPanel");
        if (panel == null)
        {
            Report(interactive, "'LobbyPanel' bulunamadı.", true);
            return;
        }

        // Şablon buton: mevcut butonlardan biri. Font, boyut, sprite hepsi
        // ondan kopyalanıyor — sıfırdan kurup ayarları tekrar tutturmaya
        // çalışmaktan çok daha güvenli.
        var template = FindDeep(panel, "HostButton") ?? panel.GetComponentInChildren<Button>(true)?.transform;
        if (template == null)
        {
            Report(interactive, "Şablon olarak kullanılacak bir buton bulunamadı.", true);
            return;
        }

        var mainScreen = EnsureScreen(panel, MainScreenName);
        var roomScreen = EnsureScreen(panel, RoomScreenName);

        // 1) Mevcut objeleri taşı
        foreach (var n in ToRoom) MoveInto(panel, n, roomScreen);
        foreach (var n in ToMain) MoveInto(panel, n, mainScreen);

        // 2) Yeni butonlar (varsa yeniden kurulmuyor)
        var play = EnsureButton(mainScreen, "OynaButton", template, "menu.play", "OYNA");
        var settings = EnsureButton(mainScreen, "AyarlarButton", template, "pause.settings", "Ayarlar");
        var quit = EnsureButton(mainScreen, "CikisButton", template, "pause.quit", "Çıkış");
        var back = EnsureButton(roomScreen, "GeriButton", template, "pause.back", "Geri");
        var leave = EnsureButton(roomScreen, LeaveButtonName, template, LeaveLocKey, LeaveFallback);

        // "OYNA" ana eylem — diğerlerinden belirgin şekilde büyük olmalı.
        ((RectTransform)play.transform).sizeDelta = new Vector2(360f, 100f);

        EnsureTitle(mainScreen, template);

        // 3) Yerleşim — ana ekran ortada tek sütun
        Place(play, 0f, 170f);
        Place(FindDeep(mainScreen, "NasilOynanirButton"), 0f, 50f);
        Place(settings, 0f, -40f);
        Place(FindDeep(mainScreen, "GeriBildirimButton"), 0f, -130f);
        Place(quit, 0f, -240f);
        Place(FindDeep(mainScreen, "GeriBildirimHatirlatma"), 0f, -350f);

        // Oda ekranı: solda oyuncu listesi, sağda aksiyonlar
        Place(FindDeep(roomScreen, "PlayerListText"), -520f, 60f);
        Place(FindDeep(roomScreen, "IsimKutusu"), 260f, 190f);
        Place(FindDeep(roomScreen, "HostButton"), 260f, 80f);
        Place(FindDeep(roomScreen, "HizliKatilButton"), 260f, -10f);
        Place(FindDeep(roomScreen, "InviteButton"), 260f, 80f);
        Place(FindDeep(roomScreen, "ReadyButton"), 260f, -10f);
        Place(back, 260f, -140f);
        // "Lobiden Ayrıl" tam "Geri"nin yerinde duruyor: biri oturum YOKKEN,
        // diğeri oturum VARKEN görünüyor — ikisi asla aynı anda ekranda olmuyor.
        Place(leave, 260f, -140f);

        // 4) Akış bileşeni
        var flow = panel.GetComponent<MainMenuFlow>();
        if (flow == null) flow = panel.gameObject.AddComponent<MainMenuFlow>();

        flow.mainScreen = mainScreen.gameObject;
        flow.roomScreen = roomScreen.gameObject;
        flow.playButton = play;
        flow.settingsButton = settings;
        flow.quitButton = quit;
        flow.backButton = back;
        flow.leaveLobbyButton = leave;

        // Oturum YOKKEN: isim + Oyun Kur + Hızlı Katıl + Geri
        // (IsimKutusu bilerek listede YOK — onu PlayerNameField zaten kendisi
        //  gizleyip gösteriyor, iki sahip olsaydı birbirleriyle kavga ederdi.)
        flow.hideWhenConnected = Collect(roomScreen, "HostButton", "HizliKatilButton", "GeriButton");

        // Oturum VARKEN: davet + hazırım + oyuncu listesi
        flow.showWhenConnected = Collect(roomScreen, "InviteButton", "ReadyButton", "PlayerListText", LeaveButtonName);

        // 5) BAŞLANGIÇ DURUMU — Editor'de de doğru görünsün.
        // Bu olmadan iki ekran birden açık kalıyor ve "Oyun Kur" ile
        // "Davet Et" üst üste biniyor (ikisi aynı yerde duruyor çünkü asla
        // AYNI ANDA görünmüyorlar). Play'e basınca MainMenuFlow zaten
        // düzeltiyordu ama Scene/Game view'da bozuk görünüyordu.
        mainScreen.gameObject.SetActive(true);
        roomScreen.gameObject.SetActive(false);

        foreach (var go in flow.hideWhenConnected) if (go != null) go.SetActive(true);
        foreach (var go in flow.showWhenConnected) if (go != null) go.SetActive(false);

        EditorUtility.SetDirty(panel.gameObject);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        Debug.Log("[Ana Menü] İki ekrana bölündü.\n" +
                  "  ANA EKRAN: OYNA, Nasıl Oynanır, Ayarlar, Geri Bildirim, Çıkış\n" +
                  "  ODA EKRANI: isim + Oyun Kur/Hızlı Katıl (oturum yokken), " +
                  "Davet Et/Hazırım/oyuncu listesi (oturum varken)\n" +
                  "SAHNE KAYDEDİLMEDİ.");

        Report(interactive,
            "Ana menü iki ekrana bölündü.\n\n" +
            "ANA EKRAN: OYNA / Nasıl Oynanır / Ayarlar / Geri Bildirim / Çıkış\n" +
            "ODA EKRANI: OYNA'ya basınca açılıyor.\n\n" +
            "Şimdi 'Stili Uygula'yı bir kez daha çalıştır (yeni butonlar da stillensin).\n\n" +
            "SAHNE KAYDEDİLMEDİ.", false);
    }

    // ══ "LOBİDEN AYRIL" BUTONU ═══════════════════════════════════════════

    const string LeaveButtonName = "LobidenAyrilButton";
    const string LeaveLocKey = "menu.leavelobby";
    const string LeaveFallback = "Lobiden Ayrıl";

    /// <summary>
    /// Oda ekranına "Lobiden Ayrıl" butonunu EKLER — tüm menüyü yeniden
    /// kurmadan.
    ///
    /// NEDEN AYRI BİR KOMUT: yukarıdaki Build() bütün butonları kodda yazılı
    /// koordinatlara YENİDEN diziyor. Menü bir kez kurulduktan sonra
    /// geliştirici konumları elle oynatmış olabilir; sırf tek bir buton
    /// eklemek için o düzeni ezmek yanlış olurdu. Bu komut sadece eksik
    /// butonu koyuyor.
    ///
    /// İKİ KEZ ÇALIŞTIRMAK ZARARSIZ: buton zaten varsa yeniden kurulmuyor,
    /// sadece bağlantıları tazeleniyor.
    /// </summary>
    [MenuItem("SaboTour/Menü Stili/Odaya 'Lobiden Ayrıl' Butonu Ekle", false, 111)]
    public static void AddLeaveLobbyButton() => AddLeaveLobbyButton(true);

    public static void AddLeaveLobbyButton(bool interactive)
    {
        var canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                           .FirstOrDefault(c => c.name == "LobbyCanvas");
        if (canvas == null)
        {
            Report(interactive, "'LobbyCanvas' bulunamadı. Önce Offline Scene'i aç.", true);
            return;
        }

        var panel = canvas.transform.Find("LobbyPanel");
        var roomScreen = panel != null ? FindDeep(panel, RoomScreenName) : null;
        var flow = panel != null ? panel.GetComponent<MainMenuFlow>() : null;
        if (roomScreen == null || flow == null)
        {
            Report(interactive, "Oda ekranı bulunamadı. Önce 'Ana Menüyü İki Ekrana Böl' komutunu çalıştır.", true);
            return;
        }

        // ŞABLON "GERİ" BUTONU: yeni buton onun TAM YERİNDE duracağı için
        // fontunun/puntosunun/ölçüsünün de birebir aynı olması gerekiyor.
        // Yoksa oda ekranındaki herhangi bir butona düşüyoruz.
        var template = FindDeep(roomScreen, "GeriButton")
                       ?? FindDeep(panel, "HostButton")
                       ?? roomScreen.GetComponentInChildren<Button>(true)?.transform;
        if (template == null)
        {
            Report(interactive, "Şablon olarak kullanılacak bir buton bulunamadı.", true);
            return;
        }

        bool alreadyThere = FindDeep(roomScreen, LeaveButtonName) != null;
        var leave = EnsureButton(roomScreen, LeaveButtonName, template, LeaveLocKey, LeaveFallback);
        if (leave == null)
        {
            Report(interactive, "Buton kurulamadı (şablonda Button bileşeni yok).", true);
            return;
        }

        // KONUM: kodda yazılı sabit yerine "Geri"nin GERÇEK konumu — geliştirici
        // düzeni elle oynattıysa yeni buton yine onunla hizalı çıksın.
        var backRt = FindDeep(roomScreen, "GeriButton") as RectTransform;
        var leaveRt = (RectTransform)leave.transform;
        if (backRt != null)
        {
            leaveRt.anchorMin = backRt.anchorMin;
            leaveRt.anchorMax = backRt.anchorMax;
            leaveRt.pivot = backRt.pivot;
            leaveRt.sizeDelta = backRt.sizeDelta;
            leaveRt.anchoredPosition = backRt.anchoredPosition;
        }
        else Place(leave, 260f, -140f);

        flow.leaveLobbyButton = leave;
        flow.showWhenConnected = AppendUnique(flow.showWhenConnected, leave.gameObject);

        // Editörde oturum YOK, yani bu buton gizli olmalı — açık bırakırsak
        // Scene view'da "Geri" ile üst üste görünür.
        leave.gameObject.SetActive(false);

        // Menü zaten stillenmişse (şablonda MenuButton varsa) yeni butonu da
        // stille — yoksa tek başına stilsiz kalırdı. Stil hiç uygulanmamışsa
        // DOKUNMUYORUZ: geliştirici bilerek kaldırmış olabilir.
        bool styled = template.GetComponent<MenuButton>() != null;
        if (styled) MenuStyler.Apply(false);

        EditorUtility.SetDirty(panel.gameObject);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        string note = alreadyThere ? "Buton zaten vardı, bağlantıları tazelendi." : "Buton eklendi.";
        Debug.Log($"[Ana Menü] 'Lobiden Ayrıl' — {note} Stil uygulandı mı: {styled}. SAHNE KAYDEDİLMEDİ.");
        Report(interactive,
            note + "\n\n" +
            "Oda ekranında, oturumdayken 'Geri'nin yerinde görünüyor.\n" +
            (styled ? "Menü stili yeniden uygulandı.\n" : "Menüde stil yok — buton şablonun sade hâlinde.\n") +
            "\nBeğenirsen Ctrl+S ile sahneyi kaydet. SAHNE KAYDEDİLMEDİ.", false);
    }

    static GameObject[] AppendUnique(GameObject[] list, GameObject item)
    {
        var result = new List<GameObject>();
        if (list != null) result.AddRange(list.Where(g => g != null));
        if (!result.Contains(item)) result.Add(item);
        return result.ToArray();
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────

    static RectTransform EnsureScreen(Transform panel, string name)
    {
        var tr = panel.Find(name) as RectTransform;
        if (tr == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            tr = (RectTransform)go.transform;
            tr.SetParent(panel, false);
        }
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        tr.SetAsLastSibling();   // MenuBackground index 0'da kalsın
        return tr;
    }

    const string TitleFontPath = "Assets/TextMesh Pro/Fonts/TitanOne-Regular SDF.asset";
    const string TitleMaterialDir = "Assets/UI/Fonts";

    /// <summary>
    /// Ana ekranın en üstüne "SaboTour" başlığını koyar (Steam capsule art fontu).
    ///
    /// "Tour" ana renkte yazılıyor ve zemin de ana renk olduğu için tek başına
    /// zemine karışırdı — bu yüzden başlığa KALIN SİYAH KONTUR + GÖLGE veriliyor.
    /// Referanstaki "DA GAME" başlığında da aynı numara var.
    /// </summary>
    static void EnsureTitle(Transform screen, Transform template)
    {
        const string TitleName = "OyunBasligi";

        var style = screen.GetComponentInParent<MenuStyle>();
        Color accent = style != null ? style.accent : new Color32(0xDC, 0x79, 0x10, 0xFF);

        var existing = FindDeep(screen, TitleName);
        TextMeshProUGUI tmp;

        if (existing != null)
        {
            tmp = existing.GetComponent<TextMeshProUGUI>();
            if (tmp == null) return;
        }
        else
        {
            var go = new GameObject(TitleName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(screen, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 330f);
            rt.sizeDelta = new Vector2(1000f, 180f);
            tmp = go.AddComponent<TextMeshProUGUI>();
        }

        // "Sabo" beyaz, "Tour" ana renkte — capsule art'taki gibi.
        tmp.text = $"Sabo<color=#{ColorUtility.ToHtmlStringRGB(accent)}>Tour</color>";
        tmp.fontSize = 130f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.richText = true;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        var titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
        if (titleFont != null)
        {
            tmp.font = titleFont;

            var mat = EnsureOutlineMaterial(titleFont);
            if (mat != null)
            {
                tmp.fontSharedMaterial = mat;
                // Kontur/gölge yazının kutusundan TAŞIYOR; bu çağrı olmadan
                // TMP eski dar kutuya göre çiziyor ve kenarlar kırpılıyor.
                tmp.UpdateMeshPadding();
            }
        }
        else
        {
            Debug.LogWarning($"[Ana Menü] Başlık fontu bulunamadı: {TitleFontPath}");
            var srcTmp = template.GetComponentInChildren<TMP_Text>(true);
            if (srcTmp != null && srcTmp.font != null) tmp.font = srcTmp.font;
        }

        // 🚨 Başlık BUTONLARLA AYNI hareketi kullanmak zorunda.
        // Önce MenuUIDrift (Perlin gürültüsü) denendi — tek başına güzeldi
        // ama butonlar sinüsle süzülürken başlık başka bir ritimde hareket
        // ediyor, yan yana bakınca kopuk görünüyordu. Artık ikisi de
        // MenuFloatMath'ten okuyor.
        var oldDrift = tmp.GetComponent<MenuUIDrift>();
        if (oldDrift != null) Object.DestroyImmediate(oldDrift);

        var flt = tmp.GetComponent<MenuFloat>();
        if (flt == null) flt = tmp.gameObject.AddComponent<MenuFloat>();
        // Hız butonlarla BİREBİR aynı; sadece genlik biraz büyük çünkü başlık
        // ekranın en büyük öğesi ve aynı mesafe onda daha az fark ediliyor.
        flt.speed = style != null ? style.floatSpeed : 0.35f;
        flt.amount = (style != null ? style.floatAmount : 4f) * 1.5f;

        // Başlık oyunun ADI — çevrilmiyor, her dilde aynı. Bu yüzden
        // bilerek LocalizedText EKLENMİYOR.
    }

    /// <summary>
    /// 🚨 TMP'de kontur `text.outlineWidth` ile DEĞİL, fontun MATERYALİYLE
    /// veriliyor. Kod tarafı daha önce denenip sonuç vermemişti (bkz. sıralama
    /// tablosu notu) — doğru yol bu: font asset'inin materyalinden bir kopya
    /// (Material Preset) üretip kontur + alt gölge (underlay) ayarlarını
    /// oraya yazmak.
    ///
    /// Kopya olması ŞART: fontun ASIL materyalini değiştirseydik aynı fontu
    /// kullanan her yazı konturlu olurdu.
    /// </summary>
    static Material EnsureOutlineMaterial(TMP_FontAsset font)
    {
        if (font == null || font.material == null) return null;

        if (!AssetDatabase.IsValidFolder(TitleMaterialDir))
        {
            var parent = System.IO.Path.GetDirectoryName(TitleMaterialDir).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets", "UI");
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(TitleMaterialDir));
        }

        string path = $"{TitleMaterialDir}/{font.name} - Baslik.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(font.material) { name = font.name + " - Baslik" };
            AssetDatabase.CreateAsset(mat, path);
        }

        // Kontur
        mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(0x0E, 0x0E, 0x12, 0xFF));
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);

        // Alt gölge. UNDERLAY_ON keyword'ü açılmazsa aşağıdaki değerler
        // yazılır ama shader onları HİÇ okumaz — gölge görünmez.
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.75f));
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.55f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.55f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
        mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.05f);

        EditorUtility.SetDirty(mat);

        // Konturun genişliği fontun atlas DOLGUSUYLA sınırlı. Dolgu küçükse
        // kontur kalınlaştıkça kenarlardan kesilir — sessizce bozulmasın diye
        // uyarı basıyoruz.
        if (font.atlasPadding < 6)
            Debug.LogWarning($"[Ana Menü] '{font.name}' atlas dolgusu {font.atlasPadding} — kontur kesik " +
                             "görünürse Font Asset'i daha yüksek Padding ile yeniden üret.");

        return mat;
    }

    static void MoveInto(Transform panel, string name, Transform screen)
    {
        var tr = FindDeep(panel, name);
        if (tr == null || tr.parent == screen) return;
        tr.SetParent(screen, false);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var found = FindDeep(c, name);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject[] Collect(Transform screen, params string[] names)
    {
        var list = new List<GameObject>();
        foreach (var n in names)
        {
            var tr = FindDeep(screen, n);
            if (tr != null) list.Add(tr.gameObject);
        }
        return list.ToArray();
    }

    // Component alıyor ki hem Transform (FindDeep sonucu) hem Button
    // (yeni kurulan butonlar) doğrudan verilebilsin.
    static void Place(Component c, float x, float y)
    {
        if (c == null) return;
        var rt = c.transform as RectTransform;
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
    }

    static Button EnsureButton(Transform parent, string name, Transform template,
                               string locKey, string fallbackText)
    {
        var existing = FindDeep(parent, name);
        if (existing != null) return existing.GetComponent<Button>();

        var clone = Object.Instantiate(template.gameObject, parent, false);
        clone.name = name;
        clone.SetActive(true);

        // 🚨 ŞABLONDAKİ PERSONA PARÇALARINI TEMİZLE. Şablon zaten stillenmiş
        // olabilir; kopyalasaydık klonun "orijinal rengi" alfa 0 olarak
        // kaydedilir ve ileride "Stili Kaldır" bu butonu GÖRÜNMEZ bırakırdı.
        StripMenuStyle(clone);

        var img = clone.GetComponent<Image>();
        if (img != null)
        {
            var c = img.color;
            c.a = 1f;
            img.color = c;
        }

        // Şablonun çalışma anında bağlanan tıklama olayları klona geçmiyor
        // (hepsi AddListener ile, kalıcı değil) — yine de temizliyoruz.
        var btn = clone.GetComponent<Button>();
        if (btn != null) btn.onClick = new Button.ButtonClickedEvent();

        SetLabel(clone, locKey, fallbackText);
        return btn;
    }

    static void StripMenuStyle(GameObject go)
    {
        var root = go.transform.Find(MenuButton.RootName);
        if (root != null) Object.DestroyImmediate(root.gameObject);

        var pb = go.GetComponent<MenuButton>();
        if (pb != null) Object.DestroyImmediate(pb);

        var pe = go.GetComponent<MenuEntrance>();
        if (pe != null) Object.DestroyImmediate(pe);

        var cg = go.GetComponent<CanvasGroup>();
        if (cg != null) Object.DestroyImmediate(cg);
    }

    static void SetLabel(GameObject button, string locKey, string fallbackText)
    {
        var tmp = button.GetComponentInChildren<TMP_Text>(true);
        var legacy = tmp == null ? button.GetComponentInChildren<Text>(true) : null;
        var labelGo = tmp != null ? tmp.gameObject : legacy != null ? legacy.gameObject : null;
        if (labelGo == null) return;

        if (tmp != null) tmp.text = fallbackText;
        else legacy.text = fallbackText;

        // Çeviri etiketi: yazı hangi anahtara ait olduğunu kendisi biliyor,
        // dil değişince kendini yeniliyor.
        var loc = labelGo.GetComponent<LocalizedText>();
        if (loc == null) loc = labelGo.AddComponent<LocalizedText>();
        loc.key = locKey;
        loc.prefix = "";
        loc.suffix = "";
    }

    static void Report(bool interactive, string message, bool isError)
    {
        if (interactive) EditorUtility.DisplayDialog("Ana Menü", message, "Tamam");
        else if (isError) Debug.LogError("[Ana Menü] " + message);
    }
}
