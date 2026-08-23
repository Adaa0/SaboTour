using UnityEngine;
using TMPro;
using Mirror;

/// <summary>
/// ANA MENÜDEKİ İSİM KUTUSU. Oyuncu lobiye girmeden ÖNCE adını buraya
/// yazıyor; isim `PlayerNameSettings` üzerinden kaydediliyor ve lobiye
/// bağlanınca sunucuya gönderiliyor (bkz. `LobbyPlayer.OnStartAuthority`).
///
/// KUTU HER ZAMAN GÖRÜNÜR, ayrı bir "isim seç" penceresi açılmıyor. Sebep:
/// oyuncu ismini bir kere yazıyor, sonraki açılışlarda kayıtlı geliyor —
/// her oyun kurmak istediğinde önüne çıkan bir pencere gereksiz bir adım
/// olurdu. Kutu menüde durduğu için isim yine "katılmadan önce" seçiliyor.
///
/// KURULUM: `Assets/Editor/MainMenuPanelBuilder.cs` →
/// **SaboTour > Ana Menüye İsim Kutusu Ekle**.
/// </summary>
public class PlayerNameField : MonoBehaviour
{
    [Tooltip("Oyuncunun adını yazdığı kutu.")]
    public TMP_InputField input;

    void Start()
    {
        if (input == null) return;

        // Sınır TEK KAYNAKTAN geliyor: kutuya da sunucuya da aynı sabit
        // uygulanıyor, ikisi ayrışamıyor.
        input.characterLimit = PlayerNameSettings.MaxLength;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.text = PlayerNameSettings.PlayerName;

        // Listener'lar önce kaldırılıp sonra ekleniyor — bu obje sahne
        // geçişlerinde yeniden kurulabiliyor ve iki kez bağlanmak istenmeyen
        // çift tetiklemeye yol açardı.
        input.onValueChanged.RemoveListener(OnChanged);
        input.onValueChanged.AddListener(OnChanged);

        input.onEndEdit.RemoveListener(OnEndEdit);
        input.onEndEdit.AddListener(OnEndEdit);
    }

    /// <summary>
    /// KUTU SADECE OYUNA GİRMEDEN ÖNCE GÖRÜNÜR — lobiye katılınca gizleniyor.
    ///
    /// NEDEN: İsim sunucuya SADECE bağlanma anında gönderiliyor
    /// (`LobbyPlayer.OnStartAuthority` → `CmdSetName`). Lobideyken kutu açık
    /// kalsaydı oyuncu adını değiştirebilir, ama değişiklik hiçbir yere
    /// gitmezdi — kendi ekranında bir şey yazıp listede eski adını görürdü.
    /// Çalışmayan bir kutu, olmayan kutudan kötüdür.
    ///
    /// Durum HER KAREDE okunuyor ama `SetActive` sadece DEĞİŞTİĞİNDE
    /// çağrılıyor: iki bool okumanın maliyeti yok, gereksiz SetActive ise
    /// Canvas'ı her karede yeniden çizmeye zorlardı.
    /// </summary>
    void Update()
    {
        if (input == null) return;

        // NetworkServer.active de kontrol ediliyor: host'ta client kapanmış
        // ama sunucu hâlâ kapanıyor olabilir (Mirror'ın kapanışı asenkron).
        bool inSession = NetworkClient.active || NetworkServer.active;

        if (inSession == hidden) return;

        hidden = inSession;
        input.gameObject.SetActive(!inSession);

        // Gizlenirken kaydet: oyuncu Enter'a basmadan doğrudan "Oyun Kur"a
        // tıklamış olabilir.
        if (inSession) PlayerNameSettings.Save();
    }

    // Kutunun şu anki gizlilik durumu — SetActive'i boşuna çağırmamak için.
    private bool hidden;

    // Yazarken sadece bellekteki değeri güncelliyoruz — her tuşta
    // PlayerPrefs'e yazmak gereksiz disk trafiği olurdu.
    private void OnChanged(string value) => PlayerNameSettings.PlayerName = value;

    private void OnEndEdit(string value)
    {
        PlayerNameSettings.PlayerName = value;
        PlayerNameSettings.Save();

        // Temizlenmiş hâli kutuya geri yazılıyor: oyuncu sadece boşluk ya da
        // "<b>" gibi bir şey yazdıysa ne olduğunu GÖRSÜN, lobiye girince
        // sürpriz yaşamasın.
        if (input != null) input.SetTextWithoutNotify(PlayerNameSettings.PlayerName);
    }

    void OnDisable()
    {
        // Oyuncu Enter'a basmadan doğrudan "Oyun Kur"a tıklarsa da kaydedilsin.
        PlayerNameSettings.Save();
    }
}
