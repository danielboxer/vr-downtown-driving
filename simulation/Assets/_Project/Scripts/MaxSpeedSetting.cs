// Top speed for both vehicles, set from the options menu slider. The slider counts
// whole steps rather than km/h so it can only land on multiples of StepKmh.
public static class MaxSpeedSetting
{
    public const float StepKmh = 5f;

    public static float Kmh { get; set; } = 20f;
}
