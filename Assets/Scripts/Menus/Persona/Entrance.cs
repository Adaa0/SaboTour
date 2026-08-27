using UnityEngine;

// Videodaki "menü VAR OLMAMALI, GELMELİ" fikri: öğeler aynı anda değil,
// birbiri ardına milisaniyelerle kayarak ve hedefi hafifçe AŞARAK (overshoot)
// giriyor.
//
// 🚨 HEPSİNE AYNI DELAY VERİLİRSE ETKİ TAMAMEN KAYBOLUR. Kademeli gecikme
// (0.00 / 0.06 / 0.12 ...) işin tamamı.
[RequireComponent(typeof(RectTransform))]
public class PersonaEntrance : MonoBehaviour
{
    [Tooltip("Bu öğe kaç saniye sonra girmeye başlasın. Sırayla artır: 0.00 / 0.06 / 0.12 ...")]
    public float delay = 0f;

    [Tooltip("Nereden gelsin (piksel). (-260, 0) = soldan kayarak.")]
    public Vector2 fromOffset = new Vector2(-260f, 0f);

    [Tooltip("Giriş süresi (saniye).")]
    public float duration = 0.42f;

    [Tooltip("Girerken saydamlıktan da gelsin mi (CanvasGroup gerekiyorsa otomatik ekleniyor).")]
    public bool fadeIn = true;

    [Tooltip("Giriş eğrisi. Ortada 1'i AŞIP geri dönmesi 'overshoot' hissini veriyor.")]
    public AnimationCurve curve = DefaultCurve();

    RectTransform rt;
    CanvasGroup group;
    Vector2 target;
    float age;
    bool running;

    static AnimationCurve DefaultCurve()
    {
        // 0 -> 1.10 -> 1 : hedefi aşıp geri oturuyor
        return new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 3.2f),
            new Keyframe(0.62f, 1.10f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));
    }

    void Awake()
    {
        rt = (RectTransform)transform;
        target = rt.anchoredPosition;   // yazarın verdiği ASIL konum
    }

    void OnEnable()
    {
        // 🚨 Editor'de (Play dışında) HİÇBİR ŞEY yapma. Önceki denemede
        // component eklenir eklenmez Awake+OnEnable çalışıp butonu ekranın
        // dışına itiyor ve Update olmadığı için orada donuyordu.
        if (!Application.isPlaying) return;

        age = 0f;
        running = true;

        if (fadeIn)
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
        }

        rt.anchoredPosition = target + fromOffset;
    }

    void Update()
    {
        if (!running) return;

        age += Time.unscaledDeltaTime;
        float t = age - delay;
        if (t < 0f) return;

        float k = duration <= 0f ? 1f : Mathf.Clamp01(t / duration);
        float e = curve.Evaluate(k);

        // LerpUnclamped: eğri 1'i aştığında hedefi GERÇEKTEN aşabilsin diye.
        rt.anchoredPosition = Vector2.LerpUnclamped(target + fromOffset, target, e);
        if (group != null) group.alpha = Mathf.Clamp01(k * 1.6f);

        if (k >= 1f)
        {
            rt.anchoredPosition = target;
            if (group != null) group.alpha = 1f;
            running = false;
        }
    }
}
