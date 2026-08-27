using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GERİ BİLDİRİM PANELİ (playtest için geçici).
///
/// Oyuncu oyundan hiç çıkmadan bir şey yazıp gönderiyor; mesaj
/// FeedbackSender üzerinden bir Google Form'a, oradan da geliştiricinin
/// Google Sheets tablosuna düşüyor. Yanına pist seed'i, rol, FPS gibi teknik
/// bağlam otomatik ekleniyor ve tabloda AYRI BİR SÜTUNA yazılıyor.
/// (Discord webhook'u denendi ve iptal edildi — Türkiye'de engelli, bkz.
/// FeedbackSender.cs.)
///
/// ─── SettingsMenuController İLE AYNI DESEN ────────────────────────────
/// UI, PauseMenu.prefab'ın içinde duruyor; bu script sadece oradaki
/// referansları okuyup mantığı çalıştırıyor (14 Ağustos kararı: UI KODDA
/// KURULMAZ — Scene view'da görünmüyor ve font/sprite değiştirmek için kod
/// yazmak gerekiyor).
///
/// 🚨 Initialize() NEDEN Awake() DEĞİL: Bu panel prefabda KAPALI başlıyor,
/// Unity ise kapalı objelerde Awake()'i HİÇ çağırmıyor — kurulum Awake'te
/// olsaydı buton olayları hiç bağlanmazdı. (Ayarlar panelinde birebir bu
/// yaşandı, bkz. CLAUDE.md.) Bu yüzden kurulumu, her zaman AÇIK olan
/// PauseMenuController çağırıyor.
/// </summary>
public class FeedbackMenuController : MonoBehaviour
{
    [Header("Google Form Ayarları")]
    [Tooltip("Formun gönderim adresi — '.../formResponse' ile bitmeli.\n\n" +
             "Formun normal linki '.../viewform' ile biter; sondaki 'viewform' " +
             "kelimesini 'formResponse' ile değiştirmen yeterli.\n\n" +
             "Boş bırakılırsa gönder butonu oyuncuya nazik bir hata gösterir, " +
             "oyun bozulmaz.")]
    [SerializeField] private string formUrl = "";

    [Tooltip("İsim sorusunun kimliği (ör. entry.123456789). Boş bırakılırsa isim gönderilmez.")]
    [SerializeField] private string nameEntryId = "";

    [Tooltip("Mesaj sorusunun kimliği. BU ŞART — boşsa oyuncunun yazdığı metin hiç gitmez.")]
    [SerializeField] private string messageEntryId = "";

    [Tooltip("Teknik bilgi sorusunun kimliği (pist seed'i, rol, FPS vb. buraya gider). " +
             "Boş bırakılırsa bağlam gönderilmez — ama asıl işe yarayan kısım burası.")]
    [SerializeField] private string contextEntryId = "";

    [Header("Prefab Referansları")]
    public InputField messageInput;
    public InputField nameInput;
    public Button gonderButton;
    public Button geriButton;
    public Text statusText;

    [Header("Davranış")]
    [Tooltip("İki gönderim arasında beklenmesi gereken süre — yanlışlıkla " +
             "arka arkaya basmayı engelliyor.")]
    [SerializeField] private float sendCooldownSeconds = 10f;

    private PauseMenuController pauseMenu;
    private float nextAllowedSendTime;
    private bool sending;

    /// <summary>
    /// PauseMenuController tarafından bir kere çağrılıyor (kapalı objede
    /// Awake çalışmadığı için).
    /// </summary>
    public void Initialize(PauseMenuController owner)
    {
        pauseMenu = owner;

        if (gonderButton != null)
        {
            gonderButton.onClick.RemoveListener(Send);
            gonderButton.onClick.AddListener(Send);
        }

        if (geriButton != null)
        {
            geriButton.onClick.RemoveListener(Back);
            geriButton.onClick.AddListener(Back);
        }

        Hide();
    }

    public void Show()
    {
        gameObject.SetActive(true);
        SetStatus("");

        if (gonderButton != null) gonderButton.interactable = !sending;
    }

    public void Hide() => gameObject.SetActive(false);

    private void Back()
    {
        if (pauseMenu != null) pauseMenu.ShowMainPanel();
        else Hide();
    }

    private void Send()
    {
        if (sending) return;

        if (Time.unscaledTime < nextAllowedSendTime)
        {
            int kalan = Mathf.CeilToInt(nextAllowedSendTime - Time.unscaledTime);
            SetStatus(Loc.T("fb.cooldown", kalan));
            return;
        }

        string message = messageInput != null ? messageInput.text : "";
        string playerName = nameInput != null ? nameInput.text : "";

        if (string.IsNullOrWhiteSpace(message))
        {
            SetStatus(Loc.T("fb.empty"));
            return;
        }

        sending = true;
        if (gonderButton != null) gonderButton.interactable = false;
        SetStatus(Loc.T("fb.sending"));

        StartCoroutine(FeedbackSender.Send(formUrl, nameEntryId, messageEntryId, contextEntryId,
                                           playerName, message, OnSendFinished));
    }

    private void OnSendFinished(bool success, string userMessage)
    {
        sending = false;
        if (gonderButton != null) gonderButton.interactable = true;

        SetStatus(userMessage);

        if (!success) return;

        nextAllowedSendTime = Time.unscaledTime + sendCooldownSeconds;

        // Başarılıysa kutuyu temizle — aynı mesajın yanlışlıkla ikinci kez
        // gönderilmesini engelliyor. İsim alanı KALIYOR, aynı oyuncu ikinci
        // bir şey yazarken tekrar yazmak zorunda kalmasın.
        if (messageInput != null) messageInput.text = "";
    }

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }
}
