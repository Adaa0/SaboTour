using UnityEngine;
using UnityEngine.UI;
using Mirror;
using Steamworks;

/// <summary>
/// STEAM LOBİSİ + ARKADAŞ DAVET SİSTEMİ
///
/// NE İŞE YARAR: Artık kimsenin SteamID64 kopyalayıp yapıştırmasına gerek yok.
/// Host "Oyun Kur"a basınca gerçek bir Steam lobisi açılıyor; arkadaşlar
/// (a) oyun içi Steam overlay'inden "Arkadaş Davet Et" ile davet edilebiliyor,
/// (b) Steam arkadaş listesinde sağ tık > "Oyuna Katıl" (Join Game) ile
/// kendileri katılabiliyor, (c) oyun kapalıyken gelen daveti kabul ederse
/// Steam oyunu açıp otomatik bağlanıyor.
///
/// ÜÇ KATMANIN İŞ BÖLÜMÜ (karıştırmamak için):
///  1. SteamManager.cs      → Steam API'sini başlatır (SteamAPI.Init).
///  2. FizzySteamworks      → Mirror'ın verisini Steam P2P üzerinden taşır.
///  3. SteamLobbyManager.cs → BU DOSYA. Steam'in "lobi" ve "davet" sosyal
///                            katmanını yönetir; kimin kime bağlanacağını
///                            bulup Mirror'a "şu SteamID'ye bağlan" der.
///
/// NASIL ÇALIŞIR (akış):
///  HOST: HostLobby() → SteamMatchmaking.CreateLobby() → lobi oluşunca
///        lobinin içine kendi SteamID64'ünü "HostAddress" olarak yazar →
///        StartHost() çağırır.
///  CLIENT: Arkadaş daveti kabul eder → Steam GameLobbyJoinRequested
///        callback'ini tetikler → JoinLobby() → lobiye girince
///        "HostAddress"i okur → networkAddress'e yazıp StartClient() çağırır.
///
/// ÖNEMLİ — EDİTÖRDE ÇALIŞMAZ (bilinçli): SteamManager, Editor'de Steam
/// API'sini hiç başlatmıyor (Editor testleri KCP kullanıyor, bkz.
/// TransportSwitcher). Bu yüzden bu script de Editor'de kendini kapatıyor —
/// Multiplayer Play Mode testlerini hiç etkilemiyor. SADECE gerçek build'de,
/// TransportSwitcher `Steam` moduna alınmışken devreye giriyor.
///
/// UNITY'DE YAPILACAKLAR (kod değil, ayar):
///  1. Bu script'i NetworkManager objesine ekle (SteamManager ve
///     TransportSwitcher ile AYNI objeye — Offline Scene'de).
///  2. Lobi ekranındaki "Oyun Kur" butonunu Inspector'daki `Host Button`
///     alanına sürükle (ya da butonun OnClick'ine HostLobby() bağla).
///  3. "Arkadaş Davet Et" butonu oluşturup `Invite Button` alanına sürükle
///     (ya da OnClick'ine InviteFriends() bağla).
///  4. BUILD ALMADAN ÖNCE TransportSwitcher'ın Mode'unu `Steam` yap ve
///     sahneyi KAYDET (yoksa build KCP ile çıkar, kimse bağlanamaz).
/// </summary>
[DefaultExecutionOrder(-9000)] // SteamManager(-9999) API'yi başlattıktan SONRA
public class SteamLobbyManager : MonoBehaviour
{
    public static SteamLobbyManager Instance { get; private set; }

    /// <summary>
    /// Lobinin içine host'un SteamID64'ünü yazdığımız anahtar. Lobiye giren
    /// client bu anahtarı okuyup "kime bağlanacağını" öğreniyor. İsmi
    /// tamamen bize ait, Steam'in özel bir anahtarı değil.
    /// </summary>
    private const string HostAddressKey = "HostAddress";

