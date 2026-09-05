using UnityEngine;
using System.Linq;
using System.Text;
using TMPro;

/// <summary>
/// SIRALAMA TABLOSU — yarış boyunca kimin önde olduğunu gösterir.
///
/// Client-only: `PlayerRaceController.AllPlayers` statik listesini okuyup
/// tur/checkpoint/süreye göre sıralıyor. **Network mesajı GEREKMİYOR** —
/// her PlayerRaceController'ın `currentLap`/`currentCheckpoint`/`totalTime`
/// SyncVar'ları zaten Mirror tarafından her client'a otomatik yayılıyor,
/// burada sadece okunuyor. ("Bedava veri" fikri.)
///
/// ═══════════════════════════════════════════════════════════════════════
/// 🚨 SÜTUNLU DÜZEN DENENDİ VE GERİ ALINDI (23 Ağustos 2026)
/// ═══════════════════════════════════════════════════════════════════════
/// İlk yeniden yazımda sütunlar TMP'nin `&lt;pos=%&gt;` etiketiyle sabit
/// konuma alınmıştı. GERÇEK TESTTE ÜST ÜSTE BİNDİ.
///
/// SEBEP: `&lt;pos=%&gt;` yüzdesi METİN KUTUSUNUN GENİŞLİĞİNE göre
/// hesaplanıyor. Sahnedeki `LeaderboardText` kutusu dar olduğu için %55
/// birkaç yüz piksel değil, ismin bittiği yerin hemen yanına düşüyordu →
/// "aTur 1/3" gibi iç içe geçmiş bir metin. Üstüne satır kaydırma (word
/// wrap) devreye girip "0:11"i alt satıra bölüyordu.
///
/// DERS: Sabit genişlik varsaymayan bir düzen gerekiyordu. Sütun hizalaması
/// yerine AYIRAÇLI TEK SATIR kullanılıyor — kutu ne kadar dar olursa olsun
/// bozulmuyor. 3-4 yarışçı için tablo hissi zaten ayıraçla da okunuyor.
///
/// ─── GÖRÜNÜM ──────────────────────────────────────────────────────────
///   ► 1. Ahmet · Tur 1/3 · 0:11
///     2. Mehmet · Tur 1/3
/// • Her oyuncunun adı KENDİ ARABA RENGİNDE (`CarController.ColorIndex` →
///   `ColorPalette`) — renk zaten senkron, ekstra ağ mesajı yok. Oyuncu
///   tabloyu okumadan sadece renge bakarak kendini bulabiliyor.
/// • Kendi satırın ► işareti ve kalın yazıyla vurgulanıyor.
/// • Süre SADECE kendi satırında — bir yarışçı diğerinin tam süresini
///   görmemeli, sadece sırayı görmeli.
/// • İsim sınırı KAYNAKTA 12 karakter (PlayerNameSettings.MaxLength) —
///   böylece oyuncunun yazdığı isim her yerde AYNEN görünüyor, kırpılmıyor.
///
/// 🔒 GÜVENLİK: Oyuncu isimleri `PlayerNameSettings.Sanitize` ile SUNUCUDA
/// temizleniyor ve `&lt;` `&gt;` karakterleri siliniyor. Bu yüzden burada
/// zengin metin (rich text) etiketleri güvenle kullanılabiliyor — isme
/// `&lt;size=300&gt;` yazarak tabloyu bozmak mümkün değil.
/// </summary>
public class RaceLeaderboard : MonoBehaviour
{
    public TextMeshProUGUI LeaderboardText;

    [Tooltip("Tablo kaç saniyede bir yenilensin.")]
    [SerializeField] private float refreshInterval = 0.5f;

    [Header("Görünüm")]
    [Tooltip("Tablonun üstünde 'SIRALAMA' başlığı gösterilsin mi.")]
    [SerializeField] private bool showHeader = true;

    [Tooltip("Güvenlik ağı: isim bu uzunluğu aşarsa kırpılır. İsim sınırı zaten kaynakta 12 olduğu için normalde devreye girmiyor.")]
    [SerializeField] private int maxNameLength = 12;

    [Tooltip("Satırdaki parçaları ayıran işaret.")]
    [SerializeField] private string separator = " · ";

    [Tooltip("Yarışı bitirenlerin durum rengi.")]
    [SerializeField] private Color finishedColor = new Color(0.45f, 0.9f, 0.55f);

    [Tooltip("Süresi dolup bitiremeyenlerin durum rengi.")]
    [SerializeField] private Color timeoutColor = new Color(0.85f, 0.42f, 0.42f);

