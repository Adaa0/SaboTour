using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// AYARLAR PANELİ — PauseMenuController'ın "Ayarlar" butonuyla açılan alt panel.
/// Kendi ESC/cursor yönetimi YOK — PauseMenuController zaten imleci serbest
/// bırakıp panelRoot'u açtığı için bu, o overlay'in İÇİNDE bir alt panel.
///
/// ── ARTIK NORMAL BİR PREFAB'IN PARÇASI ──
/// Görsel yapı (Dropdown/Toggle/Slider) Assets/Resources/UI/PauseMenu.prefab
/// içinde — bu script sadece aşağıdaki public alanlardan referans alıp
/// mantığı çalıştırıyor. Sprite/font/renk/pozisyon değiştirmek için Unity
/// Editor'de prefab'ı aç, kod tarafına dokunmana gerek yok.
///
/// FPS sınırı → FrameRateLimiter.Instance.Apply() (zaten PlayerPrefs'e kaydediyor)
/// Tam ekran / çözünürlük → Screen API + kendi PlayerPrefs anahtarları
/// Ses seviyesi → SfxPlayer.MasterVolume (tüm efektler zaten oradan kısılıyor)
/// Fare hassasiyeti (sabotajcı) → MouseSensitivitySettings (eskiden Page Up/
/// Page Down ile SaboteurController içinde ayarlanıyordu, artık buraya taşındı)
///
/// Fullscreen/çözünürlük/ses ayarları Awake() içinde HEMEN uygulanıyor —
/// yani oyuncu menüyü hiç açmasa bile önceki oturumda seçtiği ayarlar
/// oyun açılır açılmaz devreye giriyor.
/// </summary>
public class SettingsMenuController : MonoBehaviour
{
    private const string PrefFullscreen = "Settings_Fullscreen";
    private const string PrefResWidth = "Settings_ResWidth";
    private const string PrefResHeight = "Settings_ResHeight";
    private const string PrefVolume = "Settings_Volume";

    [Header("Prefab Referansları (PauseMenuPrefabBuilder tarafından otomatik bağlanır)")]
    public Dropdown fpsDropdown;
    public Toggle fullscreenToggle;
    public Dropdown resolutionDropdown;
    public Slider volumeSlider;
    [Tooltip("Sabotajcının fare hassasiyeti. Henüz prefab'a eklenmediyse boş bırakılabilir — null-güvenli, hata vermez.")]
    public Slider sensitivitySlider;
    public Button geriButton;

    private List<Resolution> resolutions;
    private bool initialized;

    /// <summary>
    /// PauseMenuController çağırıyor — Awake() KULLANILMIYOR, bilinçli.
    ///
    /// NEDEN: Bu component, prefabda KAPALI başlayan SettingsPanel objesinin
    /// üzerinde duruyor (ayarlar paneli menü açılınca değil, "Ayarlar"a
    /// basılınca görünmeli). Unity kapalı objelerde Awake()'i HİÇ çağırmıyor —
    /// yani buradaki kurulum hiç çalışmıyor, dropdown/slider olayları
    /// bağlanmıyor, çözünürlük listesi boş kalıyordu. "Ayarlar"a basınca
    /// panel açılmamasının sebebi buydu (boş listeye erişip hata veriyordu).
    ///
    /// ÇÖZÜM: Kurulumu Unity'nin callback'ine bırakmak yerine, HER ZAMAN
    /// AÇIK olan PauseMenuController'ın elle çağırdığı bir metoda taşıdık.
    /// C#'ta kapalı bir objenin component'inin metodunu çağırmak sorunsuz —
    /// sadece Awake/Update gibi Unity callback'leri çalışmıyor.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;

        // Bayrağı doğrulamadan ÖNCE set ETMİYORUZ: bir referans boşsa kurulum
        // yarıda kalır, ama bayrak true kalsaydı bir daha hiç denenmez ve
        // panel kalıcı olarak bozuk kalırdı.
        if (!ValidateReferences()) return;

        initialized = true;

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(BuildResolutionLabels());

        fpsDropdown.onValueChanged.AddListener(OnFpsChanged);
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

