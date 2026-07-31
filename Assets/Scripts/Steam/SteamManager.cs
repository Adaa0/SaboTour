using UnityEngine;
using Steamworks;

/// <summary>
/// Steamworks API'sini oyunun EN BAŞINDA başlatır. FizzySteamworks transport'u
/// (host/client başlatırken) Steam API'sinin ZATEN init edilmiş olmasını
/// bekliyor — bu script olmadan "Steamworks is not initialized" hatası
/// alınıyordu, çünkü SteamAPI.Init()'i çağıran hiçbir yer yoktu.
///
/// [DefaultExecutionOrder(-9999)] ile bu script, projedeki HER ŞEYDEN önce
/// Awake'ini çalıştırıyor (GameObject'lerin sahnedeki sırasından bağımsız,
/// global bir script çalışma sırası) — NetworkManager host başlatana kadar
/// Steam API kesin hazır oluyor.
///
/// KULLANIM: NetworkManager objesine (Offline Scene, FizzySteamworks ile
/// aynı objeye) ekle. Steam client açık ve steam_appid.txt proje kökünde
/// olmalı (Editor'de test için — gerçek build'de Steam kendisi sağlıyor).
///
/// EDİTÖR'DE ARTIK ÇALIŞMIYOR (bilinçli): TransportSwitcher.cs Editor'de
/// KCP'ye geçtiği için (Multiplayer Play Mode'un iki sanal oyuncusu aynı
/// Steam hesabını paylaştığından Steam P2P testi orada zaten güvenilir
/// değildi), Steam'i Editor'de hiç başlatmaya gerek kalmadı — bu da her
/// Editor testinde Steam client'ın açık olması zorunluluğunu kaldırıyor.
/// Gerçek build'de (`#else` dalı) normal şekilde çalışmaya devam ediyor.
/// </summary>
[DefaultExecutionOrder(-9999)]
public class SteamManager : MonoBehaviour
{
    [Tooltip("Açıksa Editor'de de Steam API başlatılmaya çalışılır (Steam client açık olmalı). Normalde KAPALI kalmalı — Editor testleri artık KCP kullanıyor.")]
    [SerializeField] private bool initializeInEditor = false;

    // Sahne değişince (DontDestroyOnLoad) SteamManager tekrar Awake çalıştırıp
    // SteamAPI.Init()'i ikinci kez çağırmasın diye statik bayrak kullanıyoruz.
    private static bool everInitialized;

    public static bool Initialized { get; private set; }

    void Awake()
    {
#if UNITY_EDITOR
        if (!initializeInEditor)
        {
            Debug.Log("[SteamManager] Editor'de KCP kullanıldığı için Steam API başlatılmadı (bilinçli).");
            return;
        }
#endif

        if (everInitialized)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        try
        {
            if (!Packsize.Test())
                Debug.LogError("[SteamManager] Packsize.Test() başarısız — 32/64 bit derleme uyuşmazlığı olabilir.");

            if (!DllCheck.Test())
                Debug.LogError("[SteamManager] DllCheck.Test() başarısız — Steamworks .dll dosyaları eksik/bozuk olabilir.");

            Initialized = SteamAPI.Init();
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogError("[SteamManager] Steamworks yerel (native) kütüphanesi bulunamadı: " + e.Message);
            Initialized = false;
        }

        if (Initialized)
            Debug.Log("[SteamManager] Steam API başarıyla başlatıldı.");
        else
            Debug.LogError("[SteamManager] SteamAPI.Init() BAŞARISIZ! Kontrol et: " +
                            "(1) Steam client açık ve giriş yapılmış mı, " +
                            "(2) steam_appid.txt proje kökünde ve içinde 480 yazıyor mu.");

        everInitialized = Initialized;
    }

    void Update()
    {
        if (!Initialized) return;

        // Steamworks.NET'in geri çağırma (callback) sistemi elle "pompalanmalı" —
        // yoksa Steam'den gelen event'ler (lobi, davet vb.) hiç işlenmez.
        SteamAPI.RunCallbacks();
    }

    void OnDestroy()
    {
        if (Initialized)
            SteamAPI.Shutdown();
    }
}
