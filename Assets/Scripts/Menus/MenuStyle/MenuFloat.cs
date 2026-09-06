using UnityEngine;

// Yüzme hareketinin ORTAK formülü. Butonlar ve başlık aynı matematiği
// kullanmalı (hız birebir aynı, genlik farklı) — ikisi ayrı formülle
// hareket ederse yan yana bakınca kopuk görünür.
public static class MenuFloatMath
{
    /// <summary>Zaman + faz + hız + genlik alıp dikey bir ofset döndürür.</summary>
    public static float Offset(float time, float phase, float speed, float amount)
    {
        return Mathf.Sin(time * speed + phase) * amount;
    }
}

// RectTransform'u sürekli hafifçe yüzdüren bileşen (buton/başlık üstüne
// eklenir). Her öğe kendi rastgele FAZINI Awake'te alıyor — aynı olsaydı
// menü tek parça gibi sallanırdı.
public class MenuFloat : MonoBehaviour
{
    public float speed = 0.35f;
    public float amount = 4f;

    RectTransform rt;
    Vector2 basePos;
    float phase;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        basePos = rt.anchoredPosition;
        phase = Random.Range(0f, Mathf.PI * 2f);
    }

    void OnEnable()
    {
        if (rt != null) basePos = rt.anchoredPosition;
    }

    void Update()
    {
        if (rt == null) return;
        float offset = MenuFloatMath.Offset(Time.unscaledTime, phase, speed, amount);
        rt.anchoredPosition = basePos + new Vector2(0f, offset);
    }
}
