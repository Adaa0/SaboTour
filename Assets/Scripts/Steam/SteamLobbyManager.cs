using System.Collections.Generic;
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
    // KENDİNİ ONARAN INSTANCE — gerekçesi LobbyManager'daki uzun notla aynı.
    // Kısaca: Mirror ana menüye dönerken NetworkManager objesini
    // DontDestroyOnLoad'dan çıkarıp yok ediyor, Offline Scene'in taze kopyası
    // yaşıyor. Elimizdeki referans ölmüşse (Unity'de `== null` true döner)
    // sahnedeki yaşayan kopyaya geçiyoruz.
    private static SteamLobbyManager _instance;

    public static SteamLobbyManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindAnyObjectByType<SteamLobbyManager>(FindObjectsInactive.Include);

            return _instance;
        }
        private set => _instance = value;
    }

    /// <summary>
    /// Lobinin içine host'un SteamID64'ünü yazdığımız anahtar. Lobiye giren
    /// client bu anahtarı okuyup "kime bağlanacağını" öğreniyor. İsmi
    /// tamamen bize ait, Steam'in özel bir anahtarı değil.
    /// </summary>
    private const string HostAddressKey = "HostAddress";

    /// <summary>
    /// Lobinin hangi oyun sürümüne ait olduğunu yazdığımız anahtar. Hızlı
    /// Katıl SADECE aynı sürümdeki lobileri arıyor — playtest sırasında
    /// herkes aynı anda güncellemiyor ve farklı sürümler birbirine
    /// bağlanınca sessizce bozuk davranışlar çıkıyor (Mirror mesaj
    /// yapıları değişmiş olabiliyor).
    /// </summary>
    private const string VersionKey = "SaboTourVersion";

    [Header("UI Butonları (opsiyonel — OnClick'ten de bağlayabilirsin)")]
    [Tooltip("Basılınca Steam lobisi açıp host başlatan buton.")]
    [SerializeField] private Button hostButton;
    [Tooltip("Basılınca Steam'in davet penceresini açan buton. Sadece lobi kuruluyken anlamlı.")]
    [SerializeField] private Button inviteButton;

    [Tooltip("Basılınca yer olan bir PUBLIC oyun arar; bulamazsa kendisi public bir oyun kurup bekler.")]
    [SerializeField] private Button quickJoinButton;

    [Header("Ayarlar")]
    [Tooltip("Lobi tipi. FriendsOnly = sadece arkadaş listesindekiler görebilir/katılabilir (demo için doğru seçim). " +
             "Public seçilirse lobi herkese açık aramalarda çıkar.")]
    [SerializeField] private ELobbyType lobbyType = ELobbyType.k_ELobbyTypeFriendsOnly;

    [Tooltip("Hızlı Katıl kimseyi bulamayınca kuracağı lobinin tipi. PUBLIC olmalı — " +
             "aksi halde kurduğu oyunu başka hiç kimse bulamaz ve buton anlamsızlaşır.")]
    [SerializeField] private ELobbyType quickJoinLobbyType = ELobbyType.k_ELobbyTypePublic;

    [Tooltip("Hızlı Katıl'ın tarayacağı en fazla lobi sayısı.")]
    [SerializeField] private int quickJoinSearchLimit = 20;

    // Steamworks callback'leri. Bunlar Steam'den gelen olayları (lobi
    // oluştu, davet kabul edildi, lobiye girildi) yakalayan dinleyiciler.
    // ÖNEMLİ: Bir alanda TUTULMALILAR — sadece Create() çağırıp sonucu
    // atmazsan C#'ın çöp toplayıcısı (GC) onları silebilir ve callback'ler
    // sessizce hiç çalışmaz (Steamworks.NET'te çok yapılan bir hata).
    private Callback<LobbyCreated_t> lobbyCreated;
    private Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequested;
    private Callback<LobbyEnter_t> lobbyEntered;
    private Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequested;

    // Lobi ARAMA sonucu bir Callback değil CallResult ile geliyor: arama bizim
    // gönderdiğimiz tek bir isteğe verilen cevap, genel bir olay değil.
    private CallResult<LobbyMatchList_t> lobbyMatchList;

    private CSteamID currentLobbyId;
    private bool callbacksRegistered;

    // "Oyun Kur"a basıldı ama Steam'den henüz cevap gelmedi. Bu bayrak
    // olmadan butona arka arkaya basmak arka arkaya CreateLobby isteği
    // gönderiyordu (bir testte 10 istek görüldü) — her biri ayrı bir lobi
    // açıyor, sadece sonuncusundan çıkılabiliyor, gerisi Steam tarafında
    // asılı kalıyordu.
    private bool creatingLobby;

    // Şu anki lobiyi BİZ mi kurduk? OnLobbyEntered hem host'ta hem client'ta
    // çalışıyor ve ikisini ayırmak gerekiyor. Eskiden bu ayrım
    // `NetworkServer.active` ile yapılıyordu — ama az önce host'tan çıkmış bir
    // oyuncuda Mirror'ın sunucusu henüz tam kapanmamış olabiliyor ve o kişi
    // yanlışlıkla "host" sayılıp bağlanmadan geri dönülüyordu (aşağıdaki
    // bug notuna bak).
    private bool hostingThisLobby;

    // ── Hızlı Katıl durumu ───────────────────────────────────────────────
    // Arama sürüyor mu. Butona arka arkaya basmayı ve arama sonucu gelmeden
    // "Oyun Kur"a basmayı engelliyor.
    private bool quickJoining;

    // Aramadan dönen aday lobiler ve sırada hangisini deneyeceğimiz.
    // Tek adayla yetinmiyoruz: bulduğumuz lobi bu arada dolmuş ya da yarışa
    // başlamış olabilir, o zaman sıradakine geçiyoruz.
    private readonly List<CSteamID> quickJoinCandidates = new List<CSteamID>();
    private int quickJoinIndex;

    /// <summary>Şu an açık/katılınmış bir Steam lobisi var mı?</summary>
    public bool InLobby => currentLobbyId.IsValid();

    void Awake()
    {
        // ══ BURADAKİ `Destroy(this)` İKİ BUG'IN SEBEBİYDİ — KALDIRILDI ══
        // Eski kod, "zaten bir Instance var" görünce KENDİNİ yok ediyordu.
        // Ama ana menüye dönerken YAŞAYACAK olan kopya tam da bu yeni kopya
        // (bkz. LobbyManager.Awake'teki uzun Mirror notu) — yani script
        // her oturum sonunda kendi kendini öldürüyordu. Sonuç: Start() hiç
        // çalışmıyor → `WireButtons()` çağrılmıyor → "Oyun Kur" butonuna
        // basınca HİÇBİR ŞEY OLMUYOR, ikinci bir lobi kurulamıyordu.
        // Artık en son gelen kopya sahibi oluyor; ölen kopya OnDestroy'da
        // kendi callback'lerini bırakıyor.
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
        lobbyMatchList = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);

        callbacksRegistered = true;
    }

    private void WireButtons()
    {
        // Önce RemoveListener: aynı kopyada bu metot iki kez çalışırsa buton
        // tek tıkta iki kez tetiklenir (iki CreateLobby isteği) — Unity'de
        // aynı delegate'i iki kez eklemek engellenmiyor.
        if (hostButton != null)
        {
            hostButton.onClick.RemoveListener(HostLobby);
            hostButton.onClick.AddListener(HostLobby);
        }

        if (inviteButton != null)
        {
            inviteButton.onClick.RemoveListener(InviteFriends);
            inviteButton.onClick.AddListener(InviteFriends);
        }

        if (quickJoinButton != null)
        {
            quickJoinButton.onClick.RemoveListener(QuickJoin);
            quickJoinButton.onClick.AddListener(QuickJoin);
        }
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

        // Start() birkaç sebeple erken dönebiliyor (KCP modu, Steam client
        // kapalı). O durumda callback'ler hiç kaydedilmemiş olur ve istek
        // gönderirsek cevabı DUYAMAYIZ — buton sonsuza kadar "kuruluyor"da
        // kalırdı. Baştan net bir hata vermek daha iyi.
        if (!callbacksRegistered)
        {
            Debug.LogError("[SteamLobbyManager] Lobi sistemi devre dışı (TransportSwitcher 'Kcp' modunda " +
                           "ya da Steam başlatılamadı) — istek gönderilmedi.");
            return;
        }

        if (NetworkServer.active || NetworkClient.isConnected)
        {
            Debug.LogWarning("[SteamLobbyManager] Zaten bir oturum var — önce mevcut oyundan çık.");
            return;
        }

        if (creatingLobby || quickJoining)
        {
            Debug.Log("[SteamLobbyManager] Zaten bir lobi kurma/arama işlemi sürüyor — yeni istek gönderilmedi.");
            return;
        }

        CreateLobby(lobbyType);
    }

    /// <summary>
    /// Asıl lobi kurma işi. `HostLobby()` (arkadaşlarla) ve Hızlı Katıl
    /// (kimse bulunamayınca, public) aynı yerden geçsin diye ayrıldı — tek
    /// fark lobinin TİPİ.
    /// </summary>
    private void CreateLobby(ELobbyType type)
    {
        // Elde kalmış eski bir lobi varsa (ör. önceki oturumdan) önce ondan çık —
        // yoksa Steam tarafında iki lobinin üyesi kalıp davet/katılma akışı
        // hangisini kullanacağını şaşırıyor.
        if (InLobby) LeaveLobby();

        // maxConnections = Mirror'ın izin verdiği en fazla BAĞLANTI sayısı.
        // Steam lobisi ise host DAHİL toplam üye sayısını istiyor, o yüzden +1.
        int maxMembers = NetworkManager.singleton != null
            ? NetworkManager.singleton.maxConnections + 1
            : 6;

        creatingLobby = true;
        SteamMatchmaking.CreateLobby(type, maxMembers);
        Debug.Log($"[SteamLobbyManager] Steam lobisi oluşturuluyor — tip: {type}, en fazla {maxMembers} kişi...");
    }

    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        creatingLobby = false;

        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError($"[SteamLobbyManager] Lobi oluşturulamadı: {callback.m_eResult}");
            return;
        }

        currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        hostingThisLobby = true;

        // Lobinin içine kendi SteamID'mizi yazıyoruz — lobiye giren herkes
        // bunu okuyup "asıl oyunu kimin barındırdığını" öğreniyor. Steam
        // lobisi burada bir "buluşma noktası"; oyunun gerçek verisi yine
        // FizzySteamworks üzerinden P2P akıyor.
        string mySteamId = SteamUser.GetSteamID().ToString();
        SteamMatchmaking.SetLobbyData(currentLobbyId, HostAddressKey, mySteamId);

        // Sürüm etiketi: Hızlı Katıl aramasında SADECE aynı sürümdeki lobiler
        // dönsün diye. Arkadaş daveti bu filtreden geçmiyor (davet doğrudan
        // bir lobiye gidiyor), yani arkadaşınla oynamanı hiç engellemiyor.
        SteamMatchmaking.SetLobbyData(currentLobbyId, VersionKey, Application.version);

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
    // HIZLI KATIL (yabancılarla oynamak)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Hızlı Katıl" butonu bunu çağırıyor: yer olan bir PUBLIC oyun arar,
    /// bulursa girer; bulamazsa kendisi public bir oyun kurup bekler.
    ///
    /// ══ NEDEN VAR — DEMONUN EN BÜYÜK ENGELİYDİ ══
    /// Lobi tipi `FriendsOnly` olduğu için, oyunu tek başına indiren biri
    /// (Next Fest ziyaretçisi, Steam arkadaşı olmayan bir playtest'çi)
    /// kimseyle oynayamıyordu. Yaşadığı şey: "Oyun Kur" → boş lobide tek
    /// başına → sabotajcısız, tek kişilik bir zaman denemesi. Yani
    /// "asimetrik sabotaj yarışı" diye indirdiği oyunda SABOTAJIN KENDİSİNİ
    /// hiç görmüyordu. Hiçbir cila bunu düzeltmiyor.
    ///
    /// NEDEN LOBİ TARAYICISI DEĞİL: Boş bir liste ekranı, tek başına gelen
    /// oyuncuya "kimse yok" demekten başka bir şey yapmaz ve UI maliyeti
    /// yüksektir. Tek buton hem arar hem kurar — kimse yoksa oyuncu
    /// beklerken BAŞKALARININ onu bulabileceği bir lobi açmış oluyor.
    /// Yani ilk gelen kişi boşuna gelmiyor, havuzu o başlatıyor.
    ///
    /// "OYUN KUR" DEĞİŞMEDİ: o hâlâ `FriendsOnly` lobi açıyor. İki ayrı
    /// ihtiyaç: arkadaşınla oynamak (gizli) ve yabancıyla oynamak (public).
    /// </summary>
    public void QuickJoin()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobbyManager] Steam başlatılmadığı için oyun aranamıyor.");
            return;
        }

        // Start() birkaç sebeple erken dönebiliyor (KCP modu, Steam client
        // kapalı). O durumda callback'ler hiç kaydedilmemiş olur ve istek
        // gönderirsek cevabı DUYAMAYIZ — buton sonsuza kadar "kuruluyor"da
        // kalırdı. Baştan net bir hata vermek daha iyi.
        if (!callbacksRegistered)
        {
            Debug.LogError("[SteamLobbyManager] Lobi sistemi devre dışı (TransportSwitcher 'Kcp' modunda " +
                           "ya da Steam başlatılamadı) — istek gönderilmedi.");
            return;
        }

        if (NetworkServer.active || NetworkClient.isConnected)
        {
            Debug.LogWarning("[SteamLobbyManager] Zaten bir oturum var — önce mevcut oyundan çık.");
            return;
        }

        if (creatingLobby || quickJoining)
        {
            Debug.Log("[SteamLobbyManager] Arama zaten sürüyor — tekrar istek gönderilmedi.");
            return;
        }

        quickJoining = true;
        quickJoinCandidates.Clear();
        quickJoinIndex = 0;

        ScreenNotice.Show("Oyun aranıyor...", 4f);

        // FİLTRELER — istekten ÖNCE eklenmeli, sonra RequestLobbyList'e
        // otomatik uygulanıyorlar (Steam API'sinin çalışma şekli bu).
        SteamMatchmaking.AddRequestLobbyListStringFilter(
            VersionKey, Application.version, ELobbyComparison.k_ELobbyComparisonEqual);

        // En az 1 boş yeri olan lobiler. Yarış başlayınca lobi zaten
        // SetLobbyJoinable(false) ile kilitleniyor ve listede hiç çıkmıyor.
        SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(quickJoinSearchLimit);

        lobbyMatchList.Set(SteamMatchmaking.RequestLobbyList());
    }

    private void OnLobbyMatchList(LobbyMatchList_t callback, bool ioFailure)
    {
        if (!quickJoining) return;   // arada vazgeçilmiş olabilir

        if (ioFailure)
        {
            Debug.LogWarning("[SteamLobbyManager] Lobi araması başarısız (Steam'e ulaşılamadı) — yeni oyun kuruluyor.");
            HostBecauseNoneFound();
            return;
        }

        for (int i = 0; i < callback.m_nLobbiesMatching; i++)
        {
            CSteamID id = SteamMatchmaking.GetLobbyByIndex(i);

            // Host adresi yazılmamış lobi = henüz kurulum aşamasında ya da
            // bozuk. Girsek de kime bağlanacağımızı bilemezdik.
            if (string.IsNullOrEmpty(SteamMatchmaking.GetLobbyData(id, HostAddressKey))) continue;

            quickJoinCandidates.Add(id);
        }

        Debug.Log($"[SteamLobbyManager] Arama bitti: {quickJoinCandidates.Count} uygun oyun bulundu.");

        TryNextQuickJoinCandidate();
    }

    /// <summary>
    /// Sıradaki adaya katılmayı dener. Aday kalmadıysa kendisi bir oyun kurar.
    /// Tek adayla yetinmiyoruz çünkü arama sonucuyla katılma arasında geçen
    /// saniyelerde lobi dolmuş ya da yarışa başlamış olabilir.
    /// </summary>
    private void TryNextQuickJoinCandidate()
    {
        if (quickJoinIndex >= quickJoinCandidates.Count)
        {
            HostBecauseNoneFound();
            return;
        }

        CSteamID target = quickJoinCandidates[quickJoinIndex++];
        Debug.Log($"[SteamLobbyManager] Oyuna katılınıyor ({quickJoinIndex}/{quickJoinCandidates.Count}): {target}");

        SteamMatchmaking.JoinLobby(target);
    }

    private void HostBecauseNoneFound()
    {
        quickJoining = false;
        quickJoinCandidates.Clear();
        quickJoinIndex = 0;

        ScreenNotice.Show("Açık oyun bulunamadı — senin oyunun kuruldu, başkaları katılabilir.", 6f);
        Debug.Log("[SteamLobbyManager] Uygun oyun yok — public lobi kuruluyor.");

        CreateLobby(quickJoinLobbyType);
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
    /// (host da kendi lobisine "girmiş" sayılıyor), o yüzden ikisini ayırmak
    /// gerekiyor.
    ///
    /// ══ BU METOD BİR BUG'I DÜZELTİYOR ══
    /// BELİRTİ: "Oyun Kur" deyip sonra oyundan ayrılan biri, arkadaşı davet
    /// ettiğinde katılamıyordu. Hiçbir hata da vermiyordu — davet kabul
    /// ediliyor, Steam lobisine giriliyor, sonra hiçbir şey olmuyordu. Tek
    /// çözüm oyunu tamamen kapatıp açmaktı.
    ///
    /// SEBEP: Host/client ayrımı `NetworkServer.active` ile yapılıyordu. Ama
    /// az önce host'tan çıkmış bir oyuncuda Mirror'ın sunucusu birkaç kare
    /// daha kapanmaya devam ediyor (StopHost anında bitmiyor, üstüne bir de
    /// ana menü sahnesi yükleniyor). O aralıkta davet kabul edilirse
    /// `NetworkServer.active` hâlâ true görünüyor, kod bu kişiyi "host" sanıp
    /// sessizce geri dönüyor ve StartClient() HİÇ çağrılmıyordu.
    ///
    /// ÇÖZÜM İKİ PARÇALI: (1) host ayrımı artık `hostingThisLobby` bayrağıyla
    /// yapılıyor — lobiyi biz mi kurduk, kesin bilgi. (2) Önceki oturum hâlâ
    /// kapanıyorsa sessizce vazgeçmek yerine kapanmasını BEKLİYORUZ.
    /// </summary>
    private void OnLobbyEntered(LobbyEnter_t callback)
    {
        // GİRİŞ BAŞARISIZ OLABİLİR: lobi bu arada dolmuş, kilitlenmiş
        // (yarış başlamış) ya da kapanmış olabilir. Hızlı Katıl'daysak
        // pes etmek yerine sıradaki adayı deniyoruz.
        if (callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            Debug.LogWarning($"[SteamLobbyManager] Lobiye girilemedi: " +
                             $"{(EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse}");

            if (quickJoining) TryNextQuickJoinCandidate();
            else ScreenNotice.Show("Oyuna katılınamadı — oyun dolmuş ya da başlamış olabilir.", 5f);

            return;
        }

        currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);

        // Kendi kurduğumuz lobiye "girdik" — host zaten StartHost() ile hem
        // sunucu hem client, burada tekrar bağlanmak mevcut oturumu bozar.
        if (hostingThisLobby)
        {
            SetInviteButtonInteractable(true);
            return;
        }

        string hostAddress = SteamMatchmaking.GetLobbyData(currentLobbyId, HostAddressKey);

        if (string.IsNullOrEmpty(hostAddress))
        {
            Debug.LogError("[SteamLobbyManager] Lobide host adresi bulunamadı — bağlanılamıyor. " +
                           "(Host'un oyunu eski bir sürüm olabilir.)");

            if (quickJoining)
            {
                SteamMatchmaking.LeaveLobby(currentLobbyId);
                currentLobbyId = default;
                TryNextQuickJoinCandidate();
            }

            return;
        }

        // Buraya geldiysek geçerli bir lobiye girdik — arama bitti.
        quickJoining = false;
        quickJoinCandidates.Clear();
        quickJoinIndex = 0;

        StartCoroutine(JoinWhenPreviousSessionClosed(hostAddress));
    }

    /// <summary>
    /// Önceki oyun oturumu tamamen kapanana kadar bekleyip client'ı başlatır.
    /// Zaten kapalıysa neredeyse anında geçer, yani normal akışa maliyeti yok.
    /// </summary>
    private System.Collections.IEnumerator JoinWhenPreviousSessionClosed(string hostAddress)
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.Log("[SteamLobbyManager] Önceki oturum hâlâ kapanıyor — bitmesi beklenip öyle bağlanılacak.");

            // Kapanma başlamamışsa biz başlatıyoruz: oyuncu davet kabul
            // ederek zaten "buradan çıkmak istiyorum" demiş oluyor.
            if (NetworkManager.singleton is MyNetworkManager manager)
                manager.LeaveGameIntentionally();
        }

        const float timeout = 8f;
        float waited = 0f;

        while ((NetworkServer.active || NetworkClient.active) && waited < timeout)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.LogError("[SteamLobbyManager] Önceki oturum 8 saniyede kapanmadı — davete katılınamadı. " +
                           "Oyunu yeniden başlatman gerekebilir.");
            ScreenNotice.Show("Önceki oyundan çıkılamadı, davete katılınamadı.", 5f);
            yield break;
        }

        // Mirror ana menü sahnesini yüklerken StartClient çağırmak bağlantıyı
        // yarıda bırakabiliyor — sahne geçişinin oturması için bir kare bırak.
        yield return null;

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
        // Bu iki bayrak lobiden BAĞIMSIZ olarak her koşulda sıfırlanmalı:
        // "InLobby false" diye erken dönersek, yarım kalmış bir lobi kurma
        // isteği creatingLobby'yi sonsuza kadar true bırakır ve "Oyun Kur"
        // butonu bir daha hiç çalışmaz.
        creatingLobby = false;
        hostingThisLobby = false;
        quickJoining = false;
        quickJoinCandidates.Clear();
        quickJoinIndex = 0;

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
        UnregisterCallbacks();

        // Property'nin getter'ı gerekirse sahneyi tarıyor; sahne yıkılırken
        // bunu tetiklememek için doğrudan alanı kullanıyoruz.
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// ══ SESSİZ BİR SIZINTIYI KAPATIYOR ══
    /// Steamworks.NET'te `Callback&lt;T&gt;.Create(...)` kaydı, verilen
    /// delegate'e GÜÇLÜ referans tutuyor. Bu MonoBehaviour yok edilse bile
    /// (ana menüye her dönüşte oluyor) kayıt Steam tarafında duruyor ve C#
    /// nesnesi hayatta kalıyor — çünkü Unity'de "yok edilmiş" olmak C#
    /// nesnesinin ölmesi demek değil.
    ///
    /// Sonucu: her oturumdan sonra bir OnLobbyCreated dinleyicisi daha
    /// birikiyordu. İkinci lobide callback İKİ kez, üçüncüde ÜÇ kez
    /// çalışıyor — her biri `NetworkManager.singleton.StartHost()` çağırıyor.
    /// Yani lobi kurulamama sorununun ikinci katmanı buydu.
    /// </summary>
    private void UnregisterCallbacks()
    {
        lobbyCreated?.Dispose();
        gameLobbyJoinRequested?.Dispose();
        lobbyEntered?.Dispose();
        richPresenceJoinRequested?.Dispose();
        lobbyMatchList?.Dispose();

        lobbyCreated = null;
        gameLobbyJoinRequested = null;
        lobbyEntered = null;
        richPresenceJoinRequested = null;
        lobbyMatchList = null;

        callbacksRegistered = false;
    }
}
