using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Mirror;

/// <summary>
/// OYUN İÇİ DURAKLATMA / ÇIKIŞ MENÜSÜ — ESC ile açılır.
///
/// Dört butonu var: Devam Et / Ayarlar / Oyundan Ayrıl / Oyunu Kapat.
/// "Ayarlar" aynı panelin İÇİNDE bir alt panele geçiyor (SettingsMenuController),
/// oyunu durdurmadan hem lobide hem yarış sırasında kullanılabiliyor.
///
/// ── ARTIK NORMAL BİR PREFAB ──
/// Görsel yapı (Canvas/Button/Dropdown) kodda DEĞİL, Assets/Resources/UI/
/// PauseMenu.prefab içinde — Unity Editor'de normal bir sahne gibi açıp
/// düzenleyebilirsin (sprite/font/renk/pozisyon). Bu script sadece o
/// prefab'taki referansları (aşağıdaki public alanlar) okuyup mantığı
/// çalıştırıyor. Prefab kayıpsa/bozulmuşsa üst menüden
/// "SaboTour > Ayarlar Menüsü Prefabını Oluştur" ile yeniden üretilebilir.
///
/// ── NEDEN Time.timeScale = 0 KULLANMIYORUZ ──
/// Eski tek oyunculu PauseMenu.cs oyunu `Time.timeScale = 0` ile
/// donduruyordu. Multiplayer'da bu YANLIŞ: sunucu ve diğer oyuncular
/// oynamaya devam ediyor, sadece SENİN fiziğin duruyor. Üstelik Mirror'ın
/// kendi zamanı (NetworkTime) timeScale'i bilerek yok sayıyor, yani ağ
/// trafiği de akmaya devam ediyor. Sonuç: "duraklattım" sanırken arabana
/// arkadan çarpılmış olurdu. Bu menü oyunu DURDURMUYOR, sadece imleci
/// serbest bırakıp panel gösteriyor.
///
/// ── "OYUNDAN AYRIL" NE YAPIYOR ──
/// MyNetworkManager.LeaveGameIntentionally() çağırıyor. Bu, host ise
/// StopHost, client ise StopClient yapıyor ve Mirror otomatik olarak
/// Offline Scene'i (ana menü) yüklüyor. `SceneManager.LoadScene` ile
/// DOĞRUDAN sahne değiştirmek Mirror'ı bozardı — bağlantı açık kalır,
/// spawn edilmiş objeler ortada kalırdı.
/// </summary>
[DefaultExecutionOrder(-100)] // SaboteurController'dan ÖNCE çalışsın (ESC devri için)
public class PauseMenuController : MonoBehaviour
{
    /// <summary>
    /// Menü şu an açık mı? SaboteurController bunu okuyup imleç/hareket
    /// durumunu buna göre ayarlıyor — ESC'yi iki ayrı yerin okuması
    /// karışıklık yaratmasın diye ESC'nin TEK SAHİBİ bu script.
    /// </summary>
    public static bool IsOpen { get; private set; }

    private static PauseMenuController instance;

    [Header("Prefab Referansları (PauseMenuPrefabBuilder tarafından otomatik bağlanır)")]
    public GameObject panelRoot;
    public GameObject mainButtonsPanel;
    public SettingsMenuController settingsMenu;
    public Button devamEtButton;
    public Button ayarlarButton;
    public Button oyundanAyrilButton;
    public Button oyunuKapatButton;

    private bool showingSettings;


    /// <summary>
    /// Oyun açılır açılmaz Assets/Resources/UI/PauseMenu.prefab'ı Instantiate
    /// edip DontDestroyOnLoad yapıyor — her sahnede (lobi ve yarış) çalışıyor.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>("UI/PauseMenu");
        if (prefab == null)
        {
            Debug.LogError("[PauseMenu] Assets/Resources/UI/PauseMenu.prefab bulunamadı! " +
                            "Unity Editor'de üst menüden 'SaboTour > Ayarlar Menüsü Prefabını Oluştur' çalıştır.");
            return;
        }

