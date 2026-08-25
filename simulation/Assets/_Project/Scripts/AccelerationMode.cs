// How the vehicles reach and leave cruise speed. Picked from the options menu.
// Instant: speed snaps to cruise or a stop over ~100ms (VrFastSpeed) and the
// throttle latches, so the driver presses once and lets go. The drawn-out
// changing-velocity cue is what causes sim sickness, so this is the VR default.
// Gradual: the vehicle's own engine and brake physics, throttle held down.
public enum AccelerationMode
{
    Instant,
    Gradual,
}

public static class AccelerationSetting
{
    // The XR display can take a few seconds to start running
    private static AccelerationMode? _chosenMode;

    public static AccelerationMode Mode
    {
        get => _chosenMode ?? (VrActive.IsActive ? AccelerationMode.Instant : AccelerationMode.Gradual);
        set => _chosenMode = value;
    }
}
