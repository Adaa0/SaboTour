using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// PERFORMANS AYARLARI — TEK TUŞLA UYGULAMA (21 Ağustos 2026)
///
/// ─── NEDEN GEREKLİ ────────────────────────────────────────────────────
/// Prop sistemi yeniden yazılınca prop sayısı 2400 → 7500'e çıktı ve
/// 8 Ağustos profilinin tek bulgusu "asıl maliyet ÇİZİM tarafında" idi —
/// yani tam da artırdığımız yer. Ayrıca `PropCullDistances` sistemi
/// yazılmıştı ama SADECE kayalara uygulanmıştı: **3000 ağaç, gölge yayan
/// ve 366 üçgenlik olanlar, hiçbir mesafe sınırına tabi değildi** ve
/// kameranın far clip'ine (1000 birim) kadar çiziliyordu.
///
/// ─── PAKETİN MANTIĞI ──────────────────────────────────────────────────
/// Sis, propları kesme mesafesinden ÖNCE gizlemeli — yoksa ağaçlar
/// sisin içinde erimek yerine boşlukta "pat" diye kaybolur. Bu yüzden sis
/// bitişi ve prop kesme mesafeleri BİRLİKTE ayarlanıyor; birini değiştirip
/// diğerini bırakmak pop-in üretir.
///
/// Kamera far clip'ine (1000) DOKUNULMUYOR: "Zemin" objesi 20000x20000 tek
/// bir mesh ve far plane onu keserse ufukta zeminin bittiği yer görünür
/// hâle gelir. Zemin tek çizim çağrısı, yani ucuz — kesmenin kazancı yok,
/// riski var.
///
/// ─── DEĞERLER TAHMİNDİR ───────────────────────────────────────────────
/// 🚨 Bu paket ÖLÇÜMLE değil, muhafazakâr bir tahminle seçildi. Bu projede
/// "LOD iyidir" ve "darboğaz CPU'dur" gibi genel doğruların ölçünce
/// çürüdüğü iki vaka var. Development build + Profiler ile ölçtükten sonra
/// aşağıdaki sabitler serbestçe değiştirilip araç tekrar çalıştırılabilir.
/// Araç her değeri ESKİ → YENİ olarak Console'a yazıyor, geri almak kolay.
/// </summary>
public static class PerformanceSettingsApplier
{
    private const string ScenePath = "Assets/Scenes/Mirror/Online Scene.unity";

    // ── AYARLANABİLİR PAKET ──────────────────────────────────────────────
    // Ölçüm sonrası buradaki sayıları değiştirip aracı tekrar çalıştır.

    /// <summary>Ağaçların konacağı yeni katman (çizim mesafesi verebilmek için).</summary>
    private const string TreeLayerName = "PropTree";

    /// <summary>Ağaçlar bu mesafeden sonra çizilmiyor. Sis bitişinden KÜÇÜK olmalı.</summary>
    private const float TreeCullDistance = 700f;

    /// <summary>Uzak manzara halkası. Ağaçtan biraz uzak, sisten yine de yakın.</summary>
    private const float FarPropCullDistance = 750f;

    /// <summary>Kaya/çalı gibi küçük proplar — zaten 200'dü, biraz daha kısıldı.</summary>
    private const float SmallPropCullDistance = 180f;

    /// <summary>
    /// Sis bitişi. Kesme mesafelerinden BÜYÜK olmalı ki proplar kaybolmadan
    /// önce sisin içinde tamamen erimiş olsun.
    /// </summary>
    private const float FogEndDistance = 900f;
    private const float FogStartDistance = 200f;

    /// <summary>
    /// Gölge mesafesi. Gölge alanı yarıçapın KARESİYLE büyüyor:
    /// 70/100 → alanın %49'u, yani gölgeye giren ağaç sayısı yarıya iniyor.
    /// Bu paketteki en yüksek kazanç/görsel-kayıp oranına sahip ayar.
    /// </summary>
    private const float ShadowDistance = 70f;

    [MenuItem("SaboTour/Performans Ayarlarını Uygula")]
    public static void Apply() => Apply(true);

    public static void Apply(bool interactive)
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            string message = $"Bu araç Online Scene üzerinde çalışıyor. Önce '{ScenePath}' sahnesini aç.";

            if (!interactive) { Debug.LogError("[Performans] " + message); return; }

