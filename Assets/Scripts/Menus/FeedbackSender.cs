using System.Collections;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// OYUN İÇİ GERİ BİLDİRİMİ GOOGLE FORM'A GÖNDERİR (playtest için geçici sistem).
///
/// ─── NEDEN GOOGLE FORM (DISCORD DEĞİL) ────────────────────────────────
/// İlk tasarım Discord webhook'uydu ve İPTAL EDİLDİ: Discord Türkiye'de
/// engelli. Webhook isteği `discord.com` adresine, yani engellenen alan adının
/// kendisine gidiyor. Engel DNS tabanlıysa alternatif DNS kullanan testçilerde
/// çalışır, kullanmayanlarda çalışmaz; SNI/IP tabanlıysa hiç çalışmaz.
/// Sonuç: testçilerin rastgele bir kısmından geri bildirim gelir ve HANGİLERİNİN
/// gelmediğini bilemezsin — eksik olduğunu fark etmediğin için hiç toplamamaktan
/// daha kötü.
///
/// ─── NASIL ÇALIŞIYOR ──────────────────────────────────────────────────
/// Her Google Form'un görünmez bir "formResponse" adresi var. Oraya normal bir
/// form gönderisi (POST) atan herkes o formu doldurmuş sayılıyor — tarayıcı,
/// hesap ya da sunucu gerekmiyor. Cevaplar formun bağlı olduğu Google Sheets
/// tablosuna satır satır düşüyor.
///
/// 50-60 kişilik bir playtest için tablo, sohbet kanalından daha iyi: akışta
/// kaybolmuyor, sıralanabiliyor, filtrelenebiliyor, dışa aktarılabiliyor.
///
/// ─── ASIL DEĞERLİ KISIM: OTOMATİK BAĞLAM ──────────────────────────────
/// Oyuncunun yazdığı metnin yanına, onun hiç uğraşmasına gerek kalmadan teknik
/// durum ekleniyor ve AYRI BİR SÜTUNA yazılıyor. En önemlisi PİST SEED'İ: bu
/// projede pist rastgele üretiliyor, seed'i bilirsen oyuncunun şikayet ettiği
/// pistin BİREBİR aynısını kendi makinende üretebilirsin. "Şu virajda takıldım"
/// gibi bir cümle böylece tekrar üretilebilir bir hata raporuna dönüşüyor.
///
/// ─── GİZLİLİK ─────────────────────────────────────────────────────────
/// Steam adı/ID'si OTOMATİK GÖNDERİLMİYOR. Oyuncu isterse "isim" alanını
/// kendisi dolduruyor, boş bırakırsa "anonim" gidiyor.
///
/// ⚠️ Formun ayarlarında "Yanıtlayanlardan oturum açmasını iste" / "E-posta
/// adreslerini topla" KAPALI olmalı — açıksa gönderi Google tarafından
/// reddedilir ve oyuncu sebebini anlamaz.
/// </summary>
public static class FeedbackSender
{
    private const int MessageCharLimit = 2000;

