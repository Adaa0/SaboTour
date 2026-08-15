using UnityEngine;

/// <summary>
/// SABOTAJCININ FARE HASSASİYETİ — kalıcı, tüm oturumlar arası hatırlanan
/// tek bir global değer. SfxPlayer.MasterVolume ile AYNI desen: küçük statik
/// bir sınıf, SettingsMenuController'daki slider değeri değiştirdiğinde
/// buraya yazıyor, SaboteurController her karede buradan okuyor.
///
/// ── NEDEN AYRI BİR DOSYA (SaboteurController'ın kendi alanı DEĞİL) ──
/// Ayarlar menüsü (Settings) her sahnede (lobi dahil) var ama sabotajcı
/// karakteri sadece Online Scene'de, sadece sabotajcı rolündeki oyuncuda
/// spawn oluyor. İkisinin ARADA bir bağlantı kurması gerekiyordu — statik
/// bir sınıf, SaboteurController hiç spawn olmasa (yarışçı rolündeysen)
/// bile ayarlar menüsünün sorunsuz çalışmasını sağlıyor.
///
/// ── SLIDER'IN ORTASI = ESKİ VARSAYILAN (0.2) ──
/// Slider yine 1-10 arası, yuvarlak/anlaşılır sayılar gösteriyor. Ama artık
/// DOĞRUSAL DEĞİL, İKİ PARÇALI: barın TAM ORTASI (5.5) eski varsayılan
/// değere (0.2 — playtest'te en oynanabilir bulunan çarpan) denk geliyor.
/// Ortadan SOLA gidince (1'e doğru) 0.2'nin ALTINA inip daha da yavaş/hassas
/// bir bölgeye giriyorsun (yeni — eskiden bu hiç yoktu, 0.2 en düşüktü).
/// Ortadan SAĞA gidince (10'a doğru) eskisiyle AYNI üst sınıra (2.0) kadar
/// çıkıyor — o taraf değişmedi.
///
/// SaboteurController hâlâ sadece `Raw`'ı okuyor, bu iç mantığı hiç bilmesi
/// gerekmiyor.
/// </summary>
public static class MouseSensitivitySettings
{
    private const string PrefKey = "Settings_MouseSensitivityDisplay";

    /// <summary>Slider'ın izin verdiği görünen aralık.</summary>
    public const float MinDisplay = 1f;
    public const float MaxDisplay = 10f;

    /// <summary>Barın tam ortası — varsayılan konum.</summary>
    public const float MidDisplay = (MinDisplay + MaxDisplay) * 0.5f;

    /// <summary>Ortadaki (varsayılan) noktanın karşılık geldiği gerçek çarpan — eski tek sabit değer buydu.</summary>
    private const float MidRaw = 0.2f;

    /// <summary>Barın sol ucundaki (1) en düşük, en yavaş/hassas çarpan.</summary>
    private const float MinRaw = 0.05f;

    /// <summary>Barın sağ ucundaki (10) en yüksek çarpan — eski üst sınırla aynı.</summary>
    private const float MaxRaw = 2f;

    private static float display = MidDisplay;
    private static bool loaded;

    /// <summary>SaboteurController'ın kamera kodunda kullandığı gerçek (küçük) çarpan.</summary>
    public static float Raw
    {
        get
        {
            EnsureLoaded();

            // İki parçalı doğrusal enterpolasyon: ortadan sola ve sağa
            // FARKLI eğimlerde gidiyor (sol taraf 0.05-0.2 arası dar bir
            // bant, sağ taraf 0.2-2.0 arası çok daha geniş bir bant) —
            // tek bir orta nokta (5.5 → 0.2) ikisini de sağlıyor.
            if (display <= MidDisplay)
                return Mathf.Lerp(MinRaw, MidRaw, InverseLerpSafe(MinDisplay, MidDisplay, display));

            return Mathf.Lerp(MidRaw, MaxRaw, InverseLerpSafe(MidDisplay, MaxDisplay, display));
        }
    }

    /// <summary>Ayarlar menüsünde gösterilen, kullanıcı dostu (1-10 arası) değer.</summary>
    public static float Display
    {
        get
        {
            EnsureLoaded();
            return display;
        }
    }

    /// <summary>Ayarlar menüsündeki slider değiştiğinde çağrılır.</summary>
    public static void SetFromDisplay(float displayValue)
    {
        display = Mathf.Clamp(displayValue, MinDisplay, MaxDisplay);
        loaded = true;
        PlayerPrefs.SetFloat(PrefKey, display);
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        display = PlayerPrefs.GetFloat(PrefKey, MidDisplay);
        loaded = true;
    }

    private static float InverseLerpSafe(float a, float b, float value)
    {
        // a == b olma ihtimali yok (sabitler farklı) ama savunmacı kalsın.
        return Mathf.Approximately(a, b) ? 0f : Mathf.InverseLerp(a, b, value);
    }
}
