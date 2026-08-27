using UnityEngine;
using UnityEngine.UI;

// Videodaki "dikdörtgenleri paralelkenara çevir" adımı.
// Unity UI'da bir RectTransform'u eğemezsin (sadece döndürebilirsin), o yüzden
// çizim ANINDA mesh'in üst kenarını sağa, alt kenarını sola kaydırıyoruz.
// Aynı desen daha önce harf aralığı denemesinde kullanılmıştı (BaseMeshEffect).
//
// ⚠️ SADECE GÖRÜNTÜYÜ eğiyor, TIKLAMA ALANINI eğmiyor — tıklama hâlâ düz
// dikdörtgen. Menüde butonlar birbirinden uzak olduğu için sorun değil.
// ⚠️ TextMeshPro'da ÇALIŞMAZ (TMP kendi mesh'ini üretip mesh efektlerini yok
// sayıyor). Yazının eğikliği TMP'nin kendi Italic ayarıyla veriliyor.
[AddComponentMenu("UI/Effects/Persona Shear")]
public class UIShear : BaseMeshEffect
{
    [Tooltip("Yatay eğme miktarı (piksel). Üst kenar sağa, alt kenar sola kayar. 0 = düz dikdörtgen.")]
    public float shearX = 26f;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        if (graphic == null) return;

        Rect rect = graphic.rectTransform.rect;
        float half = rect.height * 0.5f;
        if (half <= 0.0001f) return;

        float centerY = rect.center.y;
        var v = new UIVertex();

        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float t = (v.position.y - centerY) / half;   // alt kenar -1, üst kenar +1
            v.position.x += t * shearX * 0.5f;
            vh.SetUIVertex(v, i);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (graphic != null) graphic.SetVerticesDirty();
    }
#endif
}
