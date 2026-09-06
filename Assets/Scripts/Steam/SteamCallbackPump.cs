using UnityEngine;
using Steamworks;

/// <summary>
/// Steam API'sinin GERÇEK sahibi: her kare `SteamAPI.RunCallbacks()` çağırır
/// ve oyun kapanırken `SteamAPI.Shutdown()` yapar. `SteamManager` sadece
/// başlatma tetikleyicisi; asıl ömür yönetimi burada.
///
/// 🚨 NEDEN AYRI BİR OBJE (20 Ağustos 2026'da yaşanan bir bug'ın çözümü):
/// Steam API'sinin ömrü UYGULAMANIN ömrü kadar olmalı. Eskiden bu iş
/// `SteamManager`'ın kendi `Update`/`OnDestroy`'undaydı ve o component
/// NetworkManager objesinin üzerinde duruyor. Mirror ise ana menüye her
/// dönüşte o objeyi `SceneManager.MoveGameObjectToScene` ile
/// DontDestroyOnLoad'dan çıkarıp YOK EDİYOR (bkz. `SteamManager.cs`
/// başındaki uzun not) — yani `OnDestroy` tetiklenip **oyunun ortasında
/// `SteamAPI.Shutdown()`** çağrılıyordu. Ondan sonra Steam lobisi kurmak
/// imkânsız hale geliyor, oyunu kapatıp açmak gerekiyordu.
///
/// Bu obje çalışma anında oluşturuluyor ve HİÇBİR SAHNEYE AİT DEĞİL, bu
/// yüzden Mirror'ın sahne taşıma numarası ona ulaşamıyor. (Aynı desen
/// projede zaten kullanılıyor: PauseMenu ve RacerMinimap de kendi kalıcı
/// objelerini runtime'da kuruyor.)
///
/// Hierarchy'de "SteamCallbackPump" adıyla DontDestroyOnLoad altında
/// görünür — orada durması NORMAL, silme.
/// </summary>
public class SteamCallbackPump : MonoBehaviour
{
    private static SteamCallbackPump instance;

    /// <summary>SteamManager, API başarıyla açıldıktan SONRA çağırıyor.</summary>
    internal static void Create()
    {
        if (instance != null) return;

        var host = new GameObject("SteamCallbackPump");
        instance = host.AddComponent<SteamCallbackPump>();

        DontDestroyOnLoad(host);
    }

    void Update()
    {
        if (!SteamManager.Initialized) return;

        // Steamworks.NET'in geri çağırma (callback) sistemi elle
        // "pompalanmalı" — yoksa Steam'den gelen olaylar (lobi oluştu, davet
        // geldi vb.) hiç işlenmez. Sessizce çalışmaz, hata da vermez.
        SteamAPI.RunCallbacks();
    }

    void OnApplicationQuit() => Shutdown();

    void OnDestroy() => Shutdown();

    /// <summary>
    /// Steam API'sini kapatır. Normalde SADECE uygulama kapanırken çalışır
    /// (bu obje hiçbir sahneye ait olmadığı için sahne geçişlerinde ölmüyor).
    /// `Initialized` kontrolü sayesinde iki kez çağrılması zararsız —
    /// OnApplicationQuit ve OnDestroy arka arkaya gelebiliyor.
    /// </summary>
    private void Shutdown()
    {
        if (!SteamManager.Initialized) return;

        // ÖNCE bayrağı düşür: Shutdown sırasında başka bir script Steam
        // çağrısı yapmaya kalkarsa kapanmış bir API'ye gitmesin.
        SteamManager.MarkShutdown();
        SteamAPI.Shutdown();

        Debug.Log("[SteamCallbackPump] Steam API kapatıldı.");
    }
}
