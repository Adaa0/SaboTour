using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

/// <summary>
/// ⚠️ GEÇİCİ ARAÇ — SADECE EKRAN GÖRÜNTÜSÜ / CAPSULE ART ÇEKİMİ İÇİN.
/// Steam görselleri bitince bu dosya ve sahnedeki objesi SİLİNECEK.
///
/// HAYALET TEKRAR (Ghost Replay) — "tek kişiyken 2 araba" düzeneği
///
/// PROBLEM: Yan yana drift atan 2 araba karesi lazım ama tek kişisin. İki
/// arabayı aynı anda süremezsin. "Ben donayım, öbürü gelsin" de çalışmıyor:
/// duman (ParticleSystem) ve skidmark (TrailRenderer) ÖLÇEKLİ zamanla
/// çalıştığı için, sen `Time.timeScale = 0` ile donduğunda öbür arabanın
/// dumanı da senin makinende donuyor — yanına dumansız bir araba geliyor.
///
/// ÇÖZÜM: Kendi driftini KAYDET, sonra o kaydı bir "hayalet araba" olarak
/// tekrar oynatırken sen ikinci arabayı onun YANINDA canlı sür. İkisi de
/// gerçekten hareket ettiği için ikisinin de dumanı/izi normal çıkar.
/// Doğru anda F7 (FreezeFrame) ile ikisini birden dondurup F9 ile çekersin.
///
/// KULLANIM SIRASI:
///   1. F5  → kayda başla, sağ şeritte güzel bir drift at, tekrar F5 → kaydı bitir.
///   2. F6  → hayalet arabayı çıkar. Kaydettiğin drifti sürekli tekrar oynatır.
///   3. Hayalet döngüye girmişken sen sol şeritte onun yanında drift at.
///   4. İkisi yan yanayken F7 → ikisi de donar (dumanlar/izler yerinde kalır).
///   5. F8 ile açıyı ayarla, F10 ile HUD'u gizle, F9 ile çek.
///   6. F6 → hayaleti kaldır.
///
/// TUŞLAR: F5 kayıt · F6 hayalet · F7 dondur · F8 serbest kamera · F9 çek · F10 HUD
///
/// KULLANIM: Online Scene'de boş bir GameObject'e ekle — ScreenshotCapture,
/// FreeCamera ve FreezeFrame ile AYNI objede durabilir.
/// </summary>
public class GhostReplay : MonoBehaviour
{
    [Header("Tuşlar")]
    [SerializeField] private Key recordKey = Key.F5;
    [SerializeField] private Key ghostKey = Key.F6;

    [Header("Kayıt")]
    [Tooltip("Kayıt en fazla kaç saniye sürsün (bu süre dolunca otomatik durur).")]
    [SerializeField] private float maxRecordSeconds = 20f;

    [Header("Hayalet")]
    [Tooltip("Hayalet arabanın tüm duman/skidmark efektleri tekrar boyunca açık kalsın mı? " +
             "Kapalıysa hayalet efektsiz, sadece hareket eden bir gövde olur.")]
    [SerializeField] private bool forceGhostEffects = true;

    [Tooltip("Tekrar bitince baştan başlasın mı? (Yanına geçmek için zamana ihtiyacın olur, açık bırak.)")]
    [SerializeField] private bool loopReplay = true;

    [Tooltip("Döngü başa dönmeden önceki bekleme (saniye) — hayaletin ışınlanması göze batmasın diye.")]
    [SerializeField] private float loopPauseSeconds = 0.5f;

    /// <summary>Kaydın tek bir karesi: o andaki dünya pozu.</summary>
    private struct Sample
    {
        public float time;
        public Vector3 position;
        public Quaternion rotation;
    }

    private readonly List<Sample> samples = new List<Sample>();

    private bool isRecording;
    private float recordStartTime;
    private Transform recordTarget;      // kaydedilen (bizim) araba
    private GameObject recordedCarObject; // hayaleti kopyalarken kaynak olarak kullanılıyor

    private GameObject ghost;
    private float replayStartTime;

