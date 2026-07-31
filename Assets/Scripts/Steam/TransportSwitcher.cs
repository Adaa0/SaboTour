using UnityEngine;
using Mirror;

/// <summary>
/// Editor'de (Multiplayer Play Mode ile hızlı solo test) KCP transport'unu,
/// gerçek build'de FizzySteamworks'ü (Steam ağı) kullanır.
///
/// NEDEN GEREKLİ: Multiplayer Play Mode'un ikinci "sanal oyuncusu" da AYNI
/// bilgisayardaki AYNI Steam hesabıyla çalışıyor — host ve client aynı
/// SteamID'ye sahip olunca Steam P2P bağlantısı "kendi kendine" bağlanmaya
/// çalışır, bu desteklenmiyor/güvenilir değil. Bu yüzden günlük geliştirme
/// testleri için Editor'de KCP'ye (LAN/localhost, Steam'e ihtiyaç duymaz)
/// geri dönülüyor. Gerçek build'de (arkadaşlarla Steam üzerinden oynanan
/// asıl senaryoda) FizzySteamworks aktif kalıyor.
///
/// [DefaultExecutionOrder(-10000)] ile SteamManager'dan (-9999) bile önce
/// çalışıyor — NetworkManager host/client başlatmadan ÖNCE doğru transport
/// seçilmiş olmalı.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class TransportSwitcher : MonoBehaviour
{
    [Tooltip("Editor'de (Play modunda) kullanılacak transport — KcpTransport.")]
    [SerializeField] private Transport editorTransport;
    [Tooltip("Gerçek build'de kullanılacak transport — FizzySteamworks.")]
    [SerializeField] private Transport buildTransport;

    void Awake()
    {
        NetworkManager nm = GetComponent<NetworkManager>();
        if (nm == null)
        {
            Debug.LogWarning("[TransportSwitcher] Bu objede NetworkManager yok, transport seçilemedi.");
            return;
        }

#if UNITY_EDITOR
        Transport chosen = editorTransport;
        Transport other = buildTransport;
        string label = "Editor → KCP";
#else
        Transport chosen = buildTransport;
        Transport other = editorTransport;
        string label = "Build → FizzySteamworks";
#endif

        if (chosen == null)
        {
            Debug.LogWarning($"[TransportSwitcher] Seçilecek transport atanmamış ({label})!");
            return;
        }

        chosen.enabled = true;
        nm.transport = chosen;
        if (other != null) other.enabled = false;

        Debug.Log($"[TransportSwitcher] Aktif transport: {label}");
    }
}
