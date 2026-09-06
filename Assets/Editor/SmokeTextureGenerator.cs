using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Billboard parçacıklar için yumuşak, düzensiz kenarlı bir DUMAN DOKUSU üretir
/// ve projeye PNG olarak kaydeder.
///
/// NEDEN GEREKLİ: Billboard parçacıklar (4 vertex) mesh parçacıklardan (küre =
/// 515 vertex) kat kat ucuz, ama düz bir kare olarak çizildikleri için dokusuz
/// halde "pütürlü bloklar" gibi görünüyorlar. İşi yapan şey dokunun ALFA
/// kanalı: merkezde opak, kenara doğru saydamlaşan düzensiz bir leke.
///
/// Dokunun RGB'si BEYAZ üretiliyor — rengi parçacık sisteminin kendi
/// "Start Color" / "Color over Lifetime" ayarı belirliyor. Yani aynı dokuyla
/// beyaz drift dumanı da, koyu gri egzoz dumanı da yapabilirsin.
///
/// KULLANIM:
///  1. Üst menü: Tools > SaboTour > Duman Dokusu Üret
///  2. Kaydırıcılarla beğendiğin görüntüyü bul (önizleme canlı güncelleniyor)
///  3. "PNG Olarak Kaydet" → Assets/Textures/ altına düşer
///  4. Duman ParticleSystem'inin materyalinde:
///     - Render Mode: Billboard
///     - Shader: Universal Render Pipeline/Particles/Unlit
///     - Surface Type: Transparent
///     - Base Map alanına bu dokuyu sürükle
///
/// Bu dosya "Editor" klasöründe olduğu için oyun build'ine dahil edilmez.
/// </summary>
public class SmokeTextureGenerator : EditorWindow
{
    private const string OutputFolder = "Assets/Textures";

    [MenuItem("Tools/SaboTour/Duman Dokusu Üret")]
    private static void Open()
    {
        GetWindow<SmokeTextureGenerator>("Duman Dokusu").minSize = new Vector2(340f, 520f);
    }

    private int size = 256;
    private float softness = 0.55f;
    private float noiseAmount = 0.40f;
    private float noiseScale = 3.5f;
    private float density = 0.35f;
    private int seed = 0;
    private string fileName = "SmokeParticle";

    private Texture2D preview;

