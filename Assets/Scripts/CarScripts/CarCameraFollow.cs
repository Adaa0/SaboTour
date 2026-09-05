using UnityEngine;
using Mirror;
public class CarCameraActivator : NetworkBehaviour
{
    [SerializeField] private GameObject carCam;

    private void Start()
    {
        CarController controller = GetComponent<CarController>();

        if (controller != null && controller.PhotoStudioMode && carCam != null)
            carCam.SetActive(true);
    }
    public override void OnStartAuthority()
    {
        base.OnStartAuthority();

        if (carCam != null)
        {
            carCam.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[CarCameraActivator] carCam atanmamış! Inspector'dan CarCam objesini sürükle.");
        }
    }
    public void SetCarCamActive(bool active)
    {
        if (carCam != null)
            carCam.SetActive(active);
    }
}