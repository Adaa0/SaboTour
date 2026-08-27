using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Videodaki sayfa geçişi: eğik bir dikdörtgen ekranı süpürüyor, arkasından
// saniyenin onda biri geriden SİYAH bir tanesi kovalıyor; ekran kapalıyken
// sayfa değiştiriliyor.
//
// KULLANIMI (tek satır):
//     PersonaPageSweep.Sweep(() => { /* burada paneli değiştir */ });
//
// Ekran tam kapandığı anda geri çağrı (callback) tetikleniyor — yani panel
// değişimini kimse görmüyor.
//
// ⚠️ Bu dosya hazır duruyor ama menüdeki panel geçişlerine BAĞLANMADI.
// Bağlamak PauseMenuController/MainMenuButtons'ın ÇALIŞAN akışına dokunmak
// demek; onu sen isteyince, gözünle görüp karar verdikten sonra yaparız.
public class PersonaPageSweep : MonoBehaviour
{
    // Süpürme renkleri STATIC: geçişler hem ana menüden hem yarışın
    // ortasındaki ESC menüsünden tetikleniyor, yani rengi tutan bir sahne
    // objesine bağlanamaz (LobbyCanvas yarışta yok). PersonaMenuStyle
    // açılışta buraya kendi paletini yazıyor.
    public static Color DefaultLeadColor = new Color32(0xD5, 0x73, 0x0B, 0xFF);
    public static Color DefaultTrailColor = new Color32(0x0E, 0x12, 0x24, 0xFF);

    static PersonaPageSweep instance;

    RectTransform lead;
    RectTransform trail;
    Canvas canvas;
    bool busy;

    /// <summary>Ekranı süpürerek kapatır, kapalıyken onCovered'ı çağırır, sonra açar.</summary>
    public static void Sweep(System.Action onCovered = null,
                             float duration = 0.55f,
                             float chaseDelay = 0.1f,
                             float tiltDegrees = -12f,
                             Color? leadColor = null,
                             Color? trailColor = null)
    {
        if (!Application.isPlaying)
        {
            onCovered?.Invoke();
            return;
        }

        EnsureInstance();
        if (instance == null) { onCovered?.Invoke(); return; }

        if (instance.busy)
        {
            // Süpürme sürerken ikinci istek gelirse işi yarıda kesmek yerine
            // değişikliği hemen uygula — panel hiç değişmemesinden iyi.
            onCovered?.Invoke();
            return;
        }

        instance.StartCoroutine(instance.Run(onCovered, duration, chaseDelay, tiltDegrees,
            leadColor ?? DefaultLeadColor,
            trailColor ?? DefaultTrailColor));
    }

    static void EnsureInstance()
    {
        if (instance != null) return;

        var go = new GameObject("PersonaPageSweep", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(go);

        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 31000;   // menünün üstünde, flaşın altında

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        instance = go.AddComponent<PersonaPageSweep>();
        instance.canvas = c;
        instance.lead = MakeBar(go.transform, "Lead");
        instance.trail = MakeBar(go.transform, "Trail");
        c.enabled = false;
    }

    static RectTransform MakeBar(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // Eğik durup yine de ekranı TAM kapatabilmesi için fazlasıyla büyük.
        rt.sizeDelta = new Vector2(1920f * 2.4f, 1080f * 2.4f);

        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return rt;
    }

    IEnumerator Run(System.Action onCovered, float duration, float chaseDelay,
                    float tilt, Color leadColor, Color trailColor)
    {
        busy = true;
        canvas.enabled = true;

        lead.GetComponent<Image>().color = leadColor;
        trail.GetComponent<Image>().color = trailColor;
        lead.localRotation = trail.localRotation = Quaternion.Euler(0f, 0f, tilt);

        float travel = 1920f * 2.2f;   // soldan tamamen dışarı -> sağdan tamamen dışarı
        float startX = -travel;
        float endX = travel;

        bool fired = false;
        float t = 0f;
        duration = Mathf.Max(0.05f, duration);

        while (t < duration + chaseDelay)
        {
            t += Time.unscaledDeltaTime;

            float kLead = Mathf.Clamp01(t / duration);
            float kTrail = Mathf.Clamp01((t - chaseDelay) / duration);

            lead.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, Smooth(kLead)), 0f);
            trail.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, Smooth(kTrail)), 0f);

            // Siyah bar ekranın ortasına geldiğinde her şey kapalı demektir.
            if (!fired && kTrail >= 0.5f)
            {
                fired = true;
                onCovered?.Invoke();
            }

            yield return null;
        }

        if (!fired) onCovered?.Invoke();

        canvas.enabled = false;
        busy = false;
    }

    static float Smooth(float k) => k * k * (3f - 2f * k);
}
