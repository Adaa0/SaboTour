using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TrackGenerator'ın Inspector'ına "Play modu gerekmeden" pist üretme
/// butonları ekler. ASIL AMAÇ: fotoğraf/capsule çekimi için ayrı bir sahne
/// kurabilmek — pist prosedürel üretildiği için normalde sadece Play modunda
/// var oluyor ve her seferinde değişiyor, bu yüzden sahneyi elle dekore etmek
/// (ağaç yerleştirmek, ışık ayarlamak, kamera kurmak) imkânsızdı.
///
/// Bu dosya "Editor" klasöründe olduğu için Unity onu OYUN BUILD'İNE HİÇ
/// DAHİL ETMEZ — yayın öncesi silmen gerekmez, kendiliğinden dışarıda kalır.
///
/// KULLANIM:
///  1. Sahnede TrackGenerator'ı olan objeyi seç.
///  2. Inspector'ın en altındaki "Editör Araçları" bölümünü kullan.
///  3. Beğendiğin pisti bulunca SEED'İ BİR YERE NOT ET — aynı seed her zaman
///     aynı pisti üretir, yani pisti kaybetsen bile geri getirebilirsin.
///  4. "Mesh'leri Asset Olarak Kaydet"e bas, sonra Ctrl+S ile sahneyi kaydet.
/// </summary>
[CustomEditor(typeof(TrackGenerator))]
public class TrackGeneratorEditor : Editor
{
    /// <summary>Üretilen mesh'lerin asset olarak kaydedileceği klasör.</summary>
    private const string BakeFolder = "Assets/GeneratedTracks";

    private int customSeed;

    public override void OnInspectorGUI()
    {
        // Önce normal Inspector alanlarını çiz, sonra butonları ekle.
        DrawDefaultInspector();

        TrackGenerator generator = (TrackGenerator)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Editör Araçları (Play modu gerekmez)", EditorStyles.boldLabel);

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Play modundasın. Bu butonlar Play modunda da çalışır ama yaptığın " +
                "değişiklikler Play'den çıkınca KAYBOLUR. Fotoğraf sahnesi kurarken " +
                "Play modundan çık.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "Beğendiğin pisti bulunca aşağıdaki Seed değerini not et — aynı seed " +
            "her zaman aynı pisti üretir.",
            MessageType.Info);

        if (GUILayout.Button("Rastgele Pist Üret"))
        {
            generator.GenerateTrack();
            customSeed = generator.seed;
            MarkDirty(generator);
        }

        EditorGUILayout.BeginHorizontal();
        customSeed = EditorGUILayout.IntField("Seed", customSeed);
        if (GUILayout.Button("Bu Seed ile Üret", GUILayout.Width(150)))
        {
            generator.GenerateTrackWithSeed(customSeed);
            MarkDirty(generator);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Şu Anki Seed'i Kutuya Al"))
            customSeed = generator.seed;

        EditorGUILayout.Space(6);

        if (GUILayout.Button("Propları Serpiştir (ağaç / kaya)"))
            ScatterProps(generator);

        EditorGUILayout.Space(6);

        if (GUILayout.Button("Mesh'leri Asset Olarak Kaydet (fotoğraf sahnesi için)"))
            BakeMeshes(generator);

        EditorGUILayout.Space(6);

        if (GUILayout.Button("Pisti Temizle"))
        {
            generator.ClearTrack();
            MarkDirty(generator);
        }
    }

    /// <summary>
    /// TrackPropScatter'ı bulup Scatter() çağırır. Scatter() artık kendi
    /// TrackGenerator referansını çözebiliyor, o yüzden editörden çağrılabilir.
    /// </summary>
    private static void ScatterProps(TrackGenerator generator)
    {
        TrackPropScatter scatter = generator.GetComponent<TrackPropScatter>();
        if (scatter == null)
            scatter = Object.FindAnyObjectByType<TrackPropScatter>();

        if (scatter == null)
        {
            Debug.LogWarning(
                "[TrackGeneratorEditor] Sahnede TrackPropScatter yok. " +
                "TrackGenerator'ın olduğu objeye TrackPropScatter component'i ekle " +
                "ve propPrefabs listesine ağaç/kaya modellerini sürükle.");
            return;
        }

        scatter.Scatter();
        MarkDirty(scatter);
    }

    /// <summary>
    /// Üretilen yol/kenarlık mesh'lerini gerçek .asset dosyalarına çevirir.
    ///
    /// NEDEN GEREKLİ: Kod içinde "new Mesh()" ile üretilen bir mesh diske ait
    /// değildir. Sahneyi kaydedip Unity'yi kapatıp açtığında bu mesh'in
    /// kaybolma riski var (yol ve kenarlık görünmez olur). Bu buton mesh'in
    /// kalıcı bir kopyasını Assets/GeneratedTracks altına yazıp MeshFilter ile
    /// MeshCollider'ı o kalıcı kopyaya bağlıyor — böylece pist artık sahnenin
    /// sabit bir parçası, tıpkı elle modellenmiş gibi.
    /// </summary>
    private static void BakeMeshes(TrackGenerator generator)
    {
        if (!Directory.Exists(BakeFolder))
        {
            Directory.CreateDirectory(BakeFolder);
            AssetDatabase.Refresh();
        }

        string sceneName = generator.gameObject.scene.name;
        if (string.IsNullOrEmpty(sceneName)) sceneName = "Scene";

        int baked = 0;

        foreach (MeshFilter filter in generator.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            // Zaten diske kayıtlıysa (ör. bu butona ikinci kez basıldıysa) atla.
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh))) continue;

            Mesh copy = Object.Instantiate(mesh);
            copy.name = $"{sceneName}_{generator.seed}_{filter.gameObject.name}";

            string path = AssetDatabase.GenerateUniqueAssetPath($"{BakeFolder}/{copy.name}.asset");
            AssetDatabase.CreateAsset(copy, path);

            filter.sharedMesh = copy;

            // Yolun MeshCollider'ı da aynı mesh'i kullanıyor — o da bağlanmalı,
            // yoksa arabalar görünmeyen eski mesh üzerinde sürer.
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMesh = copy;

            baked++;
        }

        AssetDatabase.SaveAssets();
        MarkDirty(generator);

        if (baked > 0)
            Debug.Log($"[TrackGeneratorEditor] {baked} mesh '{BakeFolder}' altına kaydedildi. " +
                      "Şimdi Ctrl+S ile sahneyi de kaydet.");
        else
            Debug.Log("[TrackGeneratorEditor] Kaydedilecek yeni mesh bulunamadı " +
                      "(hepsi zaten asset olabilir ya da pist henüz üretilmemiş olabilir).");
    }

    /// <summary>
    /// Unity'ye "bu sahnede kaydedilmemiş değişiklik var" der. Bu olmadan
    /// Ctrl+S bazen hiçbir şey kaydetmez ve yaptığın iş kaybolur.
    /// </summary>
    private static void MarkDirty(Component component)
    {
        if (component == null) return;

        EditorUtility.SetDirty(component);

        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
    }
}
