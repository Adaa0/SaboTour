using UnityEngine;

/// <summary>
/// Kule odasındaki 3 skil butonundan biri. İki işi var:
///  (1) Hangi skile ait olduğunu taşımak — SaboteurInteraction bunu okuyor.
///  (2) KENDİ GÖRÜNÜŞÜNÜ yönetmek. Bu, MinimapCheckpointMarker'ın kendi
///      rengini yönetmesiyle aynı desen.
///
/// ─── GÖRSEL DİL ───────────────────────────────────────────────────────
/// Eski radyo/kaset konsollarındaki buton sırası gibi çalışıyor:
///   SEÇİLİ skil        → buton YARI basılı kalıyor (elinde ne olduğu belli,
///                        başka bir skil seçince bu kalkıp öbürü iniyor).
///   COOLDOWN'DA        → buton TAM basılı + rengi sönük, süre doldukça
///                        rengi geri geliyor.
///   COOLDOWN BİTİNCE   → mekanik bir buton gibi kalkıyor + küçük klik sesi.
/// İki sebep aynı kanalı paylaşıyor ama IŞIK ayırıyor:
///   basılı + parlak = seçili ve hazır · basılı + sönük = şarj oluyor.
/// Kalan saniye SADECE sabotajcı o butona BAKARKEN yazıyor.
///
/// ─── NEDEN BUTONUN ÜSTÜNDE İSİM YAZMIYOR ──────────────────────────────
/// Geliştirici kararı (19 Ağustos 2026): baktığın şeyin ADININ ekranda
/// yazması odayı ucuzlatıyor ve butonlar zaten renk kodlu (buz-mavi /
/// tavuk-sarı / turuncu). İsim bilgisi ilk maçın ilk 30 saniyesinde bir
/// kere lazım — orası TUTORIAL'ın işi. Cooldown ise her seferinde lazım ve
/// ANLIK bir durum, tutorial anlatamaz — o yüzden odada duruyor.
///
/// ─── ÇÖKMEDE NEDEN POZİSYON DEĞİL SCALE KULLANILIYOR ──────────────────
/// 🚨 Bir kere denendi ve GERİ ALINDI: butonu `localPosition`'da "aşağı"
/// kaydırmak YANLIŞ YÖNE hareket ettiriyor, çünkü konsol FBX'i döndürülmüş
/// geliyor ve objenin local "aşağı"sı dünya aşağısı değil. Ayrıca model
/// 1000 kat büyütülmüş olduğu için metre cinsinden girilen her değer 1000
/// katına çıkıyor. Ölçek küçültmek ikisinden de etkilenmiyor — bu yüzden
/// çökme işi tamamen InteractableFeedback'e (scale) bırakıldı.
/// </summary>
public class SkillSelectButton : MonoBehaviour
{
    public SkillType skill;

    [Tooltip("Buton birden fazla parçadan oluşuyorsa (kaide + kubbe gibi) TÜM parçaları " +
             "içeren üst obje buraya sürüklenmeli. Boş bırakılırsa bu objenin kendisi kullanılır.")]
    [SerializeField] private Transform visualRoot;

    /// <summary>Çökme animasyonunun uygulanacağı kök obje.</summary>
    public Transform FeedbackRoot => visualRoot != null ? visualRoot : transform;

    // ─── BASILI KALMA: İKİ SEBEP, İKİ DERİNLİK ───────────────────────────
    [Header("BASILI KALMA — seçili (yarı) / cooldown (tam)")]
    [Tooltip("SEÇİLİ butonun ne kadar basılı duracağı. 0 = hiç basılı kalmaz, " +
             "1 = cooldown'daki kadar tam çöker. Yarı bir değer, 'bu skil elimde' ile " +
             "'bu skil şarj oluyor' hâllerini birbirinden ayırt edilebilir kılıyor.")]
    [Range(0f, 1f)][SerializeField] private float armedPressAmount = 0.55f;

    // ─── IŞIK: SKİL HAZIR MI ─────────────────────────────────────────────
    [Header("IŞIK — cooldown'da sönük, doldukça geri geliyor")]
    [Tooltip("Kararıp parlayacak Renderer'lar. BOŞ bırakılırsa bu objenin (ve alt objelerinin) " +
             "ilk Renderer'ı otomatik kullanılır.")]
    [SerializeField] private Renderer[] lightRenderers;

    [Tooltip("Cooldown'un EN BAŞINDA buton ne kadar sönük olsun. 0 = simsiyah, 1 = hiç kararmaz. " +
             "Tam 0 yapma — buton kaybolmuş gibi görünür.")]
    [Range(0f, 1f)][SerializeField] private float cooldownDimness = 0.2f;

