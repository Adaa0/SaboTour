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
