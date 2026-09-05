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
/// EDİTÖR'DE ÇALIŞMIYOR (bilinçli): TransportSwitcher.cs Editor'de KCP'ye
/// geçtiği için (Multiplayer Play Mode'un iki sanal oyuncusu aynı Steam
/// hesabını paylaştığından Steam P2P testi orada zaten güvenilir değildi),
/// Steam'i Editor'de hiç başlatmaya gerek kalmadı.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🚨 20 AĞUSTOS 2026 — BU DOSYA BAŞTAN YAZILDI. ESKİ HÂLİ, SADECE STEAM
///    BUILD'İNDE GÖRÜLEN ŞU HATANIN SEBEBİYDİ: "ana menüye dönünce lobide
///    hiçbir buton yok, her yeni oyun için oyunu kapatıp açmak gerekiyor."
///
/// ESKİ KOD İKİ ÖLÜMCÜL ŞEY YAPIYORDU:
///   1. `OnDestroy()` içinde `SteamAPI.Shutdown()` çağırıyordu.
///   2. `Awake()` içinde, daha önce init edilmişse `Destroy(gameObject)`
///      diyordu — yani KENDİ OBJESİNİ, ki o obje NetworkManager'ın TA
///      KENDİSİ.
///
/// NEDEN PATLIYOR: Bu script `DontDestroyOnLoad(gameObject)` çağırıp
/// "objem sonsuza kadar yaşar" varsayıyordu. AMA MIRROR BUNU GERİ ALIYOR —
/// ana menüye dönerken (`NetworkManager.cs` → `StopServer()` ~587. satır ve
/// `OnClientDisconnectInternal()` ~1280. satır):
///     SceneManager.MoveGameObjectToScene(gameObject, GetActiveScene());
/// ile NetworkManager objesini DontDestroyOnLoad'DAN ÇIKARIYOR, kendi
/// yorumuyla "let a fresh Network Manager be created". Sonuç: obje ana menü
/// yüklenirken YOK EDİLİYOR.
///
/// ZİNCİR:
///   (a) Obje ölürken `OnDestroy` → `SteamAPI.Shutdown()` → Steam API o
///       süreç için TAMAMEN ÖLÜYOR. (`Initialized` ise `true` kalıyordu,
///       yani kod hâlâ Steam'in ayakta olduğunu sanıyordu.)
///   (b) Offline Scene'in TAZE NetworkManager'ı geliyor, üzerindeki yeni
///       SteamManager `everInitialized == true` görüp `Destroy(gameObject)`
///       diyor → NetworkManager, TransportSwitcher, FizzySteamworks,
///       SteamLobbyManager, LobbyManager ve LOBİ CANVAS'I TOPTAN SİLİNİYOR.
///       "Hiçbir buton gözükmüyor" şikayeti buydu — butonlar gizlenmiyordu,
///       objeleriyle birlikte YOK EDİLİYORLARDI.
///   Üstelik `-9999` çalışma sırası yüzünden bu, Mirror'ın kendi Awake'inden
///   ÖNCE oluyordu.
///
/// NEDEN SADECE STEAM BUILD'İNDE: Editor'de/KCP'de `Awake` en baştaki
/// `#if UNITY_EDITOR` dalından dönüyor, yani bu kodun hiçbiri çalışmıyor.
///
/// ÇÖZÜM: Steam API'sinin ömrü artık NetworkManager objesine BAĞLI DEĞİL.
/// Bu component sadece bir "tetikleyici"; asıl iş (her kare RunCallbacks +
/// kapanışta Shutdown) `SteamCallbackPump`'ta, çalışma anında oluşturulan
/// KENDİ kalıcı objesinde — Mirror'ın hiç dokunmadığı bir yerde.
/// (Aynı desen projede zaten var: PauseMenu ve RacerMinimap de kendi kalıcı
/// objelerini runtime'da kuruyor.)
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
[DefaultExecutionOrder(-9999)]
public class SteamManager : MonoBehaviour
{
    [Tooltip("Açıksa Editor'de de Steam API başlatılmaya çalışılır (Steam client açık olmalı). Normalde KAPALI kalmalı — Editor testleri artık KCP kullanıyor.")]
    [SerializeField] private bool initializeInEditor = false;

    // Init SADECE BİR KEZ denenmeli. Steamworks'te SteamAPI.Shutdown()'dan
    // sonra aynı süreçte yeniden Init etmek desteklenmiyor — bu yüzden
    // "tekrar başlatalım" gibi bir kurtarma yolu YOK, tek doğru davranış
    // hiç kapatmamak.
    private static bool initAttempted;

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

        Initialize();

        // 🚨 ESKİDEN BURADA `Destroy(gameObject)` VARDI — NetworkManager'ı
        // komple siliyordu (bkz. yukarıdaki uzun not). Artık hiçbir şey
        // silinmiyor: bu component ikinci kez çalıştığında Initialize()
        // zaten sessizce geri dönüyor, yani fazlalık kopya tamamen zararsız.
    }

    /// <summary>
    /// Steam API'sini (bir kez) başlatır ve callback pompasını kurar.
    /// İkinci kez çağrılırsa hiçbir şey yapmaz.
    /// </summary>
    private void Initialize()
    {
        if (initAttempted) return;
        initAttempted = true;

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
        {
            Debug.Log("[SteamManager] Steam API başarıyla başlatıldı.");
            SteamCallbackPump.Create();
        }
        else
        {
            Debug.LogError("[SteamManager] SteamAPI.Init() BAŞARISIZ! Kontrol et: " +
                           "(1) Steam client açık ve giriş yapılmış mı, " +
                           "(2) Oyunu Steam ÜZERİNDEN mi başlattın? Exe'ye doğrudan çift " +
                           "tıklıyorsan, exe'nin yanında steam_appid.txt olmalı ve içinde " +
                           "o build'in App ID'si yazmalı (playtest: 5071180, ana oyun: 5070720).");
        }
    }

    /// <summary>SteamCallbackPump, API'yi kapatırken bunu çağırıyor.</summary>
    internal static void MarkShutdown() => Initialized = false;
}
