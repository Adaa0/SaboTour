using UnityEngine;

/// <summary>
/// KATMAN BAŞINA ÇİZİM MESAFESİ — küçük propları erken kesip GPU bütçesini
/// uzaktaki ağaçlara harcamak için.
///
/// ─── SORUN ────────────────────────────────────────────────────────────
/// Kaya, çalı, çim gibi küçük proplar 150-200 birimden sonra ekranda birkaç
/// piksel kalıyor — yani pratikte GÖRÜNMÜYORLAR. Ama Unity onları uzaktaki
/// bir ağaçla aynı şekilde çiziyor: aynı çizim çağrısı, aynı üçgenler, aynı
/// gölge maliyeti. Bu tamamen boşa giden bir bütçe.
///
/// ─── ÇÖZÜM ────────────────────────────────────────────────────────────
/// Unity'nin `Camera.layerCullDistances` özelliği her KATMAN için ayrı bir
/// maksimum çizim mesafesi tanımlamaya izin veriyor. Küçük propları kendi
/// katmanına koyup (bkz. TrackPropScatter.cullLayerName) o katmanı erken
/// kesiyoruz; ağaçlar tam mesafede çizilmeye devam ediyor.
///
/// ─── NEDEN OCCLUSION CULLING DEĞİL ────────────────────────────────────
/// Occlusion Culling bu projede ÇALIŞMAZ: önceden hesaplama (bake) gerektiriyor
/// ve sadece Editor'de "Static" işaretlenmiş objelerde işliyor. Bizim pistimiz
/// ve proplarımız çalışma anında rastgele seed'den üretiliyor, bake edilecek
/// sabit bir sahne yok. Katman mesafesi ise runtime üretimiyle tamamen uyumlu.
/// (Frustum culling — kamera görüş konisi dışını çizmeme — zaten varsayılan
/// olarak açık, ekstra bir şey yapmaya gerek yok.)
///
/// ─── KULLANIM ─────────────────────────────────────────────────────────
/// Sahnede herhangi bir objeye ekle (pist üreticisiyle aynı objeye konabilir),
/// Rules listesine katman adı + mesafe gir. Mesafe 0 bırakılırsa o katman
/// kameranın normal Far Clip mesafesine kadar çizilir.
/// </summary>
public class PropCullDistances : MonoBehaviour
{
    [System.Serializable]
    public class LayerRule
    {
        [Tooltip("Katman adı — TrackPropScatter'daki 'Cull Layer Name' ile AYNI yazılmalı.")]
        public string layerName;

        [Tooltip("Bu katmandaki objeler kaç birimden sonra çizilmesin. " +
                 "0 = sınır yok (kameranın Far Clip'ine kadar çizilir).")]
        public float maxDrawDistance = 200f;
    }

    [Tooltip("Katman başına çizim mesafeleri.")]
    [SerializeField] private LayerRule[] rules;

    [Tooltip("AÇIK: mesafe kameradan KÜRESEL olarak ölçülür (her yöne eşit) — " +
             "proplar için doğal olan bu.\n" +
             "KAPALI: Unity'nin varsayılanı, mesafe kameranın baktığı yöndeki " +
             "düzleme göre ölçülür; yanlara doğru bakınca kesme mesafesi tutarsız hissettirir.")]
    [SerializeField] private bool spherical = true;

    [SerializeField] private bool showDebugLogs = true;

    // Ayarın uygulandığı kameralar. Bu projede kamera DEĞİŞEBİLİYOR: izleyici
    // modu sahnedeki Main Camera'yı devralıyor, podyumun kendi kamerası var,
    // sabotajcının kendi FPCam'i var. Bir kere uygulayıp bırakmak yetmiyor.
    private readonly System.Collections.Generic.HashSet<Camera> appliedCameras = new();

    // Camera.GetAllCameras'a verilen tampon — Camera.allCameras her çağrıda
    // yeni bir dizi ayırıyor, bu ise her karede çalışan bir metot.
    private Camera[] cameraBuffer = new Camera[8];

    /// <summary>
    /// ══ BU METOT BİR BUG'I DÜZELTİYOR (21 Ağustos 2026) ══
    /// Eskiden SADECE `Camera.main`'e uygulanıyordu. Ama **sabotajcının
    /// FPCam'i "MainCamera" etiketli DEĞİL** — yani sabotajcı oynarken
    /// çizim mesafeleri HİÇ uygulanmıyordu ve bütün proplar far clip'e
    /// (1000 birim) kadar çiziliyordu.
    ///
    /// Üstelik kule pistin TAM ORTASINDA ve YÜKSEKTE, dört yanı pencere:
    /// oradan bakınca dünyanın tamamı görüş konisine giriyor ve önünü
    /// kesen hiçbir şey yok. Yani culling'e EN ÇOK ihtiyaç duyan görüntü,
    /// culling'in hiç uygulanmadığı görüntüydü. Zayıf bir makinede
    /// yarışçıda 110 FPS alınırken sabotajcıda 30 FPS'e düşmesinin
    /// sebeplerinden biri buydu.
    ///
    /// Artık hangi kamera aktifse ona uygulanıyor — etiketine bakılmıyor.
    /// </summary>
    void Update()
    {
        int count = Camera.allCamerasCount;
        if (count == 0) return;

        if (cameraBuffer.Length < count) cameraBuffer = new Camera[count];
        Camera.GetAllCameras(cameraBuffer);

        for (int i = 0; i < count; i++)
        {
            Camera cam = cameraBuffer[i];

            if (cam == null || appliedCameras.Contains(cam)) continue;

            Apply(cam);
            appliedCameras.Add(cam);
        }
    }

    public void Apply(Camera cam)
    {
        if (cam == null) return;

        // Dizi HER ZAMAN 32 uzunluğunda olmak zorunda (Unity'de 32 katman var).
        // Kameradan mevcut diziyi alıp sıfırlıyoruz ki elle ayarlanmış başka
        // bir değer varsa da temiz bir başlangıç olsun.
        float[] distances = cam.layerCullDistances;
        for (int i = 0; i < distances.Length; i++) distances[i] = 0f;

        int applied = 0;

        if (rules != null)
        {
            foreach (LayerRule rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.layerName)) continue;

                int layer = LayerMask.NameToLayer(rule.layerName);
                if (layer < 0)
                {
                    Debug.LogWarning($"[PropCullDistances] '{rule.layerName}' diye bir katman yok " +
                                     "(Project Settings > Tags and Layers). Bu kural atlandı.", this);
                    continue;
                }

                distances[layer] = Mathf.Max(0f, rule.maxDrawDistance);
                applied++;
            }
        }

        cam.layerCullSpherical = spherical;
        cam.layerCullDistances = distances;
        appliedCameras.Add(cam);

        if (showDebugLogs)
            Debug.Log($"[PropCullDistances] {applied} katman kuralı '{cam.name}' kamerasına uygulandı " +
                      $"(Far Clip {cam.farClipPlane}).");
    }
}