    [Tooltip("Buton hazırken ayrıca ışık yaysın mı. ⚠️ ÇALIŞMASI İÇİN butonun MATERYALİNDE " +
             "Emission AÇIK olmalı. Şu an üç buton materyalinde de KAPALI (buz buton dış / " +
             "hazard_orange / straw_yellow) — açılmazsa bu ayarın etkisi olmaz, sadece " +
             "kararma/parlama çalışır.")]
    [SerializeField] private bool useEmission = true;

    [Tooltip("Hazırken yayılan ışığın gücü. 1 civarı normal, 3+ göz alıcı olur.")]
    [SerializeField] private float readyEmission = 1.5f;

    // ─── HAZIR OLMA SESİ ─────────────────────────────────────────────────
    [Header("HAZIR OLMA SESİ — cooldown bitip buton kalkarken")]
    [Tooltip("Butonun yukarı kalkarken çıkardığı küçük mekanik klik/hazır sesi. " +
             "Boş bırakılabilir — SfxPlayer null-güvenli, klip yoksa sessizce atlıyor.")]
    [SerializeField] private AudioClip readyClip;
    [Range(0f, 1f)][SerializeField] private float readyVolume = 0.7f;

    // ─── KALAN SÜRE YAZISI (SADECE BAKARKEN) ─────────────────────────────
    [Header("KALAN SÜRE YAZISI — sadece butona bakarken")]
    [SerializeField] private bool showCountdown = true;

    [Tooltip("Yazı, butonun kendi boyunun kaç katı kadar üstünde dursun. 1 = tam bir buton boyu yukarıda.")]
    [SerializeField] private float countdownHeightRatio = 1f;

    [Tooltip("Yazı boyutu, butonun kendi boyuna oranla. Yazı çok küçük/büyük çıkıyorsa burayı ayarla.")]
    [SerializeField] private float countdownSizeRatio = 0.6f;

    [SerializeField] private Color countdownColor = Color.white;

    // ─── İÇ DURUM ────────────────────────────────────────────────────────
    // Bu değerleri her karede SaboteurInteraction yazıyor (UpdateVisualState).
    private bool armed;
    private float cooldownRemaining;
    private float cooldownTotal;
    private bool hovered;
    private Transform viewer;

    // Bu buton sabotajcının makinesinde mi güncelleniyor. Yarışçıların
    // makinesinde de bu obje duruyor ama orada kimse konsola bakmıyor —
    // ses/yazı gibi şeyler boşuna tetiklenmesin diye ayırt ediliyor.
    private bool drivenBySaboteur;
    private bool wasOnCooldown;

    private InteractableFeedback feedback;
    private MaterialPropertyBlock block;
    private Color[] baseColors;            // her renderer'ın orijinal rengi
    private float lastBrightness = -1f;    // -1 = henüz hiç uygulanmadı
    private float buttonWorldSize = 1f;    // butonun dünyadaki gerçek boyu (metre)

    private GameObject countdownObject;
    private TextMesh countdownText;

    void Awake()
    {
        if (lightRenderers == null || lightRenderers.Length == 0)
        {
            Renderer own = GetComponentInChildren<Renderer>();
            lightRenderers = own != null ? new[] { own } : new Renderer[0];
        }

        block = new MaterialPropertyBlock();
        baseColors = new Color[lightRenderers.Length];
        for (int i = 0; i < lightRenderers.Length; i++)
            baseColors[i] = ReadBaseColor(lightRenderers[i]);

        buttonWorldSize = MeasureWorldSize();

        // Çökme animasyonunun sahibi InteractableFeedback. SaboteurInteraction
        // da tıklama anında aynı component'i arayıp buluyor (yoksa ekliyor) —
        // burada baştan oluşturmak, cooldown'a girildiğinde butona hiç
        // tıklanmamış olsa bile basılı kalabilmesini garantiliyor.
        feedback = FeedbackRoot.GetComponent<InteractableFeedback>();
        if (feedback == null) feedback = FeedbackRoot.gameObject.AddComponent<InteractableFeedback>();

        ApplyBrightness(1f);
    }

    /// <summary>
    /// Butonun dünyadaki gerçek boyu (metre). Yazının konumu/boyutu buna
    /// oranlanıyor — konsol 1000 kat büyütülmüş olduğu için sabit metre
    /// değerleri kullanılamıyor.
    /// </summary>
    private float MeasureWorldSize()
    {
        Renderer r = lightRenderers.Length > 0 ? lightRenderers[0] : GetComponentInChildren<Renderer>();
        if (r == null) return 1f;

        Vector3 size = r.bounds.size;
        float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        return largest > 0.0001f ? largest : 1f;
    }

