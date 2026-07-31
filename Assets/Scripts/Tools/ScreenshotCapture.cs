using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// STEAM EKRAN GÖRÜNTÜSÜ ÇEKME ARACI
///
/// Play modundayken F9'a basınca Game penceresinin görüntüsünü PNG olarak
/// kaydeder. Dosyalar projenin YANINDAKİ "Screenshots" klasörüne düşer
/// (Assets'in içine değil — yoksa Unity onları oyun asset'i sanıp import
/// etmeye çalışırdı).
///
/// KULLANIM:
///  1. Online Scene'de boş bir GameObject'e bu script'i ekle.
///  2. Game penceresinin çözünürlüğünü 1920x1080 yap (Game sekmesinin
///     üstündeki "Free Aspect" yazan yerden + ile yeni çözünürlük ekle).
///  3. Play'e bas, güzel bir an yakalayınca F9.
///
/// SUPER SIZE NOTU: 1'den büyük yaparsan Unity sahneyi daha yüksek
/// çözünürlükte render edip daha keskin bir görüntü verir (ör. 2 = 3840x2160),
/// ama bu modda ekran üstü UI (HUD, tur sayacı, crosshair) görüntüye
/// GİRMEZ — bu Unity'nin bilinen bir kısıtı. HUD'un da görünmesini
/// istiyorsan superSize'ı 1 bırak.
/// </summary>
public class ScreenshotCapture : MonoBehaviour
{
    [Header("Çekim")]
    [Tooltip("1 = Game penceresi çözünürlüğü (HUD dahil). 2+ = daha keskin ama HUD görünmez.")]
    [Range(1, 4)]
    [SerializeField] private int superSize = 1;

    [Tooltip("Görüntülerin kaydedileceği klasör (proje klasörünün yanında oluşturulur).")]
    [SerializeField] private string folderName = "Screenshots";

    [Header("Yardımcılar")]
    [Tooltip("F10 ile HUD/Canvas'ları gizle-göster — temiz (UI'sız) kareler için.")]
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
        // Application.dataPath = ".../SaboTour/Assets" → bir üstü proje kökü
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folder = Path.Combine(projectRoot, folderName);
        Directory.CreateDirectory(folder);

        string fileName = $"SaboTour_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        string fullPath = Path.Combine(folder, fileName);

        ScreenCapture.CaptureScreenshot(fullPath, superSize);

        Debug.Log($"[ScreenshotCapture] Kaydedildi → {fullPath}");
    }

    /// <summary>
    /// Sahnedeki tüm Canvas'ları VE Mirror'ın "Stop Host / Stop Client"
    /// debug panelini açıp kapatır — UI'sız "sinematik" kareler için.
    ///
    /// NOT: Mirror'ın paneli (NetworkManagerHUD) bir Canvas değil, eski
    /// OnGUI sistemiyle çiziliyor — bu yüzden Canvas'ları kapatmak onu
    /// gizlemeye yetmiyor, ayrıca kapatmak gerekiyor.
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
