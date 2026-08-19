// Vignette strength, set from the options menu. Both vehicles carry their own
// VrComfortVignette, so the level is held here rather than on either prefab and a
// vehicle switch keeps whatever the participant chose.
public enum VignetteLevel
{
    Off,
    Low,
    High,
}

public static class VignetteSetting
{
    public static VignetteLevel Level { get; set; } = VignetteLevel.High;
}
