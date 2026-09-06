using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SABOTAJCI EKRAN UI'I PREFABI (tek seferlik Editor aracı)
///
/// `Assets/Resources/UI/SaboteurHud.prefab` üretiyor. Şu an içinde sadece
/// **crosshair (nişangah)** var — sabotajcının ekranında duran tek şey o.
/// İleride sabotajcıya özel başka bir ekran üstü gösterge gelirse (yetenek
/// durumu, uyarı ikonu) buraya eklenir; yeri hazır.
///
/// ─── NEDEN PREFAB ─────────────────────────────────────────────────────
/// Crosshair eskiden `SaboteurController.CreateCrosshair()` içinde KODDA
/// kuruluyordu. İki sorunu vardı:
///   1. Scene view'da görünmüyordu — şeklini/boyutunu değiştirmek için kod
///      yazmak gerekiyordu.
///   2. Kodda kurulan Canvas'ın CanvasScaler'ı VARSAYILAN ayarla geliyordu,
///      yani **Constant Pixel Size**: 2K ekranda nişangah fiziksel olarak
///      küçülüyordu. Artık prefabta 1920×1080 referanslı ölçekleme var.
///
/// `PauseMenuPrefabBuilder` / `RaceHudPrefabBuilder` ile aynı desen.
/// 🚨 `Build(bool interactive)` aşırı yüklemesi: `false` ile hiç dialog
/// açmıyor (modal pencere otomasyondan çağrılınca Unity'yi kilitliyor —
/// bu projede yaşandı).
/// </summary>
public static class SaboteurHudPrefabBuilder
{
    private const string FolderPath = "Assets/Resources/UI";
    private const string PrefabPath = FolderPath + "/SaboteurHud.prefab";

    [MenuItem("SaboTour/UI Prefabları/Sabotajcı HUD Prefabını Oluştur")]
    public static void Build() => Build(true);

    public static void Build(bool interactive)
    {
        if (File.Exists(PrefabPath) && interactive)
        {
            bool overwrite = EditorUtility.DisplayDialog("Zaten var",
                $"{PrefabPath} zaten mevcut.\n\nÜzerine yazılsın mı? Prefab üzerinde " +
                "yaptığın elle düzenlemeler (boyut, renk, şekil) KAYBOLUR.",
                "Üzerine yaz", "Vazgeç");

            if (!overwrite) return;
        }

        Directory.CreateDirectory(FolderPath);

        // Nişangah tıklanmıyor, sadece görünüyor → GraphicRaycaster YOK.
        // sortingOrder 100: yarış HUD'ıyla aynı katman, ekran mesajlarının
        // (500) ve duraklatma menüsünün (400) ALTINDA.
        GameObject root = UiPrefabUtil.CreateOverlayCanvas("SaboteurHud", 100, false);

        BuildCrosshair(root.transform);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SaboteurHud] Prefab üretildi: {PrefabPath}");

        if (interactive)
        {
            EditorUtility.DisplayDialog("Tamam",
                "Sabotajcı HUD'ı oluşturuldu.\n\n" +
                "Sahneye elle bir şey eklemene gerek yok — sabotajcı karakteri " +
                "doğduğunda kendi yükleniyor.\n\n" +
                "Nişangahın şeklini/boyutunu/rengini değiştirmek için prefaba " +
                "çift tıkla: Crosshair > Yatay / Dikey.",
                "Tamam");
        }
    }

    /// <summary>
    /// Artı şeklinde nişangah: ekranın tam ortasında iki ince çubuk.
    ///
    /// Ortadaki `Crosshair` kabı ekran merkezine sabitli ve boyutu sıfır —
    /// çubuklar ona göre konumlanıyor. Böylece nişangahı bir bütün olarak
    /// taşımak/döndürmek/gizlemek tek objeden yapılabiliyor.
    /// </summary>
    private static void BuildCrosshair(Transform parent)
    {
        GameObject crosshair = UiPrefabUtil.CreateRect("Crosshair", parent);
        RectTransform rect = crosshair.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        const float size = 18f;
        const float thickness = 2f;
        Color color = new Color(1f, 1f, 1f, 0.8f);

        CreateBar(crosshair.transform, "Yatay", new Vector2(size, thickness), color);
        CreateBar(crosshair.transform, "Dikey", new Vector2(thickness, size), color);
    }

    private static void CreateBar(Transform parent, string name, Vector2 size, Color color)
    {
        GameObject bar = UiPrefabUtil.CreateRect(name, parent);

        Image image = bar.AddComponent<Image>();
        image.color = color;

        // Nişangah tıklama hedefi DEĞİL — açık kalsaydı ekranın tam
        // ortasındaki tıklamaları yutardı.
        image.raycastTarget = false;

        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }
}
