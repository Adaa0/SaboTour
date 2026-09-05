using UnityEngine;
using TMPro;
using Mirror;

/// <summary>
/// MİMARİ NOT (Mirror Entegrasyonu — Checkpoint/Timer Sync):
///
/// Bu script artık NetworkBehaviour. Checkpoint geçişi, tur sayısı ve toplam
/// süre SERVER-AUTHORITATIVE: sadece server bu değerleri değiştirebiliyor,
/// tüm client'lar SyncVar ile otomatik güncel kopyayı alıyor (hile/desync
/// riski yok — biri checkpoint atlayamaz, çünkü server sırayı kontrol
/// ediyor).
///
/// Akış:
///   1. Checkpoint.cs, aracın SAHİBİ (owner) client'ında tetiklenince
///      CmdReachedCheckpoint() çağırır (Command = client -> server).
///   2. Server sırayı doğrular, SyncVar'ları günceller.
///   3. Hook metodları HER client'ta çalışır ama HUD sadece owner'da
///      güncellenir (isOwned kontrolü) — böylece bir oyuncunun ekranı başka
///      bir oyuncunun turu/süresiyle karışmaz.
///   4. Leaderboard için tüm PlayerRaceController'lar statik bir listede
///      tutuluyor (AllPlayers), RaceLeaderboard.cs bunları okuyup sıralıyor.
/// </summary>
public class PlayerRaceController : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnTotalCheckpointsChanged))]
    private int totalCheckpoints;

    public int maxLaps = 3;

    [SyncVar(hook = nameof(OnCurrentCheckpointChanged))]
    private int currentCheckpoint = -1;
    public int CurrentCheckpoint => currentCheckpoint;

    [SyncVar(hook = nameof(OnCurrentLapChanged))]
    private int currentLap = 0;
    public int CurrentLap => currentLap;

    [SyncVar]
    private bool isRacingSynced = true;
    public bool isRacing => isRacingSynced;

    // isRacing false OLMASININ İKİ farklı sebebi olabilir: (1) yarışçı
    // GERÇEKTEN bitirdi, (2) sabotajcı süre dolarak kazandı ve bu yarışçı
    // ServerStopForRaceEnd() ile zorla durduruldu (bitirmedi). RaceLeaderboard
    // eskiden ikisini de "BİTİRDİ" gösteriyordu — süre dolan yarışçı da
    // "kazandım" izlenimi alıyordu. Bu SyncVar ikisini ayırt ediyor.
    [SyncVar]
    private bool hasFinishedRace = false;
    public bool HasFinished => hasFinishedRace;

    [SyncVar(hook = nameof(OnTotalTimeChanged))]
    private float totalTime = 0f;
    public float TotalTime => totalTime;

    /// <summary>Leaderboard gibi dışarıdan okunan yerler için hazır formatlanmış süre.</summary>
    public string FormattedTotalTime => FormatTime(totalTime);

    [SyncVar]
    private string playerLabel;
    public string PlayerLabel => playerLabel;

    /// <summary>
    /// Oyuncunun lobide seçtiği ismi sunucuda yazar. MyNetworkManager,
    /// aracı spawn ederken (AddPlayerForConnection'dan ÖNCE) çağırıyor.
    ///
    /// Metin burada TEKRAR temizlenmiyor — lobide `LobbyPlayer.CmdSetName`
    /// zaten sunucuda temizledi ve buraya sunucunun kendi kaydından geliyor,
    /// client'tan değil.
    /// </summary>
    [Server]
    public void ServerSetLabel(string label)
    {
        if (!string.IsNullOrEmpty(label)) playerLabel = label;
    }

    [Header("UI")]
    public TextMeshProUGUI LapCount;
    public TextMeshProUGUI CheckpointInfo;

    [Header("Timer UI")]
    public TextMeshProUGUI TotalTimeText;
    public TextMeshProUGUI LastLapTimeText;

    // ─── SESLER ──────────────────────────────────────────────────
    // HEPSİ SADECE BU ARABANIN SAHİBİNİN EKRANINDA ÇALIYOR. Sebebi:
    // bunlar konumu olan dünya sesleri değil, "sana ait" bildirim sesleri —
    // başka bir oyuncu checkpoint geçtiğinde senin kulağında tık sesi
    // duyulması kafa karıştırıcı olurdu. Bu yüzden 2D (PlayUI) çalıyorlar
    // ve tetiklendikleri yerler zaten owner'a özel: SyncVar hook'unda
    // isOwned kontrolü, ya da doğrudan [TargetRpc] (Mirror'da TargetRpc
    // sadece o objenin sahibi olan client'ta çalışır).
    [Header("Sesler (sadece bu arabanın sahibi duyar)")]
    [Tooltip("Her checkpoint'ten geçerken çalan kısa tık/bip sesi. Bir yarışta 30+ kez çalıyor — bu yüzden AYRI ve düşük bir ses seviyesi var (aşağıdaki Checkpoint Volume). İstemiyorsan bu alanı BOŞ BIRAK, ses tamamen kalkar (kodda hiçbir değişiklik gerekmez).")]
    [SerializeField] private AudioClip checkpointClip;
    [Tooltip("SADECE checkpoint sesinin seviyesi. Diğer bildirimlerden ayrı tutuldu çünkü çok sık tekrarlanan bir ses, diğerleriyle aynı seviyede olursa rahatsız ediyor. 0 yaparsan da susar.")]
    [Range(0f, 1f)][SerializeField] private float checkpointVolume = 0.3f;
    [Tooltip("Bir tur tamamlanınca çalan, checkpoint sesinden daha belirgin bir bildirim.")]
    [SerializeField] private AudioClip lapCompleteClip;
    [Range(0f, 1f)][SerializeField] private float raceSfxVolume = 0.8f;

    // Sadece server'da anlamlı — tur başlangıç zamanı ve timer durumu.
    private float currentLapStartTime = 0f;
    private bool timerRunning = false;

    // Sadece owner'ın client'ında kullanılır (HUD gösterimi).
    private string lastLapDisplayText = "";

    // ─── Leaderboard Kaydı ───────────────────────────────────────
    // Her client, sahnede spawn olan TÜM PlayerRaceController'ları burada
    // tutar (kendisi dahil). RaceLeaderboard.cs bunu okuyup sıralı tablo
    // gösterir. Mirror OnStartClient/OnStopClient callback'leri HER
    // spawn/despawn edilen networked objede (owner olsun olmasın) çağrılır.
    public static readonly System.Collections.Generic.List<PlayerRaceController> AllPlayers = new();

    /// <summary>
    /// Server'da bir yarışçı turlarını bitirdiğinde tetiklenir (ServerFinishRace
    /// içinde). RacePodiumManager bunu dinleyip "yarışçılar kazandı" podyum
    /// akışını başlatıyor. Sadece SERVER tarafında çalışan kod bu event'i
    /// tetikler (ServerFinishRace zaten [Server] guard'lı), yani bu event
    /// sadece server sürecinde (host ya da dedicated server) ateşlenir.
    /// </summary>
    public static event System.Action<PlayerRaceController> OnPlayerFinishedRace;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!AllPlayers.Contains(this))
            AllPlayers.Add(this);
    }

    // Tanıtım ipucu OTURUMDA BİR KEZ. Static olmak zorunda: araba her yarışta
    // yeniden doğuyor, instance alanı hatırlamaz.
    private static bool roleHintShown;

    // İpucu "BAŞLA!" yazısının hemen ardından çıksın diye küçük bir gecikme.
    private float hintTimer = -1f;

    /// <summary>
    /// Yarışçının ilk yarışında kısa bir kontrol hatırlatması. Sabotajcıdaki
    /// ipucunun karşılığı — ama daha kısa, çünkü "araba, gaz ver" zaten
    /// sezgisel; burada asıl söylenmesi gereken şey rakibin ne yaptığı.
    ///
    /// ⚠️ NEDEN SPAWN ANINDA DEĞİL: geri sayım da `ScreenNotice` kullanıyor
    /// ve aynı yazı alanını paylaşıyorlar. Spawn'da gösterseydik "3" hemen
    /// üstüne yazardı. Bu yüzden ipucu yarış BAŞLADIKTAN sonra çıkıyor.
    /// </summary>
    private void UpdateRoleHint()
    {
        if (roleHintShown) return;
        if (!RacePodiumManager.RaceStarted) return;

        if (hintTimer < 0f) hintTimer = 1.4f;   // "BAŞLA!" okunacak kadar bekle

        hintTimer -= Time.deltaTime;
        if (hintTimer > 0f) return;

        roleHintShown = true;

        // Ekranın ortası DEĞİL, sağ üstteki küçük panel (minimabın altı) —
        // bkz. RaceHud.ShowRoleHint'teki gerekçe.
        RaceHud.ShowRoleHint(Loc.T("hint.racer"), 15f);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        AllPlayers.Remove(this);
    }

    // ─── Server Başlangıcı ───────────────────────────────────────
    public override void OnStartServer()
    {
        base.OnStartServer();

        // KOŞULLU: isim lobiden geldiyse (ServerSetLabel spawn'dan ÖNCE
        // çağrılıyor) onu EZMEYELİM. Sadece isim bilinmiyorsa — ör. yarışın
        // ortasında bağlanan biri, ya da lobisiz doğrudan test — yedek ada
        // düşüyoruz.
        if (string.IsNullOrEmpty(playerLabel))
            playerLabel = $"Oyuncu {netId}";

        // Pist zaten TrackSeedSync ile deterministik üretildiği için
        // checkpointsPerLap tüm client'larda aynı — server buradan okuyup
        // SyncVar ile resmi olarak yayınlıyor.
        TrackGenerator trackGenerator = FindAnyObjectByType<TrackGenerator>();
        int checkpointCount = trackGenerator != null ? trackGenerator.checkpointsPerLap : 1;

        ServerInitializeRace(checkpointCount);
    }

    [Server]
    private void ServerInitializeRace(int checkpointCount)
    {
        totalCheckpoints = checkpointCount <= 0 ? 1 : checkpointCount;
        currentCheckpoint = -1;
        currentLap = 0;
        isRacingSynced = true;
        totalTime = 0f;
        currentLapStartTime = 0f;
        timerRunning = false;
    }

    // ─── Start (sadece HUD referanslarını bul, sadece owner ilgilensin) ──
    public void Start()
    {
        if (!isOwned) return;

        if (LapCount == null)
            LapCount = GameObject.Find("LapCountText")?.GetComponent<TextMeshProUGUI>();

        if (CheckpointInfo == null)
            CheckpointInfo = GameObject.Find("CheckpointInfoText")?.GetComponent<TextMeshProUGUI>();

        if (TotalTimeText == null)
            TotalTimeText = GameObject.Find("TotalTimeText")?.GetComponent<TextMeshProUGUI>();

        if (LastLapTimeText == null)
            LastLapTimeText = GameObject.Find("LastLapTimeText")?.GetComponent<TextMeshProUGUI>();

        UpdateLapUI();
        UpdateCheckpointUI();
        UpdateTimerUI();

        if (LastLapTimeText != null)
        {
            LastLapTimeText.text = "";
            LastLapTimeText.color = Color.white;
            lastLapDisplayText = "";
        }
    }

    // ─── Server Timer ────────────────────────────────────────────
    // totalTime SyncVar olduğu için sadece server artırabilir, Mirror
    // periyodik olarak (component'in Sync Interval ayarına göre) tüm
    // client'lara yayar.
    void Update()
    {
        // Sadece kendi ekranımda: rol ipucu (yarış başladıktan sonra).
        if (isOwned) UpdateRoleHint();

        if (!isServer || !isRacingSynced) return;

        // ══ SAAT ARTIK YARIŞ BAŞLANGICINA BAĞLI ══
        // ESKİDEN: `timerRunning` ancak yarışçı checkpoint 0'ı GEÇİNCE true
        // oluyordu. Sabotajcının kazanma süresi ise sahne yüklenir yüklenmez
        // işlemeye başlıyordu — yani iki saat AYRI ANLARDA başlıyor ve fark
        // yarışçının aleyhine oluyordu (spawn + araçların yere oturması +
        // başlangıç çizgisine kadar sürme kadar geriden başlıyordu).
        // Artık ikisi de RacePodiumManager'ın geri sayımına bağlı.
        if (!RacePodiumManager.RaceStarted) return;

        if (!timerRunning)
        {
            timerRunning = true;
            currentLapStartTime = totalTime;
        }

        totalTime += Time.deltaTime;
    }

    // ─── Checkpoint'e Ulaşıldı (Command) ────────────────────────
    /// <summary>
    /// Checkpoint.cs, aracın SAHİBİ olan client'ta çağırır. Command olduğu
    /// için gerçek çalışma her zaman SERVER'da olur — client sadece
    /// isteği gönderir, server sırayı doğrulayıp SyncVar'ları günceller.
    /// </summary>
    [Command]
    public void CmdReachedCheckpoint(int index, bool isFinishLine)
    {
        if (!isRacingSynced || totalCheckpoints <= 0) return;

        // Yarışın EN BAŞINDAKİ ilk temas mı (henüz hiç checkpoint geçilmedi).
        // Aşağıda tur sayacının yanlışlıkla artmasını engellemek için lazım.
        //
        // NOT: Saat artık burada BAŞLAMIYOR — yarış saati geri sayım bitince
        // başlıyor (bkz. Update). Eskiden ilk checkpoint temasında başlıyordu
        // ve sabotajcının saatiyle uyuşmuyordu.
        bool isRaceStartTouch = currentCheckpoint == -1;

        // Sıra dışı checkpoint'i yok say (hile/atlama koruması).
        if (index != (currentCheckpoint + 1) % totalCheckpoints) return;

        currentCheckpoint = index;

        // Finish line artık checkpoint 0 (start/finish aynı çizgi). Bu yüzden
        // "tur tamamlandı" sayımı, finish checkpoint'ine her değiş(me)de değil,
        // sadece YARIŞ BAŞLANGICINDAKİ İLK TEMAS DIŞINDA tetiklenir — yoksa
        // spawn anında checkpoint 0'a değmek tek başına bir tur sayardı.
        if (isFinishLine && !isRaceStartTouch)
        {
            currentLap++;

            float lapTime = totalTime - currentLapStartTime;
            currentLapStartTime = totalTime;

            TargetLapCompleted(lapTime);

            // SON TUR BİLDİRİMİ — yarış oyunlarının klasiği ve duygusal
            // getirisi maliyetinin kat kat üstünde.
            //
            // KOŞUL: bu turu bitirdikten sonra geriye TAM OLARAK bir tur
            // kaldıysa. Örnek (maxLaps = 3): currentLap 2 olduğunda son tura
            // giriliyor. Yarışı bitiren son turda (currentLap == maxLaps)
            // tetiklenmiyor, çünkü orada zaten ServerFinishRace çalışıyor.
            if (currentLap == maxLaps - 1)
                TargetFinalLap();

            if (currentLap >= maxLaps)
                ServerFinishRace();
        }
    }

    /// <summary>
    /// SADECE son tura giren yarışçının kendi ekranında çalışır (TargetRpc).
    /// Diğer yarışçılara gönderilmiyor — herkes kendi turunu ayrı zamanlarda
    /// bitiriyor, ortak bir bildirim olsaydı ekran sürekli yazı yağardı.
    /// </summary>
    [TargetRpc]
    private void TargetFinalLap()
    {
        ScreenNotice.Show(Loc.T("race.finallap"), 2.5f);
    }

    [Server]
    private void ServerFinishRace()
    {
        isRacingSynced = false;
        hasFinishedRace = true;
        timerRunning = false;
        TargetRaceFinished();
        OnPlayerFinishedRace?.Invoke(this);
    }

    /// <summary>
    /// Sabotajcı süre dolarak kazandığında, RacePodiumManager hâlâ yarışan
    /// (bitirmemiş) yarışçıları durdurmak için bunu çağırır — böylece
    /// checkpoint/timer işlemeye devam etmezler. Normal bitiriş (ServerFinishRace)
    /// ile karıştırılmasın diye ayrı: burada "FINISHED!" yazısı gösterilmiyor,
    /// çünkü yarışçı gerçekten bitirmedi, sadece süre doldu.
    /// </summary>
    [Server]
    public void ServerStopForRaceEnd()
    {
        isRacingSynced = false;
        timerRunning = false;
    }

    /// <summary>Server -> sadece bu aracın sahibi. Tur bitti bildirimi.</summary>
    [TargetRpc]
    private void TargetLapCompleted(float lapTime)
    {
        string lapText = $"Last: {FormatTime(lapTime)}";
        lastLapDisplayText = lapText;

        SfxPlayer.PlayUI(lapCompleteClip, raceSfxVolume);

        UpdateLapUI();

        if (LastLapTimeText != null)
        {
            LastLapTimeText.color = Color.white;
            LastLapTimeText.text = lapText;
        }
    }

    /// <summary>Server -> sadece bu aracın sahibi. Yarış bitti bildirimi.</summary>
    [TargetRpc]
    private void TargetRaceFinished()
    {
        // BİLEREK SES YOK: yarışı bitiren oyuncu 1-2 saniye sonra zaten
        // podyuma geçiyor ve RacePodiumManager zafer sesini çalıyor. Buraya
        // ayrı bir "bitirdin" sesi konsaydı ikisi üst üste binerdi.
        if (LapCount != null) LapCount.text = "FINISHED!";
        if (CheckpointInfo != null) CheckpointInfo.text = "Race Complete";
        Debug.Log($"🏆 {name} BİTİRDİ! Toplam: {FormatTime(totalTime)}");
    }

    // ─── ESKİ DRIFT CEZA SİSTEMİ BURADAN KALDIRILDI ─────────────
    // Sabotajcının 3. yeteneği yeniden tasarlandı: artık drift ölçüp
    // gecikmeli ceza veren bir sistem yok. Yerine gelen "Motor Arızası"
    // tuzağı (EngineFailureTrap.cs) kendi [TargetRpc]'lerini kendisi
    // gönderiyor ve arabaya doğrudan CarController.ApplyEngineFailure()
    // uyguluyor — bu yüzden buradaki ShowLiveDriftPenalty /
    // ClearLiveDriftPenalty / ApplyDriftSlowdown metotlarına gerek kalmadı.
    //
    // Ekran yazısı da artık ScreenNotice ile ekranın ortasında gösteriliyor
    // (LastLapTimeText'in köşesinde değil) — eski yerinde fark edilmiyordu.

    // ─── SyncVar Hook'ları (HER client'ta çalışır, HUD sadece owner'da) ──
    private void OnTotalCheckpointsChanged(int oldValue, int newValue) => UpdateCheckpointUI();

    private void OnCurrentCheckpointChanged(int oldValue, int newValue)
    {
        UpdateCheckpointUI();

        // Checkpoint sesi neden BURADA (Checkpoint.cs'in OnTriggerEnter'ında
        // değil): trigger her client'ın kendi fizik dünyasında tetikleniyor
        // ve server sırayı doğrulamadan önce çalışıyor — sıra dışı/geçersiz
        // bir checkpoint'e değince de ses çıkardı. Bu hook ise SADECE server
        // geçişi ONAYLAYIP SyncVar'ı güncellediğinde çalışıyor, yani ses
        // her zaman gerçekten sayılan bir geçişi işaret ediyor.
        if (!isOwned || newValue < 0) return;

        // Tur tamamlandığında checkpoint 0'a dönülüyor — o an ayrıca
        // lapCompleteClip çalacağı için iki ses üst üste binmesin diye
        // buradaki tık atlanıyor (bkz. TargetLapCompleted).
        bool isLapWrap = newValue == 0 && oldValue > 0;
        if (isLapWrap) return;

        SfxPlayer.PlayUI(checkpointClip, checkpointVolume);
    }
    private void OnCurrentLapChanged(int oldValue, int newValue) => UpdateLapUI();
    private void OnTotalTimeChanged(float oldValue, float newValue) => UpdateTimerUI();

    // ─── Yardımcı ────────────────────────────────────────────────
    private void UpdateLapUI()
    {
        if (!isOwned || LapCount == null) return;
        LapCount.text = $"Lap : {currentLap} / {maxLaps}";
    }

    private void UpdateCheckpointUI()
    {
        if (!isOwned || CheckpointInfo == null) return;

        int denom = totalCheckpoints > 0 ? totalCheckpoints : 1;
        int next = (currentCheckpoint + 1) % denom;

        if (currentCheckpoint < 0)
            CheckpointInfo.text = $"Checkpoint: - / {totalCheckpoints - 1}\nNext: {next}";
        else
            CheckpointInfo.text = $"Checkpoint: {currentCheckpoint} / {totalCheckpoints - 1}\nNext: {next}";
    }

    private void UpdateTimerUI()
    {
        if (!isOwned || TotalTimeText == null) return;
        TotalTimeText.text = FormatTime(totalTime);
    }

    // Salise (ms) kısmı kaldırıldı: totalTime bir SyncVar, sadece Mirror'ın
    // sync interval'ında güncelleniyor (her frame değil) — bu yüzden ms
    // basamağı sürekli atlaya atlaya gidip kötü görünüyordu.
    private string FormatTime(float t)
    {
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        return $"{m:00}:{s:00}";
    }
}