    private void OnEnable() => Regenerate();

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Billboard duman parçacıkları için doku üretir. RGB beyaz kalır — " +
            "rengi parçacık sisteminden ayarlarsın.", MessageType.Info);

        EditorGUI.BeginChangeCheck();

        size = EditorGUILayout.IntPopup("Boyut", size,
            new[] { "128", "256", "512" }, new[] { 128, 256, 512 });

        softness = EditorGUILayout.Slider(
            new GUIContent("Yumuşaklık", "Kenarın ne kadar geniş bir alanda saydamlaştığı. " +
                                         "Yüksek = daha puslu, düşük = daha keskin kenarlı."),
            softness, 0.1f, 0.95f);

        noiseAmount = EditorGUILayout.Slider(
            new GUIContent("Düzensizlik", "0 = kusursuz daire (yapay durur). " +
                                          "Yükseltince kenar gerçek duman gibi girintili çıkıntılı olur."),
            noiseAmount, 0f, 0.8f);

        noiseScale = EditorGUILayout.Slider(
            new GUIContent("Desen Sıklığı", "Düşük = büyük yumuşak yumrular. " +
                                            "Yüksek = ince, tırtıklı kenar."),
            noiseScale, 1f, 10f);

        density = EditorGUILayout.Slider(
            new GUIContent("İç Doku", "Lekenin içindeki koyu-açık dalgalanma. " +
                                      "0 = düz leke, yüksek = bulutlu."),
            density, 0f, 0.8f);

        EditorGUILayout.BeginHorizontal();
        seed = EditorGUILayout.IntField("Seed", seed);
        if (GUILayout.Button("Rastgele", GUILayout.Width(80))) seed = Random.Range(0, 99999);
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck()) Regenerate();

        EditorGUILayout.Space(8);

        // Önizleme. Alfa görünsün diye arkaya koyu bir zemin çiziyoruz —
        // beyaz zeminde beyaz duman görünmezdi.
        Rect rect = GUILayoutUtility.GetRect(256f, 256f, GUILayout.ExpandWidth(false));
        rect.x = (position.width - rect.width) * 0.5f;
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.17f, 0.2f));
        if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);

        EditorGUILayout.Space(8);

        fileName = EditorGUILayout.TextField("Dosya Adı", fileName);

        if (GUILayout.Button("PNG Olarak Kaydet", GUILayout.Height(30)))
            Save();
    }

    private void Regenerate()
    {
        if (preview != null) DestroyImmediate(preview);
        preview = Generate();
    }

    /// <summary>
    /// Dokuyu üretir. Mantık şu: merkeze olan mesafe alfayı belirliyor, ama bu
    /// mesafeyi Perlin gürültüsüyle bozuyoruz ki kusursuz bir daire yerine
    /// düzensiz bir duman lekesi çıksın.
    /// </summary>
    private Texture2D Generate()
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            name = fileName
        };

        // Seed'i gürültüye kaydırma olarak veriyoruz — Mathf.PerlinNoise'un
        // kendi seed parametresi yok, girdiyi kaydırmak standart yöntem.
        float offsetX = seed * 0.7213f % 1000f;
        float offsetY = seed * 1.3179f % 1000f;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;

                // Merkezden uzaklık: 0 = orta, 1 = kenarın ortası
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                float rawDistance = Mathf.Sqrt(dx * dx + dy * dy) * 2f;

                float shapeNoise = Fbm(u * noiseScale + offsetX, v * noiseScale + offsetY);
                float innerNoise = Fbm(u * noiseScale * 2.3f + offsetY, v * noiseScale * 2.3f + offsetX);

                // Mesafeyi gürültüyle boz → kenar düzensizleşir
                float distance = rawDistance * (1f + (shapeNoise - 0.5f) * noiseAmount * 2f);

                // Yumuşak geçiş: merkezde 1, dış tarafta 0
                float alpha = 1f - Mathf.SmoothStep(1f - softness, 1f, distance);

                // İç dalgalanma — düz bir leke yerine bulutlu görünüm
                alpha *= Mathf.Lerp(1f - density, 1f, innerNoise);

                // GÜVENLİK: gürültü kenarı dışarı taşırabilir ve doku karesinin
                // kenarında sert bir kesik oluşur. Ham mesafeye göre zorunlu bir
                // sönümleme uygulayıp kenarın kesin olarak 0'a inmesini sağlıyoruz.
                float border = 1f - Mathf.Clamp01((rawDistance - 0.80f) / 0.20f);
                alpha *= border;

                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Üç katmanlı Perlin gürültüsü (fBm). Tek katman fazla düzgün duruyor;
    /// katmanları farklı ölçek ve ağırlıkla üst üste koyunca doğal bir
    /// düzensizlik oluşuyor.
    /// </summary>
    private static float Fbm(float x, float y)
    {
        float value = 0f;
        float amplitude = 0.5f;
        float frequency = 1f;
        float total = 0f;

        for (int i = 0; i < 3; i++)
        {
            value += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return value / total;
    }

    private void Save()
    {
        if (!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder);
            AssetDatabase.Refresh();
        }

        Texture2D texture = Generate();
        byte[] png = texture.EncodeToPNG();
        DestroyImmediate(texture);

        string safeName = string.IsNullOrWhiteSpace(fileName) ? "SmokeParticle" : fileName;
        string path = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{safeName}.png");

        File.WriteAllBytes(path, png);
        AssetDatabase.Refresh();

        // Import ayarları: alfa saydamlık olarak yorumlansın, kenarda tekrar
        // etmesin. Bunlar elle ayarlanmazsa doku parçacıkta yanlış görünür.
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        Debug.Log($"[SmokeTextureGenerator] Doku kaydedildi → {path}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
    }

    private void OnDisable()
    {
        if (preview != null) DestroyImmediate(preview);
    }
}