    [Header("UI Butonları (opsiyonel — OnClick'ten de bağlayabilirsin)")]
    [Tooltip("Basılınca Steam lobisi açıp host başlatan buton.")]
    [SerializeField] private Button hostButton;
    [Tooltip("Basılınca Steam'in davet penceresini açan buton. Sadece lobi kuruluyken anlamlı.")]
    [SerializeField] private Button inviteButton;

    [Header("Ayarlar")]
    [Tooltip("Lobi tipi. FriendsOnly = sadece arkadaş listesindekiler görebilir/katılabilir (demo için doğru seçim). " +
             "Public seçilirse lobi herkese açık aramalarda çıkar.")]
    [SerializeField] private ELobbyType lobbyType = ELobbyType.k_ELobbyTypeFriendsOnly;

    // Steamworks callback'leri. Bunlar Steam'den gelen olayları (lobi
    // oluştu, davet kabul edildi, lobiye girildi) yakalayan dinleyiciler.
    // ÖNEMLİ: Bir alanda TUTULMALILAR — sadece Create() çağırıp sonucu
    // atmazsan C#'ın çöp toplayıcısı (GC) onları silebilir ve callback'ler
    // sessizce hiç çalışmaz (Steamworks.NET'te çok yapılan bir hata).
    private Callback<LobbyCreated_t> lobbyCreated;
    private Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequested;
    private Callback<LobbyEnter_t> lobbyEntered;
    private Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequested;

    private CSteamID currentLobbyId;
    private bool callbacksRegistered;

    /// <summary>Şu an açık/katılınmış bir Steam lobisi var mı?</summary>
    public bool InLobby => currentLobbyId.IsValid();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        if (!SteamManager.Initialized)
        {
            // Editor'de bu tamamen normal (Steam hiç başlatılmıyor). Gerçek
            // build'de görülüyorsa Steam client kapalı ya da steam_appid.txt
            // eksik demektir.
            Debug.Log("[SteamLobbyManager] Steam API başlatılmamış — lobi/davet sistemi devre dışı. " +
                      "(Editor'de bu beklenen davranış, gerçek build'de Steam client'ı kontrol et.)");
            SetInviteButtonInteractable(false);
            return;
        }

        // BİLİNEN TUZAĞA KARŞI KORUMA: Build almadan önce TransportSwitcher'ın
        // Mode'unu Steam'e çevirmek kolayca unutuluyor. KCP modunda Steam
        // lobisi kurarsak, davet edilen arkadaş lobiye girer, host'un
        // SteamID'sini alır ama KCP o adrese bağlanamaz — hiçbir hata vermeden
        // "bağlanmıyor" gibi görünür. Bu yüzden baştan net bir hata veriyoruz.
        TransportSwitcher switcher = GetComponent<TransportSwitcher>();
        if (switcher != null && !switcher.IsSteamMode)
        {
            Debug.LogError("[SteamLobbyManager] TransportSwitcher 'Kcp' modunda olduğu için Steam lobi/davet " +
                           "sistemi KAPATILDI. Steam üzerinden oynanacaksa NetworkManager objesindeki " +
                           "TransportSwitcher > Mode alanını 'Steam' yapıp sahneyi kaydet ve build'i yenile.");
            SetInviteButtonInteractable(false);
            return;
        }

        RegisterCallbacks();
        WireButtons();
        SetInviteButtonInteractable(false); // Lobi kurulana kadar davet edilemez