    /// <summary>
    /// SaboteurInteraction her karede çağırıyor. Bu obje NETWORK'E BAĞLI
    /// DEĞİL — cooldown bilgisi skil scriptlerinin SyncVar'ından okunuyor,
    /// buton sadece o sayıyı görselleştiriyor.
    ///
    /// </summary>
    public void UpdateVisualState(bool isArmed, float remaining, float total, bool isHovered, Transform viewerTransform)
    {
        armed = isArmed;
        cooldownRemaining = remaining;
        cooldownTotal = total;
        hovered = isHovered;
        viewer = viewerTransform;
        drivenBySaboteur = true;
    }

    void Update()
    {
        UpdateHeldState();
        UpdateLight();
        UpdateCountdown();
    }

    /// <summary>
    /// Buton İKİ SEBEPLE basılı kalabiliyor ve ikisi farklı derinlikte:
    ///   SEÇİLİ      → yarı basılı (armedPressAmount). "Elimde bu var."
    ///   COOLDOWN'DA → tam basılı. "Bu şu an kilitli, şarj oluyor."
    /// Cooldown her zaman baskın — seçili bir skil ateşlenince buton yarıdan
    /// tama iniyor, cooldown bitince yarıya geri çıkıyor (hâlâ seçili çünkü).
    /// Seçili değilse tamamen yukarı kalkıyor.
    ///
    /// İkisi aynı kanalı paylaşsa da karışmıyor, çünkü IŞIK ayırıyor:
    /// basılı + parlak = seçili ve hazır, basılı + sönük = şarj oluyor.
    /// </summary>
    private void UpdateHeldState()
    {
        bool onCooldown = cooldownRemaining > 0f;

        float target = onCooldown ? 1f : (armed ? armedPressAmount : 0f);
        feedback?.SetHeldAmount(target);   // değer değişmediyse kendi içinde hiçbir şey yapmıyor

        // Cooldown BİTTİĞİ AN → buton kalkıyor, klik sesi. Geçiş anı
        // yakalanıyor ki ses her karede değil bir kere çalsın.
        if (onCooldown == wasOnCooldown) return;
        wasOnCooldown = onCooldown;

        // Sadece sabotajcının makinesinde: yarışçılarda bu değerler hiç
        // güncellenmediği için zaten buraya girilmiyor, yine de açıkça
        // kontrol ediliyor.
        if (!onCooldown && drivenBySaboteur)
            SfxPlayer.PlayAt(readyClip, transform.position, readyVolume, 0.03f, 3f, 25f);
    }

    /// <summary>
    /// Cooldown doldukça parlaklık geri geliyor. Cooldown yoksa tam parlak.
    /// </summary>
    private void UpdateLight()
    {
        float fill = 1f;

        if (cooldownRemaining > 0f)
        {
            // Toplam süre bilinmiyorsa (SyncVar henüz gelmediyse) en azından
            // "sönük" göster — yanlışlıkla "hazır" gibi parlamasın.
            fill = cooldownTotal > 0.01f
                ? Mathf.Clamp01(1f - (cooldownRemaining / cooldownTotal))
                : 0f;
        }

        ApplyBrightness(Mathf.Lerp(cooldownDimness, 1f, fill));
    }

