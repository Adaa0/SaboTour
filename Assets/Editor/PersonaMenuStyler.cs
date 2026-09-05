using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Videodaki menü stilini GERÇEK lobi butonlarına uygulayan araç.
//
// 🚨 SAHNEYİ KAYDETMİYOR. Bilerek: bu tamamen görsel/beğeni işi ve daha önce
// bir kez uygulanıp beğenilmediği için geri alınmıştı. Kaydetmeden bırakmak,
// beğenmezsen sahneyi yeniden yükleyerek tek adımda geri dönebilmen demek.
// Beğenirsen Ctrl+S. Beğenmezsen ya "Stili Kaldır" ya da sahneyi kaydetmeden
// yeniden aç.
public static class PersonaMenuStyler
{
    // Videodaki dekoratif işleme SOKULMAYACAK objeler.
    // Buton muamelesi görmeyecek objeler. (İsim kutusu artık DIŞARIDA DEĞİL —
    // ama buton gibi değil, kendi sakin bileşeniyle: PersonaField.)
    static readonly string[] SkipNames = new string[0];

    [MenuItem("SaboTour/Persona Menü/Stili Uygula", false, 100)]
    public static void Apply() => Apply(true);

    // interactive = false ile çağrılırsa HİÇ pencere açmıyor.
    // (Modal pencere otomasyondan çağrılınca Unity'yi kilitliyor — bu projede
    // daha önce yaşandı, prefab builder'larda da aynı aşırı yükleme var.)
    public static void Apply(bool interactive)
    {
        var canvas = FindLobbyCanvas(interactive);
        if (canvas == null) return;

        var style = canvas.GetComponent<PersonaMenuStyle>();
        if (style == null) style = canvas.gameObject.AddComponent<PersonaMenuStyle>();

        var panel = canvas.transform.Find("LobbyPanel");
        if (panel == null)
        {
            if (interactive)
                EditorUtility.DisplayDialog("Persona Menü",
                    "LobbyCanvas içinde 'LobbyPanel' bulunamadı.", "Tamam");
            else
                Debug.LogError("[Persona Menü] 'LobbyPanel' bulunamadı.");
            return;
        }

        // 1) ARKA PLAN — şeritler + parıltılar (en arkada)
        //
        // 🚨 LobbyCanvas'ın değil, LOBBYPANEL'İN çocuğu olmak ZORUNDA.
        // İlk denemede canvas'ın doğrudan çocuğuydu ve yarış başlayınca ekranda
        // KALIYORDU: LobbyManager yarışa geçerken sadece LobbyPanel'i
        // kapatıyor (SetActive(false)), onun kardeşleri açık kalıyor.
        // Panelin İÇİNDE olunca panelle birlikte kendiliğinden kayboluyor —
        // ekstra koda gerek yok.
        var existingBg = canvas.GetComponentInChildren<PersonaBackgroundFX>(true);
        Transform bgTr = existingBg != null ? existingBg.transform : null;
        if (bgTr == null)
        {
            var go = new GameObject("PersonaBackground", typeof(RectTransform));
            bgTr = go.transform;
        }
        bgTr.SetParent(panel, false);   // eski kurulumdan kalanı da buraya taşır
        var bgRt = (RectTransform)bgTr;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        // 🚨 Ekrandan 60px BÜYÜK. Üstünde PersonaUIDrift var ve arka planı
        // birkaç piksel oynatıyor — tam ekran boyutunda olsaydı kayarken
        // kenarlarda boşluk açılıp arkadaki kamera rengi sızardı.
        bgRt.offsetMin = new Vector2(-60f, -60f);
        bgRt.offsetMax = new Vector2(60f, 60f);
        bgTr.SetSiblingIndex(0);   // her şeyin arkasında

        if (bgTr.GetComponent<PersonaBackgroundFX>() == null)
            bgTr.gameObject.AddComponent<PersonaBackgroundFX>();
        if (bgTr.GetComponent<PersonaUIDrift>() == null)
            bgTr.gameObject.AddComponent<PersonaUIDrift>();

        // 2) BUTONLAR + İSİM KUTUSU
        var buttons = panel.GetComponentsInChildren<Button>(true)
            .Where(b => !SkipNames.Contains(b.gameObject.name))
            .Select(b => b.transform)
            .ToList();

        var fields = panel.GetComponentsInChildren<TMP_InputField>(true)
            .Select(f => f.transform)
            .ToList();

        if (buttons.Count == 0 && fields.Count == 0)
        {
            if (interactive)
                EditorUtility.DisplayDialog("Persona Menü",
                    "LobbyPanel içinde hiç buton/yazı kutusu bulunamadı.", "Tamam");
            else
                Debug.LogError("[Persona Menü] LobbyPanel içinde hiç buton/yazı kutusu bulunamadı.");
            return;
        }

        // Kademeli giriş yukarıdan aşağı aksın diye HEPSİ BİRLİKTE sıralanıyor —
        // isim kutusu butonların arasında kendi doğru sırasını alsın diye
        // (ayrı sıralansaydı en başta ya da en sonda girerdi, akış bozulurdu).
        var elements = buttons.Concat(fields)
            .OrderByDescending(t => ((RectTransform)t).anchoredPosition.y)
            .ToList();

        for (int i = 0; i < elements.Count; i++)
        {
            var tr = elements[i];
            var rt = (RectTransform)tr;

            if (fields.Contains(tr))
            {
                // Yazı kutusu: aynı şekil dili, sakin davranış.
                var pf = tr.GetComponent<PersonaField>();
                if (pf == null) pf = tr.gameObject.AddComponent<PersonaField>();

                pf.fill = style.idleFill;
                pf.textColor = style.idleText;
                pf.shadowColor = style.shadowColor;
                pf.focusShadowColor = style.accent;
                pf.shearX = style.shearX;
                pf.shadowOffset = style.shadowOffset;
                pf.shadowOffsetFocus = style.shadowOffsetHover;
                pf.BuildPieces();
            }
            else
            {
                var pb = tr.GetComponent<PersonaButton>();
                if (pb == null) pb = tr.gameObject.AddComponent<PersonaButton>();

                pb.hoverFill = style.accent;
                pb.idleFill = style.idleFill;
                pb.idleText = style.idleText;
                pb.hoverText = style.hoverText;
                pb.shadowColor = style.shadowColor;
                pb.shearX = style.shearX;
                pb.shadowOffset = style.shadowOffset;
                pb.shadowOffsetHover = style.shadowOffsetHover;
                pb.flashOnHover = style.flashOnSelect;
                pb.BuildPieces();
            }

            // "Izgarayı kır": hepsini eğ.
            rt.localEulerAngles = new Vector3(0f, 0f, style.tiltDegrees);

            // Kademeli giriş — sıradakine biraz daha gecikme.
            var entrance = tr.GetComponent<PersonaEntrance>();
            if (entrance == null) entrance = tr.gameObject.AddComponent<PersonaEntrance>();
            entrance.delay = i * style.staggerSeconds;
            entrance.fromOffset = style.entranceFrom;
            entrance.duration = style.entranceDuration;

            EditorUtility.SetDirty(tr.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        Debug.Log($"[Persona Menü] {elements.Count} öğeye uygulandı " +
                  $"({buttons.Count} buton + {fields.Count} yazı kutusu): " +
                  string.Join(", ", elements.Select(t => t.name)) +
                  "\nSAHNE KAYDEDİLMEDİ — beğenirsen Ctrl+S, beğenmezsen 'Stili Kaldır'.");

        if (interactive)
            EditorUtility.DisplayDialog("Persona Menü",
                $"{buttons.Count} buton + {fields.Count} yazı kutusuna uygulandı.\n\n" +
                "Şeritli arka plan, giriş animasyonu ve yıldız SADECE Play modunda görünür.\n" +
                "Şekil/renk Game view'da hemen görünüyor.\n\n" +
                "SAHNE KAYDEDİLMEDİ.\n" +
                "Beğenirsen Ctrl+S, beğenmezsen SaboTour > Persona Menü > Stili Kaldır.",
                "Tamam");
    }

    [MenuItem("SaboTour/Persona Menü/Stili Kaldır", false, 101)]
    public static void Remove() => Remove(true);

    public static void Remove(bool interactive)
    {
        var canvas = FindLobbyCanvas(interactive);
        if (canvas == null) return;

        int count = 0;

        foreach (var pb in canvas.GetComponentsInChildren<PersonaButton>(true))
        {
            pb.RemovePieces();
            StripExtras(pb.gameObject);
            Object.DestroyImmediate(pb);
            count++;
        }

        foreach (var pf in canvas.GetComponentsInChildren<PersonaField>(true))
        {
            pf.RemovePieces();
            StripExtras(pf.gameObject);
            Object.DestroyImmediate(pf);
            count++;
        }

        // Arka planı ADA göre değil BİLEŞENE göre buluyoruz — eski kurulumda
        // canvas'ın altındaydı, yenisinde LobbyPanel'in altında.
        var bg = canvas.GetComponentInChildren<PersonaBackgroundFX>(true);
        if (bg != null) Object.DestroyImmediate(bg.gameObject);

        var style = canvas.GetComponent<PersonaMenuStyle>();
        if (style != null) Object.DestroyImmediate(style);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        Debug.Log($"[Persona Menü] Kaldırıldı ({count} buton eski haline döndü). Sahne kaydedilmedi.");
        if (interactive)
            EditorUtility.DisplayDialog("Persona Menü",
                $"{count} buton eski haline döndürüldü.\n\nSahne kaydedilmedi.", "Tamam");
    }

    [MenuItem("SaboTour/Persona Menü/Renkleri Yeniden Uygula", false, 102)]
    public static void Repaint()
    {
        var canvas = FindLobbyCanvas(true);
        if (canvas == null) return;

        var style = canvas.GetComponent<PersonaMenuStyle>();
        if (style == null)
        {
            EditorUtility.DisplayDialog("Persona Menü",
                "Önce 'Stili Uygula' çalıştırılmalı.", "Tamam");
            return;
        }

        style.ApplyToAllButtons();
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
    }

    // ─────────────────────────────────────────────────────────────────────
    // OYUN İÇİ MENÜLER (PauseMenu.prefab): ESC menüsü, ayarlar, nasıl oynanır,
    // geri bildirim.
    //
    // 🚨 BU AYRI BİR MENÜ KOMUTU, çünkü PREFAB DİSKE KAYDEDİLİYOR — sahnede
    // olduğu gibi "kaydetmeden bırakıp geri dönme" imkânı yok. Geri almak için
    // "Oyun İçi Menülerden Kaldır" komutu var.
    //
    // 🚨 YILDIZ KAPALI. Ana menüde tek seferlik bir vurgu, ama ayarlar
    // menüsünde her butonun üstünde patlaması yorucu oluyor.
    // ─────────────────────────────────────────────────────────────────────

    const string PauseMenuPath = "Assets/Resources/UI/PauseMenu.prefab";

    [MenuItem("SaboTour/Persona Menü/Oyun İçi Menülere Uygula", false, 120)]
    public static void ApplyToPauseMenu() => ApplyToPauseMenu(true);

    public static void ApplyToPauseMenu(bool interactive)
    {
        var root = PrefabUtility.LoadPrefabContents(PauseMenuPath);
        if (root == null)
        {
            Debug.LogError("[Persona Menü] PauseMenu.prefab açılamadı.");
            return;
        }

        // Palet açık sahnedeki ayarlardan okunuyor ki iki menü aynı görünsün.
        var style = Object.FindObjectsByType<PersonaMenuStyle>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .FirstOrDefault();

        Color accent = style != null ? style.accent : new Color32(0xD8, 0x1E, 0x2C, 0xFF);
        Color idleFill = style != null ? style.idleFill : new Color32(0xEC, 0xEC, 0xEC, 0xFF);
        Color idleText = style != null ? style.idleText : new Color32(0x12, 0x12, 0x1A, 0xFF);
        Color hoverText = style != null ? style.hoverText : Color.white;
        Color shadow = style != null ? style.shadowColor : new Color(0f, 0f, 0f, 0.85f);
        float tilt = style != null ? style.tiltDegrees : -8f;
        float shear = style != null ? style.shearX : 26f;
        Vector2 shOff = style != null ? style.shadowOffset : new Vector2(10f, -10f);
        Vector2 shOffHover = style != null ? style.shadowOffsetHover : new Vector2(20f, -18f);
        float stagger = style != null ? style.staggerSeconds : 0.07f;

        var buttons = root.GetComponentsInChildren<Button>(true).ToList();

        // Her PANEL kendi içinde kademeli girsin — hepsini tek sıraya dizsek
        // ayarlar panelindeki "Geri" butonu ESC menüsünün 6 butonundan sonra,
        // yani yarım saniye gecikmeyle girerdi.
        foreach (var group in buttons.GroupBy(b => b.transform.parent))
        {
            var ordered = group
                .OrderByDescending(b => ((RectTransform)b.transform).anchoredPosition.y)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var b = ordered[i];

                var pb = b.GetComponent<PersonaButton>();
                if (pb == null) pb = b.gameObject.AddComponent<PersonaButton>();

                pb.hoverFill = accent;
                pb.idleFill = idleFill;
                pb.idleText = idleText;
                pb.hoverText = hoverText;
                pb.shadowColor = shadow;
                pb.shearX = shear;
                pb.shadowOffset = shOff;
                pb.shadowOffsetHover = shOffHover;
                pb.spawnStar = false;      // ← oyun içi menülerde yıldız YOK
                pb.flashOnHover = false;
                pb.BuildPieces();

                ((RectTransform)b.transform).localEulerAngles = new Vector3(0f, 0f, tilt);

                var entrance = b.GetComponent<PersonaEntrance>();
                if (entrance == null) entrance = b.gameObject.AddComponent<PersonaEntrance>();
                entrance.delay = i * stagger;
                entrance.fromOffset = new Vector2(-220f, 0f);
                entrance.duration = 0.34f;
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, PauseMenuPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Persona Menü] Oyun içi menülerdeki {buttons.Count} butona uygulandı (yıldız kapalı). Prefab diske KAYDEDİLDİ.");

        if (interactive)
            EditorUtility.DisplayDialog("Persona Menü",
                $"Oyun içi menülerdeki {buttons.Count} butona uygulandı.\n\n" +
                "Yıldız efekti bu menülerde KAPALI.\n\n" +
                "⚠️ Prefab diske kaydedildi — geri almak için " +
                "'Oyun İçi Menülerden Kaldır'.", "Tamam");
    }

    [MenuItem("SaboTour/Persona Menü/Oyun İçi Menülerden Kaldır", false, 121)]
    public static void RemoveFromPauseMenu()
    {
        var root = PrefabUtility.LoadPrefabContents(PauseMenuPath);
        if (root == null) return;

        int count = 0;
        foreach (var pb in root.GetComponentsInChildren<PersonaButton>(true))
        {
            pb.RemovePieces();
            StripExtras(pb.gameObject);
            Object.DestroyImmediate(pb);
            count++;
        }

        PrefabUtility.SaveAsPrefabAsset(root, PauseMenuPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Persona Menü] Oyun içi menülerden kaldırıldı ({count} buton).");
        EditorUtility.DisplayDialog("Persona Menü",
            $"{count} buton eski haline döndürüldü.", "Tamam");
    }

    // Persona'nın eklediği yardımcı bileşenleri söker.
    static void StripExtras(GameObject go)
    {
        var entrance = go.GetComponent<PersonaEntrance>();
        if (entrance != null) Object.DestroyImmediate(entrance);

        var group = go.GetComponent<CanvasGroup>();
        if (group != null) Object.DestroyImmediate(group);
    }

    static Canvas FindLobbyCanvas(bool interactive)
    {
        var all = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var canvas = all.FirstOrDefault(c => c.gameObject.name == "LobbyCanvas");

        if (canvas == null)
        {
            if (interactive)
                EditorUtility.DisplayDialog("Persona Menü",
                    "'LobbyCanvas' bulunamadı.\n\nÖnce Offline Scene'i aç.", "Tamam");
            else
                Debug.LogError("[Persona Menü] 'LobbyCanvas' bulunamadı. Offline Scene açık mı?");
        }
        return canvas;
    }
}
