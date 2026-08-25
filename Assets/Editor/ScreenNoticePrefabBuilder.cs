using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EKRAN MESAJI PREFABI (tek seferlik Editor aracı)
///
/// `Assets/Resources/UI/ScreenNotice.prefab` üretiyor — oyunun ortasında
/// çıkan TÜM kısa bildirimler bu tek yazıyı kullanıyor:
///   • 3 → 2 → 1 → BAŞLA! geri sayımı
///   • "SON TUR!"
///   • "⚠ MOTOR ARIZASI" ve sabotajcıya giden "{oyuncu} yakalandı"
///   • yarış başındaki rol ipuçları
///   • bağlantı/oturum uyarıları
///
/// Yani bu prefabın fontunu/rengini/boyutunu değiştirmek oyundaki bütün
/// ekran mesajlarını birden değiştiriyor.
///
/// ─── NEDEN PREFAB ─────────────────────────────────────────────────────
/// Eskiden `ScreenNotice.Build()` içinde kodda kuruluyordu ve Canvas'ın
/// CanvasScaler'ı varsayılan (Constant Pixel Size) kalıyordu — 2K ekranda
/// yazı fiziksel olarak küçülüyordu. Ayrıca yazı tipi legacy `Text` +
/// `LegacyRuntime.ttf` idi; artık projenin geri kalanıyla aynı TMP fontu.
///
/// ⚠️ ARKA PLAN TAM EKRAN: `Background` objesi ekranın TAMAMINI kaplayan
/// %70 opak siyah bir panel. Kod tarafındaki eski hâli de böyleydi, görüntü
/// DEĞİŞMESİN diye aynen korundu. Ama pratikte bu, "SON TUR!" yazarken bile
/// bütün ekranı karartıyor. İstemiyorsan prefabta `Background` objesini
/// seçip ya kapat, ya alfasını düşür, ya da sadece yazının arkasını
/// kaplayacak şekilde küçült — kod tarafında hiçbir şey değişmez.
/// </summary>
public static class ScreenNoticePrefabBuilder
{
    private const string FolderPath = "Assets/Resources/UI";
    private const string PrefabPath = FolderPath + "/ScreenNotice.prefab";

    [MenuItem("SaboTour/UI Prefabları/Ekran Mesajı Prefabını Oluştur")]
    public static void Build() => Build(true);

    public static void Build(bool interactive)
    {
        if (File.Exists(PrefabPath) && interactive)
        {
            bool overwrite = EditorUtility.DisplayDialog("Zaten var",
                $"{PrefabPath} zaten mevcut.\n\nÜzerine yazılsın mı? Prefab üzerinde " +
                "yaptığın elle düzenlemeler KAYBOLUR.",
                "Üzerine yaz", "Vazgeç");

            if (!overwrite) return;
        }

        Directory.CreateDirectory(FolderPath);

        // sortingOrder 500: duraklatma menüsünün (400) ve HUD'ın (100)
        // ÜSTÜNDE — bağlantı koptu gibi bir mesaj menü açıkken de okunmalı.
        // Ekran karartmasının (600) altında.
        GameObject root = UiPrefabUtil.CreateOverlayCanvas("ScreenNotice", 500, false);
        ScreenNotice notice = root.AddComponent<ScreenNotice>();

        // Mesaj kabı — kod bunu açıp kapatıyor. Canvas'ın KENDİSİ değil ayrı
        // bir çocuk olması önemli: canvas kapatılsaydı `GameObject.Find`
        // gibi arama yapan başka sistemler onu bulamazdı.
        GameObject noticeRoot = UiPrefabUtil.CreateRect("NoticeRoot", root.transform);
        UiPrefabUtil.Stretch(noticeRoot.GetComponent<RectTransform>());

        GameObject bgObj = UiPrefabUtil.CreateRect("Background", noticeRoot.transform);
        UiPrefabUtil.Stretch(bgObj.GetComponent<RectTransform>());
        Image bg = bgObj.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        bg.raycastTarget = false;

        GameObject textObj = UiPrefabUtil.CreateRect("Message", noticeRoot.transform);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(900f, 300f);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "";
        text.fontSize = 44f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;

        TMP_FontAsset font = UiPrefabUtil.FindFont();
        if (font != null) text.font = font;

        notice.noticeRoot = noticeRoot;
        notice.noticeText = text;

        noticeRoot.SetActive(false);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ScreenNotice] Prefab üretildi: {PrefabPath}");

        if (interactive)
        {
            EditorUtility.DisplayDialog("Tamam",
                "Ekran mesajı prefabı oluşturuldu.\n\n" +
                "Oyundaki TÜM ortadaki bildirimler (geri sayım, SON TUR, motor " +
                "arızası, rol ipuçları) artık bu prefabı kullanıyor.\n\n" +
                "⚠️ Arka plan şu an TÜM EKRANI %70 karartıyor (eski koddaki " +
                "davranışın aynısı). Beğenmezsen prefabtaki 'Background' " +
                "objesini kapat ya da küçült.",
                "Tamam");
        }
    }
}
