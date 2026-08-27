using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// ANA MENÜ (lobi ekranı) — "Nasıl Oynanır" ve "Geri Bildirim" butonları
/// + geri bildirim hatırlatıcısı.
///
/// NEDEN VAR: Bu iki panel zaten yazılmıştı ama SADECE ESC menüsünden
/// erişilebiliyordu. Kimse oyuna girip menü karıştırmıyor — özellikle
/// oyunu ilk kez açan biri "nasıl oynanır"ı ESC'nin altında aramıyor.
/// Playtest 50-60 kişiye gideceği için ikisinin de ana ekranda, göz
/// hizasında olması gerekiyor.
///
/// PANELLER BURADA DEĞİL — `PauseMenu.prefab` içinde yaşıyorlar. O prefab
/// DontDestroyOnLoad olduğu için ana menüde de hazır. Bu script sadece
/// butonları o panellere bağlıyor; aynı panelin ikinci bir kopyasını
/// lobiye kurmak (iki ayrı yerde bakım gerektirirdi) bilinçli olarak
/// yapılmadı.
///
/// KURULUM: Bu script + butonlar `Assets/Editor/MainMenuPanelBuilder.cs`
/// tarafından oluşturuluyor — üst menüden
/// **SaboTour > Ana Menüye Nasıl Oynanır + Geri Bildirim Ekle**.
/// Butonları sonradan elle taşıyabilir/boyutlandırabilirsin, kod
/// pozisyonlarına bir daha karışmıyor.
/// </summary>
public class MainMenuButtons : MonoBehaviour
{
    [Header("Butonlar")]
    [Tooltip("Basılınca 'Nasıl Oynanır' panelini açar.")]
    public Button howToPlayButton;

    [Tooltip("Basılınca 'Geri Bildirim' panelini açar.")]
    public Button feedbackButton;

    [Header("Geri Bildirim Hatırlatıcısı")]
    [Tooltip("Butonların yanında duran hatırlatma yazısı. Boş bırakılabilir.")]
    public TMP_Text reminderText;

    [Tooltip("AÇIK (önerilen): hatırlatma yazıları Loc.cs sözlüğünden, oyuncunun " +
             "kendi dilinde gelir. KAPALI: aşağıdaki iki alan aynen kullanılır.")]
    public bool useLocalizedReminders = true;

    [Tooltip("Oyuncu HENÜZ oynamamışken gösterilen metin. Sadece 'Use Localized Reminders' KAPALIYKEN kullanılır.")]
    [TextArea]
    public string reminderBeforePlaying =
        "Bu bir playtest — fikirlerini yazman oyunun gelişmesinin tek yolu.";

    [Tooltip("Oyuncu en az bir yarışa girdikten SONRA gösterilen metin. Sadece 'Use Localized Reminders' KAPALIYKEN kullanılır.")]
    [TextArea]
    public string reminderAfterPlaying =
        "Nasıl geçti? Geri Bildirim'e tıklayıp yaz — 30 saniyeni alır.";

    [Tooltip("Oyuncu oynadıktan sonra hatırlatma yazısı yavaşça yanıp sönsün mü (göze çarpsın diye).")]
    public bool pulseAfterPlaying = true;

    [Tooltip("Yanıp sönme hızı (saniyedeki döngü sayısı).")]
    public float pulseSpeed = 0.6f;

    [Tooltip("Yanıp sönerken yazının en sönük hâli (1 = hiç sönmüyor).")]
    [Range(0.2f, 1f)] public float pulseMinAlpha = 0.45f;

    // ── "Oyuncu bu oturumda oynadı mı?" ──────────────────────────────────
    // STATIC olmak ZORUNDA: bu script LobbyCanvas'ta duruyor, LobbyCanvas
    // NetworkManager'ın altında ve Mirror ana menüye dönerken o objeyi yok
    // edip yeniden kuruyor (bkz. CLAUDE.md, 20 Ağustos "SteamManager"
    // bölümü). Yani instance alanında tutulan hiçbir bilgi yarıştan geri
    // dönüşte hayatta kalmıyor.
    private static bool hasPlayedThisSession;

