using UnityEngine;
using UnityEngine.UI;

// Videodaki yıldız: "iki dikdörtgen çaprazlanıyor, 90 derece dönerken
// küçülüp kayboluyor." Tam olarak o — artı (+) şeklinde iki ince dikdörtgen.
//
// Prefab GEREKMİYOR: çağrıldığı anda kendi objelerini kurup, işi bitince
// kendini yok ediyor (projedeki "prefab yoksa kendini kur" deseni).
public class PersonaStarBurst : MonoBehaviour
{
    RectTransform rt;
    CanvasGroup group;
    float life;
    float age;
    float spinDegrees;

    /// <summary>Verilen UI ebeveyninin içinde, verilen konumda bir yıldız patlatır.</summary>
    public static void Spawn(RectTransform parent, Vector2 anchoredPos, Color color,
                             float size = 110f, float thickness = 16f,
                             float duration = 0.32f, float spin = 90f)
    {
        if (parent == null || !Application.isPlaying) return;

        var go = new GameObject("PersonaStar", typeof(RectTransform), typeof(CanvasGroup));
        var starRt = (RectTransform)go.transform;
        starRt.SetParent(parent, false);
        starRt.anchorMin = starRt.anchorMax = new Vector2(0.5f, 0.5f);
        starRt.pivot = new Vector2(0.5f, 0.5f);
        starRt.anchoredPosition = anchoredPos;
        starRt.sizeDelta = new Vector2(size, size);
        starRt.SetAsLastSibling();

        MakeBar(starRt, color, size, thickness, 0f);
        MakeBar(starRt, color, size, thickness, 90f);

        var star = go.AddComponent<PersonaStarBurst>();
        star.rt = starRt;
        star.group = go.GetComponent<CanvasGroup>();
        star.group.blocksRaycasts = false;
        star.group.interactable = false;
        star.life = Mathf.Max(0.05f, duration);
        star.spinDegrees = spin;
    }

    static void MakeBar(RectTransform parent, Color color, float size, float thickness, float angle)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        var barRt = (RectTransform)go.transform;
        barRt.SetParent(parent, false);
        barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0.5f);
        barRt.pivot = new Vector2(0.5f, 0.5f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(size, thickness);
        barRt.localRotation = Quaternion.Euler(0f, 0f, angle);

        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;   // ŞART: yoksa yıldız butonun tıklamasını yer
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(age / life);

        // Hızlı büyüyüp hızlanarak küçülüyor (k*k), dönerken saydamlaşıyor.
        rt.localScale = Vector3.one * Mathf.Lerp(1.25f, 0f, k * k);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, spinDegrees, k));
        group.alpha = 1f - k;

        if (k >= 1f) Destroy(gameObject);
    }
}
