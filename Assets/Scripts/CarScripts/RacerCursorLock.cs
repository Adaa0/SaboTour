using UnityEngine;

/// <summary>
/// YARIŞÇI OYNARKEN FARE İMLECİNİ GİZLER/KİLİTLER, ESC'ye BASINCA GERİ VERİR.
///
/// Sabotajcıda bu iş zaten yapılıyordu (SaboteurController.HandleCursorToggle),
/// ama yarışçıda hiç imleç yönetimi yoktu — araba sürerken imleç ekranda
/// serbest duruyor, oyuncu yanlışlıkla oyunun dışına tıklayıp pencereden
/// çıkabiliyordu. Bu script o eksiği kapatıyor ve sabotajcıdaki İLE AYNI
/// mantığı kullanıyor, böylece iki rolde de davranış tutarlı.
///
/// ── ESC'nin SAHİBİ BU SCRIPT DEĞİL ──
/// ESC tuşunu okuyan tek yer PauseMenuController. Burada sadece "menü açık mı"
/// diye bakıp imleci ona göre ayarlıyoruz. NEDEN BÖYLE: iki ayrı yer birden
/// ESC okusaydı, tek basışta hem menü açılır hem imleç AYRICA serbest
/// bırakılırdı; menüyü kapatınca iki durum birbirini tutmaz ve imleç yanlış
/// durumda kalırdı (sabotajcıda bu ders bir kere yaşandı, aynı hataya
/// düşmemek için aynı çözüm kullanılıyor).
///
/// ── SADECE KENDİ ARABANDA ──
/// `isOwned` kontrolü şart: bu component her arabada (başkalarınınkinde de)
/// çalışıyor, ama imleç MAKİNE BAŞINA tek bir şey. Kontrol olmasaydı, 4
/// oyunculu bir yarışta senin ekranındaki 4 arabanın 4 kopyası da aynı imleci
/// aynı anda ayarlamaya çalışırdı.
///
/// ── MANUEL ADIM (Unity'de senin yapman gerekiyor) ──
/// Bu component'i `Assets/Prefabs/Car.prefab` üzerine EKLE:
///   1. Project penceresinde Car.prefab'a çift tıkla (Prefab Mode açılır).
///   2. En üstteki kök objeyi seç (CarController'ın olduğu obje).
///   3. Add Component → "Racer Cursor Lock" ara ve ekle.
///   4. Ctrl+S ile kaydet.
/// </summary>
[RequireComponent(typeof(CarController))]
public class RacerCursorLock : MonoBehaviour
{
    [Tooltip("Kapatırsan yarışçıda imleç eskisi gibi hep serbest kalır (hata ayıklama için işine yarayabilir).")]
    [SerializeField] private bool lockCursorWhileRacing = true;

    private CarController car;

    // Sadece DEĞİŞTİĞİNDE Cursor'a yazmak için — her karede aynı değeri
    // tekrar tekrar atamak gereksiz.
    private bool cursorLocked;
    private bool applied;

    private void Awake()
    {
        car = GetComponent<CarController>();
    }

    private void Update()
    {
        if (!lockCursorWhileRacing) return;

        // Mirror'ın isOwned'ı spawn anında hemen dolmuyor, o yüzden Awake'te
        // bir kere değil her karede bakıyoruz (CheckpointRecovery ile aynı desen).
        // IsNetworkOwned kullanılıyor, düz isOwned DEĞİL: bu prefab network
        // dışında da Instantiate edilebiliyor (minimap araba marker'ı gibi) ve
        // orada isOwned NullReferenceException fırlatıyor.
        if (car == null || !car.IsNetworkOwned) return;

        bool shouldBeLocked = !PauseMenuController.IsOpen;

        // Sadece DEĞİŞTİĞİNDE yazıyoruz — her karede Cursor'a yazmaya gerek yok.
        //
        // BİLİNEN, KABUL EDİLEN DAVRANIŞ: Unity Editor, ESC'ye basılınca imleç
        // kilidini kendi iradesiyle bırakıyor (Game view'da mahsur kalınmasın
        // diye yerleşik bir kolaylık) ve script "Locked" dese bile Game view'a
        // tıklanana kadar geri yakalamıyor. Bu SADECE Editor'de oluyor, gerçek
        // build'de böyle bir kısıtlama yok — o yüzden kodda telafi edilmiyor.
        if (applied && shouldBeLocked == cursorLocked) return;

        SetCursorLocked(shouldBeLocked);
    }

    private void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;
        applied = true;

        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    /// <summary>
    /// Araba yok olurken (yarıştan çıkma, sahne geçişi, bağlantı kopması)
    /// imleci mutlaka geri ver — yoksa oyuncu ana menüye döndüğünde imleç
    /// kilitli kalır ve hiçbir butona tıklayamaz.
    /// </summary>
    private void OnDestroy()
    {
        if (car != null && car.IsNetworkOwned && cursorLocked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
