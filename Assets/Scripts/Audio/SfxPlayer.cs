using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TÜM TEK SEFERLİK SES EFEKTLERİNİN ORTAK ÇALMA NOKTASI.
///
/// NEDEN BÖYLE BİR ŞEY VAR (mimari gerekçe):
/// Unity'de bir sesi çalmanın en bilinen yolu `AudioSource.PlayOneShot()`.
/// Ama bu, sesi çalan objenin üstünde bir AudioSource olmasını şart koşuyor —
/// ve o obje YOK OLURSA ses de anında kesiliyor. Bizim durumumuzda çalan
/// seslerin çoğu tam da yok olan objelere ait:
///   - Buz bombası patlarken kendini `Destroy` ediyor (IceBomb.Explode),
///   - Tavuk arabaya çarpınca anında `Destroy` oluyor (Chicken.OnHitByCar).
/// Bu yüzden ses, ÇALAN OBJEDEN BAĞIMSIZ bir yerden çalınmalı.
///
/// Bu sınıf, sahneden bağımsız (DontDestroyOnLoad) gizli bir obje altında
/// bir AudioSource HAVUZU tutuyor. Her ses isteği boştaki bir kaynağı
/// kapıyor, sesi çalıp serbest bırakıyor. Havuz sayesinde her sette yeni
/// GameObject yaratılmıyor (çöp toplayıcıyı yormuyor).
///
/// KULLANIMI (kod yazman gerekmez, zaten bağlandı — bilgi olsun diye):
///   SfxPlayer.PlayAt(clip, worldPosition);  // 3D — mesafeye göre kısılır
///   SfxPlayer.PlayUI(clip);                 // 2D — her yerde aynı ses (menü/HUD)
///
/// TÜM METODLAR NULL-GÜVENLİ: clip atanmamışsa (null) sessizce hiçbir şey
/// yapmıyor, hata vermiyor. Yani ses dosyalarını Inspector'a sürüklemeden
/// önce de oyun bugünkü gibi sorunsuz çalışmaya devam eder.
/// </summary>
public static class SfxPlayer
{
    /// <summary>
    /// Aynı anda çalabilecek en fazla ses sayısı. Bu sayıya ulaşılırsa yeni
    /// istekler sessizce düşürülür — 20 tavuk aynı anda gıdaklayıp sesi
    /// çamura çevirmesin diye bilinçli bir sınır.
    /// </summary>
    public const int MaxVoices = 24;

    private static readonly List<AudioSource> pool = new List<AudioSource>();
    private static Transform poolRoot;
    private static float masterVolume = 1f;

    /// <summary>
    /// Genel efekt sesi seviyesi (0-1). İleride ayarlar menüsü yazılınca
    /// slider'ın yazacağı yer BURASI — başka hiçbir dosyaya dokunmak
    /// gerekmeyecek.
    /// </summary>
    public static float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = Mathf.Clamp01(value);
    }

    /// <summary>
    /// Unity'de "Enter Play Mode Options" açıkken static alanlar Play'ler
    /// arasında SIFIRLANMIYOR — havuzda önceki oturumdan kalma, artık yok
    /// edilmiş AudioSource referansları kalır ve ses hiç çıkmaz. Bu metod
    /// her Play başında havuzu temizleyerek o tuzağı kapatıyor.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pool.Clear();
        poolRoot = null;
        masterVolume = 1f;
    }

    /// <summary>
    /// 3D ses — dünyada belirli bir noktada çalar, uzaklaştıkça kısılır.
    /// Patlama, çarpma, gıdaklama, buton gibi "bir yerden gelen" sesler için.
    /// </summary>
    /// <param name="pitchJitter">Her çalışta perdeyi ±bu kadar rastgele kaydırır (0.05 = %5). Aynı sesin arka arkaya tekrarı robotik/tekdüze duyulmasın diye.</param>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f,
                              float pitchJitter = 0.05f, float minDistance = 8f, float maxDistance = 90f)
    {
        if (clip == null) return;

        AudioSource src = GetFreeSource();
        if (src == null) return;

        src.transform.position = position;
        src.spatialBlend = 1f;          // tamamen 3D
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume) * masterVolume;
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.Play();
    }

    /// <summary>
    /// 2D ses — konumdan bağımsız, hep aynı seviyede duyulur. Buton tıklaması,
    /// tur bitti bildirimi, kazandın/kaybettin gibi ARAYÜZ sesleri için.
    /// </summary>
    public static void PlayUI(AudioClip clip, float volume = 1f, float pitchJitter = 0f)
    {
        if (clip == null) return;

        AudioSource src = GetFreeSource();
        if (src == null) return;

        src.transform.position = Vector3.zero;
        src.spatialBlend = 0f;          // tamamen 2D
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume) * masterVolume;
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.Play();
    }

    /// <summary>
    /// Bir diziden rastgele bir klip seçip 3D çalar. Çarpma/gıdaklama gibi
    /// SIK TEKRARLAYAN sesler için: aynı dosyayı her seferinde duymak
    /// yapaylık hissi veriyor, 3-4 varyasyon arasında dönmek çok daha
    /// doğal duyuluyor.
    /// </summary>
    public static void PlayRandomAt(AudioClip[] clips, Vector3 position, float volume = 1f,
                                    float pitchJitter = 0.08f, float minDistance = 8f, float maxDistance = 90f)
    {
        AudioClip clip = Pick(clips);
        if (clip != null) PlayAt(clip, position, volume, pitchJitter, minDistance, maxDistance);
    }

    /// <summary>Bir diziden rastgele klip seçer (dizi boş/null ise null döner).</summary>
    public static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    // ─── Havuz Yönetimi ──────────────────────────────────────────────────

    private static AudioSource GetFreeSource()
    {
        EnsureRoot();

        // Çalmayı bitirmiş bir kaynağı geri dönüştür.
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null) continue;
            if (!pool[i].isPlaying) return pool[i];
        }

        if (pool.Count >= MaxVoices) return null; // sınır doldu, sesi düşür

        return CreateSource();
    }

    private static void EnsureRoot()
    {
        if (poolRoot != null) return;

        GameObject go = new GameObject("SfxPlayer (otomatik)");
        // DontDestroyOnLoad ŞART: Offline Scene → Online Scene geçişinde
        // (lobiden yarışa) havuz yok olsaydı, ilk sesle birlikte yeniden
        // kurulurdu — hem gereksiz iş hem de sahne geçişi sırasında çalan
        // sesler kesilirdi.
        UnityEngine.Object.DontDestroyOnLoad(go);
        poolRoot = go.transform;
    }

    private static AudioSource CreateSource()
    {
        GameObject go = new GameObject($"Sfx_{pool.Count}");
        go.transform.SetParent(poolRoot, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;

        pool.Add(src);
        return src;
    }
}