        // OYUN KAPALIYKEN GELEN DAVET: Steam, oyunu açarken komut satırına
        // "+connect_lobby <lobiID>" ekliyor. Oyun zaten açıksa bunun yerine
        // GameLobbyJoinRequested callback'i geliyor (aşağıda) — yani iki ayrı
        // yol var ve ikisi de ele alınmalı, yoksa "oyun kapalıyken davet
        // kabul etme" senaryosu sessizce çalışmaz.
        TryJoinFromCommandLine();
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered) return;

        lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
        lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        richPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoinRequested);

        callbacksRegistered = true;
    }

    private void WireButtons()
    {
        if (hostButton != null) hostButton.onClick.AddListener(HostLobby);
        if (inviteButton != null) inviteButton.onClick.AddListener(InviteFriends);
    }

    private void SetInviteButtonInteractable(bool value)
    {
        if (inviteButton != null) inviteButton.interactable = value;
    }

    // ─────────────────────────────────────────────────────────────────
    // HOST TARAFI
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Oyun Kur" butonu bunu çağırıyor. Önce Steam lobisi açılıyor; Mirror
    /// host'u LOBİ BAŞARIYLA OLUŞTUKTAN SONRA (OnLobbyCreated) başlatılıyor.
    ///
    /// NEDEN BU SIRA: Önce StartHost() deyip sonra lobi açmayı denersek ve
    /// lobi açılamazsa, ortada kimsenin katılamayacağı yarım bir host kalır.
    /// Bu sırayla, lobi kurulamazsa host hiç başlamıyor ve kullanıcıya net
    /// bir hata gösterebiliyoruz.
    /// </summary>
    public void HostLobby()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobbyManager] Steam başlatılmadığı için lobi kurulamıyor.");
            return;
        }

        if (NetworkServer.active || NetworkClient.isConnected)
        {
            Debug.LogWarning("[SteamLobbyManager] Zaten bir oturum var — önce mevcut oyundan çık.");
            return;
        }

        // maxConnections = Mirror'ın izin verdiği en fazla BAĞLANTI sayısı.
        // Steam lobisi ise host DAHİL toplam üye sayısını istiyor, o yüzden +1.
        int maxMembers = NetworkManager.singleton != null
            ? NetworkManager.singleton.maxConnections + 1
            : 6;

        SteamMatchmaking.CreateLobby(lobbyType, maxMembers);
        Debug.Log($"[SteamLobbyManager] Steam lobisi oluşturuluyor (en fazla {maxMembers} kişi)...");
    }

    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError($"[SteamLobbyManager] Lobi oluşturulamadı: {callback.m_eResult}");
            return;
        }

        currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);

        // Lobinin içine kendi SteamID'mizi yazıyoruz — lobiye giren herkes
        // bunu okuyup "asıl oyunu kimin barındırdığını" öğreniyor. Steam
        // lobisi burada bir "buluşma noktası"; oyunun gerçek verisi yine
        // FizzySteamworks üzerinden P2P akıyor.
        string mySteamId = SteamUser.GetSteamID().ToString();
        SteamMatchmaking.SetLobbyData(currentLobbyId, HostAddressKey, mySteamId);

        // Rich Presence "connect" anahtarı: Steam arkadaş listesinde bu
        // oyuncunun yanında "Oyuna Katıl" (Join Game) butonunun çıkmasını
        // sağlayan şey bu. Değerin formatı Steam tarafından tanımlı.
        SteamFriends.SetRichPresence("connect", $"+connect_lobby {currentLobbyId}");

        NetworkManager.singleton.StartHost();
        SetInviteButtonInteractable(true);

        Debug.Log($"[SteamLobbyManager] Lobi hazır (id: {currentLobbyId}), host başlatıldı. Artık arkadaş davet edebilirsin.");
    }

    /// <summary>
    /// "Arkadaş Davet Et" butonu bunu çağırıyor. Steam'in kendi overlay
    /// penceresini açıyor — oyuncu oradan arkadaş listesinden seçip davet
    /// gönderiyor. Kendi arkadaş listesi UI'ı yazmamıza gerek yok.
    ///
    /// NOT: Overlay'in açılabilmesi için oyunun Steam üzerinden başlatılmış
    /// olması gerekiyor (exe'ye çift tıklayarak değil).
    /// </summary>
    public void InviteFriends()
    {
        if (!InLobby)
        {
            Debug.LogWarning("[SteamLobbyManager] Henüz bir lobi yok — önce oyunu kur.");
            return;
        }

        // OVERLAY KONTROLÜ: Overlay yoksa ActivateGameOverlayInviteDialog
        // HİÇBİR ŞEY YAPMIYOR ve hata da vermiyor — buton "bozuk" görünüyor.
        // (Gerçekten yaşandı.) Bu yüzden durumu açıkça logluyoruz.
        if (!SteamUtils.IsOverlayEnabled())
        {
            Debug.LogWarning(
                "[SteamLobbyManager] Steam OVERLAY kapalı/enjekte olmamış — davet penceresi açılamıyor.\n" +
                "SEBEP: Overlay'in çalışması için oyunun Steam ÜZERİNDEN başlatılması gerekiyor " +
                "(exe'ye çift tıklamak yetmiyor).\n" +
                "ÇÖZÜM 1: Steam > Kitaplık > Oyun Ekle > Steam Dışı Bir Oyun Ekle ile exe'yi ekleyip " +
                "Steam'den başlat.\n" +
                "ÇÖZÜM 2 (bu olmadan da çalışır): Arkadaşın, Steam arkadaş listesinde sana sağ tıklayıp " +
                "'Oyuna Katıl' diyebilir — Rich Presence ayarlandığı için o yol overlay'e ihtiyaç duymuyor.");
            return;
        }

        SteamFriends.ActivateGameOverlayInviteDialog(currentLobbyId);
        Debug.Log("[SteamLobbyManager] Steam davet penceresi açılıyor...");
    }

    // ─────────────────────────────────────────────────────────────────
    // CLIENT TARAFI (davet kabul etme / katılma)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// OYUN AÇIKKEN davet kabul edildiğinde ya da arkadaş listesinden
    /// "Oyuna Katıl" tıklandığında Steam bunu tetikliyor.
    /// </summary>
    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        Debug.Log("[SteamLobbyManager] Davet kabul edildi, lobiye katılınıyor...");
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    /// <summary>
    /// Rich Presence üzerinden katılma isteği (arkadaş listesindeki "Oyuna
    /// Katıl" butonunun bazı akışları buradan geliyor). Gelen bağlantı
    /// dizesi "+connect_lobby &lt;id&gt;" formatında.
    /// </summary>
    private void OnRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback)
    {
        Debug.Log($"[SteamLobbyManager] Rich Presence katılma isteği: {callback.m_rgchConnect}");
        TryJoinFromConnectString(callback.m_rgchConnect);
    }

    /// <summary>
    /// Lobiye GİRİLDİ. Bu callback hem host'ta hem client'ta çalışıyor
    /// (host da kendi lobisine "girmiş" sayılıyor) — bu yüzden host için
    /// tekrar StartClient çağırmamak adına NetworkServer.active kontrolü şart.
    /// </summary>
    private void OnLobbyEntered(LobbyEnter_t callback)
    {
        currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);

        // Host zaten StartHost() ile hem sunucu hem client — burada tekrar
        // bağlanmaya çalışırsa mevcut oturumu bozar.
        if (NetworkServer.active)
        {
            SetInviteButtonInteractable(true);
            return;
        }

        string hostAddress = SteamMatchmaking.GetLobbyData(currentLobbyId, HostAddressKey);

        if (string.IsNullOrEmpty(hostAddress))
        {
            Debug.LogError("[SteamLobbyManager] Lobide host adresi bulunamadı — bağlanılamıyor. " +
                           "(Host'un oyunu eski bir sürüm olabilir.)");
            return;
        }

        // FizzySteamworks için "adres" = host'un SteamID64'ü. Eskiden bu
        // değeri oyuncu ELLE kutuya yazıyordu; artık lobiden otomatik geliyor.
        NetworkManager.singleton.networkAddress = hostAddress;
        NetworkManager.singleton.StartClient();
        SetInviteButtonInteractable(true);

        Debug.Log($"[SteamLobbyManager] Lobiye girildi, host'a bağlanılıyor: {hostAddress}");
    }

    // ─────────────────────────────────────────────────────────────────
    // OYUN KAPALIYKEN GELEN DAVET
    // ─────────────────────────────────────────────────────────────────

    private void TryJoinFromCommandLine()
    {
        foreach (string arg in System.Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith("+connect_lobby"))
            {
                TryJoinFromConnectString(string.Join(" ", System.Environment.GetCommandLineArgs()));
                return;
            }
        }
    }

    /// <summary>
    /// "+connect_lobby &lt;id&gt;" biçimindeki dizeden lobi kimliğini ayıklayıp
    /// katılır. Hem komut satırı hem Rich Presence yolu bunu kullanıyor.
    /// </summary>
    private void TryJoinFromConnectString(string connect)
    {
        if (string.IsNullOrEmpty(connect)) return;

        const string token = "+connect_lobby";
        int index = connect.IndexOf(token, System.StringComparison.Ordinal);
        if (index < 0) return;

        string rest = connect.Substring(index + token.Length).Trim();
        // Arkasında başka argümanlar olabilir — sadece ilk kelimeyi al.
        int space = rest.IndexOf(' ');
        if (space > 0) rest = rest.Substring(0, space);

        if (!ulong.TryParse(rest, out ulong lobbyId))
        {
            Debug.LogWarning($"[SteamLobbyManager] Lobi kimliği okunamadı: '{rest}'");
            return;
        }

        Debug.Log($"[SteamLobbyManager] Davetle açıldı, lobiye katılınıyor: {lobbyId}");
        SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
    }

    // ─────────────────────────────────────────────────────────────────
    // TEMİZLİK
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// YARIŞ BAŞLARKEN çağrılır — lobiyi kilitler.
    ///
    /// NEDEN GEREKLİ: Kilitlenmezse, yarış başladıktan sonra bir arkadaş
    /// "Oyuna Katıl" diyebiliyor. O oyuncunun rol ataması (yarışçı/sabotajcı)
    /// lobide yapıldığı için hiç yok — MyNetworkManager onu varsayılan olarak
    /// YARIŞÇI sayıp yarışın ortasında, başlangıç çizgisinde spawn ediyor.
    /// Hem o oyuncu için anlamsız hem yarışı bozan bir durum.
    ///
    /// SetLobbyJoinable(false) ile lobi Steam tarafında "dolu/kapalı" hale
    /// geliyor: arkadaş listesindeki "Oyuna Katıl" butonu kayboluyor ve
    /// davet gönderilse bile katılamıyorlar.
    /// </summary>
    public void SetLobbyJoinable(bool joinable)
    {
        if (!SteamManager.Initialized || !InLobby) return;

        SteamMatchmaking.SetLobbyJoinable(currentLobbyId, joinable);

        // Rich Presence "connect" anahtarı, arkadaş listesindeki "Oyuna Katıl"
        // butonunu GÖSTEREN şey — lobi kilitliyken bunu da temizlemezsek
        // buton görünmeye devam eder, tıklayan arkadaş sebepsiz bir hata alır.
        if (joinable)
            SteamFriends.SetRichPresence("connect", $"+connect_lobby {currentLobbyId}");
        else
            SteamFriends.SetRichPresence("connect", string.Empty);

        Debug.Log($"[SteamLobbyManager] Lobi {(joinable ? "katılıma AÇIK" : "KİLİTLENDİ (yarış başladı)")}.");
    }

    /// <summary>
    /// Oyundan çıkarken/ana menüye dönerken çağrılmalı — Steam lobisinden
    /// ayrılıp Rich Presence'i temizler, yoksa arkadaşlar artık var olmayan
    /// bir oyuna "Katıl" görmeye devam eder.
    /// </summary>
    public void LeaveLobby()
    {
        if (!SteamManager.Initialized || !InLobby) return;

        SteamMatchmaking.LeaveLobby(currentLobbyId);
        SteamFriends.ClearRichPresence();
        currentLobbyId = default;
        SetInviteButtonInteractable(false);

        Debug.Log("[SteamLobbyManager] Steam lobisinden çıkıldı.");
    }

    void OnDestroy()
    {
        LeaveLobby();
    }
}
