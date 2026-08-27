using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Videodaki "violent selection".
//
// Bir buton ÜÇ parçadan ibaret:
//   1) arkada offsetli bir GÖLGE levhası,
//   2) asıl LEVHA (renk buradan değişiyor),
//   3) YAZI.
// Üstüne gelince aynı anda üç şey oluyor: taşarak büyüyor, aşağı doğru bir
// tokat yiyor, rengi ANINDA değişiyor.
//
// OLUŞTURDUĞU YAPI (mevcut hiyerarşiyi BOZMADAN, butonun kendi içinde):
//   Buton (Button + Image[alfa 0, sadece tıklama alanı])
//     ├─ PersonaSlabRoot        ← animasyon burada
//     │    ├─ PersonaShadow
//     │    └─ PersonaSlab
//     └─ Yazı (mevcut, en sonda kaldığı için hep üstte çiziliyor)
//
// 🚨 YAZI İKİ TÜRLÜ OLABİLİYOR: lobi butonları TextMeshPro, duraklatma
// menüsü butonları Legacy Text kullanıyor. İkisi de destekleniyor.
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class PersonaButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler
{
    public const string RootName = "PersonaSlabRoot";
    public const string ShadowName = "PersonaShadow";
    public const string SlabName = "PersonaSlab";

    [Header("Renkler")]
    public Color idleFill = new Color32(0xEC, 0xEC, 0xEC, 0xFF);
    public Color idleText = new Color32(0x12, 0x12, 0x1A, 0xFF);
    public Color hoverFill = new Color32(0xD8, 0x1E, 0x2C, 0xFF);
    public Color hoverText = Color.white;
    public Color shadowColor = new Color(0f, 0f, 0f, 0.85f);

    [Header("Şekil")]
    [Tooltip("Levhaların paralelkenar eğikliği (piksel).")]
    public float shearX = 26f;
    public Vector2 shadowOffset = new Vector2(10f, -10f);
    [Tooltip("Üstüne gelince gölge daha da açılıyor — buton öne fırlamış gibi duruyor.")]
    public Vector2 shadowOffsetHover = new Vector2(20f, -18f);
    [Tooltip("Yazı da levha gibi eğik olsun mu.")]
    public bool italicLabel = true;

    [Header("Seçim hissi")]
    public float hoverScale = 1.06f;
    [Tooltip("İlk anda buraya kadar TAŞIYOR, sonra hoverScale'e oturuyor.")]
    public float overshoot = 1.16f;
    [Tooltip("Aşağı doğru tokat mesafesi (piksel).")]
    public float punchDown = 12f;
    [Tooltip("Tokadın toplam süresi (saniye). Küçült = daha sert.")]
    public float reactDuration = 0.22f;
    public float pressScale = 0.94f;

    [Header("Yıldız")]
    [Tooltip("Sadece ana menü butonlarında açık. Oyun içi menülerde kapalı — her yerde patlaması yorucu oluyor.")]
    public bool spawnStar = true;
    public float starSize = 110f;
    public Vector2 starOffset = new Vector2(-170f, 26f);

    [Header("Ekran flaşı")]
    // Videodaki 2 karelik beyaz flaş. VARSAYILAN KAPALI: denendi ve her buton
    // değişiminde ekranın çakması rahatsız edici bulundu.
    public bool flashOnHover = false;
    public float flashAlpha = 0.45f;
    public int flashFrames = 2;

    // --- Geri alma için saklananlar (Inspector'da gizli) ---
    [HideInInspector] [SerializeField] bool storedOriginals;
    [HideInInspector] [SerializeField] Color originalImageColor = Color.white;
    [HideInInspector] [SerializeField] Selectable.Transition originalTransition = Selectable.Transition.ColorTint;
    [HideInInspector] [SerializeField] Color originalLabelColor = Color.white;
    [HideInInspector] [SerializeField] int originalTmpFontStyle;
    [HideInInspector] [SerializeField] int originalLegacyFontStyle;
    [HideInInspector] [SerializeField] float originalRotationZ;

    RectTransform rt;
    RectTransform slabRoot;
    RectTransform shadowRt;
    Image shadowImg;
    Image slabImg;

    TMP_Text tmpLabel;
    Text legacyLabel;
    RectTransform labelRt;
    Vector2 labelBasePos;

    float hoverT;
    float punchAge = 999f;
    bool hovered;
    bool pressed;

    void Awake()
    {
        rt = (RectTransform)transform;
        CachePieces();
    }

    void OnEnable()
    {
        // Panel kapanıp açılınca buton büyümüş/kaymış halde donmasın.
        hovered = false;
        pressed = false;
        hoverT = 0f;
        punchAge = 999f;
        transform.localScale = Vector3.one;
    }

    void OnDisable()
    {
        hovered = false;
        pressed = false;
        transform.localScale = Vector3.one;
    }

    void CachePieces()
    {
        slabRoot = transform.Find(RootName) as RectTransform;
        if (slabRoot != null)
        {
            shadowRt = slabRoot.Find(ShadowName) as RectTransform;
            if (shadowRt != null) shadowImg = shadowRt.GetComponent<Image>();

            var slabTr = slabRoot.Find(SlabName);
            if (slabTr != null) slabImg = slabTr.GetComponent<Image>();
        }

        tmpLabel = GetComponentInChildren<TMP_Text>(true);
        legacyLabel = tmpLabel == null ? GetComponentInChildren<Text>(true) : null;

        labelRt = tmpLabel != null ? tmpLabel.rectTransform
                : legacyLabel != null ? legacyLabel.rectTransform : null;
        if (labelRt != null) labelBasePos = labelRt.anchoredPosition;
    }

    void SetLabelColor(Color c)
    {
        if (tmpLabel != null) tmpLabel.color = c;
        else if (legacyLabel != null) legacyLabel.color = c;
    }

    // ---------------------------------------------------------------------
    // KURULUM — Editor aracı çağırıyor
    // ---------------------------------------------------------------------

    public void BuildPieces()
    {
        rt = (RectTransform)transform;
        CachePieces();

        var ownImage = GetComponent<Image>();
        var button = GetComponent<Button>();

        // Orijinalleri SADECE ilk kurulumda sakla — ikinci çalıştırmada kendi
        // yazdığımız değerleri "orijinal" diye kaydetmeyelim.
        if (!storedOriginals)
        {
            if (ownImage != null) originalImageColor = ownImage.color;
            if (button != null) originalTransition = button.transition;

            if (tmpLabel != null)
            {
                originalLabelColor = tmpLabel.color;
                originalTmpFontStyle = (int)tmpLabel.fontStyle;
            }
            else if (legacyLabel != null)
            {
                originalLabelColor = legacyLabel.color;
                originalLegacyFontStyle = (int)legacyLabel.fontStyle;
            }

            originalRotationZ = rt.localEulerAngles.z;
            storedOriginals = true;
        }

        // Butonun KENDİ Image'ı görünmez ama tıklanabilir kalıyor:
        // alfa 0 bir Image hâlâ raycast alıyor.
        if (ownImage != null)
        {
            var c = ownImage.color;
            c.a = 0f;
            ownImage.color = c;
            ownImage.raycastTarget = true;
        }

        // Unity'nin kendi renk geçişi bizim renklerimizle kavga etmesin.
        if (button != null) button.transition = Selectable.Transition.None;

        slabRoot = transform.Find(RootName) as RectTransform;
        if (slabRoot == null)
        {
            var go = new GameObject(RootName, typeof(RectTransform));
            slabRoot = (RectTransform)go.transform;
            slabRoot.SetParent(transform, false);
        }
        Stretch(slabRoot);
        slabRoot.anchoredPosition = Vector2.zero;
        slabRoot.SetSiblingIndex(0);   // yazı en sonda kalsın = hep üstte çizilsin

        shadowRt = EnsurePiece(slabRoot, ShadowName, out shadowImg);
        var slabTr = EnsurePiece(slabRoot, SlabName, out slabImg);
        slabTr.anchoredPosition = Vector2.zero;
        shadowRt.SetSiblingIndex(0);
        slabTr.SetSiblingIndex(1);

        if (italicLabel)
        {
            // TMP mesh efektlerini yok saydığı için yazının eğikliği fontun
            // KENDİ italik ayarından geliyor (bkz. UIShear'daki not).
            if (tmpLabel != null) tmpLabel.fontStyle |= FontStyles.Italic;
            else if (legacyLabel != null) legacyLabel.fontStyle = ToItalic(legacyLabel.fontStyle);
        }

        if (labelRt != null)
        {
            labelBasePos = labelRt.anchoredPosition;
            labelRt.SetAsLastSibling();
        }

        transform.localScale = Vector3.one;
        ApplyPalette();
    }

    static FontStyle ToItalic(FontStyle current)
    {
        // Legacy Text'te kalın+italik AYRI bir değer, "or" ile eklenemiyor.
        return current == FontStyle.Bold || current == FontStyle.BoldAndItalic
            ? FontStyle.BoldAndItalic
            : FontStyle.Italic;
    }

    RectTransform EnsurePiece(RectTransform parent, string name, out Image img)
    {
        var tr = parent.Find(name) as RectTransform;
        if (tr == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            tr = (RectTransform)go.transform;
            tr.SetParent(parent, false);
        }
        Stretch(tr);

        img = tr.GetComponent<Image>();
        if (img == null) img = tr.gameObject.AddComponent<Image>();
        img.raycastTarget = false;   // tıklamayı butonun kendi Image'ı alıyor

        var shear = tr.GetComponent<UIShear>();
        if (shear == null) shear = tr.gameObject.AddComponent<UIShear>();
        shear.shearX = shearX;

        return tr;
    }

    static void Stretch(RectTransform t)
    {
        t.anchorMin = Vector2.zero;
        t.anchorMax = Vector2.one;
        t.sizeDelta = Vector2.zero;
        t.pivot = new Vector2(0.5f, 0.5f);
        t.localScale = Vector3.one;
    }

    /// <summary>Renkleri/şekli parçalara yazar.</summary>
    public void ApplyPalette()
    {
        CachePieces();

        if (shadowImg != null)
        {
            shadowImg.color = shadowColor;
            var sh = shadowImg.GetComponent<UIShear>();
            if (sh != null) sh.shearX = shearX;
        }
        if (shadowRt != null) shadowRt.anchoredPosition = shadowOffset;

        if (slabImg != null)
        {
            slabImg.color = idleFill;
            var sh = slabImg.GetComponent<UIShear>();
            if (sh != null) sh.shearX = shearX;
        }
        SetLabelColor(idleText);
    }

    /// <summary>Persona parçalarını söküp orijinal görünümü geri getirir.</summary>
    public void RemovePieces()
    {
        var root = transform.Find(RootName);
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject);
            else DestroyImmediate(root.gameObject);
        }

        if (!storedOriginals) return;

        var ownImage = GetComponent<Image>();
        if (ownImage != null) ownImage.color = originalImageColor;

        var button = GetComponent<Button>();
        if (button != null) button.transition = originalTransition;

        CachePieces();
        if (tmpLabel != null)
        {
            tmpLabel.color = originalLabelColor;
            tmpLabel.fontStyle = (FontStyles)originalTmpFontStyle;
        }
        else if (legacyLabel != null)
        {
            legacyLabel.color = originalLabelColor;
            legacyLabel.fontStyle = (FontStyle)originalLegacyFontStyle;
        }

        var r = (RectTransform)transform;
        r.localEulerAngles = new Vector3(0f, 0f, originalRotationZ);
        r.localScale = Vector3.one;
    }

    // ---------------------------------------------------------------------
    // ETKİLEŞİM
    // ---------------------------------------------------------------------

    public void OnPointerEnter(PointerEventData e) { SetHovered(true); }
    public void OnPointerExit(PointerEventData e) { SetHovered(false); pressed = false; }
    public void OnPointerDown(PointerEventData e) { pressed = true; }
    public void OnPointerUp(PointerEventData e) { pressed = false; }
    public void OnSelect(BaseEventData e) { SetHovered(true); }      // klavye/kumanda
    public void OnDeselect(BaseEventData e) { SetHovered(false); }

    void SetHovered(bool value)
    {
        if (hovered == value) return;
        hovered = value;
        if (!value) return;

        punchAge = 0f;   // yeni seçim: tokat baştan oynasın

        if (spawnStar)
            PersonaStarBurst.Spawn(rt, starOffset, hoverFill, starSize);

        if (flashOnHover)
            PersonaScreenFlash.Trigger(Color.white, flashFrames, flashAlpha);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        hoverT = Mathf.MoveTowards(hoverT, hovered ? 1f : 0f, dt / 0.09f);

        // Tokat zarfı: HIZLI yüksel, yavaş in. "Violent" hissi buradan geliyor.
        punchAge += dt;
        float punchK = Mathf.Clamp01(punchAge / Mathf.Max(0.01f, reactDuration));
        const float attack = 0.22f;
        float punchEnv = punchK < attack
            ? punchK / attack
            : 1f - (punchK - attack) / (1f - attack);
        punchEnv = Mathf.Clamp01(punchEnv);

        float scale = Mathf.Lerp(1f, hoverScale, hoverT) + (overshoot - hoverScale) * punchEnv;
        if (pressed) scale *= pressScale;
        transform.localScale = new Vector3(scale, scale, 1f);

        float drop = -punchDown * punchEnv;
        if (slabRoot != null) slabRoot.anchoredPosition = new Vector2(0f, drop);
        if (labelRt != null) labelRt.anchoredPosition = labelBasePos + new Vector2(0f, drop);

        // Renk ANINDA değişiyor (Persona menüleri renk geçişi yapmaz, çakar).
        if (slabImg != null) slabImg.color = hovered ? hoverFill : idleFill;
        SetLabelColor(hovered ? hoverText : idleText);
        if (shadowRt != null) shadowRt.anchoredPosition = Vector2.Lerp(shadowOffset, shadowOffsetHover, hoverT);
    }
}
