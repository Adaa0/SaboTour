using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TrackPropScatter'ın Inspector'ına Play modu gerektirmeyen prop üretme
/// butonları ekler. Böylece ağaç/kaya yoğunluğunu ayarlarken Play'e basıp
/// çıkmak yerine, ayarı değiştirip butona basıp sonucu ANINDA Scene view'da
/// görebilirsin.
///
/// Bu dosya "Editor" klasöründe olduğu için oyun build'ine dahil edilmez.
/// </summary>
[CustomEditor(typeof(TrackPropScatter))]
public class TrackPropScatterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TrackPropScatter scatter = (TrackPropScatter)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Editör Araçları (Play modu gerekmez)", EditorStyles.boldLabel);

        // Pist üretilmemişse propların yerleşeceği bir yol yok — kullanıcı
        // butona basıp "hiçbir şey olmadı" diye şaşırmasın diye önden uyarıyoruz.
        if (!HasTrack(scatter))
        {
            EditorGUILayout.HelpBox(
                "Önce pist üretilmeli. TrackGenerator'ı seçip 'Rastgele Pist Üret' " +
                "butonuna bas, sonra buraya dön.",
                MessageType.Warning);
        }

        if (GUILayout.Button("Propları Üret / Yenile"))
        {
            scatter.Scatter();
            MarkDirty(scatter);
        }

        if (GUILayout.Button("Farklı Dizilim Dene (pist aynı kalır)"))
        {
            // Prop dizilimi "pist seed + offset"ten türetiliyor. Offset'i
            // artırmak pisti hiç bozmadan ormanı baştan diziyor.
            SerializedProperty offset = serializedObject.FindProperty("propSeedOffset");
            if (offset != null)
            {
                offset.intValue++;
                serializedObject.ApplyModifiedProperties();
            }

            scatter.Scatter();
            MarkDirty(scatter);
        }

        EditorGUILayout.Space(6);

        if (GUILayout.Button("Propları Temizle"))
        {
            scatter.ClearAllProps();
            MarkDirty(scatter);
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Performans", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "GPU Instancing, aynı modelden yüzlerce kopyayı TEK çizim çağrısında " +
            "çizer — binlerce ağaç için doğru araç budur.\n\n" +
            "ŞARTI: URP Asset'te SRP Batcher KAPALI olmalı ve bu scatter'da " +
            "'Use Static Batching' KAPALI olmalı. Üçü aynı anda çalışmıyor, " +
            "biri diğerlerini devre dışı bırakıyor.",
            MessageType.Info);

        if (GUILayout.Button("Prop Materyallerinde GPU Instancing'i Aç"))
            EnableInstancingOnPropMaterials();
    }

    /// <summary>
    /// propPrefabs listesindeki her prefabın kullandığı TÜM materyallerde
    /// "Enable GPU Instancing" kutusunu işaretler.
    ///
    /// Elle yapılabilir ama her materyali tek tek bulup tıklamak gerekiyor;
    /// 10-15 farklı ağaç/kaya modeli varsa sıkıcı ve biri atlanırsa o model
    /// instancing'den faydalanamıyor.
    /// </summary>
    private void EnableInstancingOnPropMaterials()
    {
        SerializedProperty prefabs = serializedObject.FindProperty("propPrefabs");
        if (prefabs == null || prefabs.arraySize == 0)
        {
            Debug.LogWarning("[TrackPropScatter] propPrefabs listesi boş.");
            return;
        }

        // HashSet: aynı materyal birden fazla prefabda kullanılıyor olabilir,
        // iki kez işlemeye gerek yok.
        var processed = new System.Collections.Generic.HashSet<Material>();
        int changed = 0;

        for (int i = 0; i < prefabs.arraySize; i++)
        {
            GameObject prefab = prefabs.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (prefab == null) continue;

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    if (!processed.Add(material)) continue;
                    if (material.enableInstancing) continue;

                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                    changed++;
                }
            }
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[TrackPropScatter] {processed.Count} materyal tarandı, " +
                  $"{changed} tanesinde GPU Instancing açıldı.");
    }

    /// <summary>
    /// Sahnede üretilmiş bir pist var mı? TrackGenerator'ın nokta listesi
    /// doluysa pist üretilmiş demektir.
    /// </summary>
    private static bool HasTrack(TrackPropScatter scatter)
    {
        TrackGenerator generator = scatter.GetComponent<TrackGenerator>();
        if (generator == null)
            generator = Object.FindAnyObjectByType<TrackGenerator>();

        if (generator == null) return false;

        var points = generator.GetTrackPoints();
        return points != null && points.Count > 2;
    }

    /// <summary>
    /// Unity'ye "bu sahnede kaydedilmemiş değişiklik var" der — bu olmadan
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
