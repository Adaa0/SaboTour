using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// UI ÇÖZÜNÜRLÜK DÜZELTİCİSİ (Editor aracı)
///
/// ─── ÇÖZDÜĞÜ SORUN ────────────────────────────────────────────────────
/// "1080p'de güzel duruyor ama 2K'da ortaya kayık." Sebebi tek bir ayardı:
/// bazı Canvas'lar **Constant Pixel Size** modunda kalmıştı.
///
/// O modda UI ekran büyüdükçe piksel olarak AYNI kalıyor. İki sonucu var:
///   • Yazılar/butonlar fiziksel olarak küçülüyor (2K'da 1080p'nin ~%75'i).
///   • Ekranın MERKEZİNE sabitlenip büyük bir piksel kaydırmasıyla
///     yerleştirilen her şey ortaya doğru sürükleniyor. Örnek: sıralama
///     tablosu merkeze sabitli ve (-800, +400) kaydırılmıştı →
///     1080p'de sol kenardan %8 içeride, 1440p'de %19 içeride.
///
/// Doğrusu **Scale With Screen Size** + 1920×1080 referans: "1080p'de nasıl
/// tasarladıysam her çözünürlükte öyle görünsün".
///
/// ─── NE YAPIYOR ───────────────────────────────────────────────────────
///  1. `Assets/Resources/UI/` altındaki TÜM prefabların Canvas'larını düzeltir.
///  2. AÇIK olan sahnelerdeki Canvas'ları düzeltir (kaydetmeyi sana bırakır).
///  3. Online Scene'i ek olarak (additive) açıp düzeltir ve kaydeder —
///     açık sahneni HİÇ bozmadan.
///  4. Sıralama tablosu artık RaceHud prefabında yaşadığı için, sahnede
///     kalmış eski `LeaderboardText` kopyasını temizler (yalnızca prefabta
///     bir tane VARSA — tek kopyayı asla silmez).
///
/// 🚨 `Run(bool interactive)`: `false` ile hiç dialog açmıyor. Modal pencere
/// otomasyondan çağrılınca Unity'yi kilitliyor (bu projede yaşandı).
/// </summary>
public static class UiResolutionFixer
{
    private const string UiFolder = "Assets/Resources/UI";
    private const string OnlineScenePath = "Assets/Scenes/Mirror/Online Scene.unity";
    private const string LeaderboardName = "LeaderboardText";

    [MenuItem("SaboTour/UI Prefabları/Çözünürlük Ayarlarını Düzelt")]
    public static void Run() => Run(true);

    public static void Run(bool interactive)
    {
        var report = new StringBuilder();
        int fixedCount = 0;

        fixedCount += FixPrefabs(report);
        fixedCount += FixOpenScenes(report);
        fixedCount += FixOnlineScene(report);

        string header = fixedCount == 0
            ? "UI çözünürlük ayarları zaten doğruydu — değişiklik yok."
            : $"{fixedCount} yerde ayar düzeltildi.";

        Debug.Log($"[UiResolutionFixer] {header}\n{report}");

        if (interactive)
        {
            EditorUtility.DisplayDialog("UI Çözünürlük Ayarları",
                header + "\n\nDetaylar Console'da.\n\n" +
                "AÇIK sahnede yapılan değişiklikler için Ctrl+S ile kaydet.",
                "Tamam");
        }
    }

    /// <summary>
    /// Tek tuşla her şeyi kurar: eksik UI prefablarını üretir, sonra
    /// çözünürlük ayarlarını düzeltir. Prefablar zaten varsa üzerine
    /// YAZMAZ (elle yaptığın düzenlemeler korunur).
    /// </summary>
    [MenuItem("SaboTour/UI Prefabları/Eksik UI Prefablarını Kur + Düzelt")]
    public static void SetupAll()
    {
        if (!System.IO.File.Exists(UiFolder + "/SaboteurHud.prefab"))
            SaboteurHudPrefabBuilder.Build(false);

        if (!System.IO.File.Exists(UiFolder + "/ScreenNotice.prefab"))
            ScreenNoticePrefabBuilder.Build(false);

        // Sıralama tablosu MEVCUT RaceHud prefabına ekleniyor — prefabı
        // sıfırdan üretmek elle yapılmış renk/konum ayarlarını silerdi.
        RaceHudPrefabBuilder.EnsureLeaderboard();

        Run(false);

        EditorUtility.DisplayDialog("UI Kurulumu",
            "Eksik UI prefabları üretildi ve çözünürlük ayarları düzeltildi.\n\n" +
            "Detaylar Console'da. AÇIK sahnede değişiklik olduysa Ctrl+S ile kaydet.",
            "Tamam");
    }

    // ─── 1. Prefablar ───────────────────────────────────────────────────

