using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TEK SEFERLİK ARAÇ — Yarışçının yuvarlak minimap'inin Prefab'ını oluşturur.
///
/// PauseMenuPrefabBuilder ile AYNI DESEN ve aynı gerekçe: UI kodun içinde
/// runtime'da kurulursa Scene view'da görünmüyor ve bir rengi/sprite'ı
/// değiştirmek için kod yazmak gerekiyor. Bu araç minimap'i NORMAL bir Unity
/// prefab'ı olarak bir kere üretiyor; ondan sonra
/// Assets/Resources/UI/RacerMinimap.prefab'a çift tıklayıp istediğin gibi
/// düzenleyebilirsin (boyut, konum, renk, kendi sprite'ların).
///
/// Minimap için gereken iki sprite (dolu daire + ok) de burada üretiliyor ve
/// Assets/UI/Sprites/ altına GERÇEK asset olarak kaydediliyor. Kendi
/// görsellerini yapınca prefabtaki Image'lara sürükleyip değiştirebilirsin —
/// kod tarafında hiçbir şey değişmez.
///
/// ── KULLANIMI ──
/// Unity Editor üst menüsü: SaboTour > Yarışçı Minimap Prefabını Oluştur.
/// </summary>
public static class RacerMinimapPrefabBuilder
{
    private const string UiFolder = "Assets/Resources/UI";
    private const string PrefabPath = UiFolder + "/RacerMinimap.prefab";

    private const string SpriteFolder = "Assets/UI/Sprites";
    private const string CircleSpritePath = SpriteFolder + "/MinimapCircle.png";
    private const string ArrowSpritePath = SpriteFolder + "/MinimapArrow.png";

    // Minimap'in ekrandaki çapı (piksel, 1920x1080 referans çözünürlükte).
    private const float Diameter = 240f;
    // Ekranın sağ üst köşesinden boşluk.
    private const float ScreenMargin = 22f;

    [MenuItem("SaboTour/Yarışçı Minimap Prefabını Oluştur")]
    public static void Build() => Build(interactive: true);

