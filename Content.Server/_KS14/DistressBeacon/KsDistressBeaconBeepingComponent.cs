namespace Content.Server._KS14.DistressBeacon;

/// <summary>
///     On a distress beacon while it is on, so that only beacons that are beeping are looked at each tick. Added and
///         removed with the beacon's state. Server-only runtime state, never saved.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsDistressBeaconBeepSystem))]
public sealed partial class KsDistressBeaconBeepingComponent : Component
{
    /// <summary>
    ///     When it beeps next.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextBeep;
}
