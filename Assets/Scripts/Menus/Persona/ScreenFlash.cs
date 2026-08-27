using UnityEngine;
using UnityEngine.UI;

// Videodaki "kitaptaki en ucuz numara": her buton değişiminde 2 karelik
// beyaz flaş. Neredeyse bedava ama seçimi çok daha sert hissettiriyor.
//
// Kalıcı obje/prefab GEREKMİYOR — ilk çağrıldığında kendi Canvas'ını kuruyor.
// 🚨 Bu obje NetworkManager'ın ALTINDA DEĞİL, kendi bağımsız objesinde duruyor:
// Mirror sahne geçişinde NetworkManager objesini DontDestroyOnLoad'dan çıkarıp
// yok ediyor (20 Ağustos SteamManager dersi), oraya bağlansaydı ölürdü.
public class PersonaScreenFlash : MonoBehaviour
{
    static PersonaScreenFlash instance;

    Image image;
    int framesLeft;

    /// <summary>Ekranı birkaç kareliğine çaktırır.</summary>
    public static void Trigger(Color color, int frames = 2, float alpha = 0.5f)
    {
        if (!Application.isPlaying) return;

        EnsureInstance();
        if (instance == null) return;

        var c = color;
        c.a = Mathf.Clamp01(alpha);
        instance.image.color = c;
        instance.image.enabled = true;
        instance.framesLeft = Mathf.Max(1, frames);
    }

    static void EnsureInstance()
    {
        if (instance != null) return;

        var go = new GameObject("PersonaScreenFlash", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(go);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // her şeyin üstünde

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var imgGo = new GameObject("Flash", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)imgGo.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = imgGo.GetComponent<Image>();
        img.raycastTarget = false;   // 🚨 ŞART: açık kalsa TÜM menü tıklanamaz olurdu
        img.enabled = false;

        instance = go.AddComponent<PersonaScreenFlash>();
        instance.image = img;
    }

    // Süre saniyeyle değil KAREYLE ölçülüyor — video da öyle diyor ("two frame
    // white flash"). Saniyeyle verilseydi yüksek FPS'te göz kırpması gibi
    // kaybolur, düşük FPS'te uzun bir beyaz perde olurdu.
    void LateUpdate()
    {
        if (!image.enabled) return;

        framesLeft--;
        if (framesLeft <= 0) image.enabled = false;
    }
}
