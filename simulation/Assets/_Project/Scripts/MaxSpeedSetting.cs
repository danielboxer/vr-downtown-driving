public static class MaxSpeedSetting
{
    public const float StepKmh = 5f;

    private const float CarVrDefaultKmh = 20f;
    private const float CarDesktopDefaultKmh = 55f;
    private const float BikeVrDefaultKmh = 10f;
    private const float BikeDesktopDefaultKmh = 20f;

    // null defers the default until VrActive.IsActive is known
    private static float? _chosenCarKmh;
    private static float? _chosenBikeKmh;

    public static float CarKmh
    {
        get => _chosenCarKmh ?? (VrActive.IsActive ? CarVrDefaultKmh : CarDesktopDefaultKmh);
        set => _chosenCarKmh = value;
    }

    public static float BikeKmh
    {
        get => _chosenBikeKmh ?? (VrActive.IsActive ? BikeVrDefaultKmh : BikeDesktopDefaultKmh);
        set => _chosenBikeKmh = value;
    }
}
