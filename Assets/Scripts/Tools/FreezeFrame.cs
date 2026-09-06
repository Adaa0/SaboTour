using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Mirror;

/// <summary>
/// ⚠️ GEÇİCİ ARAÇ — SADECE EKRAN GÖRÜNTÜSÜ / CAPSULE ART ÇEKİMİ İÇİN.
/// Steam görselleri bitince bu dosya ve sahnedeki objesi SİLİNECEK.
///
/// KARE DONDURMA (F7)
///
/// F7'ye basınca SADECE SENİN bilgisayarında oyun donar — drift anını,
/// patlamayı, tavuk sürüsünü yakalayıp rahatça kadrajlayabilirsin. Tekrar
/// F7 ile kaldığın yerden devam eder.
///
/// DİĞER OYUNCULAR ETKİLENMEZ: Bu script sadece `Time.timeScale`'i 0
/// yapıyor, o da makineye özel bir ayar — network üzerinden karşı tarafa
/// gitmez. Yani sen donmuşken arkadaşın normal oynamaya devam eder.
///
/// EFEKTLER SİLİNMEZ: Duman/buz/skidmark gibi şeyler timeScale'e bağlı
/// çalıştığı için donar ama EKRANDA KALIR. `Destroy(obj, süre)` ve
/// `WaitForSeconds` de ölçekli zamanı kullandığı için, donmuşken buz alanı
/// gibi süreli objeler de yok olmaz — sayaçları durur.
///
/// KONTROLLER:
///   F7  → dondur / devam et
///   F8  → serbest kamera (FreeCamera.cs — donmuşken de gezebilirsin)
///   F9  → ekran görüntüsü al (ScreenshotCapture.cs)
///   F10 → HUD'u gizle (bu script'in "DONDURULDU" yazısını da gizler)
///
/// KULLANIM: Online Scene'de boş bir GameObject'e ekle — ScreenshotCapture
/// ve FreeCamera ile AYNI objede durabilir, birbirlerine karışmazlar.
/// </summary>
[DefaultExecutionOrder(10000)] // LateUpdate'i EN SON çalışsın: aşağıdaki
                               // "uzak oyuncuları sabitleme" işi, Mirror'ın
                               // pozisyon yazmasından SONRA olmak zorunda.
public class FreezeFrame : MonoBehaviour
{
    [Header("Tuş")]
    [SerializeField] private Key toggleKey = Key.F7;

    [Header("Davranış")]
    [Tooltip("Diğer oyuncuların arabaları da senin ekranında dursun mu? " +
             "Kapalıysa sen donmuş olsan bile onlar kayıp gitmeye devam eder " +
             "(çünkü Mirror pozisyonları timeScale'den bağımsız işler).")]
    [SerializeField] private bool freezeRemotePlayers = true;

    [Tooltip("Ekranın üstünde 'DONDURULDU' yazısı görünsün mü? F10 ile de gizlenebilir.")]
    [SerializeField] private bool showIndicator = true;

    /// <summary>Donma anında kaydedilen bir transform'un dünya pozisyonu/rotasyonu.</summary>
    private struct PinnedPose
    {
        public Transform target;
        public Vector3 position;
        public Quaternion rotation;
    }

    private readonly List<PinnedPose> pinnedPoses = new List<PinnedPose>();

