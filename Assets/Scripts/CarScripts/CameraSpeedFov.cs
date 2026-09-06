using UnityEngine;
using Unity.Cinemachine;

[RequireComponent(typeof(CinemachineCamera))]
public class CameraSpeedFov : MonoBehaviour
{
    [Header("Görüş Açısı (FOV)")]
    [SerializeField] private float baseFov = 60f;
    [SerializeField] private float maxFov = 78f;
    [SerializeField] private float speedForMaxFov = 200f;

    [Header("His Ayarları")]
    [SerializeField] private float smoothing = 2.5f;
    [Range(0.5f, 4f)][SerializeField] private float fovCurvePower = 1.6f;

    private CinemachineCamera cam;
    private CarController car;
    private float currentFov;

    void Awake()
    {
        cam = GetComponent<CinemachineCamera>();
        car = GetComponentInParent<CarController>();
        currentFov = baseFov;
        ApplyFov(baseFov);
    }
    void Update()
    {
        if (car == null) return;

        float t = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, speedForMaxFov));
        t = Mathf.Pow(t, fovCurvePower);

        float targetFov = Mathf.Lerp(baseFov, maxFov, t);

        currentFov = Mathf.Lerp(currentFov, targetFov, Time.deltaTime * smoothing);
        ApplyFov(currentFov);
    }

    private void ApplyFov(float fov)
    {
        LensSettings lens = cam.Lens;
        lens.FieldOfView = fov;
        cam.Lens = lens;
    }
}