    /// <summary>
    /// `interactive: false` ile çağrılırsa hiçbir onay penceresi açmaz ve
    /// prefab ZATEN VARSA hiç dokunmaz. Otomasyondan (script/komut) çağırmak
    /// için — açılan modal bir pencere Unity'yi kilitler ve komut takılır.
    /// </summary>
    public static void Build(bool interactive)
    {
        if (File.Exists(PrefabPath))
        {
            if (!interactive)
            {
                Debug.Log($"[RacerMinimap] {PrefabPath} zaten var — dokunulmadı.");
                return;
            }

            bool overwrite = EditorUtility.DisplayDialog(
                "Prefab zaten var",
                $"{PrefabPath} zaten mevcut. Üzerine yazmak, yaptığın TÜM görsel " +
                "özelleştirmeleri (boyut/konum/renk/sprite) SİLER. Emin misin?",
                "Üzerine Yaz", "İptal");
            if (!overwrite) return;
        }

        Directory.CreateDirectory(UiFolder);
        Directory.CreateDirectory(SpriteFolder);

        Sprite circle = EnsureCircleSprite();
        Sprite arrow = EnsureArrowSprite();

        // ── Kök: sadece scripti taşıyor, hep aktif kalıyor ──────────────────
        GameObject rootGo = new GameObject("RacerMinimapRoot");
        RacerMinimapHUD hud = rootGo.AddComponent<RacerMinimapHUD>();

        // ── Canvas ─────────────────────────────────────────────────────────
        GameObject canvasGo = new GameObject("MinimapCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(rootGo.transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 90: crosshair'in (100), PauseMenu'nün (400) ve ScreenNotice'in (500)
        // ALTINDA kalsın — minimap hiçbir zaman menünün önüne geçmemeli.
        canvas.sortingOrder = 90;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        // GraphicRaycaster BİLEREK yok: minimap tıklanmıyor, eklemek her
        // tıklamada gereksiz bir raycast maliyeti olurdu.

        // ── Yuvarlak alanın kökü (sağ üst köşe) ────────────────────────────
        GameObject minimapRoot = new GameObject("MinimapRoot", typeof(RectTransform));
        minimapRoot.transform.SetParent(canvasGo.transform, false);

        RectTransform rootRect = minimapRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 1f); // sağ üst
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(Diameter, Diameter);
        rootRect.anchoredPosition = new Vector2(-(Diameter * 0.5f + ScreenMargin),
                                                -(Diameter * 0.5f + ScreenMargin));

        // Çerçeve: minimap'in birkaç piksel dışına taşan açık renkli bir daire.
        // Ayrı bir "halka" sprite'ı yapmak yerine iki daireyi üst üste koymak
        // yeterli — üstteki koyu arka plan ortayı kapatınca geriye halka kalıyor.
        GameObject border = CreateStretched(minimapRoot, "Border", -5f);
        Image borderImage = border.AddComponent<Image>();
        borderImage.sprite = circle;
        borderImage.color = new Color(1f, 1f, 1f, 0.55f);
        borderImage.raycastTarget = false;

        GameObject background = CreateStretched(minimapRoot, "Background", 0f);
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.sprite = circle;
        backgroundImage.color = new Color(0.04f, 0.05f, 0.08f, 0.72f);
        backgroundImage.raycastTarget = false;

        // ── Maske: bu dairenin DIŞINA taşan her şey kırpılıyor ─────────────
        GameObject maskRoot = CreateStretched(minimapRoot, "MaskRoot", 0f);
        Image maskImage = maskRoot.AddComponent<Image>();
        maskImage.sprite = circle;
        maskImage.raycastTarget = false;

        Mask mask = maskRoot.AddComponent<Mask>();
        // Maskenin kendi dairesi çizilmesin — arka planı zaten yukarıda
        // ayrı bir Image çiziyor, ikisi üst üste binince renk koyulaşırdı.
        mask.showMaskGraphic = false;

        // ── Harita katmanı: HER KAREDE kaydırılıp döndürülüyor ─────────────
        GameObject mapContent = new GameObject("MapContent", typeof(RectTransform));
        mapContent.transform.SetParent(maskRoot.transform, false);
        RectTransform mapRect = mapContent.GetComponent<RectTransform>();
        mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = new Vector2(0.5f, 0.5f);
        mapRect.sizeDelta = Vector2.zero;
        mapRect.anchoredPosition = Vector2.zero;

        GameObject road = new GameObject("Road", typeof(RectTransform));
        road.transform.SetParent(mapContent.transform, false);
        MinimapRoadGraphic roadGraphic = road.AddComponent<MinimapRoadGraphic>();
        roadGraphic.raycastTarget = false;

        RectTransform roadRect = roadGraphic.rectTransform;
        roadRect.anchorMin = roadRect.anchorMax = roadRect.pivot = new Vector2(0.5f, 0.5f);
        roadRect.sizeDelta = Vector2.zero;
        roadRect.anchoredPosition = Vector2.zero;

        GameObject checkpointLayer = new GameObject("Checkpoints", typeof(RectTransform));
        checkpointLayer.transform.SetParent(mapContent.transform, false);
        RectTransform checkpointRect = checkpointLayer.GetComponent<RectTransform>();
        checkpointRect.anchorMin = checkpointRect.anchorMax = checkpointRect.pivot = new Vector2(0.5f, 0.5f);
        checkpointRect.sizeDelta = Vector2.zero;

        // ── Araba ikonları: maskenin İÇİNDE ama harita katmanının DIŞINDA ──
        // Neden dışında: görüş alanına sığmayan rakipleri minimap'in kenarına
        // yapıştırabilmek için konumlarını elle hesaplıyoruz, dönen bir
        // katmanın çocuğu olsalardı bu hesap bozulurdu.
        GameObject carIconLayer = new GameObject("CarIcons", typeof(RectTransform));
        carIconLayer.transform.SetParent(maskRoot.transform, false);
        RectTransform carIconRect = carIconLayer.GetComponent<RectTransform>();
        carIconRect.anchorMin = carIconRect.anchorMax = carIconRect.pivot = new Vector2(0.5f, 0.5f);
        carIconRect.sizeDelta = Vector2.zero;

        // ── Merkezdeki "sen" oku (maskenin dışında — hep tam ortada) ───────
        GameObject playerArrow = new GameObject("PlayerArrow", typeof(RectTransform));
        playerArrow.transform.SetParent(minimapRoot.transform, false);
        Image arrowImage = playerArrow.AddComponent<Image>();
        arrowImage.sprite = arrow;
        arrowImage.color = Color.white;
        arrowImage.raycastTarget = false;

        RectTransform arrowRect = playerArrow.GetComponent<RectTransform>();
        arrowRect.anchorMin = arrowRect.anchorMax = arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.sizeDelta = new Vector2(26f, 26f);
        arrowRect.anchoredPosition = Vector2.zero;

        // ── Referansları bağla ─────────────────────────────────────────────
        hud.canvas = canvas;
        hud.maskRoot = maskRoot.GetComponent<RectTransform>();
        hud.mapContent = mapRect;
        hud.roadGraphic = roadGraphic;
        hud.checkpointLayer = checkpointRect;
        hud.carIconLayer = carIconRect;
        hud.playerArrow = arrowImage;
        hud.carIconSprite = arrow;
        hud.checkpointIconSprite = circle;

        PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
        Object.DestroyImmediate(rootGo);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[RacerMinimap] {PrefabPath} oluşturuldu.");

        if (!interactive) return;

        EditorUtility.DisplayDialog("Hazır",
            $"{PrefabPath} oluşturuldu.\n\n" +
            "Oyun açılırken otomatik yükleniyor — sahneye elle bir şey eklemen " +
            "GEREKMİYOR. Minimap sadece yarışçının ekranında, yarış sürerken " +
            "görünür.\n\nGörünümü değiştirmek için prefaba çift tıkla.", "Tamam");

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    // ─── Yardımcılar ─────────────────────────────────────────────────────────

    /// <summary>Parent'ı tamamen kaplayan bir çocuk. `expand` pozitifse içeri, negatifse dışarı taşar.</summary>
    private static GameObject CreateStretched(GameObject parent, string name, float expand)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(expand, expand);
        rect.offsetMax = new Vector2(-expand, -expand);

        return go;
    }