        GameObject go = Instantiate(prefab);
        go.name = "PauseMenu (otomatik)";
        DontDestroyOnLoad(go);
        // `instance` artık burada DEĞİL, Awake()'te set ediliyor — Instantiate
        // sırasında Awake zaten çalışıyor, yani buraya gelindiğinde iş bitmiş
        // oluyor. Burada tekrar atamak, aşağıdaki tekillik korumasıyla
        // çakışıp devre dışı bırakılmış bir kopyayı "asıl" ilan edebilirdi.
    }

    void Awake()
    {
        // ── TEKİLLİK KORUMASI ──
        // NEDEN ŞART: IsOpen `static`, yani TÜM kopyalar aynı değeri paylaşıyor.
        // İki kopya varken ESC'ye basıldığında biri menüyü açıp IsOpen'ı true
        // yapıyor, diğeri aynı karede o true'yu görüp kapatıyor. Net sonuç:
        // panel ekranda kalıyor ama IsOpen false — oyuncu menüde kilitleniyor
        // (ne ESC ne "Devam Et" işe yarıyor) ve imleç de kayboluyor, çünkü
        // RacerCursorLock IsOpen=false görüp imleci kilitliyor.
        // Bu gerçekten yaşandı: prefabın içine iç içe ikinci bir kopya girmişti.
        if (instance != null && instance != this)
        {
            Debug.LogWarning(
                $"[PauseMenu] Fazladan bir PauseMenuController bulundu ('{name}') ve devre dışı " +
                "bırakıldı. Assets/Resources/UI/PauseMenu.prefab içinde iç içe geçmiş bir kopya " +
                "olabilir — prefabı açıp fazlalığı silmen önerilir.", this);

            if (panelRoot != null) panelRoot.SetActive(false);
            enabled = false;
            return;
        }

        instance = this;
        IsOpen = false;

        devamEtButton.onClick.AddListener(Close);
        ayarlarButton.onClick.AddListener(ShowSettings);
        oyundanAyrilButton.onClick.AddListener(LeaveGame);
        oyunuKapatButton.onClick.AddListener(QuitApplication);

        // Prefabda SettingsPanel'in tiki AÇIK kalmış olsa bile tutarlı bir
        // başlangıç durumu garanti ediyoruz: ana butonlar görünür, ayarlar
        // gizli. Bu olmadan ayarlar paneli ana butonların ÜSTÜNDE duruyor ve
        // tıklamaları emiyordu ("Devam Et" çalışmıyor gibi görünmesinin
        // sebeplerinden biri buydu).
        ShowMainPanel();
        panelRoot.SetActive(false);

        // EN SONA bilinçli olarak bırakıldı: ayarlar panelinin kurulumu
        // (eksik referans vb. yüzünden) hata verirse, yukarıdaki "menüyü
        // kapat" satırları ÇALIŞMIŞ olsun. Önceden bu çağrı yukarıdaydı ve
        // hata verdiğinde panelRoot hiç kapatılmıyordu — menü ekranda açık
        // kalıyor ama IsOpen false olduğu için hiçbir buton onu kapatamıyordu.
        if (settingsMenu != null) settingsMenu.Initialize();
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

        // Ayarlar panelindeyken ESC menüyü tamamen kapatmak yerine önce
        // ana panele "geri" dönsün — beklenen menü davranışı bu.
        if (IsOpen && showingSettings)
            ShowMainPanel();
        else
            Toggle();
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;

        IsOpen = true;
        panelRoot.SetActive(true);

        FreeCursor();
    }

    private static void FreeCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        if (!IsOpen)
        {
            // SAVUNMA: "menü açık değil" sayılıyor ama panel ekranda görünür
            // kaldıysa (bir hata Awake'i yarıda kestiyse olabiliyor) yine de
            // kapat. Eskiden burada koşulsuz `return` vardı ve oyuncu menüde
            // KİLİTLİ kalıyordu — ne "Devam Et" ne ESC işe yarıyordu, çünkü
            // ikisi de bu metoda geliyor ve ilk satırda geri dönüyordu.
            if (panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
                ShowMainPanel();
            }
            return;
        }

        IsOpen = false;
        panelRoot.SetActive(false);

        // İmleci KİLİTLEMİYORUZ, serbest bırakıyoruz. Kilitlemesi gereken varsa
        // (yarışçıda RacerCursorLock, sabotajcıda SaboteurController) IsOpen
        // artık false olduğu için bir sonraki karede kendisi kilitliyor.
        //
        // ESKİDEN: menü açılırken imlecin o anki durumu kaydedilip kapanırken
        // geri yükleniyordu. Bu, BAYAT bir anlık görüntüyü geri yükleyebiliyor
        // ve imleci yazan üç script'in birbiriyle çakışmasına sebep oluyordu.
        // Lobide (araba/sabotajcı yokken) imleç zaten serbest kalmalı, bu
        // davranış orada da doğru.
        FreeCursor();

        // Bir sonraki açılışta her zaman ana panelden başlasın — ayarlar
        // içindeyken kapatılmış olsa bile.
        ShowMainPanel();
    }

    private void ShowSettings()
    {
        mainButtonsPanel.SetActive(false);
        settingsMenu.Show();
        showingSettings = true;
    }

    /// <summary>Ayarlar panelindeki "Geri" butonu da bunu çağırıyor.</summary>
    public void ShowMainPanel()
    {
        if (settingsMenu != null) settingsMenu.Hide();
        mainButtonsPanel.SetActive(true);
        showingSettings = false;
    }

    // ─── Buton Eylemleri ─────────────────────────────────────────────────

    private void LeaveGame()
    {
        Close();

        if (NetworkManager.singleton is MyNetworkManager manager)
        {
            // Bu, "bağlantı koptu" uyarısının çıkmasını da engelliyor —
            // oyuncu kendi isteğiyle çıkıyor, hata mesajı görmemeli.
            manager.LeaveGameIntentionally();
        }
        else
        {
            Debug.LogWarning("[PauseMenu] MyNetworkManager bulunamadı — oturum kapatılamadı.");
        }
    }

    private void QuitApplication()
    {
        // Oyundan çıkmadan ÖNCE oturumu düzgün kapat: yoksa Steam lobisi
        // açık kalıyor ve arkadaşların listesinde var olmayan bir oyuna
        // "Katıl" butonu görünmeye devam ediyor (SteamLobbyManager.LeaveLobby
        // OnStopServer/OnStopClient üzerinden tetikleniyor).
        if (NetworkServer.active || NetworkClient.isConnected)
        {
            if (NetworkManager.singleton is MyNetworkManager manager)
                manager.LeaveGameIntentionally();
        }

        Application.Quit();

#if UNITY_EDITOR
        // Editor'de Application.Quit hiçbir şey yapmıyor — test edebilmek
        // için Play modunu durduruyoruz.
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
