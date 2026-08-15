using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// YARIŞÇININ EKRANINDAKİ YUVARLAK MİNİMAP (sağ üst köşe)
///
/// NE GÖSTERİYOR: pistin senin ÇEVRENDEKİ kısmını (tamamını değil), diğer
/// arabaları KENDİ RENKLERİYLE (lobide dağıtılan 12 renklik paletten) ve
/// checkpoint'leri — sıradaki checkpoint vurgulu.
///
/// ── NEDEN İKİNCİ BİR KAMERA (RenderTexture) KULLANILMADI ──
/// Klasik yol, arabanın üstüne ikinci bir ortografik kamera koyup dünyayı
/// bir dokuya çizdirmektir. Bu projede bilinçli olarak SEÇİLMEDİ: 8 Ağustos
/// profilinde tek gerçek maliyetin çizim tarafı olduğu ölçüldü (2.28M üçgen,
/// zayıf GPU'da 60-70 FPS). İkinci bir render pass o makinelerde doğrudan
/// FPS'ten yerdi. Bunun yerine pistin şekli TrackGenerator'dan alınıp UI'da
/// BİR KERE çiziliyor, sonra her karede sadece kaydırılıp döndürülüyor —
/// maliyeti ölçülemeyecek kadar küçük.
///
/// ── HARİTA DÖNÜYOR, ARABA SABİT ──
/// Senin araban her zaman merkezde ve YUKARI bakıyor, harita altında dönüyor
/// (yarış oyunlarında standart). Bu yüzden diğer arabaların ikonları da
/// senin yönüne GÖRE dönüyor: ekrandaki açıları (senin yaw'ın - onun yaw'ı).
///
/// ── NASIL YÜKLENİYOR (PauseMenu ile AYNI DESEN) ──
/// Oyun açılırken Assets/Resources/UI/RacerMinimap.prefab otomatik
/// Instantiate ediliyor ve DontDestroyOnLoad yapılıyor. Her karede yerel
/// oyuncunun bir yarışçı olup olmadığına bakıp Canvas'ı açıp kapatıyor —
/// yani lobide, sabotajcıda ve podyumda kendiliğinden gizleniyor, hiçbir
/// sahneye elle bir şey eklemek gerekmiyor.
///
/// PREFAB'I DÜZENLEMEK: Assets/Resources/UI/RacerMinimap.prefab'a çift tıkla,
/// normal bir sahne gibi düzenle (boyut, konum, renk, sprite). Prefabı
/// bozarsan Unity üst menüsünden "SaboTour > Yarışçı Minimap Prefabını
/// Oluştur" ile yeniden üretebilirsin.
/// </summary>
public class RacerMinimapHUD : MonoBehaviour
{
    private static RacerMinimapHUD instance;

    // ─── Prefab referansları (RacerMinimapPrefabBuilder dolduruyor) ──────
    [Header("Prefab Referansları — normalde dokunmana gerek yok")]
    public Canvas canvas;
    [Tooltip("Yuvarlak alanın kendisi. Boyutunu değiştirirsen minimap büyür/küçülür, ölçek otomatik uyum sağlar.")]
    public RectTransform maskRoot;
    [Tooltip("Yol + checkpoint'leri taşıyan katman. HER KAREDE kaydırılıp döndürülüyor.")]
    public RectTransform mapContent;
    [Tooltip("Pistin şeridini çizen bileşen.")]
    public MinimapRoadGraphic roadGraphic;
    [Tooltip("Checkpoint ikonlarının konacağı katman (mapContent'in çocuğu — haritayla birlikte döner).")]
    public RectTransform checkpointLayer;
    [Tooltip("Araba ikonlarının katmanı. mapContent'in DIŞINDA, çünkü ekran dışına taşan arabaları kenara yapıştırabilmek için konumlarını elle hesaplıyoruz.")]
    public RectTransform carIconLayer;
    [Tooltip("Merkezdeki 'sen' oku. Hiç hareket etmiyor, sadece rengi senin araba rengine boyanıyor.")]
    public Image playerArrow;
    [Tooltip("Araba ikonu olarak kullanılacak sprite (ok/üçgen).")]
    public Sprite carIconSprite;
    [Tooltip("Checkpoint ikonu olarak kullanılacak sprite (nokta/daire).")]
    public Sprite checkpointIconSprite;