    void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[recordKey].wasPressedThisFrame)
            ToggleRecording();

        if (Keyboard.current[ghostKey].wasPressedThisFrame)
            ToggleGhost();

        if (isRecording) RecordFrame();
        if (ghost != null) ReplayFrame();
    }

    // ──────────────────────────────────────────────────────────────────────
    // KAYIT
    // ──────────────────────────────────────────────────────────────────────

    private void ToggleRecording()
    {
        if (isRecording) StopRecording();
        else StartRecording();
    }

    private void StartRecording()
    {
        recordTarget = FindLocalCar();
        if (recordTarget == null)
        {
            Debug.LogWarning("[GhostReplay] Kendi araban bulunamadı — kayıt başlatılamadı. " +
                             "Araba olarak spawn olduğundan emin ol (sabotajcıyken çalışmaz).");
            return;
        }

        recordedCarObject = recordTarget.gameObject;

        samples.Clear();
        recordStartTime = Time.time;
        isRecording = true;

        Debug.Log($"[GhostReplay] KAYIT BAŞLADI. Drift at, bitince tekrar {recordKey}. " +
                  $"(En fazla {maxRecordSeconds} sn)");
    }

    private void RecordFrame()
    {
        float elapsed = Time.time - recordStartTime;

        if (elapsed > maxRecordSeconds)
        {
            StopRecording();
            return;
        }

        if (recordTarget == null)
        {
            StopRecording();
            return;
        }

        samples.Add(new Sample
        {
            time = elapsed,
            position = recordTarget.position,
            rotation = recordTarget.rotation
        });
    }

    private void StopRecording()
    {
        isRecording = false;
        Debug.Log($"[GhostReplay] KAYIT BİTTİ — {samples.Count} kare, " +
                  $"{(samples.Count > 0 ? samples[samples.Count - 1].time : 0f):F1} saniye. " +
                  $"{ghostKey} ile hayaleti çıkar.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // HAYALET
    // ──────────────────────────────────────────────────────────────────────

    private void ToggleGhost()
    {
        if (ghost != null) RemoveGhost();
        else SpawnGhost();
    }

    private void SpawnGhost()
    {
        if (samples.Count < 2)
        {
            Debug.LogWarning($"[GhostReplay] Önce {recordKey} ile bir drift kaydetmelisin.");
            return;
        }

        if (recordedCarObject == null)
        {
            Debug.LogWarning("[GhostReplay] Kaynak araba yok olmuş — tekrar kaydetmen gerekiyor.");
            return;
        }

        // ÖNEMLİ: Kopyayı doğrudan üretemiyoruz. Mirror'ın NetworkIdentity'si
        // Awake()'inde "bu obje zaten spawn edilmiş" diye hata basıp kopyayı
        // ANINDA yok ediyor (bkz. Mirror/Core/NetworkIdentity.cs:341-346) —
        // çünkü `hasSpawned` alanı kopyaya da true olarak geçiyor.
        //
        // Çözüm: kopyayı KAPALI bir taşıyıcının altında üretiyoruz. Unity,
        // kapalı hiyerarşideki objelerin Awake()'ini HİÇ çağırmıyor; biz de
        // bu arada network bileşenlerini temizleyip objeyi ondan sonra
        // aktif ediyoruz. Böylece o Awake() hiç çalışmamış oluyor.
        GameObject holder = new GameObject("GhostHolder");
        holder.SetActive(false);

        ghost = Instantiate(recordedCarObject, holder.transform);

        StripGhost(ghost);

        // Temizlik bitti — artık aktif edilebilir.
        ghost.transform.SetParent(null, false);
        ghost.transform.SetPositionAndRotation(samples[0].position, samples[0].rotation);
        ghost.name = "GhostCar (çekim aracı)";
        ghost.SetActive(true);

        Destroy(holder);

        if (forceGhostEffects)
            EnableGhostEffects(ghost);

        replayStartTime = Time.time;

        Debug.Log("[GhostReplay] Hayalet çıktı, kayıt tekrar oynatılıyor. " +
                  "Yanına geçip drift at, sonra F7 ile dondur.");
    }

    /// <summary>
    /// Kopyalanan arabayı zararsız bir "kukla"ya çevirir.
    ///
    /// NEDEN: Kopya, gerçek arabanın birebir aynısı — üzerinde Mirror
    /// bileşenleri, fizik, kendi sürüş kodu ve kamerası var. Bunlar
    /// temizlenmezse hayalet ya yere düşer, ya gerçek arabaya çarpar, ya da
    /// network'e karışıp hata verir. Biz sadece GÖRÜNTÜSÜNÜ istiyoruz;
    /// pozisyonunu her karede kayıttan biz yazacağız.
    ///
    /// SIRA ÖNEMLİ: NetworkBehaviour'lar NetworkIdentity'ye bağımlı olduğu
    /// için önce onlar, sonra identity yok ediliyor.
    ///
    /// NEDEN `DestroyImmediate`: Normal `Destroy()` işi KARE SONUNA erteler.
    /// Biz ise hayaleti aynı karede aktif ediyoruz — o an bileşenler hâlâ
    /// duruyor olurdu ve NetworkIdentity.Awake() yine çalışıp hayaleti yok
    /// ederdi. `DestroyImmediate` o satırda gerçekten siliyor.
    /// </summary>
    private static void StripGhost(GameObject target)
    {
        foreach (NetworkBehaviour behaviour in target.GetComponentsInChildren<NetworkBehaviour>(true))
            if (behaviour != null) DestroyImmediate(behaviour);

        foreach (NetworkIdentity identity in target.GetComponentsInChildren<NetworkIdentity>(true))
            if (identity != null) DestroyImmediate(identity);

        // Geriye kalan scriptleri (Cinemachine bileşenleri vb.) yok etmiyoruz —
        // aralarında birbirini zorunlu kılan (RequireComponent) ilişkiler
        // olabilir ve silme sırası hata verebilir. Kapatmak yeterli, çünkü
        // hayaletin pozisyonunu zaten biz yazıyoruz.
        foreach (MonoBehaviour script in target.GetComponentsInChildren<MonoBehaviour>(true))
            if (script != null) script.enabled = false;

        // Fizik: yok etmek yerine kinematik yapmak daha güvenli — başka
        // bileşenler Rigidbody referansı tutuyor olabilir.
        foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        // Gerçek arabanın hayalete çarpmaması için çarpışma kapatılıyor.
        foreach (Collider col in target.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // Kopyalanan araba kendi kamerasını da getiriyor — açık kalırsa
        // ekranı ele geçirir.
        foreach (Camera cam in target.GetComponentsInChildren<Camera>(true))
            cam.enabled = false;

        foreach (AudioListener listener in target.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;
    }

    /// <summary>
    /// Hayaletin duman ve lastik izlerini açar.
    ///
    /// Hayaletin CarController'ı kapatıldığı için efektleri artık kimse
    /// yönetmiyor — biz elle açıyoruz. Hayalet GERÇEKTEN hareket ettiği için
    /// (pozisyonu her kare değişiyor) TrailRenderer düzgün bir iz çiziyor ve
    /// partiküller arkada normal şekilde kalıyor.
    /// </summary>
    private static void EnableGhostEffects(GameObject target)
    {
        foreach (TrailRenderer trail in target.GetComponentsInChildren<TrailRenderer>(true))
        {
            trail.Clear();
            trail.emitting = true;
        }

        foreach (ParticleSystem particles in target.GetComponentsInChildren<ParticleSystem>(true))
            particles.Play();
    }

    private void RemoveGhost()
    {
        if (ghost != null) Destroy(ghost);
        ghost = null;
        Debug.Log("[GhostReplay] Hayalet kaldırıldı.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TEKRAR OYNATMA
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hayaleti kayıttaki zamana göre konumlandırır.
    ///
    /// `Time.time` (ölçekli zaman) kullanıyoruz — böylece F7 ile oyunu
    /// dondurduğunda hayalet de olduğu yerde donuyor, ayrıca bir şey
    /// yapmaya gerek kalmıyor.
    /// </summary>
    private void ReplayFrame()
    {
        float duration = samples[samples.Count - 1].time;
        float elapsed = Time.time - replayStartTime;

        if (elapsed > duration)
        {
            if (!loopReplay) return;

            // Döngü sonu: kısa bir bekleme, sonra başa sar.
            if (elapsed > duration + loopPauseSeconds)
            {
                replayStartTime = Time.time;
                RestartGhostTrails();
            }
            return;
        }

        // Kayıttaki iki komşu kareyi bulup aralarını yumuşatıyoruz —
        // kayıt frame hızına bağlı olduğu için bu, oynatmayı akıcı tutuyor.
        int index = FindSampleIndex(elapsed);
        Sample a = samples[index];
        Sample b = samples[Mathf.Min(index + 1, samples.Count - 1)];

        float span = b.time - a.time;
        float t = span > 0.0001f ? (elapsed - a.time) / span : 0f;

        ghost.transform.SetPositionAndRotation(
            Vector3.Lerp(a.position, b.position, t),
            Quaternion.Slerp(a.rotation, b.rotation, t));
    }

    /// <summary>Başa sarınca eski iz çizgisi havada asılı kalmasın diye temizlik.</summary>
    private void RestartGhostTrails()
    {
        if (ghost == null || !forceGhostEffects) return;

        foreach (TrailRenderer trail in ghost.GetComponentsInChildren<TrailRenderer>(true))
            trail.Clear();
    }

    private int FindSampleIndex(float elapsed)
    {
        // Kayıt zaman sırasında olduğu için basit ileri tarama yeterli;
        // birkaç yüz kare için performans sorunu olmaz.
        for (int i = samples.Count - 1; i >= 0; i--)
            if (samples[i].time <= elapsed) return i;

        return 0;
    }

    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Sahnedeki, bize ait olan arabayı bulur.</summary>
    private static Transform FindLocalCar()
    {
        foreach (CarController car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            if (car.isOwned) return car.transform;

        return null;
    }

    void OnDisable()
    {
        RemoveGhost();
    }
}
