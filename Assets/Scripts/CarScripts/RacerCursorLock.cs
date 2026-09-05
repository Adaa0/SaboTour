using UnityEngine;
[RequireComponent(typeof(CarController))]
public class RacerCursorLock : MonoBehaviour
{
    [SerializeField] private bool lockCursorWhileRacing = true;

    private CarController car;
    private bool cursorLocked;
    private bool applied;

    private void Awake()
    {
        car = GetComponent<CarController>();
    }

    private void Update()
    {
        if (!lockCursorWhileRacing) return;

        if (car == null || !car.IsNetworkOwned) return;

        bool raceOver = RacePodiumManager.Instance != null && RacePodiumManager.Instance.RaceOver;

        bool shouldBeLocked = !PauseMenuController.IsOpen && !raceOver;

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

    private void OnDestroy()
    {
        if (car != null && car.IsNetworkOwned && cursorLocked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
