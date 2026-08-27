using UnityEngine;

// Videodaki son madde: "kimsenin fark etmediği ama herkesin hissettiği
// detaylar" — çok hafif, sürekli bir kayma.
//
// 🚨 VİDEODAKİ GİBİ KAMERAYA DEĞİL, UI'A UYGULANIYOR. Sebep: bu menü
// Screen Space OVERLAY bir Canvas'ta duruyor ve overlay canvas'lar kameradan
// TAMAMEN BAĞIMSIZ çiziliyor — kamerayı oynatmak menüde hiçbir şeyi
// kıpırdatmazdı. Aynı hissi veren doğru yer UI'ın kendisi.
[RequireComponent(typeof(RectTransform))]
public class PersonaUIDrift : MonoBehaviour
{
    [Tooltip("Kayma mesafesi (piksel). 12'nin üstü fark edilir hale gelir, amaç fark EDİLMEMESİ.")]
    public float positionAmount = 9f;

    [Tooltip("Kayma dönüşü (derece).")]
    public float rotationAmount = 0.35f;

    [Tooltip("Kayma hızı. Küçük = daha ağır, daha sakin.")]
    public float speed = 0.12f;

    RectTransform rt;
    Vector2 basePos;
    float baseRotZ;
    float seed;

    void Awake()
    {
        rt = (RectTransform)transform;
        basePos = rt.anchoredPosition;
        baseRotZ = rt.localEulerAngles.z;
        seed = Random.value * 100f;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying) return;

        float t = Time.unscaledTime * speed;

        // Perlin gürültüsü: rastgele ama YUMUŞAK. Random.value kullanılsaydı
        // her karede zıplardı; Perlin komşu değerleri birbirine yakın verir.
        float x = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f;
        float y = (Mathf.PerlinNoise(seed + 31.7f, t) - 0.5f) * 2f;
        float r = (Mathf.PerlinNoise(seed + 63.1f, t) - 0.5f) * 2f;

        rt.anchoredPosition = basePos + new Vector2(x, y) * positionAmount;
        rt.localRotation = Quaternion.Euler(0f, 0f, baseRotZ + r * rotationAmount);
    }
}
