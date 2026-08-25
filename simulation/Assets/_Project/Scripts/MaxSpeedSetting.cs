// Top speed for both vehicles, set from the options menu slider. The slider counts
// whole steps rather than km/h so it can only land on multiples of StepKmh.
public static class MaxSpeedSetting
{
    public const float StepKmh = 5f;

    private const float VrDefaultKmh = 20f;
    private const float DesktopDefaultKmh = 70f;

    // The XR display can take a few seconds to start running
    private static float? _chosenKmh;

    public static float Kmh
    {
        get => _chosenKmh ?? (VrActive.IsActive ? VrDefaultKmh : DesktopDefaultKmh);
        set => _chosenKmh = value;
    }
}
