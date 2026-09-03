public enum AccelerationMode
{
    Instant,
    Gradual,
}

public static class AccelerationSetting
{
    // null defers the default until VrActive.IsActive is known
    private static AccelerationMode? _chosenMode;

    public static AccelerationMode Mode
    {
        get => _chosenMode ?? (VrActive.IsActive ? AccelerationMode.Instant : AccelerationMode.Gradual);
        set => _chosenMode = value;
    }
}
