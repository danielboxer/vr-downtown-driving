public enum VignetteLevel
{
    Off,
    Low,
    High,
}

public static class VignetteSetting
{
    // null defers the default until VrActive.IsActive is known
    private static VignetteLevel? _chosenLevel;

    public static VignetteLevel Level
    {
        get => _chosenLevel ?? (VrActive.IsActive ? VignetteLevel.Low : VignetteLevel.Off);
        set => _chosenLevel = value;
    }
}
