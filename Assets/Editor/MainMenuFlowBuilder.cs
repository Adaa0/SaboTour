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

    [MenuItem("SaboTour/Persona Menü/Ana Menüyü İki Ekrana Böl", false, 110)]
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

        // "OYNA" ana eylem — diğerlerinden belirgin şekilde büyük olmalı.
        ((RectTransform)play.transform).sizeDelta = new Vector2(360f, 100f);

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

        // 4) Akış bileşeni
        var flow = panel.GetComponent<MainMenuFlow>();
        if (flow == null) flow = panel.gameObject.AddComponent<MainMenuFlow>();

        flow.mainScreen = mainScreen.gameObject;
        flow.roomScreen = roomScreen.gameObject;
        flow.playButton = play;
        flow.settingsButton = settings;
        flow.quitButton = quit;
        flow.backButton = back;

        // Oturum YOKKEN: isim + Oyun Kur + Hızlı Katıl + Geri
        // (IsimKutusu bilerek listede YOK — onu PlayerNameField zaten kendisi
        //  gizleyip gösteriyor, iki sahip olsaydı birbirleriyle kavga ederdi.)
        flow.hideWhenConnected = Collect(roomScreen, "HostButton", "HizliKatilButton", "GeriButton");

        // Oturum VARKEN: davet + hazırım + oyuncu listesi
        flow.showWhenConnected = Collect(roomScreen, "InviteButton", "ReadyButton", "PlayerListText");

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
            "Şimdi 'Stili Uygula'yı bir kez daha çalıştır (yeni butonlar da Persona olsun).\n\n" +
            "SAHNE KAYDEDİLMEDİ.", false);
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
        tr.SetAsLastSibling();   // PersonaBackground index 0'da kalsın
        return tr;
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
        StripPersona(clone);

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

    static void StripPersona(GameObject go)
    {
        var root = go.transform.Find(PersonaButton.RootName);
        if (root != null) Object.DestroyImmediate(root.gameObject);

        var pb = go.GetComponent<PersonaButton>();
        if (pb != null) Object.DestroyImmediate(pb);

        var pe = go.GetComponent<PersonaEntrance>();
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