    /// <summary>
    /// Rengi MaterialPropertyBlock ile uyguluyoruz, materyalin kendisini
    /// DEĞİŞTİRMİYORUZ. Sebep: `renderer.material` çağırmak o materyalin bir
    /// KOPYASINI yaratıyor — üç buton üç ayrı materyal demek, bu da GPU
    /// Instancing'i kırıyor (bkz. CLAUDE.md performans bölümü; CarController
    /// araba boyamada aynı sebeple PropertyBlock kullanıyor).
    /// </summary>
    private void ApplyBrightness(float brightness)
    {
        // Parlaklık değişmediyse hiç dokunma — cooldown yokken buton kareler
        // boyunca aynı görünüyor, her karede SetPropertyBlock çağırmak boşa iş.
        if (Mathf.Approximately(brightness, lastBrightness)) return;
        lastBrightness = brightness;

        for (int i = 0; i < lightRenderers.Length; i++)
        {
            Renderer r = lightRenderers[i];
            if (r == null) continue;

            Color tinted = baseColors[i] * brightness;
            tinted.a = baseColors[i].a;   // saydamlığa dokunma (buz butonu camsı, alfa 0.29)

            r.GetPropertyBlock(block);

            if (HasColorProperty(r, "_BaseColor")) block.SetColor("_BaseColor", tinted);
            if (HasColorProperty(r, "_Color")) block.SetColor("_Color", tinted);

            if (useEmission && HasColorProperty(r, "_EmissionColor"))
                block.SetColor("_EmissionColor", baseColors[i] * (brightness * readyEmission));

            r.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// Kalan saniye — SADECE sabotajcı bu butona bakarken VE cooldown
    /// sürerken. Hazırken hiçbir şey yazmıyor (yerde duran bir "0" gürültü
    /// olurdu).
    ///
    /// Yazı objesi bilerek HİÇBİR ŞEYİN ÇOCUĞU DEĞİL: Unity'de çocuk objeler
    /// parent'ın scale'ini miras alıyor ve bu konsol 1000 kat ölçekli —
    /// minimap'te birebir bu yaşandı, yazılar ezik çıkmıştı (bkz. CLAUDE.md
    /// MinimapRoot ters-scale çözümü). Sahnede bağımsız durup her karede
    /// butonun üstüne konumlanmak bu sorunu tamamen ortadan kaldırıyor.
    /// </summary>
    private void UpdateCountdown()
    {
        bool shouldShow = showCountdown && hovered && cooldownRemaining > 0.05f;

        if (!shouldShow)
        {
            if (countdownObject != null && countdownObject.activeSelf)
                countdownObject.SetActive(false);
            return;
        }

        EnsureCountdownObject();
        if (countdownObject == null) return;

        if (!countdownObject.activeSelf) countdownObject.SetActive(true);

        countdownObject.transform.position =
            transform.position + Vector3.up * (buttonWorldSize * countdownHeightRatio);

        // Yazı hep bakan kişiye dönük dursun. LookRotation'a "bakandan yazıya"
        // yönü veriliyor (tersi verilirse yazı ayna gibi ters okunur).
        if (viewer != null)
            countdownObject.transform.rotation =
                Quaternion.LookRotation(countdownObject.transform.position - viewer.position);

        countdownText.text = Mathf.CeilToInt(cooldownRemaining).ToString();
        countdownText.color = countdownColor;
    }

    private void EnsureCountdownObject()
    {
        if (countdownObject != null) return;

        countdownObject = new GameObject($"SkillCountdown_{skill}");

        // Obje hiçbir şeyin çocuğu olmadığı için localScale = dünya ölçeği.
        // Butonun boyuna oranlıyoruz, böylece konsol ölçeği değişse bile
        // yazı orantılı kalıyor.
        countdownObject.transform.localScale = Vector3.one * (buttonWorldSize * countdownSizeRatio);

        countdownText = countdownObject.AddComponent<TextMesh>();
        countdownText.fontSize = 64;
        countdownText.characterSize = 0.1f;
        countdownText.anchor = TextAnchor.MiddleCenter;
        countdownText.alignment = TextAlignment.Center;
        countdownText.color = countdownColor;
        countdownText.text = "";
    }

    /// <summary>
    /// Materyalin ORİJİNAL rengi. `sharedMaterial` okunuyor (`material`
    /// değil) — `material` okumak bile materyalin kopyasını yaratıyor.
    /// </summary>
    private static Color ReadBaseColor(Renderer r)
    {
        if (r == null || r.sharedMaterial == null) return Color.white;

        if (r.sharedMaterial.HasProperty("_BaseColor")) return r.sharedMaterial.GetColor("_BaseColor");
        if (r.sharedMaterial.HasProperty("_Color")) return r.sharedMaterial.GetColor("_Color");
        return Color.white;
    }

    private static bool HasColorProperty(Renderer r, string property)
    {
        return r.sharedMaterial != null && r.sharedMaterial.HasProperty(property);
    }

    /// <summary>
    /// Yazı objesi hiçbir şeyin çocuğu olmadığı için, buton yok olduğunda
    /// sahnede öksüz kalmasın diye elle temizleniyor.
    /// </summary>
    void OnDestroy()
    {
        if (countdownObject != null) Destroy(countdownObject);
    }

#if UNITY_EDITOR
    /// <summary>
    /// TEK BAŞINA TEST İÇİN: Play mode'dayken bu component'in sağ üstündeki
    /// üç noktaya tıklayıp "Test: 5sn Cooldown Oynat" seçince buton gerçekten
    /// cooldown'a girmiş gibi davranır (çöker, kararır, 5 saniye sonra kalkar).
    /// İki oyunculu test kurmadan görselleri ayarlayabilmek için.
    /// Sadece Editor'de derleniyor, gerçek build'e HİÇ girmiyor.
    /// </summary>
    [ContextMenu("Test: 5sn Cooldown Oynat")]
    private void DebugPlayCooldown()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SkillSelectButton] Bu test sadece Play mode'da çalışır.");
            return;
        }

        drivenBySaboteur = true;
        StartCoroutine(DebugCooldownRoutine(5f));
    }

    private System.Collections.IEnumerator DebugCooldownRoutine(float duration)
    {
        float left = duration;
        while (left > 0f)
        {
            cooldownRemaining = left;
            cooldownTotal = duration;
            left -= Time.deltaTime;
            yield return null;
        }

        cooldownRemaining = 0f;
    }
#endif
}