    // ─── Ayarlar ─────────────────────────────────────────────────────────
    [Header("Görüş Alanı")]
    [Tooltip("Minimap'in KENARINA kadar kaç metrelik alan görünsün. Küçültürsen daha yakını daha büyük görürsün (yol daha okunaklı), büyütürsen daha çok rakip aynı anda görünür. Pist ~300m çapında olduğu için 180 civarı iyi bir denge.")]
    [SerializeField] private float viewRadiusMeters = 180f;
    [Tooltip("Yol şeridinin kalınlık çarpanı. 1 = gerçek yol genişliğiyle orantılı. Yol minimap'te çok kalın/ince geliyorsa buradan ayarla.")]
    [SerializeField] private float roadWidthScale = 1f;

    [Header("İkonlar")]
    [SerializeField] private float carIconSize = 20f;
    [SerializeField] private float checkpointIconSize = 11f;
    [Tooltip("AÇIK: görüş alanının dışındaki arabalar kaybolmak yerine minimap'in kenarına yapışıp yönlerini gösterir (küçültülmüş halde). KAPALI: sadece yakındakiler görünür.")]
    [SerializeField] private bool clampOffscreenCars = true;
    [Tooltip("Kenara yapışan (uzaktaki) arabaların ikonu bu oranda küçülür — yakındakilerle karışmasın diye.")]
    [Range(0.3f, 1f)][SerializeField] private float offscreenIconScale = 0.65f;

