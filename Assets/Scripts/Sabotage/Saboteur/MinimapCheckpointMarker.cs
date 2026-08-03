using System.Collections;
using UnityEngine;

/// <summary>
/// Minimap üzerindeki her checkpoint işaretçisine MinimapController
/// tarafından runtime'da eklenir. Sadece hangi checkpoint'e denk geldiğini
/// taşımıyor artık — RENK DURUMUNU da kendisi yönetiyor. Sadece İKİ renk
/// kullanılıyor (mor/sarı YOK):
///   - Hazır/boşta: YEŞİL (255 yeşil).
///   - Seçili (sabotajcı bu checkpoint'i hedef aldı): KIRMIZI (255 kırmızı).
///   - Cooldown'da (az önce bu checkpoint'e bir skil ateşlendi): KIRMIZIDAN
///     YEŞİLE doğru yavaşça kayan bir renk — tam yeşile ulaştığında
///     checkpoint yeniden kullanıma hazır demektir (bkz. CheckpointCooldownManager).
/// Cooldown animasyonu, seçim rengine göre HER ZAMAN önceliklidir — cooldown
/// bitmeden checkpoint'i "seçili" gibi göstermek yanıltıcı olurdu. Cooldown
/// bitince (yeşile ulaşınca) seçim durumu ne olursa olsun HAZIR rengi
/// (yeşil) gösterilir — "yeşil = kullanılabilir" kuralı hep geçerli kalsın diye.
/// </summary>
public class MinimapCheckpointMarker : MonoBehaviour
{
    public int checkpointIndex;

    [Tooltip("Marker birden fazla parçadan oluşuyorsa TÜM parçaları içeren " +
             "üst obje buraya sürüklenmeli. Boş bırakılırsa bu objenin " +
             "kendisi kullanılır.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Rengin uygulanacağı Renderer. Boş bırakılırsa bu objenin kendi Renderer'ı kullanılır.")]
    [SerializeField] private Renderer colorRenderer;

    private static readonly Color ReadyColor = Color.green;   // 255 yeşil — hazır/boşta, cooldown bitince de bu
    private static readonly Color SelectedColor = Color.red;  // 255 kırmızı — hedef seçili / cooldown başlangıcı

    private Coroutine cooldownRoutine;
    private bool isSelected;

    public bool IsOnCooldown { get; private set; }

    /// <summary>Outline/basma animasyonunun uygulanacağı gerçek kök obje.</summary>
    public Transform FeedbackRoot => visualRoot != null ? visualRoot : transform;

    void Awake()
    {
        if (colorRenderer == null) colorRenderer = GetComponent<Renderer>();
        ApplyColor(ReadyColor);
    }

    /// <summary>
    /// SaboteurInteraction, sabotajcı bu marker'a tıklayıp hedef seçtiğinde
    /// true, başka bir checkpoint seçildiğinde (bu marker artık hedef
    /// değilken) false ile çağırır. Cooldown sürüyorsa görmezden gelinir —
    /// kırmızı/yeşil geçişi bozulmasın diye.
    /// </summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        if (IsOnCooldown) return;
        ApplyColor(selected ? SelectedColor : ReadyColor);
    }

    /// <summary>
    /// CheckpointCooldownManager bu checkpoint'e bir skil ateşlendiğini
    /// bildirdiğinde çağrılır. Renk, verilen süre boyunca kırmızıdan yeşile
    /// lineer olarak kayar; süre bitince checkpoint tekrar seçilebilir hale
    /// gelmiş gibi (yeşil) görünür.
    /// </summary>
    public void PlayCooldown(float duration)
    {
        if (cooldownRoutine != null) StopCoroutine(cooldownRoutine);
        cooldownRoutine = StartCoroutine(CooldownRoutine(duration));
    }

    private IEnumerator CooldownRoutine(float duration)
    {
        IsOnCooldown = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            ApplyColor(Color.Lerp(SelectedColor, ReadyColor, elapsed / duration));
            yield return null;
        }

        IsOnCooldown = false;
        cooldownRoutine = null;

        // Cooldown bitince HER ZAMAN yeşil — "yeşil = hazır" kuralı bozulmasın.
        ApplyColor(ReadyColor);
    }

    /// <summary>
    /// Sadece RGB'yi değiştirir, ALPHA'ya DOKUNMAZ — materyaldeki saydamlık
    /// ayarı (ör. camsı/Transparent bir materyal, 120/255 alpha) korunsun
    /// diye. Color.red/Color.green gibi hazır renklerin alpha'sı hep 1
    /// (tam opak) olduğu için, doğrudan atansaydı her renk değişiminde
    /// materyal opaklaşırdı.
    /// </summary>
    private void ApplyColor(Color color)
    {
        if (colorRenderer == null) return;

        // Hem URP (_BaseColor) hem legacy/Unlit (_Color) shader'larla uyumlu olsun diye.
        if (colorRenderer.material.HasProperty("_BaseColor"))
        {
            Color current = colorRenderer.material.GetColor("_BaseColor");
            colorRenderer.material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, current.a));
        }
        if (colorRenderer.material.HasProperty("_Color"))
        {
            Color current = colorRenderer.material.GetColor("_Color");
            colorRenderer.material.SetColor("_Color", new Color(color.r, color.g, color.b, current.a));
        }
    }
}