    // Oyunun açılışta yüklediği ilk sahne = ana menü. Sahne ADINI koda
    // yazmak yerine bunu ölçüyoruz — sahne yeniden adlandırılsa bile çalışır.
    private static string menuSceneName;

    /// <summary>
    /// Sahne takibini oyunun EN BAŞINDA, herhangi bir lobi objesi doğmadan
    /// kuruyoruz. Bu script'in kendi objesi yarış sırasında yok olduğu için
    /// takibi ona bağlayamayız.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void TrackSessions()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (string.IsNullOrEmpty(menuSceneName))
                menuSceneName = scene.name;      // ilk yüklenen sahne = ana menü
            else if (scene.name != menuSceneName)
                hasPlayedThisSession = true;     // başka bir sahne = yarışa girildi
        };
    }

    void Start()
    {
        WireButton(howToPlayButton, OpenHowToPlay);
        WireButton(feedbackButton, OpenFeedback);

        // Prefab güncel değilse (paneller yok) butonu hiç gösterme — basınca
        // hiçbir şey olmayan bir buton, olmayan butondan kötüdür.
        PauseMenuController pause = PauseMenuController.Instance;

        if (pause != null)
        {
            if (howToPlayButton != null && !pause.HasHowToPlay)
                howToPlayButton.gameObject.SetActive(false);

            if (feedbackButton != null && !pause.HasFeedback)
                feedbackButton.gameObject.SetActive(false);
        }

        RefreshReminder();
    }

    /// <summary>
    /// Hatırlatma yazısı LocalizedText ile DEĞİL kodla yazılıyor (metin
    /// oyuncunun oynayıp oynamadığına göre değişiyor), bu yüzden dil
    /// değişimini kendimiz dinlemek zorundayız — yoksa oyuncu ayarlardan
    /// dili değiştirip ana menüye döndüğünde tek bu yazı eski dilde kalırdı.
    /// </summary>
    private void OnEnable()
    {
        GameLanguage.OnLanguageChanged += RefreshReminder;
    }

    private void OnDisable()
    {
        GameLanguage.OnLanguageChanged -= RefreshReminder;
    }

    /// <summary>
    /// Listener önce KALDIRILIP sonra ekleniyor: bu obje sahne geçişlerinde
    /// yeniden kurulabiliyor ve aynı butona iki kez bağlanmak tek tıkta iki
    /// kez tetiklenmeye yol açardı (aynı ders SteamLobbyManager'da yaşandı).
    /// </summary>
    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void OpenHowToPlay()
    {
        if (PauseMenuController.Instance == null) return;

        PauseMenuController.Instance.OpenHowToPlayFromMainMenu();
    }

    private void OpenFeedback()
    {
        if (PauseMenuController.Instance == null) return;

        PauseMenuController.Instance.OpenFeedbackFromMainMenu();
    }

    private void RefreshReminder()
    {
        if (reminderText == null) return;

        if (useLocalizedReminders)
            reminderText.text = Loc.T(hasPlayedThisSession ? "menu.reminder.after" : "menu.reminder.before");
        else
            reminderText.text = hasPlayedThisSession ? reminderAfterPlaying : reminderBeforePlaying;

        // Yanıp sönme kapalıysa ya da henüz oynanmadıysa yazı sabit kalsın:
        // oyuna daha girmemiş birini geri bildirim için dürtmek anlamsız,
        // söyleyecek bir şeyi yok.
        if (!pulseAfterPlaying || !hasPlayedThisSession)
            SetAlpha(1f);
    }

    void Update()
    {
        if (reminderText == null) return;
        if (!pulseAfterPlaying || !hasPlayedThisSession) return;

        // 0..1 arası yumuşak gidip gelen bir değer (sinüs), oradan alfaya.
        float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;

        SetAlpha(Mathf.Lerp(pulseMinAlpha, 1f, t));
    }

    private void SetAlpha(float alpha)
    {
        Color c = reminderText.color;
        c.a = alpha;
        reminderText.color = c;
    }
}