        // Null-güvenli: bu slider'ı henüz prefab'a EKLEMEDİYSEN (bkz. dosya
        // başındaki manuel adım notu) menünün geri kalanı yine de çalışsın.
        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = MouseSensitivitySettings.MinDisplay;
            sensitivitySlider.maxValue = MouseSensitivitySettings.MaxDisplay;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        }

        // "Geri" butonu PauseMenuController'ın genel "ana panele dön" mantığını
        // tetikliyor (tek yönlü bağımlılık: Pause → Settings, tersi yok, bu
        // yüzden referansı GetComponentInParent ile buluyoruz).
        PauseMenuController pauseMenu = GetComponentInParent<PauseMenuController>(true);
        if (pauseMenu != null)
            geriButton.onClick.AddListener(pauseMenu.ShowMainPanel);

        // Menü hiç açılmasa bile önceki oturumdan kalan ayarlar devreye girsin.
        ApplyPersistedOnStartup();
        RefreshFromCurrentState();
    }

    public void Show()
    {
        // Initialize() burada da çağrılıyor (PauseMenuController zaten Awake'te
        // çağırıyor olsa bile) — içindeki bayrak sayesinde iki kere çalışmıyor,
        // ama sıralama ne olursa olsun panelin hazır olmasını garantiliyor.
        Initialize();
        gameObject.SetActive(true);
        RefreshFromCurrentState();
    }

    public void Hide() => gameObject.SetActive(false);

    /// <summary>
    /// Inspector'da atanmamış bir alan varsa, çıplak bir NullReferenceException
    /// yerine HANGİ alanın boş olduğunu söyleyen net bir mesaj veriyor —
    /// prefab elle düzenlendiğinde bir referansın kopması sık yaşanan bir
    /// durum ve yığın izinden hangisi olduğu anlaşılmıyor.
    /// </summary>
    private bool ValidateReferences()
    {
        string missing = null;

        if (fpsDropdown == null) missing = "Fps Dropdown";
        else if (fullscreenToggle == null) missing = "Fullscreen Toggle";
        else if (resolutionDropdown == null) missing = "Resolution Dropdown";
        else if (volumeSlider == null) missing = "Volume Slider";
        else if (geriButton == null) missing = "Geri Button";

        if (missing == null) return true;

        Debug.LogError($"[SettingsMenu] '{missing}' alanı Inspector'da BOŞ. " +
                        "Assets/Resources/UI/PauseMenu.prefab'ı aç, SettingsPanel objesini seç, " +
                        "Settings Menu Controller component'inde bu alana ilgili UI objesini sürükle.", this);
        return false;
    }

    // ─── Başlangıçta Kalıcı Ayarları Uygulama ─────────────────────────────
    private void ApplyPersistedOnStartup()
    {
        if (PlayerPrefs.HasKey(PrefVolume))
            SfxPlayer.MasterVolume = PlayerPrefs.GetFloat(PrefVolume);

        if (PlayerPrefs.HasKey(PrefFullscreen))
        {
            bool fs = PlayerPrefs.GetInt(PrefFullscreen) == 1;
            Screen.fullScreenMode = fs ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        if (PlayerPrefs.HasKey(PrefResWidth) && PlayerPrefs.HasKey(PrefResHeight))
        {
            int w = PlayerPrefs.GetInt(PrefResWidth);
            int h = PlayerPrefs.GetInt(PrefResHeight);
            Screen.SetResolution(w, h, Screen.fullScreenMode);
        }
    }

    // ─── Mevcut Durumu Okuyup UI'a Yansıtma ───────────────────────────────
    private void RefreshFromCurrentState()
    {
        // Kurulum yapılamadıysa (eksik referans) buraya hiç girme — aksi
        // halde `resolutions` gibi henüz doldurulmamış alanlara erişip
        // asıl hatayı gizleyen ikinci bir hata üretiyor.
        if (!initialized) return;

        if (FrameRateLimiter.Instance != null)
        {
            int idx;
            switch (FrameRateLimiter.Instance.CurrentMode)
            {
                case FrameRateLimiter.Mode.VSync: idx = 0; break;
                case FrameRateLimiter.Mode.SabitSinir:
                    idx = FrameRateLimiter.Instance.CurrentTargetFrameRate >= 120 ? 2 : 1;
                    break;
                default: idx = 3; break;
            }
            fpsDropdown.SetValueWithoutNotify(idx);
        }

        fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreenMode != FullScreenMode.Windowed);

        int currentResIndex = resolutions.FindIndex(r => r.width == Screen.width && r.height == Screen.height);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, currentResIndex));

        volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(PrefVolume, SfxPlayer.MasterVolume));

        if (sensitivitySlider != null)
            sensitivitySlider.SetValueWithoutNotify(MouseSensitivitySettings.Display);
    }

    // ─── Değişiklik Olayları ───────────────────────────────────────────────
    private void OnFpsChanged(int index)
    {
        if (FrameRateLimiter.Instance == null)
        {
            Debug.LogWarning("[SettingsMenu] FrameRateLimiter sahnede yok, FPS ayarı uygulanamadı.");
            return;
        }

        switch (index)
        {
            case 0: FrameRateLimiter.Instance.Apply(FrameRateLimiter.Mode.VSync, 60); break;
            case 1: FrameRateLimiter.Instance.Apply(FrameRateLimiter.Mode.SabitSinir, 60); break;
            case 2: FrameRateLimiter.Instance.Apply(FrameRateLimiter.Mode.SabitSinir, 120); break;
            default: FrameRateLimiter.Instance.Apply(FrameRateLimiter.Mode.Sinirsiz, 120); break;
        }
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        Screen.fullScreenMode = isFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        PlayerPrefs.SetInt(PrefFullscreen, isFullscreen ? 1 : 0);
    }

    private void OnResolutionChanged(int index)
    {
        if (index < 0 || index >= resolutions.Count) return;

        Resolution r = resolutions[index];
        Screen.SetResolution(r.width, r.height, Screen.fullScreenMode);
        PlayerPrefs.SetInt(PrefResWidth, r.width);
        PlayerPrefs.SetInt(PrefResHeight, r.height);
    }

    private void OnVolumeChanged(float value)
    {
        SfxPlayer.MasterVolume = value;
        PlayerPrefs.SetFloat(PrefVolume, value);
    }

    private void OnSensitivityChanged(float displayValue)
    {
        MouseSensitivitySettings.SetFromDisplay(displayValue);
    }

    private List<string> BuildResolutionLabels()
    {
        resolutions = Screen.resolutions
            .Select(r => new Resolution { width = r.width, height = r.height })
            .GroupBy(r => (r.width, r.height))
            .Select(g => g.First())
            .OrderBy(r => r.width * r.height)
            .ToList();

        if (resolutions.Count == 0)
            resolutions.Add(new Resolution { width = Screen.width, height = Screen.height });

        return resolutions.Select(r => $"{r.width} x {r.height}").ToList();
    }
}
