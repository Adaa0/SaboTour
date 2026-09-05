using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// YARIŞÇI MİNİMAP'İNDEKİ YOL ŞERİDİNİ ÇİZEN UI BİLEŞENİ
///
/// NEDEN ÖZEL BİR BİLEŞEN: Unity'nin hazır UI parçaları (Image, RawImage)
/// sadece dikdörtgen çizebiliyor. Bize pistin kıvrımlı şeklini takip eden
/// bir şerit lazım. Unity UI'ın kendi çizim altyapısında (Graphic ->
/// OnPopulateMesh) istediğimiz üçgenleri doğrudan üretebiliyoruz.
///
/// PERFORMANS: Şerit BİR KERE üretiliyor (pist yarış boyunca değişmiyor),
/// sonra her karede sadece parent'ın konumu/açısı değiştiriliyor. Yani
/// minimap'in kare başına maliyeti neredeyse sıfır — projedeki tek gerçek
/// darboğaz çizim tarafı olduğu için (bkz. CLAUDE.md performans profili)
/// ikinci bir kamera/RenderTexture yerine bilinçli olarak bu yol seçildi.
///
/// MITER JOIN: Şeridin genişliği, her noktada ÖNCEKİ ve SONRAKİ segmentin
/// açıortayına dik olarak veriliyor. Sadece kendi segmentine dik verilseydi
/// keskin virajlarda ardışık dörtgenler hizalanmaz, şerit kendi üstüne
/// katlanıp çentik yapardı — bu proje aynı sorunu gerçek pistin kenarlığında
/// bir kere yaşadı (bkz. TrackGenerator.ComputeMiterRight). Burada aynı
/// mantığın 2D (UI düzlemi) hali var.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class MinimapRoadGraphic : MaskableGraphic
{
    private readonly List<Vector2> points = new();
    private float halfWidth = 4f;

    /// <summary>
    /// Şeridin şeklini belirler. Noktalar minimap'in KENDİ piksel
    /// koordinatlarında olmalı (dünya metreleri değil) — dönüşümü çağıran
    /// taraf (RacerMinimapHUD) yapıyor, çünkü ölçek oradaki "kaç metre
    /// görünsün" ayarına bağlı.
    /// </summary>
    public void SetTrack(IList<Vector2> mapPoints, float widthPixels)
    {
        points.Clear();
        if (mapPoints != null) points.AddRange(mapPoints);

        halfWidth = Mathf.Max(0.5f, widthPixels * 0.5f);

        // Unity'ye "mesh'i yeniden üret" der — OnPopulateMesh bir sonraki
        // çizimde otomatik çağrılır.
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        int count = points.Count;
        if (count < 3) return;

        Color32 vertexColor = color;

        // Pist KAPALI BİR HALKA (son nokta ilk noktaya bağlanıyor), bu yüzden
        // komşu aramalarında modulo kullanılıyor — ayrı bir "kapanış" parçası
        // eklemeye gerek kalmıyor.
        Vector2 lastRight = Vector2.right;

        for (int i = 0; i < count; i++)
        {
            Vector2 prev = points[(i - 1 + count) % count];
            Vector2 curr = points[i];
            Vector2 next = points[(i + 1) % count];

            Vector2 right = ComputeMiterRight(prev, curr, next, lastRight);
            if (right.sqrMagnitude > 0.0001f) lastRight = right.normalized;

            vh.AddVert(curr - right * halfWidth, vertexColor, Vector2.zero);
            vh.AddVert(curr + right * halfWidth, vertexColor, Vector2.zero);
        }

        for (int i = 0; i < count; i++)
        {
            int a = i * 2;
            int b = ((i + 1) % count) * 2;

            vh.AddTriangle(a, b, a + 1);
            vh.AddTriangle(b, b + 1, a + 1);
        }
    }

    /// <summary>
    /// Bir noktada şeridin "sağ" yönü — önceki ve sonraki segmentin
    /// açıortayı. miterLimit, çok keskin köşelerde ucun sonsuza uzamasını
    /// engelliyor (o durumda köşe hafifçe kırpılıyor, ki bu görünmez).
    /// </summary>
    private static Vector2 ComputeMiterRight(Vector2 prev, Vector2 curr, Vector2 next, Vector2 fallback, float miterLimit = 3f)
    {
        Vector2 dirIn = curr - prev;
        Vector2 dirOut = next - curr;

        if (dirIn.sqrMagnitude < 0.0000001f || dirOut.sqrMagnitude < 0.0000001f)
            return fallback;

        dirIn.Normalize();
        dirOut.Normalize();

        Vector2 rightIn = new Vector2(dirIn.y, -dirIn.x);
        Vector2 rightOut = new Vector2(dirOut.y, -dirOut.x);

        Vector2 miter = rightIn + rightOut;
        if (miter.sqrMagnitude < 0.0001f) return rightOut; // ~180° dönüş, çok nadir

        miter.Normalize();

        float dot = Vector2.Dot(rightIn, miter);
        float scale = dot > (1f / miterLimit) ? 1f / dot : miterLimit;

        return miter * scale;
    }
}
