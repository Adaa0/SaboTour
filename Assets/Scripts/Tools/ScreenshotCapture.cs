using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// GEÇİCİ ÇEKİM ARACI — Steam görselleri bitince SİLİNECEK (bkz. CLAUDE.md
/// silme listesi). Oyunun parçası değil.
///
/// STEAM EKRAN GÖRÜNTÜSÜ / CAPSULE ÇEKME ARACI
///
/// Play modundayken F9'a basınca aktif kameranın görüntüsünü, seçtiğin Steam
/// capsule ölçüsünde PNG olarak kaydeder. Dosyalar projenin YANINDAKİ
/// "Screenshots" klasörüne düşer (Assets'in içine değil — yoksa Unity onları
/// oyun asset'i sanıp import etmeye çalışırdı).
///
/// NEDEN ARTIK ScreenCapture.CaptureScreenshot KULLANMIYORUZ:
/// O fonksiyonun "superSize" parametresi ESKİ (built-in) render pipeline için
/// yazılmış; URP'de sessizce yok sayılıyor, yani süper örnekleme hiç
/// çalışmıyordu. Bunun yerine kamerayı doğrudan bir RenderTexture'a render
/// edip sonucu küçültüyoruz. Bunun üç avantajı var:
///  1. Süper örnekleme GERÇEKTEN çalışıyor (2x render → küçült = çok temiz
///     kenarlar, oyundaki görüntüden bile keskin).
///  2. PNG tam olarak istediğin ölçüde çıkıyor (1232x706 gibi) — Game
///     penceresinin çözünürlüğü ne olursa olsun. Sonradan kırpma/boyutlandırma
///     derdi yok.
///  3. Kadraj, Game penceresinin oranından bağımsız olarak hedef orana göre
///     hesaplanıyor.
///
/// KULLANIM:
///  1. Boş bir GameObject'e bu script'i ekle.
///  2. "Capsule" listesinden çekeceğin ölçüyü seç.
///  3. Play'e bas, F4 ile kadrajı seç, F9 ile çek.
///
/// ÖNEMLİ: Kadrajı doğru görebilmek için Game penceresini de aynı orana ayarla
/// (Game sekmesi > Free Aspect > + > Fixed Resolution). Yoksa PNG doğru ölçüde
/// çıkar ama çekerken gördüğün kadraj farklı olur.
///
/// NOT: Bu yöntem ekran üstü UI'ı (Canvas, HUD, crosshair) görüntüye ALMAZ —
/// capsule için istediğimiz zaten bu. HUD'lu kare gerekirse F10 ile HUD'u
/// açıp Windows'un kendi ekran görüntüsü aracını kullan.
/// </summary>
public class ScreenshotCapture : MonoBehaviour
{
    /// <summary>Steam'in istediği hazır ölçüler + serbest seçenek.</summary>
    public enum CapsuleSize
    {
        Main_1232x706,
        Header_920x430,
        Small_462x174,
        Vertical_748x896,
        Library_600x900,
        LibraryHero_3840x1240,
        Screenshot_1920x1080,
        Custom
    }

    [Header("Çekim")]
    [Tooltip("Hangi Steam ölçüsünde çekilecek. 'Custom' seçersen aşağıdaki " +
             "genişlik/yükseklik alanları kullanılır.")]
    [SerializeField] private CapsuleSize capsule = CapsuleSize.Main_1232x706;

    [SerializeField] private int customWidth = 1920;
    [SerializeField] private int customHeight = 1080;

    [Tooltip("Kaç katı çözünürlükte render edilip küçültülecek. 2 = temiz " +
             "kenarlar, 3-4 = daha da temiz ama yavaş ve çok bellek yer. " +
             "Büyük ölçülerde (Library Hero gibi) 2'de bırak.")]
    [Range(1, 4)]
    [SerializeField] private int supersampleFactor = 2;

    [Tooltip("Görüntülerin kaydedileceği klasör (proje klasörünün yanında oluşturulur).")]
    [SerializeField] private string folderName = "Screenshots";

    [Header("Kamera")]
    [Tooltip("Boş bırakırsan sahnedeki AKTİF kamerayı kendisi bulur — " +
             "PhotoCameraSwitcher (F4) ile geçiş yaptığında doğru kamerayı seçer.")]
    [SerializeField] private Camera targetCamera;

    [Header("Yardımcılar")]
    [Tooltip("F10 ile HUD/Canvas'ları gizle-göster.")]
    [SerializeField] private bool allowHudToggle = true;

    private bool hudHidden;

