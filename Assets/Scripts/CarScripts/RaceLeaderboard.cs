using UnityEngine;
using System.Linq;
using System.Text;
using TMPro;

/// <summary>
/// Client-only leaderboard: PlayerRaceController'ın statik AllPlayers
/// listesini okuyup tur/checkpoint/süreye göre sıralı bir tablo gösterir.
/// Network mesajı GEREKMİYOR — çünkü her PlayerRaceController'ın
/// currentLap/currentCheckpoint/totalTime SyncVar'ları zaten Mirror
/// tarafından her client'a otomatik yayılıyor, burada sadece okunuyor.
/// </summary>
public class RaceLeaderboard : MonoBehaviour
{
    public TextMeshProUGUI LeaderboardText;
    [SerializeField] private float refreshInterval = 0.5f;

    private float timer;

    void Start()
    {
        if (LeaderboardText == null)
            LeaderboardText = GameObject.Find("LeaderboardText")?.GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < refreshInterval) return;
        timer = 0f;
        Refresh();
    }

    private void Refresh()
    {
        if (LeaderboardText == null) return;

        var ordered = PlayerRaceController.AllPlayers
            .Where(p => p != null)
            .OrderByDescending(p => p.CurrentLap)
            .ThenByDescending(p => p.CurrentCheckpoint)
            .ThenBy(p => p.TotalTime)
            .ToList();

        var sb = new StringBuilder();
        for (int i = 0; i < ordered.Count; i++)
        {
            var p = ordered[i];
            string status = p.isRacing ? $"Tur {p.CurrentLap}/{p.maxLaps} — CP {p.CurrentCheckpoint}" : "BİTİRDİ";

            // Süre SADECE kendi satırında gösteriliyor — bir yarışçı diğerinin
            // tam süresini görmemeli, sadece sırayı görmeli.
            if (p.isOwned)
                status += $" — Süre: {p.FormattedTotalTime}";

            sb.AppendLine($"{i + 1}. {p.PlayerLabel} — {status}");
        }

        LeaderboardText.text = sb.ToString();
    }
}
