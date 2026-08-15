using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// HERHANGİ BİR ARAYÜZ BUTONUNA TIKLAMA/ÜZERİNE GELME SESİ EKLER.
///
/// KULLANIMI (kod gerekmez): Bir Button objesini seç → Add Component →
/// "UI Button Sound" → klipleri sürükle. Başka hiçbir şey yapmana gerek yok,
/// butonun kendi `onClick` olayına otomatik bağlanıyor.
///
/// Ana menü, ayarlar, lobi gibi tüm UI butonları için tek tip bir çözüm —
/// her menü scriptine ayrı ayrı ses kodu yazmaya gerek kalmıyor.
///
/// NOT: Bu, sabotajcının KULEDEKİ FİZİKSEL butonları için DEĞİL. Onlar
/// 3D dünya objesi ve sesleri SaboteurInteraction.cs'te çalıyor.
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonSound : MonoBehaviour, IPointerEnterHandler
{
    [Tooltip("Butona tıklanınca çalar.")]
    [SerializeField] private AudioClip clickClip;
    [Tooltip("İmleç butonun üzerine gelince çalar (opsiyonel — boş bırakılabilir). Kısık ve çok kısa bir 'tık' olmalı, yoksa menüde gezerken rahatsız eder.")]
    [SerializeField] private AudioClip hoverClip;

    [SerializeField] private float clickVolume = 0.8f;
    [SerializeField] private float hoverVolume = 0.35f;

    private Button button;

    void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(PlayClick);
    }

    void OnDestroy()
    {
        // Buton yok olurken dinleyiciyi bırak — sahne geçişlerinde ölü
        // referans kalmasın.
        if (button != null) button.onClick.RemoveListener(PlayClick);
    }

    private void PlayClick()
    {
        // Devre dışı (interactable = false) butonlar zaten onClick
        // tetiklemiyor, ekstra kontrole gerek yok.
        SfxPlayer.PlayUI(clickClip, clickVolume);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        SfxPlayer.PlayUI(hoverClip, hoverVolume);
    }
}
