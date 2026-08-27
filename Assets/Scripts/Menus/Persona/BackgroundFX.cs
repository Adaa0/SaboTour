using UnityEngine;
using UnityEngine.UI;

// Videodaki "düz arka plan yeterli değil" adımı: arkaya kayan şeritler ve
// parlayıp sönen elmas parıltılar. Menü bir anda "yaşıyor" gibi duruyor.
//
// Bu aynı zamanda dosyada kayıtlı gerçek bir şikayeti de kapatıyor:
// ana menü şu an DÜZ MAVİ bir renk (Main Camera, Solid Color).
//
// ⚠️ Objeler ÇALIŞMA ANINDA üretiliyor — sahnede 30+ obje birikmesin diye.
// Yani efekti sadece PLAY MODE'da görürsün, Scene view'da görünmez.
// Renk/hız/sayı ayarları Play sırasında Inspector'dan canlı denenebilir.
[DisallowMultipleComponent]
public class PersonaBackgroundFX : MonoBehaviour
{
    [Header("Zemin")]
    [Tooltip("Kapatırsan arkadaki 3B sahne (kule/ağaçlar) görünür kalır.")]
    public bool drawSolidBackground = true;
    public Color backgroundColor = new Color32(0x0B, 0x0B, 0x12, 0xFF);

    [Header("Şeritler")]
    public int stripeCount = 14;
    [Tooltip("Alfası DÜŞÜK olmalı. Yüksek verirsen şeritler arka plan olmaktan çıkıp öne fırlar.")]
    public Color stripeColor = new Color32(0xD8, 0x1E, 0x2C, 0x1A);
    [Tooltip("Şeritlerin eğim açısı. Butonlarla aynı yöne verirsen daha derli toplu durur.")]
    public float stripeAngle = -12f;
    public float stripeWidthMin = 20f;
    public float stripeWidthMax = 90f;
    [Tooltip("Kayma hızı (piksel/saniye). Her şerit kendi hızında akıyor.")]
    public float stripeSpeedMin = 18f;
    public float stripeSpeedMax = 70f;

    [Header("Parıltılar (elmas)")]
    public int glintCount = 14;
    public Color glintColor = new Color(1f, 1f, 1f, 0.55f);
    public float glintSizeMin = 8f;
    public float glintSizeMax = 26f;
    [Tooltip("Bir parıltının yanıp sönme döngüsü (saniye).")]
    public float glintCycleMin = 1.2f;
    public float glintCycleMax = 3.5f;

    [Header("Rastgelelik")]
    [Tooltip("Aynı sayı = her açılışta aynı dizilim.")]
    public int seed = 1337;

    const float Width = 1920f;
    const float Height = 1080f;

    RectTransform container;
    RectTransform[] stripes;
    float[] stripeSpeeds;
    Image[] glints;
    float[] glintCycles;
    float[] glintPhases;

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        Build();
    }

    void OnDisable()
    {
        if (container != null) Destroy(container.gameObject);
        container = null;
    }

    void Build()
    {
        if (container != null) Destroy(container.gameObject);

        var rnd = new System.Random(seed);

        var go = new GameObject("FX", typeof(RectTransform));
        container = (RectTransform)go.transform;
        container.SetParent(transform, false);
        Stretch(container);

        // 1) Düz zemin
        if (drawSolidBackground)
        {
            var bg = NewImage(container, "Zemin", backgroundColor);
            Stretch(bg.rectTransform);
        }

        // 2) Kayan şeritler
        stripes = new RectTransform[Mathf.Max(0, stripeCount)];
        stripeSpeeds = new float[stripes.Length];
        for (int i = 0; i < stripes.Length; i++)
        {
            var img = NewImage(container, "Serit" + i, stripeColor);
            var srt = img.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);

            float w = Mathf.Lerp(stripeWidthMin, stripeWidthMax, (float)rnd.NextDouble());
            // Eğik durduğu için ekrandan taşacak kadar uzun olmalı.
            srt.sizeDelta = new Vector2(w, Height * 2f);
            srt.localRotation = Quaternion.Euler(0f, 0f, stripeAngle);

            // 🚨 KONUM EŞİT ARALIKLI + HAFİF SAPMA — tamamen rastgele DEĞİL.
            // İlk versiyonda X tamamen rastgeleydi; şeritler kümeleniyor,
            // üst üste binen saydam katmanlar birikip çamurlu turuncu lekeler
            // yapıyordu. Eşit aralık kümelenmeyi engelliyor, küçük sapma da
            // "cetvelle çizilmiş" görünmesini engelliyor.
            float slot = (Width * 2f) / stripes.Length;
            float jitter = ((float)rnd.NextDouble() - 0.5f) * slot * 0.55f;
            srt.anchoredPosition = new Vector2(-Width + slot * (i + 0.5f) + jitter, 0f);

            stripes[i] = srt;
            stripeSpeeds[i] = Mathf.Lerp(stripeSpeedMin, stripeSpeedMax, (float)rnd.NextDouble());
        }

        // 3) Elmas parıltılar
        glints = new Image[Mathf.Max(0, glintCount)];
        glintCycles = new float[glints.Length];
        glintPhases = new float[glints.Length];
        for (int i = 0; i < glints.Length; i++)
        {
            var img = NewImage(container, "Pariltı" + i, glintColor);
            var grt = img.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);

            float s = Mathf.Lerp(glintSizeMin, glintSizeMax, (float)rnd.NextDouble());
            grt.sizeDelta = new Vector2(s, s);
            grt.localRotation = Quaternion.Euler(0f, 0f, 45f);   // kareyi elmasa çevirir
            grt.anchoredPosition = new Vector2(
                Mathf.Lerp(-Width * 0.5f, Width * 0.5f, (float)rnd.NextDouble()),
                Mathf.Lerp(-Height * 0.5f, Height * 0.5f, (float)rnd.NextDouble()));

            glints[i] = img;
            glintCycles[i] = Mathf.Lerp(glintCycleMin, glintCycleMax, (float)rnd.NextDouble());
            glintPhases[i] = (float)rnd.NextDouble() * 10f;
        }
    }

    static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;   // 🚨 ŞART: arka plan butonların tıklamasını yemesin
        return img;
    }

    static void Stretch(RectTransform t)
    {
        t.anchorMin = Vector2.zero;
        t.anchorMax = Vector2.one;
        t.offsetMin = Vector2.zero;
        t.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (container == null) return;
        float time = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;

        // Şeritler sağa akıyor, ekranın sağından çıkınca soldan geri giriyor.
        if (stripes != null)
        {
            for (int i = 0; i < stripes.Length; i++)
            {
                var srt = stripes[i];
                if (srt == null) continue;
                var p = srt.anchoredPosition;
                p.x += stripeSpeeds[i] * dt;
                if (p.x > Width) p.x -= Width * 2f;
                srt.anchoredPosition = p;
            }
        }

        // Parıltılar kendi döngülerinde yanıp sönüyor.
        if (glints != null)
        {
            for (int i = 0; i < glints.Length; i++)
            {
                var img = glints[i];
                if (img == null) continue;
                float k = Mathf.PingPong(time / Mathf.Max(0.05f, glintCycles[i]) + glintPhases[i], 1f);
                var c = glintColor;
                c.a *= k * k;   // karesi: çoğu zaman sönük, kısa bir an parlıyor
                img.color = c;
            }
        }
    }

    [ContextMenu("Yeniden Kur (Play modunda)")]
    void Rebuild()
    {
        if (Application.isPlaying) Build();
    }
}
