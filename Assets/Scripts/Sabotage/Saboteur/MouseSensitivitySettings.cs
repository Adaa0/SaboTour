using UnityEngine;
public static class MouseSensitivitySettings
{
    private const string PrefKey = "Settings_MouseSensitivityDisplay";
    public const float MinDisplay = 1f;
    public const float MaxDisplay = 10f;
    public const float MidDisplay = (MinDisplay + MaxDisplay) * 0.5f;
    private const float MidRaw = 0.2f;
    private const float MinRaw = 0.05f;
    private const float MaxRaw = 2f;
    private static float display = MidDisplay;
    private static bool loaded;

    public static float Raw
    {
        get
        {
            EnsureLoaded();

            if (display <= MidDisplay)
                return Mathf.Lerp(MinRaw, MidRaw, InverseLerpSafe(MinDisplay, MidDisplay, display));

            return Mathf.Lerp(MidRaw, MaxRaw, InverseLerpSafe(MidDisplay, MaxDisplay, display));
        }
    }

    public static float Display
    {
        get
        {
            EnsureLoaded();
            return display;
        }
    }

    public static void SetFromDisplay(float displayValue)
    {
        display = Mathf.Clamp(displayValue, MinDisplay, MaxDisplay);
        loaded = true;
        PlayerPrefs.SetFloat(PrefKey, display);
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        display = PlayerPrefs.GetFloat(PrefKey, MidDisplay);
        loaded = true;
    }

    private static float InverseLerpSafe(float a, float b, float value)
    {
        return Mathf.Approximately(a, b) ? 0f : Mathf.InverseLerp(a, b, value);
    }
}
