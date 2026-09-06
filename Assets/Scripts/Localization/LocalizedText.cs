using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PREFABTAKİ SABİT YAZILARI ÇEVİREN BİLEŞEN.
///
/// Kodda üretilen metinler Loc.T() ile çevriliyor, ama buton yazıları ve
/// panel başlıkları KODDA DEĞİL — prefabın içinde, TextMeshPro objesinin
/// kendi alanında duruyor ("Oyun Kur", "Hazırım", "Nasıl Oynanır"...).
/// Onlara koddan tek tek ulaşmak, her biri için PauseMenuController'a bir
/// referans alanı eklemek demekti. Bu bileşen o işi tersine çeviriyor:
/// yazının KENDİSİ hangi anahtara ait olduğunu biliyor.
///
/// KULLANIMI: Yazı objesini seç → Add Component → Localized Text →
/// "Key" alanına sözlükteki anahtarı yaz (ör. menu.host).
/// (Elle tek tek eklemene gerek yok — "SaboTour > Dil > Prefablara Çeviri
/// Etiketi Ekle" aracı bilinen yazıları otomatik eşleştiriyor.)
///
/// ── HEM TMP HEM LEGACY TEXT DESTEKLENİYOR ──
/// Bu projede ikisi karışık kullanılıyor (ayarlar menüsü legacy UI.Dropdown,
/// sıralama tablosu TextMeshProUGUI). Hangisi varsa onu buluyor.
/// </summary>
[AddComponentMenu("SaboTour/Localized Text")]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("Loc.cs sözlüğündeki anahtar (ör. menu.host). Yanlış/eksik yazarsan " +
             "ekranda anahtarın kendisi görünür ve Console'a uyarı düşer — " +
             "yani sessizce bozulmaz, fark edersin.")]
    public string key;

    [Tooltip("Metnin başına/sonuna eklenecek sabit karakterler (ör. \"► \"). " +
             "Çeviriye dahil değil, her dilde aynı kalıyor.")]
    public string prefix = "";
    public string suffix = "";

    private TMP_Text tmp;
    private Text legacy;

    private void Awake()
    {
        tmp = GetComponent<TMP_Text>();
        if (tmp == null) legacy = GetComponent<Text>();
    }

    private void OnEnable()
    {
        // Dil değişince kendini yenilemek için abone oluyoruz. OnDisable'da
        // aboneliği BIRAKMAK ŞART: bu bileşenler DontDestroyOnLoad prefabların
        // (PauseMenu, RaceHud) içinde de yaşıyor ve sahne geçişlerinde yok
        // edilen kopyalar olabiliyor. Bırakılmasa, ölü objelere işaret eden
        // abonelikler birikirdi — bu projede aynı sızıntı SteamLobbyManager'ın
        // Steamworks callback'lerinde bir kez yaşandı.
        GameLanguage.OnLanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        GameLanguage.OnLanguageChanged -= Refresh;
    }

    /// <summary>Yazıyı güncel dile göre yeniden yazıyor.</summary>
    public void Refresh()
    {
        if (string.IsNullOrEmpty(key)) return;

        string value = prefix + Loc.T(key) + suffix;

        if (tmp != null) tmp.text = value;
        else if (legacy != null) legacy.text = value;
    }

    /// <summary>
    /// Anahtarı çalışma anında değiştirmek için (ör. "Hazırım" ↔ "Hazır Değilim"
    /// gibi duruma göre değişen butonlar). Değiştirir ve hemen yeniler.
    /// </summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        Refresh();
    }
}
