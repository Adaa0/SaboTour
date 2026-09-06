using UnityEngine;
[RequireComponent(typeof(CarController))] //obejde CarController da olmak zorunda
public class RacerCursorLock : MonoBehaviour
{
    [SerializeField] private bool lockCursorWhileRacing = true; //yarışcının cursoru kitlensin mi diye editörden değiştirilen bool

    private CarController car; //scriptin kontrol ettiği arabanın beyni
    private bool cursorLocked; //şuan kitli mi değil mi 
    private bool applied; //her karede fare tekrar tekrar kilitlenmesin diye

    private void Awake()
    {
        car = GetComponent<CarController>(); //oyun başlayınca CarController'ı bul ve car değişkenine ata 
    }

    private void Update()
    {
        if (!lockCursorWhileRacing) return; //eğer kitlenmemesi gerekiyorsa return yani birşey yapma

        if (car == null || !car.IsNetworkOwned) return; //car null ise ya da araba bizim değilse return 

        bool raceOver = RacePodiumManager.Instance != null && RacePodiumManager.Instance.RaceOver; //yarış bitince podyum açılınca cursorun geri gelmesi için 

        bool shouldBeLocked = !PauseMenuController.IsOpen && !raceOver; //durma menüsü kapalıyken ya da yarış bitmediyse kitli olması gerektiğini tutan bool

        if (applied && shouldBeLocked == cursorLocked) return; //eğer kitli olması gerekiyor ve kitli ise return

        SetCursorLocked(shouldBeLocked); //durum değiştiyse yeni durumu uygula
    }

    private void SetCursorLocked(bool locked) //cursor kitlenmesini sağlayan temel fonksiyon
    {
        cursorLocked = locked; //şuan Cursor kitli
        applied = true; //daha önce değişiklik yapıldı

        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; 
        Cursor.visible = !locked; 
    }

    private void OnDestroy() //araba yok edildiyse cursor'ı serbest bırak
    {
        if (car != null && car.IsNetworkOwned && cursorLocked) //car null değilse ve car bizimse ve cursor kitli ise
        {
            Cursor.lockState = CursorLockMode.None; //cursor kilidini açar
            Cursor.visible = true; //cursor görünür hale gelir
        }
    }
}
