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

    [SyncVar(hook = nameof(OnTotalTimeChanged))]
    private float totalTime = 0f;
    public float TotalTime => totalTime;

    /// <summary>Leaderboard gibi dışarıdan okunan yerler için hazır formatlanmış süre.</summary>
    public string FormattedTotalTime => FormatTime(totalTime);

    [SyncVar]
    private string playerLabel = "Yarışçı";
    public string PlayerLabel => playerLabel;

    [Header("UI")]
    public TextMeshProUGUI LapCount;
    public TextMeshProUGUI CheckpointInfo;

    [Header("Timer UI")]
    public TextMeshProUGUI TotalTimeText;
    public TextMeshProUGUI LastLapTimeText;

    // Sadece server'da anlamlı — tur başlangıç zamanı ve timer durumu.
    private float currentLapStartTime = 0f;
    private bool timerRunning = false;

    // Sadece owner'ın client'ında kullanılır (HUD gösterimi).
    private string lastLapDisplayText = "";
    private bool showingDriftWarning = false;

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

    public override void OnStopClient()
    {
        base.OnStopClient();
        AllPlayers.Remove(this);
    }

    // ─── Server Başlangıcı ───────────────────────────────────────
    public override void OnStartServer()
    {
        base.OnStartServer();

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
        if (isServer && timerRunning && isRacingSynced)
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

        bool isRaceStartTouch = currentCheckpoint == -1;

        if (!timerRunning && isRaceStartTouch)
        {
            timerRunning = true;
            currentLapStartTime = totalTime;
        }

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

            if (currentLap >= maxLaps)
                ServerFinishRace();
        }
    }

    [Server]
    private void ServerFinishRace()
    {
        isRacingSynced = false;
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

        UpdateLapUI();

        if (!showingDriftWarning && LastLapTimeText != null)
        {
            LastLapTimeText.color = Color.white;
            LastLapTimeText.text = lapText;
        }
    }

    /// <summary>Server -> sadece bu aracın sahibi. Yarış bitti bildirimi.</summary>
    [TargetRpc]
    private void TargetRaceFinished()
    {
        if (LapCount != null) LapCount.text = "FINISHED!";
        if (CheckpointInfo != null) CheckpointInfo.text = "Race Complete";
        Debug.Log($"🏆 {name} BİTİRDİ! Toplam: {FormatTime(totalTime)}");
    }

    // ─── CANLI DRIFT CEZA GÖSTERİMİ ─────────────────────────────
    // DriftTrap.cs artık server-authoritative bir NetworkBehaviour (bkz.
    // DriftTrap.cs). Bu üç public metod SERVER tarafında çağrılıyor;
    // gerçek HUD güncellemesi [TargetRpc] ile SADECE bu aracın sahibi olan
    // client'a gönderiliyor (Mirror'da TargetRpc zaten sadece owner'da
    // çalışır, ekstra isOwned kontrolüne gerek yok).
    public void ShowLiveDriftPenalty(float currentPenalty)
    {
        if (isServer)
            TargetShowLiveDriftPenalty(currentPenalty);
    }

    [TargetRpc]
    private void TargetShowLiveDriftPenalty(float currentPenalty)
    {
        if (LastLapTimeText == null) return;

        showingDriftWarning = true;
        LastLapTimeText.color = new Color(1f, 0.4f, 0f); // Turuncu
        LastLapTimeText.text = $"Drift Cezasi: +{currentPenalty:F1}s";
    }

    public void ClearLiveDriftPenalty()
    {
        if (isServer)
            TargetClearLiveDriftPenalty();
    }

    [TargetRpc]
    private void TargetClearLiveDriftPenalty()
    {
        showingDriftWarning = false;

        if (LastLapTimeText == null) return;

        LastLapTimeText.color = Color.white;
        LastLapTimeText.text = lastLapDisplayText;
    }

    // ─── CEZA UYGULA ─────────────────────────────────────────────
    public void AddTimePenalty(float penalty, float driftTime)
    {
        if (!isServer) return;

        totalTime += penalty;
        TargetAddTimePenalty(penalty, driftTime);

        Debug.Log($"[PlayerRaceController] 💀 +{penalty:F2}s ceza eklendi. Yeni toplam: {FormatTime(totalTime)}");
    }

    [TargetRpc]
    private void TargetAddTimePenalty(float penalty, float driftTime)
    {
        if (LastLapTimeText == null) return;

        StopAllCoroutines();
        StartCoroutine(ShowFinalPenaltyNotification(penalty, driftTime));
    }

    private System.Collections.IEnumerator ShowFinalPenaltyNotification(float penalty, float driftTime)
    {
        if (LastLapTimeText == null) yield break;

        showingDriftWarning = true;
        LastLapTimeText.color = Color.red;
        LastLapTimeText.text = $"+{penalty:F1}s CEZA! ({driftTime:F1}s drift)";

        yield return new WaitForSeconds(4f);

        showingDriftWarning = false;
        LastLapTimeText.color = Color.white;
        LastLapTimeText.text = lastLapDisplayText;
    }

    // ─── SyncVar Hook'ları (HER client'ta çalışır, HUD sadece owner'da) ──
    private void OnTotalCheckpointsChanged(int oldValue, int newValue) => UpdateCheckpointUI();
    private void OnCurrentCheckpointChanged(int oldValue, int newValue) => UpdateCheckpointUI();
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
