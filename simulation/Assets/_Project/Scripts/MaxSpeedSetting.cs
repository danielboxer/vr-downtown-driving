public static class MaxSpeedSetting
{
    public const float StepKmh = 5f;

    private const float VrDefaultKmh = 20f;
    private const float DesktopDefaultKmh = 70f;

    // null defers the default until VrActive.IsActive is known
    private static float? _chosenKmh;

    public static float Kmh
    {
        get => _chosenKmh ?? (VrActive.IsActive ? VrDefaultKmh : DesktopDefaultKmh);
        set => _chosenKmh = value;
    }
}