    void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.f9Key.wasPressedThisFrame)
            Capture();

        if (allowHudToggle && Keyboard.current.f10Key.wasPressedThisFrame)
            ToggleHud();
    }

    private void Capture()
    {
        Camera cam = ResolveCamera();
        if (cam == null)
        {
            Debug.LogWarning("[ScreenshotCapture] Aktif kamera bulunamadı — çekim yapılamadı.");
            return;
        }

        GetTargetSize(out int width, out int height);

        int factor = Mathf.Clamp(supersampleFactor, 1, 4);
        int renderWidth = width * factor;
        int renderHeight = height * factor;

        // Yüksek çözünürlüklü hedef. 24 bit depth şart — derinlik tamponu
        // olmadan sahne yanlış sırayla çizilir (uzaktaki obje öne geçer).
        RenderTexture hiRes = new RenderTexture(renderWidth, renderHeight, 24,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        hiRes.filterMode = FilterMode.Bilinear;

        RenderTexture downscaled = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            RenderCameraTo(cam, hiRes);

            RenderTexture source = hiRes;

            // Süper örnekleme: büyük render'ı hedef ölçüye indiriyoruz.
            // Küçültme sırasında birden fazla piksel ortalandığı için kenarlar
            // yumuşuyor — en kaliteli anti-aliasing yöntemi budur.
            if (factor > 1)
            {
                downscaled = new RenderTexture(width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                downscaled.filterMode = FilterMode.Bilinear;

                Graphics.Blit(hiRes, downscaled);
                source = downscaled;
            }

            RenderTexture.active = source;

            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();

            byte[] png = image.EncodeToPNG();
            Destroy(image);

            string path = WriteFile(png, width, height);
            Debug.Log($"[ScreenshotCapture] {width}x{height} ({factor}x süper örnekleme) → {path}");
        }
        finally
        {
            // Hata çıksa bile temizlik yapılsın — yoksa RenderTexture sızıntısı
            // olur ve editör yavaş yavaş bellek doldurur.
            RenderTexture.active = previousActive;

            hiRes.Release();
            Destroy(hiRes);

            if (downscaled != null)
            {
                downscaled.Release();
                Destroy(downscaled);
            }
        }
    }

    /// <summary>
    /// Kamerayı verilen RenderTexture'a render eder.
    ///
    /// Unity 6 + URP'de eski "cam.targetTexture = rt; cam.Render();" yöntemi
    /// güvenilir değil (bazı post-process adımları atlanabiliyor). Modern
    /// yol SubmitRenderRequest — desteklenmiyorsa eski yönteme düşüyoruz.
    /// </summary>
    private static void RenderCameraTo(Camera cam, RenderTexture target)
    {
        UniversalRenderPipeline.SingleCameraRequest request =
            new UniversalRenderPipeline.SingleCameraRequest { destination = target };

        if (RenderPipeline.SupportsRenderRequest(cam, request))
        {
            RenderPipeline.SubmitRenderRequest(cam, request);
            return;
        }

        RenderTexture previous = cam.targetTexture;
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = previous;
    }

    /// <summary>
    /// Inspector'da kamera atanmamışsa sahnedeki aktif kamerayı bulur.
    /// PhotoCameraSwitcher aynı anda tek kamerayı açık bıraktığı için bu
    /// doğru sonucu veriyor.
    /// </summary>
    private Camera ResolveCamera()
    {
        if (targetCamera != null && targetCamera.isActiveAndEnabled)
            return targetCamera;

        if (Camera.main != null) return Camera.main;

        foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (cam.isActiveAndEnabled && cam.targetTexture == null)
                return cam;

        return null;
    }

    private void GetTargetSize(out int width, out int height)
    {
        switch (capsule)
        {
            case CapsuleSize.Main_1232x706:      width = 1232; height = 706;  break;
            case CapsuleSize.Header_920x430:     width = 920;  height = 430;  break;
            case CapsuleSize.Small_462x174:      width = 462;  height = 174;  break;
            case CapsuleSize.Vertical_748x896:   width = 748;  height = 896;  break;
            case CapsuleSize.Library_600x900:    width = 600;  height = 900;  break;
            case CapsuleSize.LibraryHero_3840x1240: width = 3840; height = 1240; break;
            case CapsuleSize.Screenshot_1920x1080:  width = 1920; height = 1080; break;
            default:
                width = Mathf.Max(8, customWidth);
                height = Mathf.Max(8, customHeight);
                break;
        }
    }

    private string WriteFile(byte[] png, int width, int height)
    {
        // SABİT bir konuma (Masaüstü) kaydediyoruz — Application.dataPath'e
        // göreceli yol kullanmıyoruz. Sebep: Multiplayer Play Mode'un sanal
        // client'ı, Editör'ün kendi kopyasından FARKLI, gizli bir klonlanmış
        // proje klasörü kullanıyor. Application.dataPath'e göre kaydedersen
        // dosya "kaydedildi" logu doğru çıkar ama gerçekte görünmeyen bir
        // klasöre düşer — tam bu yüzden dosya bulunamadı. Masaüstü, hangi
        // ortamdan (Editör, MPPM host/client, gerçek build) çekilirse
        // çekilsin HER ZAMAN aynı, tahmin gerektirmeyen bir yer.
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            folderName);
        Directory.CreateDirectory(folder);

        string fileName = $"SaboTour_{width}x{height}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        string fullPath = Path.Combine(folder, fileName);

        File.WriteAllBytes(fullPath, png);
        return fullPath;
    }

    /// <summary>
    /// Sahnedeki tüm Canvas'ları VE Mirror'ın "Stop Host / Stop Client"
    /// debug panelini açıp kapatır.
    ///
    /// NOT: Mirror'ın paneli (NetworkManagerHUD) bir Canvas değil, eski
    /// OnGUI sistemiyle çiziliyor — bu yüzden Canvas'ları kapatmak onu
    /// gizlemeye yetmiyor, ayrıca kapatmak gerekiyor.
    ///
    /// Yeni çekim yöntemi UI'ı zaten görüntüye almıyor, ama ekranda görmek
    /// istemediğinde (kadrajı temiz değerlendirmek için) hâlâ işe yarıyor.
    /// </summary>
    private void ToggleHud()
    {
        hudHidden = !hudHidden;

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas canvas in canvases)
            canvas.enabled = !hudHidden;

        Mirror.NetworkManagerHUD[] mirrorHuds =
            FindObjectsByType<Mirror.NetworkManagerHUD>(FindObjectsSortMode.None);
        foreach (Mirror.NetworkManagerHUD hud in mirrorHuds)
            hud.enabled = !hudHidden;

        Debug.Log($"[ScreenshotCapture] HUD {(hudHidden ? "gizlendi" : "geri açıldı")}.");
    }
}
