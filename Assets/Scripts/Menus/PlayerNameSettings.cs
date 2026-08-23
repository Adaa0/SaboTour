using System.Text;
using UnityEngine;
using Steamworks;

/// <summary>
/// OYUNCU İSMİ — kaydetme, temizleme ve varsayılan belirleme.
///
/// `MouseSensitivitySettings` ve `SfxPlayer.MasterVolume` ile AYNI desen:
/// küçük bir statik sınıf. AYRI DOSYA OLMASININ SEBEBİ: ismi YAZAN yer ana
/// menüdeki kutu, KULLANAN yer ise lobideki networked obje (LobbyPlayer) ve
/// yarıştaki PlayerRaceController. Üçü birbirini tanımıyor; aradaki bağlantıyı
/// bu statik sınıf kuruyor.
///
/// VARSAYILAN = STEAM ADI: oyuncu hiçbir şey yazmasa bile lobide "Oyuncu 3"
/// değil gerçek adı görünüyor. Steam kapalıysa (Editor / KCP testleri)
/// "Oyuncu"ya düşüyor.
/// </summary>
public static class PlayerNameSettings
{
    /// <summary>İsim uzunluk sınırı. Kutu da, sunucu da bunu uyguluyor.</summary>
    public const int MaxLength = 20;

    private const string PrefsKey = "SaboTour_PlayerName";
    private const string Fallback = "Oyuncu";

    // Bellekteki güncel değer. Oyuncu yazarken her tuşta PlayerPrefs'e
    // yazmak gereksiz disk trafiği — kutu yazmayı bitirince Save() çağırıyor.
    private static string cached;

    public static string PlayerName
    {
        get
        {
            if (!string.IsNullOrEmpty(cached)) return cached;

            string saved = Sanitize(PlayerPrefs.GetString(PrefsKey, string.Empty));
            cached = string.IsNullOrEmpty(saved) ? DefaultName() : saved;

            return cached;
        }
        set
        {
            cached = Sanitize(value);
            if (string.IsNullOrEmpty(cached)) cached = DefaultName();
        }
    }

    /// <summary>Bellekteki ismi diske yazar (kutu yazmayı bitirince çağrılıyor).</summary>
    public static void Save()
    {
        PlayerPrefs.SetString(PrefsKey, PlayerName);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Oyuncu hiç isim girmediyse kullanılacak ad. Steam açıksa Steam
    /// profilindeki isim — oyuncunun kendini tanıdığı isim bu.
    /// </summary>
    public static string DefaultName()
    {
        if (SteamManager.Initialized)
        {
            string persona = Sanitize(SteamFriends.GetPersonaName());
            if (!string.IsNullOrEmpty(persona)) return persona;
        }

        return Fallback;
    }

    /// <summary>
    /// İsmi güvenli hâle getirir. SUNUCUDA DA ÇAĞRILIYOR — client'tan gelen
    /// hiçbir metne güvenilmez, oyuncu değiştirilmiş bir build ile istediğini
    /// gönderebilir.
    ///
    /// NE YAPIYOR VE NEDEN:
    /// • `&lt;` ve `&gt;` siliniyor — oyuncu listesi ve leaderboard TextMeshPro
    ///   kullanıyor ve TMP zengin metni (rich text) İŞLİYOR. Adına
    ///   "&lt;size=300&gt;" yazan biri herkesin ekranındaki tabloyu bozabilirdi.
    /// • Satır sonu/sekme/kontrol karakterleri boşluğa çevriliyor — tek satırlık
    ///   liste satırını ikiye bölmesin diye.
    /// • Arka arkaya boşluklar tekleştirilip baştaki/sondaki kırpılıyor —
    ///   sadece boşluktan oluşan "görünmez isim" engellenmiş oluyor.
    /// • En fazla MaxLength karakter.
    /// </summary>
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        bool lastWasSpace = false;

        foreach (char c in raw)
        {
            if (c == '<' || c == '>') continue;

            bool isSpace = char.IsWhiteSpace(c) || char.IsControl(c);

            if (isSpace)
            {
                if (sb.Length == 0 || lastWasSpace) continue; // baştaki/çift boşluk
                sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;

            if (sb.Length >= MaxLength) break;
        }

        return sb.ToString().TrimEnd();
    }
}