            if (!EditorUtility.DisplayDialog("Sahne açık değil", message + "\n\nAçayım mı?", "Evet, aç", "Vazgeç"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        var log = new List<string>();

        int treeLayer = EnsureLayer(TreeLayerName, log);
        ApplyScatterLayers(treeLayer, log);
        ApplyCullDistances(log);
        ApplyFog(log);
        ApplyShadowDistance(log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        string summary = string.Join("\n", log);
        Debug.Log("[Performans] Uygulanan ayarlar:\n" + summary);

        if (interactive)
        {
            EditorUtility.DisplayDialog("Performans ayarları uygulandı",
                summary +
                "\n\n🚨 KATMAN EKLENDİYSE: Unity üst menüden File > Save Project yap. " +
                "ProjectSettings değişiklikleri sahne kaydıyla diske YAZILMIYOR — bu " +
                "projede iki kere sessizce geri alındı." +
                "\n\nSonra Development Build alıp ölç. Değerler tahmindir, " +
                "PerformanceSettingsApplier.cs'in başındaki sabitlerden ayarlanabilir.",
                "Tamam");
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Katman yoksa ilk boş slota ekler, varsa indeksini döndürür.
    /// (Unity'de 0-7 arası slotlar yerleşik, 8'den sonrası kullanıcıya ait.)
    /// </summary>
    private static int EnsureLayer(string layerName, List<string> log)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
        {
            log.Add($"Katman '{layerName}' zaten var (index {existing}).");
            return existing;
        }

        Object asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
        SerializedObject tagManager = new SerializedObject(asset);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);

            if (!string.IsNullOrEmpty(slot.stringValue)) continue;

            slot.stringValue = layerName;
            tagManager.ApplyModifiedProperties();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            log.Add($"YENİ KATMAN: '{layerName}' → index {i}  (File > Save Project gerekli!)");
            return i;
        }

        log.Add($"⚠️ Boş katman slotu kalmadı — '{layerName}' eklenemedi.");
        return -1;
    }

    /// <summary>
    /// Ağaç scatter'ına çizim katmanını verir. Kaya ve uzak halka zaten
    /// kendi katmanlarındaydı, onlara dokunulmuyor.
    ///
    /// ⚠️ Collider'lı proplar bu ayardan ETKİLENMİYOR — TrackPropScatter
    /// onları her zaman `Prop` katmanında bırakıyor, çünkü buz bombasının
    /// "fırlayan araç proplardan geçsin" numarası o katmana bağlı.
    /// </summary>
    private static void ApplyScatterLayers(int treeLayer, List<string> log)
    {
        if (treeLayer < 0) return;

        foreach (TrackPropScatter scatter in Object.FindObjectsByType<TrackPropScatter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(scatter);
            SerializedProperty group = so.FindProperty("groupName");
            SerializedProperty cullLayer = so.FindProperty("cullLayerName");

            if (group == null || cullLayer == null) continue;

            // Ağaç scatter'ı: cull katmanı BOŞ olan. Kaya (PropSmall) ve uzak
            // halka (PropFar) zaten dolu, onlara karışmıyoruz.
            if (!string.IsNullOrWhiteSpace(cullLayer.stringValue))
            {
                log.Add($"'{group.stringValue}' → cull katmanı zaten '{cullLayer.stringValue}', dokunulmadı.");
                continue;
            }

            cullLayer.stringValue = TreeLayerName;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(scatter);

            log.Add($"'{group.stringValue}' → cull katmanı: (boş) → '{TreeLayerName}'");
        }
    }

    private static void ApplyCullDistances(List<string> log)
    {
        PropCullDistances culler = Object.FindAnyObjectByType<PropCullDistances>(FindObjectsInactive.Include);

        if (culler == null)
        {
            log.Add("⚠️ Sahnede PropCullDistances yok — çizim mesafeleri uygulanamadı.");
            return;
        }

        SerializedObject so = new SerializedObject(culler);
        SerializedProperty rules = so.FindProperty("rules");

        if (rules == null)
        {
            log.Add("⚠️ PropCullDistances'ta 'rules' alanı bulunamadı.");
            return;
        }

        var wanted = new (string layer, float distance)[]
        {
            (TreeLayerName, TreeCullDistance),
            ("PropSmall",   SmallPropCullDistance),
            ("PropFar",     FarPropCullDistance),
        };

        rules.arraySize = wanted.Length;

        for (int i = 0; i < wanted.Length; i++)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(i);
            SerializedProperty name = rule.FindPropertyRelative("layerName");
            SerializedProperty distance = rule.FindPropertyRelative("maxDrawDistance");

            float old = distance.floatValue;

            name.stringValue = wanted[i].layer;
            distance.floatValue = wanted[i].distance;

            log.Add($"Çizim mesafesi '{wanted[i].layer}': {old} → {wanted[i].distance}");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(culler);
    }

    private static void ApplyFog(List<string> log)
    {
        float oldEnd = RenderSettings.fogEndDistance;
        float oldStart = RenderSettings.fogStartDistance;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = FogStartDistance;
        RenderSettings.fogEndDistance = FogEndDistance;

        log.Add($"Sis: {oldStart}-{oldEnd} → {FogStartDistance}-{FogEndDistance}");
    }

    private static void ApplyShadowDistance(List<string> log)
    {
        RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;

        if (pipeline == null)
        {
            log.Add("⚠️ URP asset bulunamadı — gölge mesafesi değiştirilemedi.");
            return;
        }

        SerializedObject so = new SerializedObject(pipeline);
        SerializedProperty shadowDistance = so.FindProperty("m_ShadowDistance");

        if (shadowDistance == null)
        {
            log.Add("⚠️ URP asset'te 'm_ShadowDistance' alanı yok.");
            return;
        }

        float old = shadowDistance.floatValue;
        shadowDistance.floatValue = ShadowDistance;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssetIfDirty(pipeline);

        // Gölge alanı yarıçapın karesiyle ölçekleniyor — kazancı böyle
        // raporluyoruz. (old sıfırsa oran anlamsız, bölme de patlar.)
        string ratio = old > 0.01f
            ? $"  (gölge alanı ~%{Mathf.RoundToInt(100f * ShadowDistance * ShadowDistance / (old * old))}'ine indi)"
            : string.Empty;

        log.Add($"Gölge mesafesi: {old} → {ShadowDistance}{ratio}");
    }
}
