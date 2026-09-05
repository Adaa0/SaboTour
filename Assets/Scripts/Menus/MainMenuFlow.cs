using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ANA MENÜYÜ İKİ EKRANA BÖLER — gerçek oyunlardaki gibi.
///
/// ÖNCEDEN: oyun açılır açılmaz "Oyun Kur", "Hızlı Katıl", "Davet Et",
/// "Hazırım", isim kutusu ve oyuncu listesi AYNI ANDA ekrandaydı. Oyunu ilk
/// açan biri neye basacağını bilemiyordu; üstelik "Davet Et"/"Hazırım" bir
/// oturuma girmeden hiçbir işe yaramıyor, "Oyun Kur"/"Hızlı Katıl" ise
/// oturumdayken basılınca "zaten bir oturum var" uyarısı veriyordu.
///
/// ŞİMDİ:
///   ANA EKRAN → OYNA / Nasıl Oynanır / Ayarlar / Geri Bildirim / Çıkış
///   ODA EKRANI → isim + Oyun Kur / Hızlı Katıl   (oturum YOKKEN)
///                oyuncu listesi + Davet Et / Hazırım  (oturum VARKEN)
///
/// 🚨 ODA EKRANI KENDİ İÇİNDE İKİYE AYRILIYOR ve bu ayrım HER KAREDE
/// `NetworkClient.active` okunarak yapılıyor — SyncVar hook'una ya da
/// "şu butona basıldı" bayrağına GÜVENİLMİYOR. Sebep: bir Steam daveti
/// kabul edilirse oyuncu hiçbir butona basmadan oturuma girebiliyor
/// (bkz. SteamLobbyManager'ın üç ayrı katılma yolu). Bayrakla takip
/// etseydik o durumda ekran yanlış kalırdı.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuFlow : MonoBehaviour
{
    [Header("Ekranlar")]
    [Tooltip("OYNA / Nasıl Oynanır / Ayarlar / Geri Bildirim / Çıkış")]
    public GameObject mainScreen;

    [Tooltip("İsim + Oyun Kur / Hızlı Katıl / Davet Et / Hazırım / oyuncu listesi")]
    public GameObject roomScreen;

    [Header("Ana ekran butonları")]
    public Button playButton;
    public Button settingsButton;
    public Button quitButton;

    [Header("Oda ekranı")]
    public Button backButton;

    [Tooltip("Bir oturuma BAĞLIYKEN gizlenecekler (Oyun Kur, Hızlı Katıl, Geri).")]
    public GameObject[] hideWhenConnected;

    [Tooltip("Sadece bir oturuma BAĞLIYKEN görünecekler (Davet Et, Hazırım, oyuncu listesi).")]
    public GameObject[] showWhenConnected;

    [Header("Geçiş")]
    [Tooltip("Ekran değişiminde Persona süpürme animasyonu oynasın mı.")]
    public bool useSweepTransitions = true;

    // Son uygulanan bağlantı durumu. Her karede SetActive çağırmamak için
    // tutuluyor — SetActive Canvas'ı yeniden çizmeye zorluyor (aynı gerekçe
    // PlayerNameField'da da yazılı).
    private bool? lastConnectedState;
    private bool showingRoom;

    private static bool IsConnected => NetworkClient.active || NetworkServer.active;

    void OnEnable()
    {
        WireButtons();

        // Yarıştan lobiye dönüşte (ya da davet kabul edilmişken) oyuncu zaten
        // bir oturumda oluyor — onu ana ekrana atmak yanlış olurdu.
        SetScreen(IsConnected, instant: true);

        lastConnectedState = null;   // görünürlükler bir kere zorla uygulansın
        RefreshRoomContents();
    }

    /// <summary>
    /// Listener önce KALDIRILIP sonra ekleniyor: bu obje sahne geçişlerinde
    /// yeniden kurulabiliyor ve aynı butona iki kez bağlanmak tek tıkta iki
    /// kez tetiklenmeye yol açardı (aynı ders SteamLobbyManager'da yaşandı).
    /// </summary>
    void WireButtons()
    {
        Wire(playButton, ShowRoom);
        Wire(backButton, ShowMain);
        Wire(settingsButton, OpenSettings);
        Wire(quitButton, QuitApplication);
    }

    static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    void Update()
    {
        // Davet kabul edilirse oyuncu ana ekrandayken oturuma girebiliyor —
        // o anda oda ekranına geçmek zorundayız.
        if (IsConnected && !showingRoom) SetScreen(true, instant: true);

        RefreshRoomContents();
    }

    void RefreshRoomContents()
    {
        bool connected = IsConnected;
        if (lastConnectedState == connected) return;
        lastConnectedState = connected;

        SetAll(hideWhenConnected, !connected);
        SetAll(showWhenConnected, connected);
    }

    static void SetAll(GameObject[] objects, bool active)
    {
        if (objects == null) return;

        foreach (var go in objects)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }

    // ── Ekran değiştirme ────────────────────────────────────────────────

    public void ShowMain()
    {
        // Oturumdayken ana ekrana dönmek, oyuncuyu "yarım bağlı" bir yerde
        // bırakırdı. Oturumdan çıkış ESC > Oyundan Ayrıl'ın işi.
        if (IsConnected) return;

        Transition(() => SetScreen(false, instant: true));
    }

    public void ShowRoom() => Transition(() => SetScreen(true, instant: true));

    void SetScreen(bool room, bool instant)
    {
        showingRoom = room;
        if (mainScreen != null) mainScreen.SetActive(!room);
        if (roomScreen != null) roomScreen.SetActive(room);
    }

    void Transition(System.Action change)
    {
        if (change == null) return;

        if (useSweepTransitions) PersonaPageSweep.Sweep(change);
        else change();
    }

    // ── Ana ekran aksiyonları ───────────────────────────────────────────

    void OpenSettings()
    {
        // Ayarlar paneli PauseMenu.prefab'ın içinde yaşıyor (her sahnede
        // otomatik yükleniyor). Lobiye ikinci bir kopya kurmak, aynı paneli
        // iki ayrı yerde bakmak demek olurdu — MainMenuButtons'daki karar.
        if (PauseMenuController.Instance == null) return;

        PauseMenuController.Instance.OpenSettingsFromMainMenu();
    }

    void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