    /// <summary>
    /// Geri bildirimi gönderir. Coroutine döndürüyor çünkü UnityWebRequest ağ
    /// cevabını beklemek zorunda; çağıran (FeedbackMenuController) bunu
    /// StartCoroutine ile çalıştırıyor.
    /// </summary>
    /// <param name="formUrl">Formun ".../formResponse" ile biten adresi.</param>
    /// <param name="nameEntryId">İsim alanının kimliği (ör. "entry.123456789"). Boşsa gönderilmez.</param>
    /// <param name="messageEntryId">Mesaj alanının kimliği. Boşsa gönderilmez.</param>
    /// <param name="contextEntryId">Teknik bilgi alanının kimliği. Boşsa gönderilmez.</param>
    /// <param name="onDone">true = gönderildi, false = hata. İkinci parametre kullanıcıya gösterilecek mesaj.</param>
    public static IEnumerator Send(string formUrl, string nameEntryId, string messageEntryId, string contextEntryId,
                                   string playerName, string message,
                                   System.Action<bool, string> onDone)
    {
        if (string.IsNullOrWhiteSpace(formUrl))
        {
            onDone?.Invoke(false, "Gönderim adresi ayarlanmamış. (Geliştirici: Form Url alanını doldur.)");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            onDone?.Invoke(false, "Önce bir şeyler yaz.");
            yield break;
        }

        string who = string.IsNullOrWhiteSpace(playerName) ? "anonim" : playerName.Trim();
        if (who.Length > 60) who = who.Substring(0, 60);

        string text = message.Trim();
        if (text.Length > MessageCharLimit) text = text.Substring(0, MessageCharLimit) + "… (kısaltıldı)";

        // WWWForm, alan değerlerini kendisi URL-kodluyor (Türkçe karakterler ve
        // satır sonları dahil) — elle kodlamaya gerek yok.
        WWWForm form = new WWWForm();
        if (!string.IsNullOrWhiteSpace(nameEntryId)) form.AddField(nameEntryId.Trim(), who);
        if (!string.IsNullOrWhiteSpace(messageEntryId)) form.AddField(messageEntryId.Trim(), text);
        if (!string.IsNullOrWhiteSpace(contextEntryId)) form.AddField(contextEntryId.Trim(), BuildContextLine());

        using (UnityWebRequest request = UnityWebRequest.Post(formUrl.Trim(), form))
        {
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onDone?.Invoke(true, Loc.T("fb.sent"));
            }
            else
            {
                // Oyuncuya teknik detay göstermenin anlamı yok ama Console'a
                // yazalım ki geliştirici Player.log'dan sebebi görebilsin.
                Debug.LogWarning($"[FeedbackSender] Gönderilemedi: {request.result} / {request.responseCode} / {request.error}");
                onDone?.Invoke(false, DescribeFailure(request.responseCode));
            }
        }
    }

    /// <summary>
    /// Hata mesajını sebebe göre ayırır.
    ///
    /// NEDEN GEREKLİ: İlk sürüm her hatada "internet bağlantını kontrol et"
    /// diyordu. İlk gerçek testte Google **401 Unauthorized** döndü — yani
    /// istek Google'a ULAŞMIŞTI, sorun formun ayarındaydı. Yanlış mesaj
    /// yüzünden sebep internet sanıldı. Ağ kodunda "her hata = bağlantı yok"
    /// varsayımı teşhisi yanlış yöne çeviriyor.
    /// </summary>
    private static string DescribeFailure(long responseCode)
    {
        switch (responseCode)
        {
            case 401:
            case 403:
                // Form oturum açma istiyor: "E-posta adreslerini topla" açık,
                // ya da form bir kuruma (okul/iş hesabı) kısıtlanmış.
                return Loc.T("fb.err.closed");

            case 404:
                return Loc.T("fb.err.notfound");

            case 400:
                return Loc.T("fb.err.rejected");

            default:
                return Loc.T("fb.err.network");
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  OTOMATİK BAĞLAM
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyuncunun o anki teknik durumu — tabloda ayrı bir sütuna gidiyor.
    /// Her parçası ayrı ayrı null-güvenli: bu kod ana menüde de, yarışın
    /// ortasında da, podyumda da çalışabilmeli ve hiçbirinde patlamamalı.
    /// </summary>
    public static string BuildContextLine()
    {
        StringBuilder sb = new StringBuilder();

        sb.Append("v").Append(Application.version);
        sb.Append(" | ").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        sb.Append(" | rol: ").Append(DescribeRole());

        // PİST SEED'İ — raporun en değerli parçası. Aynı seed aynı pisti üretiyor.
        TrackGenerator track = Object.FindAnyObjectByType<TrackGenerator>();
        if (track != null) sb.Append(" | seed: ").Append(track.seed);

        sb.Append(DescribeRaceState());

        sb.Append(" | ~").Append(Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime))).Append(" FPS");
        sb.Append(" | ").Append(Mathf.RoundToInt(Time.realtimeSinceStartup / 60f)).Append(" dk oynadı");
        sb.Append(" | ").Append(SystemInfo.graphicsDeviceName);
        sb.Append(" / ").Append(SystemInfo.processorType);
        sb.Append(" / ").Append(SystemInfo.systemMemorySize).Append("MB");
        sb.Append(" | ").Append(Screen.width).Append("x").Append(Screen.height);
        sb.Append(Screen.fullScreen ? " tam ekran" : " pencere");

        return sb.ToString();
    }

    /// <summary>
    /// Oyuncu yarışçı mı sabotajcı mı. Rol bilgisi MyNetworkManager'da da var
    /// ama en güvenilir yol, yerel oyuncu objesinin ÜZERİNDE hangi component'in
    /// olduğuna bakmak — spawn edilen şey zaten role göre farklı (araba vs.
    /// sabotajcı karakteri).
    /// </summary>
    private static string DescribeRole()
    {
        if (!NetworkClient.active) return "menüde";

        NetworkIdentity local = NetworkClient.localPlayer;
        if (local == null) return "bağlı (obje yok)";

        if (local.GetComponent<SaboteurController>() != null) return "SABOTAJCI";
        if (local.GetComponent<CarController>() != null) return "yarışçı";
        return "lobide";
    }

    private static string DescribeRaceState()
    {
        if (!NetworkClient.active || NetworkClient.localPlayer == null) return "";

        PlayerRaceController race = NetworkClient.localPlayer.GetComponent<PlayerRaceController>();
        if (race == null) return "";

        if (race.HasFinished) return " | yarışı BİTİRDİ";

        return $" | tur {race.CurrentLap}/{race.maxLaps}, CP {race.CurrentCheckpoint}, süre {race.FormattedTotalTime}";
    }
}