    private float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < refreshInterval) return;
        timer = 0f;
        Refresh();
    }

    /// <summary>
    /// Yazı kutusunu bulur. Sıra şu:
    ///   1. Inspector'dan elle atanmışsa onu kullan,
    ///   2. `RaceHud` prefabındaki kutuyu al (normal yol — tablo artık
    ///      Assets/Resources/UI/RaceHud.prefab içinde yaşıyor),
    ///   3. son çare: sahnede isimle ara (eski kurulumlarla uyumluluk).
    ///
    /// 🚨 NEDEN `Start()` DEĞİL, HER YENİLEMEDE: bu bileşen Online Scene'de,
    /// HUD ise DontDestroyOnLoad'da yaşıyor. `Start()` bir kere çalışıyor;
    /// o an HUD henüz hazır değilse referans SONSUZA KADAR boş kalır ve
    /// tablo hiç görünmezdi. Bu projede "hangi obje önce hazır olur"
    /// varsayımı defalarca yanlış çıktı, o yüzden burada varsayım yok.
    /// </summary>
    private bool EnsureText()
    {
        if (LeaderboardText != null) return true;

        LeaderboardText = RaceHud.LeaderboardText as TextMeshProUGUI
                          ?? GameObject.Find("LeaderboardText")?.GetComponent<TextMeshProUGUI>();

        if (LeaderboardText == null) return false;

        // SATIR KAYDIRMA KAPALI: kutu dar olduğunda TMP satırı ortadan
        // bölüyor ve "0:11" alt satıra düşüyordu (gerçek testte yaşandı).
        // Kırpma işini biz `maxNameLength` ile kontrollü yapıyoruz.
        LeaderboardText.textWrappingMode = TextWrappingModes.NoWrap;
        return true;
    }

    private void Refresh()
    {
        if (!EnsureText()) return;

        var ordered = PlayerRaceController.AllPlayers
            .Where(p => p != null)
            .OrderByDescending(p => p.CurrentLap)
            .ThenByDescending(p => p.CurrentCheckpoint)
            .ThenBy(p => p.TotalTime)
            .ToList();

        if (ordered.Count == 0)
        {
            LeaderboardText.text = "";
            return;
        }

        var sb = new StringBuilder();

        if (showHeader)
            sb.AppendLine($"<size=80%><color=#9AA3AE>{Loc.T("lb.title")}</color></size>");

        for (int i = 0; i < ordered.Count; i++)
            sb.AppendLine(BuildRow(ordered[i], i + 1));

        LeaderboardText.text = sb.ToString();
    }

    private string BuildRow(PlayerRaceController p, int rank)
    {
        bool isMe = p.isOwned;

        string nameColor = ColorUtility.ToHtmlStringRGB(ResolveColor(p));

        // Kendi satırın: ► işareti + kalın. Diğerlerinde aynı genişlikte
        // boşluk bırakılıyor ki sıra numaraları alt alta hizalı kalsın.
        string marker = isMe ? "► " : "   ";
        string open = isMe ? "<b>" : "";
        string close = isMe ? "</b>" : "";

        string status;
        string statusColor;

        if (p.HasFinished)
        {
            status = Loc.T("lb.finished");
            statusColor = ColorUtility.ToHtmlStringRGB(finishedColor);
        }
        else if (p.isRacing)
        {
            // CurrentLap 0'dan başlıyor (ilk turdayken 0). Ham hâliyle
            // yazılırsa yarışın başında "Tur 0/3" görünüyordu.
            status = Loc.T("race.lap", Mathf.Clamp(p.CurrentLap + 1, 1, p.maxLaps), p.maxLaps);
            statusColor = "FFFFFF";
        }
        else
        {
            // Sabotajcı süreyle kazandı, bu yarışçı bitiremeden durduruldu.
            status = Loc.T("lb.timeup");
            statusColor = ColorUtility.ToHtmlStringRGB(timeoutColor);
        }

        string time = isMe ? $"{separator}<color=#C9D1DB>{p.FormattedTotalTime}</color>" : "";

        return $"{open}{marker}{rank}. <color=#{nameColor}>{Shorten(p.PlayerLabel)}</color>" +
               $"{separator}<color=#{statusColor}>{status}</color>{time}{close}";
    }

    /// <summary>
    /// Uzun isimleri kırpar. GÜVENLİK AĞI: isim sınırı artık kaynakta 12
    /// (`PlayerNameSettings.MaxLength`), yani normalde bu kırpma HİÇ devreye
    /// girmiyor. Sunucudan beklenmedik uzunlukta bir isim gelirse diye duruyor.
    /// </summary>
    private string Shorten(string name)
    {
        if (string.IsNullOrEmpty(name)) return Loc.T("lb.player");
        if (maxNameLength <= 0 || name.Length <= maxNameLength) return name;

        return name.Substring(0, maxNameLength) + "…";
    }

    /// <summary>
    /// Oyuncunun araba rengi. Renk `netColorIndex` SyncVar'ı üzerinden zaten
    /// her client'a geliyor; spawn anında henüz gelmemiş olabileceği için
    /// (-1) beyaza düşülüyor.
    /// </summary>
    private Color ResolveColor(PlayerRaceController p)
    {
        CarController car = p.GetComponent<CarController>();
        if (car == null) return Color.white;

        int index = car.ColorIndex;
        if (index < 0 || index >= CarController.ColorPalette.Length) return Color.white;

        return CarController.ColorPalette[index];
    }
}
