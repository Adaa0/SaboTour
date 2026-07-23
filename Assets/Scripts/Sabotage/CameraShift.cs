using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SABOTAJCI KOLTUK KAYDIRMA SİSTEMİ
///
/// Mantık: Koltuk 4 sabit pozisyon arasında dönebiliyor (360 derecelik bir
/// daire üzerinde 90 derece aralıklarla): Ana Monitör, Sağ Monitör, Cam
/// (arkada), Sol Monitör.
///
/// A tuşu → bir adım sola (Main -> Left -> Window -> Right -> Main ...)
/// D tuşu → bir adım sağa (Main -> Right -> Window -> Left -> Main ...)
///
/// Ana monitörden cama ulaşmak için 2 kere A ya da 2 kere D yeterli,
/// çünkü Window her iki yönden de 2 adım uzaklıkta (dairesel yapı).
///
/// MULTIPLAYER NOTU:
/// Bu tamamen LOCAL bir sistem — sabotajcının hangi monitöre baktığı
/// diğer oyunculara hiç gönderilmiyor, sadece kendi ekranını ilgilendiriyor.
/// Bu yüzden Mirror'a geçmeden ÖNCE bile tamamlanıp test edilebilir.
/// </summary>
public class SabotageChairController : MonoBehaviour
{
    private enum ChairPosition
    {
        MainMonitor = 0,
        RightMonitor = 1,
        Window = 2,
        LeftMonitor = 3
    }

    [Header("Hareket Ettirilecek Obje")]
    [Tooltip("BOŞ BIRAKIRSAN bu script'in eklendiği obje hareket eder. " +
             "CİNEMACHİNE KULLANIYORSAN: buraya Cinemachine'in 'Tracking Target' " +
             "olarak atadığın objeyi sürükle (örn. karakterin kafası/gözü, ya da " +
             "ileride kol modeli). Kamerayı DEĞİL, kameranın TAKİP ETTİĞİ objeyi " +
             "hareket ettirmen gerekiyor, yoksa Hard Lock To Target her frame " +
             "pozisyonu geri eziyor.")]
    [SerializeField] private Transform moveTarget;

    [Header("Pozisyon Hedefleri")]
    [Tooltip("Kameranın gideceği pozisyon/rotasyon noktaları. Sırasıyla: " +
             "Ana Monitör, Sağ Monitör, Cam, Sol Monitör")]
    [SerializeField] private Transform mainMonitorAnchor;
    [SerializeField] private Transform rightMonitorAnchor;
    [SerializeField] private Transform windowAnchor;
    [SerializeField] private Transform leftMonitorAnchor;

    [Header("Geçiş Ayarları")]
    [Tooltip("Koltuğun kayma süresi (saniye). Düşük = hızlı, yüksek = ağır/yavaş.")]
    [SerializeField] private float slideDuration = 0.45f;
    [Tooltip("Rotasyonun pozisyona göre ne kadar geride kalacağı. " +
             "0 = aynı anda, 1 = pozisyon bitince rotasyon başlar gibi.")]
    [Range(0f, 1f)]
    [SerializeField] private float rotationLag = 0.35f;
    [SerializeField] private bool blockInputDuringTransition = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private Transform[] anchors;
    private ChairPosition currentPosition = ChairPosition.MainMonitor;
    private int targetIndex = 0;
    private bool isTransitioning = false;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float transitionElapsed = 0f;

    void Start()
    {
        // moveTarget atanmadıysa kendi transform'unu kullan (eski davranış)
        if (moveTarget == null)
            moveTarget = transform;

        // Enum sırasıyla eşleşen anchor dizisi
        anchors = new Transform[] { mainMonitorAnchor, rightMonitorAnchor, windowAnchor, leftMonitorAnchor };

        if (mainMonitorAnchor == null)
        {
            Debug.LogError("[SabotageChairController] mainMonitorAnchor atanmamış!");
            return;
        }

        // Başlangıçta ana monitöre anında yerleş
        moveTarget.position = mainMonitorAnchor.position;
        moveTarget.rotation = mainMonitorAnchor.rotation;
        targetIndex = (int)ChairPosition.MainMonitor;
    }

    void Update()
    {
        HandleInput();
        SmoothMoveToTarget();
    }

    private void HandleInput()
    {
        if (blockInputDuringTransition && isTransitioning) return;

        // YENİ INPUT SYSTEM: Keyboard.current null olabilir (henüz klavye
        // algılanmadıysa), bu yüzden null check şart.
        if (Keyboard.current == null) return;

        if (Keyboard.current.aKey.wasPressedThisFrame)
            MoveOneStep(-1);

        if (Keyboard.current.dKey.wasPressedThisFrame)
            MoveOneStep(1);
    }

    /// <summary>
    /// Dairesel dizide bir adım ilerler. direction: -1 (A) ya da +1 (D).
    /// 4 pozisyon olduğu için mod 4 ile döngü sağlanıyor.
    /// </summary>
    private void MoveOneStep(int direction)
    {
        targetIndex = (targetIndex + direction + 4) % 4;
        isTransitioning = true;

        // Geçişi baştan başlat: mevcut konumdan hedefe doğru
        startPosition = moveTarget.position;
        startRotation = moveTarget.rotation;
        transitionElapsed = 0f;

        if (showDebugLogs)
            Debug.Log($"[SabotageChairController] Hedef pozisyon: {(ChairPosition)targetIndex}");
    }

    /// <summary>
    /// Koltuk kayması hissi için:
    /// - Pozisyon bir "ease-out" eğrisiyle hareket eder (hızlı başlar, yavaşlayarak durur)
    /// - Rotasyon pozisyondan biraz GERİDE başlar (rotationLag), yani önce
    ///   koltuk kayıyor gibi hissettirir, dönüş biraz sonra devreye giriyor.
    ///   Tıpkı gerçek bir sandalyeyi ayakla itip sonra döndürmek gibi.
    /// </summary>
    private void SmoothMoveToTarget()
    {
        Transform target = anchors[targetIndex];
        if (target == null) return;

        if (!isTransitioning)
        {
            // Geçiş bitmişse hedefte sabit kal (drift önlemek için)
            moveTarget.position = target.position;
            moveTarget.rotation = target.rotation;
            return;
        }

        transitionElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(transitionElapsed / slideDuration);

        // Ease-out eğrisi: hızlı başlar, sona doğru yavaşlar (koltuk kayması hissi)
        float positionT = 1f - Mathf.Pow(1f - t, 3f);

        // Rotasyon, pozisyondan rotationLag kadar geriden başlıyor
        float rotationT = Mathf.Clamp01((t - rotationLag) / (1f - rotationLag));
        rotationT = 1f - Mathf.Pow(1f - rotationT, 2f); // rotasyon için daha hafif ease-out

        moveTarget.position = Vector3.Lerp(startPosition, target.position, positionT);
        moveTarget.rotation = Quaternion.Slerp(startRotation, target.rotation, rotationT);

        if (t >= 1f)
        {
            isTransitioning = false;
            currentPosition = (ChairPosition)targetIndex;
            moveTarget.position = target.position;
            moveTarget.rotation = target.rotation;
        }
    }

    /// <summary>
    /// Diğer sistemler (örneğin minimap tıklaması) sadece ana monitördeyken
    /// çalışmalı. Bu property ile kontrol edilebilir.
    /// </summary>
    public bool IsAtMainMonitor => currentPosition == ChairPosition.MainMonitor && !isTransitioning;
}