    private static Sprite EnsureCircleSprite()
    {
        if (File.Exists(CircleSpritePath))
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
            if (existing != null) return existing;
        }

        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size * 0.5f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                // Kenarda 1 piksellik yumuşak geçiş — testere dişi görünmesin.
                float alpha = Mathf.Clamp01(radius - distance);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return SaveSprite(texture, CircleSpritePath);
    }

    private static Sprite EnsureArrowSprite()
    {
        if (File.Exists(ArrowSpritePath))
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
            if (existing != null) return existing;
        }

        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        // Yukarı bakan bir ok. Tabanı içe girintili (basit üçgen yerine) —
        // küçük boyutta yönü çok daha okunaklı oluyor.
        Vector2 tip = new Vector2(size * 0.5f, size * 0.97f);
        Vector2 left = new Vector2(size * 0.06f, size * 0.05f);
        Vector2 right = new Vector2(size * 0.94f, size * 0.05f);
        Vector2 notch = new Vector2(size * 0.5f, size * 0.32f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 2x2 süper örnekleme — kenarları yumuşatıyor.
                int hits = 0;
                for (int sy = 0; sy < 2; sy++)
                {
                    for (int sx = 0; sx < 2; sx++)
                    {
                        Vector2 p = new Vector2(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f);
                        if (InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right))
                            hits++;
                    }
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, hits / 4f));
            }
        }

        return SaveSprite(texture, ArrowSpritePath);
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);

        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;

        return !(hasNegative && hasPositive);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

    private static Sprite SaveSprite(Texture2D texture, string path)
    {
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
