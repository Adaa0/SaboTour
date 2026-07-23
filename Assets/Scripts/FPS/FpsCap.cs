using UnityEngine;

public class FPSLimiter : MonoBehaviour
{
    void Awake()
    {
        QualitySettings.vSyncCount = 0;   // VSync kapalı
        Application.targetFrameRate = 9999; // FPS CAP
    }
}