    private bool isFrozen;
    private float previousTimeScale = 1f;
    private GameObject indicatorRoot;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            Toggle();
    }

    /// <summary>
    /// LateUpdate = tüm normal Update'lerden sonra çalışan Unity aşaması.
    /// Mirror'ın NetworkTransform'u uzak arabaların pozisyonunu buraya kadar
    /// yazmış oluyor; biz de en son adımda üstüne donmuş pozu geri basıyoruz.
    /// </summary>
    void LateUpdate()
    {
        if (!isFrozen || !freezeRemotePlayers) return;

        for (int i = 0; i < pinnedPoses.Count; i++)
        {
            PinnedPose pose = pinnedPoses[i];
            if (pose.target == null) continue; // araç yok edilmiş olabilir

            pose.target.SetPositionAndRotation(pose.position, pose.rotation);
        }
    }

    private void Toggle()
    {
        if (isFrozen) Resume();
        else Freeze();
    }

    private void Freeze()
    {
        // FreeCamera (F8) da timeScale'i 0 yapıyor. Eğer o zaten dondurmuşsa
        // 0'ı "eski değer" diye saklayıp sonra 0'a geri dönersek oyun sonsuza
        // kadar donmuş kalırdı — o yüzden 0 gelirse 1'e düşüyoruz.
        previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        if (freezeRemotePlayers)
            CollectRemotePoses();

        isFrozen = true;

        if (showIndicator)
            SetIndicatorVisible(true);

        Debug.Log($"[FreezeFrame] Oyun DONDURULDU ({toggleKey}). " +
                  $"F8 ile gezip F9 ile çekebilirsin. Tekrar {toggleKey} = devam.");
    }

    private void Resume()
    {
        Time.timeScale = previousTimeScale;
        pinnedPoses.Clear();
        isFrozen = false;

        SetIndicatorVisible(false);

        Debug.Log("[FreezeFrame] Oyun devam ediyor.");
    }

    /// <summary>
    /// Uzak (bize ait olmayan) network objelerinin o anki pozunu kaydeder.
    ///
    /// NEDEN GEREKLİ: Mirror'ın zaman kaynağı `NetworkTime.localTime`,
    /// `Time.unscaledTimeAsDouble` kullanıyor ve timeScale'i BİLEREK yok
    /// sayıyor (bkz. Mirror/Core/NetworkTime.cs). Yani biz donsak bile
    /// diğer oyuncuların arabaları ekranımızda kaymaya devam ediyor.
    /// Mirror'ın kendi bileşenlerini kapatmak yerine (bu, snapshot
    /// tamponunu sıfırlayıp sync'i bozabilir) sadece transform'u her karenin
    /// SONUNDA eski haline geri yazıyoruz — tamamen görsel bir sabitleme,
    /// network durumuna hiç dokunmuyor.
    ///
    /// Sadece kök obje değil TÜM alt objeler kaydediliyor — yoksa tekerlek
    /// gibi ayrı hareket eden parçalar donmuş arabanın üstünde dönmeye
    /// devam ederdi.
    /// </summary>
    private void CollectRemotePoses()
    {
        pinnedPoses.Clear();

        NetworkIdentity[] identities = FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None);

        foreach (NetworkIdentity identity in identities)
        {
            // Kendi objemizi atlıyoruz — o zaten timeScale = 0 ile donuyor.
            if (identity == null || identity.isOwned) continue;

            // GetComponentsInChildren PARENT'I DA döndürür ve hiyerarşi
            // sırasıyla gelir (önce üst, sonra alt) — dünya pozisyonlarını
            // bu sırayla geri yazmak doğru sonucu veriyor.
            Transform[] children = identity.GetComponentsInChildren<Transform>(true);

            foreach (Transform child in children)
            {
                pinnedPoses.Add(new PinnedPose
                {
                    target = child,
                    position = child.position,
                    rotation = child.rotation
                });
            }
        }
    }

    /// <summary>
    /// "DONDURULDU" yazısını gösterir/gizler. Bir Canvas'a bağlı olduğu için
    /// ScreenshotCapture'ın F10 (HUD gizle) tuşu bunu da otomatik gizler —
    /// yani temiz kare çekerken yazı görüntüye girmez.
    /// </summary>
    private void SetIndicatorVisible(bool visible)
    {
        if (!visible)
        {
            if (indicatorRoot != null) indicatorRoot.SetActive(false);
            return;
        }

        if (indicatorRoot == null) CreateIndicator();
        indicatorRoot.SetActive(true);
    }

    private void CreateIndicator()
    {
        indicatorRoot = new GameObject("FreezeFrameIndicator");

        Canvas canvas = indicatorRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(indicatorRoot.transform, false);

        Text label = labelObj.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = $"DONDURULDU — {toggleKey} devam  |  F8 serbest kamera  |  F9 çek  |  F10 HUD gizle";
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(1f, 0.85f, 0.2f);
        label.raycastTarget = false;

        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -20f);
        rect.sizeDelta = new Vector2(900f, 30f);
    }

    /// <summary>Play modundan çıkarken oyunu donmuş bırakma.</summary>
    void OnDisable()
    {
        if (isFrozen)
            Time.timeScale = previousTimeScale;
    }
}