    private static int FixPrefabs(StringBuilder report)
    {
        int count = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // 🚨 `LoadPrefabContents` + `SaveAsPrefabAsset` kullanılıyor,
            // `EditPrefabContentsScope` DEĞİL: bu projede o yöntemle yapılan
            // bir prefab değişikliği sessizce KAYBOLMUŞTU (CLAUDE.md'de
            // kayıtlı). Bu yol sonucu diske yazdığından emin oluyor.
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;

            foreach (CanvasScaler scaler in contents.GetComponentsInChildren<CanvasScaler>(true))
            {
                if (UiPrefabUtil.ApplyStandardScaler(scaler))
                {
                    changed = true;
                    report.AppendLine($"  • {System.IO.Path.GetFileName(path)} → Canvas ölçekleme düzeltildi");
                }
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                count++;
            }

            PrefabUtility.UnloadPrefabContents(contents);
        }

        if (count > 0) AssetDatabase.SaveAssets();
        return count;
    }

    // ─── 2. Açık sahneler ───────────────────────────────────────────────

    private static int FixOpenScenes(StringBuilder report)
    {
        int count = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            if (FixScene(scene, report, out bool changed) && changed)
            {
                // Kaydetmeyi BİLEREK yapmıyoruz: geliştiricinin o sahnede
                // kaydedilmemiş başka değişiklikleri olabilir, ne zaman
                // kaydedeceğine kendisi karar versin.
                EditorSceneManager.MarkSceneDirty(scene);
                report.AppendLine($"  • '{scene.name}' (AÇIK) düzeltildi — kaydetmek için Ctrl+S");
                count++;
            }
        }

        return count;
    }

    // ─── 3. Online Scene (kapalıysa) ────────────────────────────────────

    private static int FixOnlineScene(StringBuilder report)
    {
        // Zaten açıksa yukarıdaki adım hallediyor.
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).path == OnlineScenePath) return 0;

        if (!System.IO.File.Exists(OnlineScenePath))
        {
            report.AppendLine($"  • {OnlineScenePath} bulunamadı, atlandı.");
            return 0;
        }

        // 🚨 ADDITIVE AÇIYORUZ: `OpenSceneMode.Single` açık sahneyi kapatır
        // ve kaydedilmemiş değişiklikler için modal bir pencere açabilir.
        // Additive, geliştiricinin üzerinde çalıştığı sahneye hiç dokunmuyor.
        Scene scene = EditorSceneManager.OpenScene(OnlineScenePath, OpenSceneMode.Additive);

        FixScene(scene, report, out bool changed);

        if (changed)
        {
            EditorSceneManager.SaveScene(scene);
            report.AppendLine("  • 'Online Scene' düzeltildi ve kaydedildi.");
        }

        EditorSceneManager.CloseScene(scene, true);
        return changed ? 1 : 0;
    }

    // ─── Ortak sahne işlemi ─────────────────────────────────────────────

    private static bool FixScene(Scene scene, StringBuilder report, out bool changed)
    {
        changed = false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (CanvasScaler scaler in root.GetComponentsInChildren<CanvasScaler>(true))
            {
                if (UiPrefabUtil.ApplyStandardScaler(scaler))
                {
                    EditorUtility.SetDirty(scaler);
                    changed = true;
                    report.AppendLine($"      - '{scaler.gameObject.name}' Canvas ölçekleme → Scale With Screen Size 1920×1080");
                }
            }

            if (RemoveStrayLeaderboards(root, report)) changed = true;
        }

        return true;
    }

    /// <summary>
    /// Sahnede kalmış eski sıralama tablosu kutusunu siler.
    ///
    /// GÜVENLİK: yalnızca RaceHud prefabında bir `LeaderboardText` VARSA
    /// siliyor. Prefab henüz üretilmemişse tabloyu tamamen yok etmiş
    /// oluruz — o yüzden önce prefabı kontrol ediyor.
    /// </summary>
    private static bool RemoveStrayLeaderboards(GameObject root, StringBuilder report)
    {
        if (!PrefabHasLeaderboard()) return false;

        var doomed = new List<GameObject>();

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            if (text.gameObject.name == LeaderboardName)
                doomed.Add(text.gameObject);

        foreach (GameObject go in doomed)
        {
            report.AppendLine($"      - Sahnedeki eski '{LeaderboardName}' silindi " +
                              "(artık RaceHud prefabının içinde)");
            Object.DestroyImmediate(go);
        }

        return doomed.Count > 0;
    }

    private static bool PrefabHasLeaderboard()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "/RaceHud.prefab");
        if (prefab == null) return false;

        foreach (TMP_Text text in prefab.GetComponentsInChildren<TMP_Text>(true))
            if (text.gameObject.name == LeaderboardName) return true;

        return false;
    }
}
