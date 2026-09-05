using UnityEngine;

// Videodaki İLK adım: "bir renk seç ve ona SADIK KAL."
//
// Bu bileşen LobbyCanvas'ta duruyor ve tüm menünün paletini TEK YERDEN
// tutuyor. Rengi değiştirip sağ tık > "Tüm Butonlara Uygula" dediğinde
// altındaki bütün PersonaButton'lara yazıyor.
//
// ⚠️ Bu SADECE bir editör kolaylığı. Çalışma anında butonlar buraya HİÇ
// bakmıyor, kendi Inspector'larındaki renkleri kullanıyorlar — yani bir
// butonu tek başına farklı yapmak istersen onu elle değiştirip bir daha
// "Tüm Butonlara Uygula" demezsen öyle kalır.
//
// (Daha önce bu iş için bir ScriptableObject tema asset'i denenip
// gereksiz bulunmuştu — bu yüzden bilerek sahnede duran basit bir
// bileşen olarak yazıldı.)
[DisallowMultipleComponent]
public class PersonaMenuStyle : MonoBehaviour
{
    [Header("Ana renk — istediğini seç, hepsi buna göre kurulur")]
    public Color accent = new Color32(0xD8, 0x1E, 0x2C, 0xFF);

    [Header("Arka plan")]
    [Tooltip("Koyu LACİVERT bilinçli: turuncunun karşıt rengi olduğu için turuncuyu en çok " +
             "patlatan zemin bu. Arka planı da turuncu yaparsan seçili butonun turuncuya " +
             "dönmesi hiçbir şey ifade etmez.")]
    public Color backgroundColor = new Color32(0x0E, 0x12, 0x24, 0xFF);

    [Tooltip("Şeritlerin görünürlüğü. 0.10 civarı doğru — yükseltirsen arka plan öne fırlar.")]
    [Range(0f, 0.4f)] public float stripeOpacity = 0.10f;

    [Tooltip("Şeritlerin eğim açısı. 0 = dik. -30 civarı belirgin çapraz verir. " +
             "Butonların eğimine BAĞLI DEĞİL — butonlar düz dururken bile şeritler çapraz olabilsin diye.")]
    [Range(-60f, 60f)] public float stripeAngle = -30f;

    [Header("Buton renkleri")]
    [Tooltip("Seçili DEĞİLKEN levha rengi. Arka plandan belirgin ayrılmalı (videonun kuralı).")]
    public Color idleFill = new Color32(0xEC, 0xEC, 0xEC, 0xFF);
    public Color idleText = new Color32(0x12, 0x12, 0x1A, 0xFF);
    public Color hoverText = Color.white;
    public Color shadowColor = new Color(0f, 0f, 0f, 0.85f);

    [Header("Şekil — 'ızgarayı kır'")]
    [Tooltip("Butonların eğiklik açısı. Video 8 derece öneriyor.")]
    public float tiltDegrees = -8f;
    [Tooltip("Paralelkenar eğikliği (piksel).")]
    public float shearX = 26f;
    public Vector2 shadowOffset = new Vector2(10f, -10f);
    public Vector2 shadowOffsetHover = new Vector2(20f, -18f);

    [Header("Ekran flaşı")]
    [Tooltip("Videodaki 2 karelik beyaz çakma. Denendi ve rahatsız edici bulundu — kapalı geliyor.")]
    public bool flashOnSelect = false;

    [Header("Giriş animasyonu")]
    [Tooltip("Butonlar arası gecikme. 0 verirsen kademeli giriş etkisi TAMAMEN kaybolur.")]
    public float staggerSeconds = 0.07f;
    public Vector2 entranceFrom = new Vector2(-280f, 0f);
    public float entranceDuration = 0.42f;

    // Sayfa süpürme renkleri STATIC bir yerde duruyor (bkz. PersonaPageSweep):
    // geçişler yarışın ortasındaki ESC menüsünden de tetikleniyor, orada bu
    // obje yok. Oyuncu her zaman önce ana menüden geçtiği için burada bir kez
    // yazmak yetiyor.
    void OnEnable() => PushSweepColors();

    void PushSweepColors()
    {
        PersonaPageSweep.DefaultLeadColor = accent;
        PersonaPageSweep.DefaultTrailColor = backgroundColor;
    }

    [ContextMenu("Tüm Butonlara Uygula")]
    public void ApplyToAllButtons()
    {
        PushSweepColors();

        var buttons = GetComponentsInChildren<PersonaButton>(true);
        foreach (var b in buttons)
        {
            b.hoverFill = accent;
            b.idleFill = idleFill;
            b.idleText = idleText;
            b.hoverText = hoverText;
            b.shadowColor = shadowColor;
            b.shearX = shearX;
            b.shadowOffset = shadowOffset;
            b.shadowOffsetHover = shadowOffsetHover;
            b.flashOnHover = flashOnSelect;
            b.ApplyPalette();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(b);
#endif
        }

        // İsim kutusu da aynı paleti kullanıyor ama kendi (sakin) davranışıyla.
        foreach (var f in GetComponentsInChildren<PersonaField>(true))
        {
            f.fill = idleFill;
            f.textColor = idleText;
            f.shadowColor = shadowColor;
            f.focusShadowColor = accent;
            f.shearX = shearX;
            f.shadowOffset = shadowOffset;
            f.shadowOffsetFocus = shadowOffsetHover;
            f.ApplyPalette();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(f);
#endif
        }

        var bg = GetComponentInChildren<PersonaBackgroundFX>(true);
        if (bg != null)
        {
            var stripe = accent;
            stripe.a = stripeOpacity;
            bg.stripeColor = stripe;
            // 🚨 Şerit açısı ARTIK butonların eğimine bağlı değil. Önceden
            // "tiltDegrees - 4" idi; eğim 0'a çekilince şeritler de dik kalıp
            // çapraz olma özelliğini tamamen kaybediyordu.
            bg.stripeAngle = stripeAngle;
            bg.backgroundColor = backgroundColor;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(bg);
#endif
        }

        Debug.Log($"[PersonaMenuStyle] {buttons.Length} butona palet uygulandı.");
    }
}
