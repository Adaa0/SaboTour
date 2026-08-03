using UnityEngine;
using Mirror;

/// <summary>
/// Hangi Mirror transport'unun kullanılacağını belirler: KCP (localhost/LAN)
/// ya da FizzySteamworks (Steam P2P).
///
/// MANUEL SEÇİM: Eskiden bu seçim `#if UNITY_EDITOR` ile OTOMATİK yapılıyordu
/// (Editor'de KCP, build'de Steam). Ama Multiplayer Play Mode'un sanal
/// oyuncusu beklendiği gibi davranmayıp Steam transport'unu açtı — client
/// penceresinde "localhost:7777" yerine SteamID kutusu çıktı. Bu yüzden
/// otomatik seçim kaldırıldı: artık aşağıdaki `mode` alanından ELLE
/// seçiliyor, ne seçtiysen o çalışıyor.
///
/// NASIL KULLANILIR:
///  • Günlük geliştirme / Multiplayer Play Mode testleri → `Kcp`
///  • Build alıp arkadaşınla Steam üzerinden oynayacaksan → `Steam`
///    (Build almadan ÖNCE bu alanı Steam'e çevirip sahneyi KAYDET.)
///
/// NEDEN İKİSİ AYRI: Multiplayer Play Mode'un ikinci "sanal oyuncusu" da AYNI
/// bilgisayardaki AYNI Steam hesabıyla çalışıyor — host ve client aynı
/// SteamID'ye sahip olunca Steam P2P "kendi kendine" bağlanmaya çalışır, bu
/// güvenilir değil. KCP ise localhost üzerinden çalışır, Steam'in açık olması
/// bile gerekmez.
///
/// [DefaultExecutionOrder(-10000)] ile SteamManager'dan (-9999) bile önce
/// çalışıyor — NetworkManager host/client başlatmadan ÖNCE doğru transport
/// seçilmiş olmalı.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class TransportSwitcher : MonoBehaviour
{
    public enum TransportMode
    {
        /// <summary>localhost / LAN. Editor testleri için.</summary>
        Kcp,
        /// <summary>Steam P2P. Gerçek build için.</summary>
        Steam
    }

    [Header("Seçim")]
    [Tooltip("HANGİ TRANSPORT KULLANILSIN? Editor testleri için Kcp, " +
             "Steam build'i alırken Steam. Build almadan önce değiştirip " +
             "sahneyi kaydetmeyi unutma.")]
    [SerializeField] private TransportMode mode = TransportMode.Kcp;

    [Header("Referanslar")]
    [Tooltip("KcpTransport component'i — localhost:7777 ile bağlanılan.")]
    [SerializeField] private Transport kcpTransport;
    [Tooltip("FizzySteamworks component'i — SteamID ile bağlanılan.")]
    [SerializeField] private Transport steamTransport;

    void Awake()
    {
        NetworkManager nm = GetComponent<NetworkManager>();
        if (nm == null)
        {
            Debug.LogWarning("[TransportSwitcher] Bu objede NetworkManager yok, transport seçilemedi.");
            return;
        }

        bool useSteam = mode == TransportMode.Steam;

        Transport chosen = useSteam ? steamTransport : kcpTransport;
        Transport other = useSteam ? kcpTransport : steamTransport;

        if (chosen == null)
        {
            Debug.LogError($"[TransportSwitcher] '{mode}' seçili ama ilgili transport " +
                           $"alanı Inspector'da BOŞ! NetworkManager objesinde " +
                           $"{(useSteam ? "Steam Transport" : "Kcp Transport")} alanını doldur.");
            return;
        }

        chosen.enabled = true;
        nm.transport = chosen;
        if (other != null) other.enabled = false;

        Debug.Log($"[TransportSwitcher] Aktif transport: {mode} ({chosen.GetType().Name})");
    }
}
