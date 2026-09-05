using UnityEngine;
using UnityEngine.UI;
using TMPro;

// İsim yazma kutusunun Persona hali.
//
// PersonaButton'ın AYNISI DEĞİL, bilerek: bir yazı kutusu "violent selection"
// istemiyor. İçine tıklayıp yazarken zıplayan, çakan, yıldız patlatan bir kutu
// sinir bozucu olur. Buradaki tepki çok daha sakin — odaklanınca gölge ana
// renge dönüyor ve buton hafifçe öne çıkıyor. Şekil dili (eğim, paralelkenar,
// gölge) butonlarla aynı, davranış farklı.
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class PersonaField : MonoBehaviour
{
    // PersonaButton ile AYNI isimler — kaldırma aracı ikisini de aynı şekilde
    // bulup söküyor.
    public const string RootName = "PersonaSlabRoot";
    public const string ShadowName = "PersonaShadow";
    public const string SlabName = "PersonaSlab";

    [Header("Renkler")]
    public Color fill = new Color32(0xEC, 0xEC, 0xEC, 0xFF);
    public Color textColor = new Color32(0x12, 0x12, 0x1A, 0xFF);
    [Tooltip("'İsmini yaz...' yazısı — asıl yazıdan soluk olmalı ki ikisi karışmasın.")]
    public Color placeholderColor = new Color(0.19f, 0.19f, 0.20f, 0.5f);
    public Color shadowColor = new Color(0f, 0f, 0f, 0.85f);
    [Tooltip("İçine tıklayınca gölge bu renge dönüyor — 'burası aktif' işareti.")]
    public Color focusShadowColor = new Color32(0xD8, 0x1E, 0x2C, 0xFF);

    [Header("Şekil")]
    public float shearX = 26f;
    public Vector2 shadowOffset = new Vector2(10f, -10f);
    public Vector2 shadowOffsetFocus = new Vector2(18f, -16f);

    [Header("Odak")]
    [Tooltip("Odaklanınca büyüme. Yazarken rahatsız etmesin diye çok küçük tutuldu.")]
    public float focusScale = 1.03f;
    [Tooltip("Yazı da butonlar gibi eğik olsun mu.")]
    public bool italicText = true;

    // --- Geri alma için saklananlar ---
    [HideInInspector] [SerializeField] bool storedOriginals;
    [HideInInspector] [SerializeField] Color originalImageColor = Color.white;
    [HideInInspector] [SerializeField] Color originalTextColor = Color.black;
    [HideInInspector] [SerializeField] Color originalPlaceholderColor = Color.gray;
    [HideInInspector] [SerializeField] int originalTextStyle;
    [HideInInspector] [SerializeField] int originalPlaceholderStyle;
    [HideInInspector] [SerializeField] float originalRotationZ;

    TMP_InputField input;
    RectTransform shadowRt;
    Image shadowImg;
    Image slabImg;
    float focusT;

    void Awake()
    {
        input = GetComponent<TMP_InputField>();
        CachePieces();
    }

    void CachePieces()
    {
        var root = transform.Find(RootName);
        if (root == null) return;

        shadowRt = root.Find(ShadowName) as RectTransform;
        if (shadowRt != null) shadowImg = shadowRt.GetComponent<Image>();

        var slab = root.Find(SlabName);
        if (slab != null) slabImg = slab.GetComponent<Image>();
    }

    // ---------------------------------------------------------------------

    /// <summary>Gölge + levhayı kurar. İki kez çağırmak zararsız.</summary>
    public void BuildPieces()
    {
        input = GetComponent<TMP_InputField>();
        var ownImage = GetComponent<Image>();
        var textCmp = input != null ? input.textComponent : null;
        var placeholder = input != null ? input.placeholder as TMP_Text : null;

        if (!storedOriginals)
        {
            if (ownImage != null) originalImageColor = ownImage.color;
            if (textCmp != null) { originalTextColor = textCmp.color; originalTextStyle = (int)textCmp.fontStyle; }
            if (placeholder != null) { originalPlaceholderColor = placeholder.color; originalPlaceholderStyle = (int)placeholder.fontStyle; }
            originalRotationZ = ((RectTransform)transform).localEulerAngles.z;
            storedOriginals = true;
        }

        // Kutunun kendi Image'ı görünmez ama tıklanabilir kalıyor
        // (alfa 0 bir Image hâlâ raycast alıyor — butonlardaki numaranın aynısı).
        if (ownImage != null)
        {
            var c = ownImage.color;
            c.a = 0f;
            ownImage.color = c;
            ownImage.raycastTarget = true;
        }

        var root = transform.Find(RootName) as RectTransform;
        if (root == null)
        {
            var go = new GameObject(RootName, typeof(RectTransform));
            root = (RectTransform)go.transform;
            root.SetParent(transform, false);
        }
        Stretch(root);
        root.anchoredPosition = Vector2.zero;
        // Index 0: yazı alanının ARKASINDA çizilsin.
        root.SetSiblingIndex(0);

        shadowRt = EnsurePiece(root, ShadowName, out shadowImg);
        var slabRt = EnsurePiece(root, SlabName, out slabImg);
        slabRt.anchoredPosition = Vector2.zero;
        shadowRt.SetSiblingIndex(0);
        slabRt.SetSiblingIndex(1);

        if (italicText)
        {
            if (textCmp != null) textCmp.fontStyle |= FontStyles.Italic;
            if (placeholder != null) placeholder.fontStyle |= FontStyles.Italic;
        }

        ((RectTransform)transform).localScale = Vector3.one;
        ApplyPalette();
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
        img.raycastTarget = false;

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

    public void ApplyPalette()
    {
        CachePieces();
        input = GetComponent<TMP_InputField>();

        if (slabImg != null)
        {
            slabImg.color = fill;
            var sh = slabImg.GetComponent<UIShear>();
            if (sh != null) sh.shearX = shearX;
        }
        if (shadowImg != null)
        {
            shadowImg.color = shadowColor;
            var sh = shadowImg.GetComponent<UIShear>();
            if (sh != null) sh.shearX = shearX;
        }
        if (shadowRt != null) shadowRt.anchoredPosition = shadowOffset;

        if (input != null)
        {
            if (input.textComponent != null) input.textComponent.color = textColor;
            if (input.placeholder is TMP_Text ph) ph.color = placeholderColor;
            input.caretColor = textColor;
            input.customCaretColor = true;   // yoksa imleç yazının rengini takip etmeyebiliyor
        }
    }

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

        var inp = GetComponent<TMP_InputField>();
        if (inp != null)
        {
            if (inp.textComponent != null)
            {
                inp.textComponent.color = originalTextColor;
                inp.textComponent.fontStyle = (FontStyles)originalTextStyle;
            }
            if (inp.placeholder is TMP_Text ph)
            {
                ph.color = originalPlaceholderColor;
                ph.fontStyle = (FontStyles)originalPlaceholderStyle;
            }
            inp.customCaretColor = false;
        }

        var rt = (RectTransform)transform;
        rt.localEulerAngles = new Vector3(0f, 0f, originalRotationZ);
        rt.localScale = Vector3.one;
    }

    // ---------------------------------------------------------------------

    void Update()
    {
        bool focused = input != null && input.isFocused;
        focusT = Mathf.MoveTowards(focusT, focused ? 1f : 0f, Time.unscaledDeltaTime / 0.14f);

        if (shadowImg != null) shadowImg.color = Color.Lerp(shadowColor, focusShadowColor, focusT);
        if (shadowRt != null) shadowRt.anchoredPosition = Vector2.Lerp(shadowOffset, shadowOffsetFocus, focusT);

        float s = Mathf.Lerp(1f, focusScale, focusT);
        transform.localScale = new Vector3(s, s, 1f);
    }
}
