using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// LOBİDEKİ GERÇEK BUTONLARA (Oyun Kur, Hızlı Katıl, Hazırım, Arkadaş
/// Davet Et, Nasıl Oynanır, Geri Bildirim) PERSONA TARZI GÖRÜNÜM VERİYOR —
/// gölge katmanı + eğik yazı + sırayla kayarak giriş + fare üzerine gelince
/// büyüme/flaş. DOTween GEREKMİYOR: sırayla giriş için zaten var olan
/// UISlideIn.cs, hover için yeni PersonaButtonFeel.cs kullanılıyor.
///
/// 🚨 NEDEN SIFIRDAN YENİ BİR CANVAS/BUTON SETİ KURULMUYOR: bir tutorial
/// harfi harfine uygulanıp ayrı bir "ButonGrubu" seti kurulsaydı, gerçek
/// LobbyCanvas'taki (Oyun Kur / Hazırım / Ayarlar vb, gerçekten çalışan)
/// butonlara hiç dokunmadan süslü ama işlevsiz bir dekor menü çıkardı.
/// Bu araç bunun yerine var olan, çalışan butonları mevcut konumlarında
/// bırakıp sadece görünüş/his katmanı ekliyor.
///
/// İKİ KEZ ÇALIŞTIRMAK ZARARSIZ (idempotent): gölge/slide/hover zaten
/// varsa tekrar eklenmiyor, yazı zaten eğikse tekrar sarılmıyor.
///
/// KULLANIM: Offline Scene açıkken üst menüden
/// SaboTour > Ana Menü > Persona Stiline Çevir. Sonucu beğenmezsen
/// Ctrl+Z ile geri al ya da Play modunda dene, kaydetmeden önce gözden geçir.
/// </summary>
public static class PersonaMenuStyler
{
    private static readonly string[] TargetButtonNames =
    {
        "HostButton", "HizliKatilButton", "ReadyButton",
        "InviteButton", "NasilOynanirButton", "GeriBildirimButton"
    };

    private const float ShearAngle = -12f;
    private const float SlideTravelDistance = 140f;
    private const float SlideDuration = 0.45f;
    private const float SlideDelayStep = 0.08f;

    [MenuItem("SaboTour/Ana Menü/Persona Stiline Çevir")]
    private static void ApplyPersonaStyle()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Offline Scene")
        {
            EditorUtility.DisplayDialog("SaboTour",
                "Önce Offline Scene'i aç, sonra tekrar dene.", "Tamam");
            return;
        }

        GameObject lobbyCanvas = GameObject.Find("LobbyCanvas");
        if (lobbyCanvas == null)
        {
            Debug.LogError("[PersonaMenuStyler] LobbyCanvas bulunamadı, sahne değişmiş olabilir.");
            return;
        }

        int styledCount = 0;
        int skippedCount = 0;

        foreach (string name in TargetButtonNames)
        {
            Transform found = FindDeep(lobbyCanvas.transform, name);
            if (found == null)
            {
                Debug.LogWarning("[PersonaMenuStyler] '" + name + "' bulunamadı, atlandı.");
                skippedCount++;
                continue;
            }

            Button button = found.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("[PersonaMenuStyler] '" + name + "' üzerinde Button component yok, atlandı.");
                skippedCount++;
                continue;
            }

