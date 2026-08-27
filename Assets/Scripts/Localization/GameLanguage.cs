using System;
using UnityEngine;

/// <summary>
/// OYUN DİLİ — kalıcı, tüm oturumlar arası hatırlanan tek bir global değer.
/// MouseSensitivitySettings / PlayerNameSettings ile BİREBİR AYNI desen:
/// küçük statik bir sınıf, ayarlar menüsündeki dropdown buraya yazıyor,
/// çeviriye ihtiyacı olan herkes buradan okuyor.
///
/// ── NEDEN UNITY'NİN LOCALIZATION PAKETİ KULLANILMADI ──
/// O paket Addressables bağımlılığı getiriyor (build süreci değişiyor) ve
/// her metni tek tek tablo asset'ine bağlamayı gerektiriyor. Bu projedeki
/// metinlerin çoğu KODDA üretiliyor (ScreenNotice.Show("SON TUR!") gibi),
/// yani tablo sistemi orada zaten ekstra kod isterdi. 150 metin için o
/// altyapıyı kurmak demo tarihine 40 gün kala gereksiz risk.
///
/// ── İLK AÇILIŞTA DİL OTOMATİK SEÇİLİYOR ──
/// Oyuncu hiçbir şey ayarlamadan önce Application.systemLanguage okunuyor:
/// Windows'u Türkçe kullanan Türkçe, geri kalan herkes İngilizce görüyor.
/// BU ÖZELLİĞİN ÖNEMİ: Next Fest'te oyunu indiren yabancı oyuncu, Türkçe
/// bir menüde "Ayarlar" yazısını arayamaz. Dil seçiciyi bulması gereken
/// kişi, zaten menüyü okuyamayan kişi olurdu. Otomatik seçim bu kısır
/// döngüyü tamamen ortadan kaldırıyor — dil seçici sadece "algılama yanlış
/// tuttu" durumu için bir düzeltme yolu.
/// </summary>
public enum Language
{
    Turkish = 0,
    English = 1,
}

public static class GameLanguage
{
    private const string PrefKey = "Settings_Language";

    private static Language current = Language.Turkish;
    private static bool loaded;

    /// <summary>
    /// Dil DEĞİŞTİĞİNDE tetikleniyor. LocalizedText bileşenleri buna abone
    /// olup kendi yazılarını yeniliyor — yani oyuncu dropdown'dan dili
    /// değiştirdiği ANDA ekrandaki her şey değişiyor, sahne yeniden
    /// yüklemeye ya da oyunu kapatıp açmaya gerek yok.
    /// </summary>
    public static event Action OnLanguageChanged;

    public static Language Current
    {
        get
        {
            EnsureLoaded();
            return current;
        }
        set
        {
            EnsureLoaded();
            if (current == value) return;

            current = value;
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();

            // Aboneleri uyar. try/catch YOK — bir abonenin hatası diğerlerini
            // engellememeli diye düşünülebilir, ama sessizce yutmak bu projede
            // daha önce teşhis edilemez hatalara sebep oldu (bkz. CLAUDE.md
            // artifact localStorage dersi). Hata varsa görünsün.
            OnLanguageChanged?.Invoke();
        }
    }

    /// <summary>İngilizce mi? Kısa kontroller için — Loc.T() bunu kendisi kullanıyor.</summary>
    public static bool IsEnglish => Current == Language.English;

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        if (PlayerPrefs.HasKey(PrefKey))
        {
            // Oyuncu daha önce bilinçli bir seçim yapmış — ona saygı duy.
            current = (Language)PlayerPrefs.GetInt(PrefKey, 0);
        }
        else
        {
            // İLK AÇILIŞ: sistem dilinden tahmin et.
            current = DetectSystemLanguage();

            // KAYDETMİYORUZ (bilinçli): PlayerPrefs'e yazsaydık bu tahmin
            // "oyuncunun seçimi" gibi kalıcılaşırdı. Boş bırakınca, oyuncu
            // ileride Windows dilini değiştirirse oyun da onu takip ediyor.
            // İlk gerçek seçim yapıldığı anda (setter) zaten kaydediliyor.
        }
    }

    /// <summary>
    /// Windows/işletim sistemi dili Türkçe ise Türkçe, DİĞER HER DURUMDA
    /// İngilizce. Varsayılanın İngilizce olması bilinçli: bilinmeyen bir dili
    /// (ör. Almanca) kullanan oyuncuya Türkçe göstermek onu tamamen kilitler,
    /// İngilizce göstermek en azından okunabilir bir yedek.
    /// </summary>
    private static Language DetectSystemLanguage()
    {
        return Application.systemLanguage == SystemLanguage.Turkish
            ? Language.Turkish
            : Language.English;
    }

    /// <summary>Ayarlar dropdown'ının göstereceği isimler. Her dil KENDİ adıyla yazılı (evrensel kural: "Türkçe" hep Türkçe yazılır) — böylece yanlış dile düşen oyuncu kendi dilini tanıyabiliyor.</summary>
    public static string[] DisplayNames => new[] { "Türkçe", "English" };
}
