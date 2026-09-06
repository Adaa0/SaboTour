using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TÜRKÇE KARAKTER YEDEK FONTU (Editor aracı)
///
/// ─── ÇÖZDÜĞÜ SORUN ────────────────────────────────────────────────────
/// Projenin varsayılan TMP fontu `LiberationSans SDF`, **statik** bir atlas
/// kullanıyor: içinde ne varsa o kadar, 250 glif. Ölçüldü ve şu Türkçe
/// BÜYÜK harfler eksik çıktı: **Ğ** ve **Ş**.
///
/// Yani ekrandaki "BAŞLA!" yazısı Ş yerine boş kutu gösterirdi — geri
/// sayımın son kelimesi. Küçük harfler (ğ ş ı) ve İ Ç Ö Ü zaten vardı, o
/// yüzden sorun bugüne kadar fark edilmemişti.
///
/// ─── ÇÖZÜM ────────────────────────────────────────────────────────────
/// `LiberationSans.ttf`'ten **Dinamik** (Dynamic) bir font asset üretiliyor
/// ve TMP'nin GLOBAL YEDEK (fallback) listesine ekleniyor.
///
/// Dinamik font ne demek: TMP bir karakteri ana fontta bulamazsa, çalışma
/// anında kaynak .ttf dosyasından o karakteri atlasa EKLİYOR. Yani bundan
/// sonra hangi Türkçe karakteri yazarsan yaz çalışır — tek tek karakter
/// listesi hazırlamaya gerek yok.
///
/// GLOBAL yedek olduğu için mevcut hiçbir yazıya dokunmaya gerek yok:
/// projedeki TÜM TMP yazıları (menü, sıralama tablosu, ekran mesajları)
/// otomatik faydalanıyor.
///
/// ⚠️ KAPSAM DIŞI: `⚠` (U+26A0) gibi SEMBOLLER hâlâ yok — LiberationSans
/// bir metin fontu, sembol fontu değil. Motor arızası mesajındaki `⚠`
/// karakteri boş kutu çıkabilir. Çözümü basit: o mesajı Inspector'dan
/// (Online Scene > tuzak objesi > `Victim Message`) sembolsüz yaz.
/// </summary>
public static class TurkishFontSetup
{
    private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
    private const string OutputFolder = "Assets/UI/Fonts";
    private const string OutputPath = OutputFolder + "/Turkce Yedek SDF.asset";

    [MenuItem("SaboTour/UI Prefabları/Türkçe Karakter Yedek Fontunu Kur")]
    public static void Run() => Run(true);

    public static void Run(bool interactive)
    {
        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath);

        if (fallback == null)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
            {
                Debug.LogError($"[TurkishFontSetup] Kaynak font bulunamadı: {SourceFontPath}");
                return;
            }

            Directory.CreateDirectory(OutputFolder);

            // Dinamik mod: eksik karakterler çalışma anında .ttf'ten atlasa
            // ekleniyor. 1024×1024 atlas Türkçe için fazlasıyla yeter.
            fallback = TMP_FontAsset.CreateFontAsset(
                source,
                90,                                     // örnekleme punto
                9,                                      // atlas dolgusu
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024, 1024,
                AtlasPopulationMode.Dynamic,
                true);                                  // çoklu atlas desteği

            fallback.name = "Turkce Yedek SDF";
            AssetDatabase.CreateAsset(fallback, OutputPath);

            // Atlas dokusu ve materyal font asset'in İÇİNE gömülmeli,
            // yoksa asset'i taşıyınca/yeniden içe aktarınca kopuyorlar.
            if (fallback.atlasTextures != null)
            {
                foreach (Texture2D tex in fallback.atlasTextures)
                {
                    if (tex == null) continue;
                    tex.name = "Turkce Yedek Atlas";
                    AssetDatabase.AddObjectToAsset(tex, fallback);
                }
            }

            if (fallback.material != null)
            {
                fallback.material.name = "Turkce Yedek Material";
                AssetDatabase.AddObjectToAsset(fallback.material, fallback);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TurkishFontSetup] Dinamik yedek font üretildi: {OutputPath}");
        }

        bool added = RegisterAsGlobalFallback(fallback);

        string message = added
            ? "Türkçe yedek fontu kuruldu ve TMP global yedek listesine eklendi.\n\n" +
              "Artık Ğ ve Ş dahil bütün Türkçe karakterler her yazıda çalışıyor."
            : "Yedek font zaten kurulu — değişiklik yapılmadı.";

        Debug.Log($"[TurkishFontSetup] {message}");

        if (interactive)
            EditorUtility.DisplayDialog("Türkçe Font", message, "Tamam");
    }

    /// <summary>
    /// Font asset'i TMP Settings'in global yedek listesine ekler.
    ///
    /// 🚨 `TMP_Settings.fallbackFontAssets` listesine doğrudan eklemek
    /// YETMEZ — o sadece bellekteki kopyayı değiştirir, Unity kapanınca
    /// kaybolur. Ayarın diske yazılması için `SerializedObject` üzerinden
    /// değiştirilip `ApplyModifiedProperties` çağrılmalı.
    /// (Bu projede "ProjectSettings değişikliği sessizce geri alındı"
    /// hatası daha önce iki kez yaşandı — aynı sınıf hata.)
    /// </summary>
    private static bool RegisterAsGlobalFallback(TMP_FontAsset fallback)
    {
        TMP_Settings settings = TMP_Settings.instance;
        if (settings == null)
        {
            Debug.LogError("[TurkishFontSetup] TMP Settings bulunamadı.");
            return false;
        }

        var so = new SerializedObject(settings);
        SerializedProperty list = so.FindProperty("m_fallbackFontAssets");
        if (list == null)
        {
            Debug.LogError("[TurkishFontSetup] TMP Settings içinde 'm_fallbackFontAssets' alanı yok.");
            return false;
        }

        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == fallback)
                return false;   // zaten kayıtlı

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fallback;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        return true;
    }
}