    [Header("Renkler")]
    [SerializeField] private Color roadColor = new Color(1f, 1f, 1f, 0.45f);
    [Tooltip("Henüz sırası gelmemiş checkpoint'ler.")]
    [SerializeField] private Color checkpointColor = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("SIRADAKİ checkpoint — gitmen gereken yer.")]
    [SerializeField] private Color nextCheckpointColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Sıradaki checkpoint ikonu bu oranda büyütülür.")]
    [SerializeField] private float nextCheckpointScale = 1.6f;

    // ─── Çalışma anı durumu ──────────────────────────────────────────────

    private CarController localCar;
    private PlayerRaceController localRace;
    private TrackGenerator trackGenerator;
    private CheckpointManager checkpointManager;
    private RacePodiumManager podiumManager;

    // İkonları Image olarak saklıyoruz (RectTransform DEĞİL): Image'ın zaten
    // bir `rectTransform` property'si var, yani ikisine de tek referanstan
    // ulaşılıyor. RectTransform saklasaydık rengi güncellemek için her karede
    // GetComponent<Image>() çağırmak gerekirdi — 30 checkpoint × 60 kare
    // hatırı sayılır bir israf olurdu.
    private readonly Dictionary<PlayerRaceController, Image> carIcons = new();
    private readonly List<Image> checkpointIcons = new();

    // Checkpoint vurgusu sadece SIRA DEĞİŞTİĞİNDE yeniden yazılıyor.
    private int highlightedCheckpoint = -999;

    // Pist her yarışta yeniden üretiliyor — hangi pist için çizdiğimizi
    // hatırlayıp sadece değiştiğinde yeniden çiziyoruz.
    private TrackGenerator builtForGenerator;
    private int builtPointCount = -1;
    private float builtPixelsPerMeter = -1f;

    private float pixelsPerMeter;
    private float radiusPixels;

    /// <summary>
    /// Oyun açılır açılmaz prefabı yükleyip DontDestroyOnLoad yapar
    /// (PauseMenuController.AutoCreate ile birebir aynı desen).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>("UI/RacerMinimap");
        if (prefab == null)
        {
            Debug.LogWarning("[RacerMinimap] Assets/Resources/UI/RacerMinimap.prefab bulunamadı! " +
                             "Unity Editor'de üst menüden 'SaboTour > Yarışçı Minimap Prefabını Oluştur' çalıştır.");
            return;
        }

        GameObject go = Instantiate(prefab);
        go.name = "RacerMinimap (otomatik)";
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        // TEKİLLİK KORUMASI — PauseMenu'de iç içe geçmiş ikinci bir kopyanın
        // menüyü kilitlemesi dersinden sonra aynı koruma buraya da kondu
        // (iki minimap üst üste çizilirse fark edilmesi zor bir görsel
        // karmaşa çıkar).
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[RacerMinimap] Sahnede birden fazla RacerMinimapHUD var — fazlalık devre dışı bırakıldı.");
            gameObject.SetActive(false);
            return;
        }

        instance = this;
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        // LateUpdate: araba fiziği/kamerası bu karede son halini aldıktan
        // SONRA çiziyoruz, yoksa minimap bir kare geriden gelirdi.

        if (!ResolveLocalRacer())
        {
            SetVisible(false);
            return;
        }

        radiusPixels = maskRoot != null ? maskRoot.rect.width * 0.5f : 0f;
        if (radiusPixels < 1f) return; // UI daha yerleşmedi (ilk kare)

        pixelsPerMeter = radiusPixels / Mathf.Max(1f, viewRadiusMeters);

        if (!EnsureTrackBuilt())
        {
            // Pist henüz üretilmedi (prosedürel, sahne açılışında birkaç kare
            // sürebiliyor) — minimap'i boş göstermek yerine gizli tutuyoruz.
            SetVisible(false);
            return;
        }

        SetVisible(true);

        Vector3 carPos = localCar.transform.position;
        float yaw = localCar.transform.eulerAngles.y;

        UpdateMapTransform(carPos, yaw);
        UpdateCheckpointIcons();
        UpdateCarIcons(carPos, yaw);
        UpdatePlayerArrowColor();
    }

    // ─── Yerel yarışçıyı bul ─────────────────────────────────────────────

    /// <summary>
    /// Minimap SADECE şu durumda gösterilir: yerel oyuncu bir araba ve o
    /// araba gerçekten yarışıyor. Sabotajcıda (araba yok), lobide
    /// (LobbyPlayer), izleyici modunda (araba gizli) ve podyumda kapalı.
    /// </summary>
    private bool ResolveLocalRacer()
    {
        NetworkIdentity local = NetworkClient.localPlayer;
        if (local == null)
        {
            localCar = null;
            localRace = null;
            return false;
        }

        // localPlayer sahne geçişinde değişiyor (lobi objesi → araba), bu
        // yüzden her karede kontrol ediyoruz. GetComponent maliyeti bu
        // sıklıkta önemsiz.
        if (localCar == null || localCar.netIdentity != local)
        {
            localCar = local.GetComponent<CarController>();
            localRace = local.GetComponent<PlayerRaceController>();
        }

        if (localCar == null) return false;

        // İzleyici modunda araba gizli — merkezi donmuş bir arabaya kilitli
        // bir harita göstermenin anlamı yok.
        if (localCar.HiddenForSpectator) return false;

        if (podiumManager == null) podiumManager = FindAnyObjectByType<RacePodiumManager>();
        if (podiumManager != null && !podiumManager.RaceInProgress) return false;

        return true;
    }

    private void SetVisible(bool visible)
    {
        if (canvas != null && canvas.enabled != visible)
            canvas.enabled = visible;
    }

    // ─── Pist şeridini çiz (yarışta bir kere) ────────────────────────────

    private bool EnsureTrackBuilt()
    {
        if (trackGenerator == null) trackGenerator = FindAnyObjectByType<TrackGenerator>();
        if (trackGenerator == null) return false;

        List<Vector3> worldPoints = trackGenerator.GetTrackPoints();
        if (worldPoints == null || worldPoints.Count < 3) return false;

        bool alreadyBuilt = builtForGenerator == trackGenerator &&
                            builtPointCount == worldPoints.Count &&
                            Mathf.Approximately(builtPixelsPerMeter, pixelsPerMeter);
        if (alreadyBuilt) return true;

        BuildTrack(worldPoints);
        return true;
    }

    private void BuildTrack(List<Vector3> worldPoints)
    {
        // Güvenlik sınırı: UI mesh'i tek seferde 65k vertex'i geçemez.
        // Normal pistlerde birkaç yüz nokta oluyor, bu sınıra hiç
        // yaklaşılmıyor — ama çok yüksek cornerSegments ayarlanırsa
        // noktaları seyreltip çizim yine de çalışsın.
        const int maxPoints = 4000;
        int step = Mathf.Max(1, Mathf.CeilToInt(worldPoints.Count / (float)maxPoints));

        var mapPoints = new List<Vector2>(worldPoints.Count / step + 1);
        for (int i = 0; i < worldPoints.Count; i += step)
            mapPoints.Add(WorldToMap(worldPoints[i]));

        if (roadGraphic != null)
        {
            roadGraphic.color = roadColor;
            roadGraphic.raycastTarget = false;
            roadGraphic.SetTrack(mapPoints, trackGenerator.roadWidth * pixelsPerMeter * roadWidthScale);
        }

        BuildCheckpointIcons();

        builtForGenerator = trackGenerator;
        builtPointCount = worldPoints.Count;
        builtPixelsPerMeter = pixelsPerMeter;

        Debug.Log($"[RacerMinimap] Pist çizildi: {mapPoints.Count} nokta, ölçek {pixelsPerMeter:F2} px/m ({viewRadiusMeters}m görüş).");
    }

    private void BuildCheckpointIcons()
    {
        foreach (Image icon in checkpointIcons)
        {
            if (icon != null) Destroy(icon.gameObject);
        }
        checkpointIcons.Clear();
        highlightedCheckpoint = -999;

        if (checkpointLayer == null) return;

        if (checkpointManager == null) checkpointManager = FindAnyObjectByType<CheckpointManager>();
        List<Transform> checkpoints = checkpointManager != null ? checkpointManager.checkpoints : null;
        if (checkpoints == null) return;

        foreach (Transform cp in checkpoints)
        {
            if (cp == null) continue;

            Image icon = CreateIcon(checkpointLayer, "Checkpoint", checkpointIconSprite, checkpointColor, checkpointIconSize);
            // Checkpoint'ler sabit — konumları bir kere yazılıyor, sonra
            // haritayla birlikte dönüyorlar (mapContent'in çocuğu oldukları için).
            icon.rectTransform.anchoredPosition = WorldToMap(cp.position);
            checkpointIcons.Add(icon);
        }
    }

    // ─── Her kare güncellenenler ─────────────────────────────────────────

    /// <summary>
    /// Haritayı, senin araban merkezde ve yukarı bakacak şekilde kaydırıp
    /// döndürür.
    ///
    /// MATEMATİK: ekranda görmek istediğimiz konum  R(yaw) * (nokta - araba).
    /// Bunu tek tek her nokta için hesaplamak yerine katmanın kendisine
    /// uyguluyoruz: katmanı yaw kadar döndürüp R(yaw) * (-araba) kadar
    /// kaydırmak, içindeki HER nokta için aynı sonucu veriyor.
    /// </summary>
    private void UpdateMapTransform(Vector3 carPos, float yaw)
    {
        if (mapContent == null) return;

        mapContent.localRotation = Quaternion.Euler(0f, 0f, yaw);
        mapContent.anchoredPosition = Rotate(-WorldToMap(carPos), yaw);
    }

    private void UpdateCheckpointIcons()
    {
        if (localRace == null || checkpointIcons.Count == 0) return;

        int total = checkpointIcons.Count;
        int next = ((localRace.CurrentCheckpoint + 1) % total + total) % total;

        // Sıra değişmediyse hiçbir şey yazmaya gerek yok — checkpoint'lerin
        // konumu zaten sabit, sadece hangisinin vurgulandığı değişiyor.
        if (next == highlightedCheckpoint) return;
        highlightedCheckpoint = next;

        for (int i = 0; i < total; i++)
        {
            Image icon = checkpointIcons[i];
            if (icon == null) continue;

            bool isNext = i == next;

            icon.color = isNext ? nextCheckpointColor : checkpointColor;

            float size = checkpointIconSize * (isNext ? nextCheckpointScale : 1f);
            icon.rectTransform.sizeDelta = new Vector2(size, size);

            // Harita dönerken checkpoint noktaları da dönüyor; ikonun KENDİSİ
            // yuvarlak olduğu için bu görünmüyor, ters çevirmeye gerek yok.
        }
    }

    private void UpdateCarIcons(Vector3 myPos, float myYaw)
    {
        if (carIconLayer == null) return;

        float edge = radiusPixels - carIconSize * 0.5f;

        foreach (PlayerRaceController player in PlayerRaceController.AllPlayers)
        {
            if (player == null) continue;

            // Kendi arabam merkezdeki sabit okla temsil ediliyor, ikinci bir
            // ikon gerekmiyor. Yarışı bitirenler de pistten kalktığı için
            // (izleyici modu) haritada görünmemeli.
            bool skip = player == localRace || player.HasFinished;

            if (!carIcons.TryGetValue(player, out Image icon) || icon == null)
            {
                if (skip) continue;
                icon = CreateIcon(carIconLayer, "Car", carIconSprite, ResolveCarColor(player), carIconSize);
                carIcons[player] = icon;
            }

            if (skip)
            {
                if (icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
                continue;
            }

            // Renk her karede yeniden yazılıyor: renk bir SyncVar ve spawn
            // anında henüz gelmemiş olabiliyor (o an -1). Bir kere yazsaydık
            // geç gelen renkleri kaçırır, ikon beyaz kalırdı.
            icon.color = ResolveCarColor(player);

            Vector3 pos = player.transform.position;
            Vector2 delta = Rotate(new Vector2(pos.x - myPos.x, pos.z - myPos.z) * pixelsPerMeter, myYaw);

            bool offscreen = delta.magnitude > edge;
            if (offscreen)
            {
                if (!clampOffscreenCars)
                {
                    if (icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
                    continue;
                }

                // Kenara yapıştır — rakip görüş alanının dışında olsa bile
                // hangi YÖNDE olduğunu görebilesin.
                delta = delta.normalized * edge;
            }

            if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);

            RectTransform iconRect = icon.rectTransform;
            iconRect.anchoredPosition = delta;

            // Ekrandaki açı = benim yaw'ım - onun yaw'ı. (Harita benim
            // yönüme göre döndüğü için onun mutlak yönü değil, bana GÖRE
            // olan yönü doğru olan.)
            iconRect.localRotation = Quaternion.Euler(0f, 0f, myYaw - player.transform.eulerAngles.y);

            float scale = offscreen ? offscreenIconScale : 1f;
            iconRect.localScale = new Vector3(scale, scale, 1f);
        }

        CleanupDeadIcons();
    }

    private void UpdatePlayerArrowColor()
    {
        if (playerArrow == null || localCar == null) return;

        int index = localCar.ColorIndex;
        if (index >= 0 && index < CarController.ColorPalette.Length)
            playerArrow.color = CarController.ColorPalette[index];
    }

    private void CleanupDeadIcons()
    {
        List<PlayerRaceController> dead = null;

        foreach (var pair in carIcons)
        {
            if (pair.Key != null) continue;

            (dead ??= new List<PlayerRaceController>()).Add(pair.Key);
            if (pair.Value != null) Destroy(pair.Value.gameObject);
        }

        if (dead == null) return;
        foreach (PlayerRaceController player in dead)
            carIcons.Remove(player);
    }

    // ─── Yardımcılar ─────────────────────────────────────────────────────

    private static Color ResolveCarColor(PlayerRaceController player)
    {
        CarController car = player.GetComponent<CarController>();
        int index = car != null ? car.ColorIndex : -1;

        return index >= 0 && index < CarController.ColorPalette.Length
            ? CarController.ColorPalette[index]
            : Color.white;
    }

    /// <summary>Dünya konumu (X/Z) → minimap piksel koordinatı.</summary>
    private Vector2 WorldToMap(Vector3 worldPosition)
        => new Vector2(worldPosition.x, worldPosition.z) * pixelsPerMeter;

    /// <summary>2D vektörü saat yönünün TERSİNE `degrees` kadar döndürür.</summary>
    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    private Image CreateIcon(RectTransform parent, string iconName, Sprite sprite, Color iconColor, float size)
    {
        GameObject go = new GameObject(iconName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = iconColor;
        image.raycastTarget = false;

        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = Vector2.zero;

        return image;
    }
}