            StyleButton(button, styledCount);
            styledCount++;
        }

        EditorUtility.SetDirty(lobbyCanvas);
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("[PersonaMenuStyler] " + styledCount + " buton işlendi, " + skippedCount + " atlandı. " +
                  "Play tuşuna basıp fare ile butonların üzerine gel, büyüme, flaş ve giriş animasyonunu göreceksin. " +
                  "Beğenirsen Ctrl+S, beğenmezsen Ctrl+Z ile geri al.");
    }

    private static void StyleButton(Button button, int orderIndex)
    {
        RectTransform btnRect = button.GetComponent<RectTransform>();
        Vector2 originalAnchoredPos = btnRect.anchoredPosition;

        // 1) Gölge katmanı: butonun kendi sprite'ını koyulaştırarak kopyalıyor,
        // yani hangi renk/şekilde olursa olsun otomatik uyuyor.
        EnsureShadow(button);

        // 2) Yazıyı eğik yap. Bu projedeki TMP sürümünde ayrı bir "Shear"
        // alanı yok (Inspector'da Extra Settings altında görmeyeceksin) —
        // italik açısı zengin metin etiketiyle veriliyor, TMP bunu otomatik
        // italic/shear olarak işliyor.
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.richText = true;
            string trimmed = label.text.TrimStart();
            if (!trimmed.StartsWith("<i"))
            {
                label.text = "<i angle=\"" + ShearAngle.ToString(System.Globalization.CultureInfo.InvariantCulture)
                              + "\">" + label.text + "</i>";
            }
        }

        // 3) Sırayla kayarak giriş: DOTween değil, zaten var olan UISlideIn.cs.
        UISlideIn slide = button.GetComponent<UISlideIn>();
        if (slide == null) slide = button.gameObject.AddComponent<UISlideIn>();

        SerializedObject slideSO = new SerializedObject(slide);
        slideSO.FindProperty("from").enumValueIndex = (int)UISlideIn.Direction.Asagidan;
        slideSO.FindProperty("travelDistance").floatValue = SlideTravelDistance;
        slideSO.FindProperty("delay").floatValue = orderIndex * SlideDelayStep;
        slideSO.FindProperty("duration").floatValue = SlideDuration;
        slideSO.FindProperty("fadeIn").boolValue = true;
        slideSO.ApplyModifiedProperties();

        // UISlideIn Editor'de eklenir eklenmez Awake+OnEnable çalışıp butonu
        // görsel olarak kayma başlangıcına (ekranın dışına) itiyor; Update()
        // Play modunda olmadığı için orada donuk kalırdı. Awake zaten doğru
        // hedefi (targetPosition) yakaladığı için konumu burada görsel olarak
        // geri almak yeterli. Play tuşuna basılınca OnEnable tekrar çalışıp
        // doğru yerden başlayarak düzgün animasyon oynatıyor.
        btnRect.anchoredPosition = originalAnchoredPos;

        // 4) Fare üzerine gelince büyüme + beyaz flaş.
        if (button.GetComponent<PersonaButtonFeel>() == null)
            button.gameObject.AddComponent<PersonaButtonFeel>();

        EditorUtility.SetDirty(button.gameObject);
    }

    private static void EnsureShadow(Button button)
    {
        Transform parent = button.transform.parent;
        string shadowName = button.name + "_PersonaGolge";

        if (parent.Find(shadowName) != null)
            return; // zaten eklenmiş, idempotent

        RectTransform btnRect = button.GetComponent<RectTransform>();
        Image btnImage = button.GetComponent<Image>();

        GameObject shadowObj = new GameObject(shadowName, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(shadowObj, "Persona Gölge Ekle");

        shadowObj.transform.SetParent(parent, false);
        // Butondan hemen önceki sıraya koyuyoruz: Unity UI sıradaki objeleri
        // önce çizip sonrakini üstüne bindiriyor, yani bu gölgeyi butonun
        // arkasında gösteriyor.
        shadowObj.transform.SetSiblingIndex(button.transform.GetSiblingIndex());

        RectTransform shadowRect = shadowObj.GetComponent<RectTransform>();
        shadowRect.anchorMin = btnRect.anchorMin;
        shadowRect.anchorMax = btnRect.anchorMax;
        shadowRect.pivot = btnRect.pivot;
        shadowRect.sizeDelta = btnRect.sizeDelta;
        shadowRect.anchoredPosition = btnRect.anchoredPosition + new Vector2(9f, -9f);

        Image shadowImage = shadowObj.GetComponent<Image>();
        shadowImage.raycastTarget = false; // tıklamayı asla engellemesin

        if (btnImage != null)
        {
            shadowImage.sprite = btnImage.sprite;
            shadowImage.type = btnImage.type;

            Color c = btnImage.color * 0.28f; // kendi rengini koyulaştır
            c.a = Mathf.Max(btnImage.color.a, 0.85f);
            shadowImage.color = c;
        }
        else
        {
            shadowImage.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDeep(root.GetChild(i), name);
            if (result != null) return result;
        }

        return null;
    }
}
