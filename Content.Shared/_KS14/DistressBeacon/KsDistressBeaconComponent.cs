using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.DistressBeacon;

/// <summary>
///     A beacon worn on the neck that strobes and beeps while its wearer is down, so that their side can find them
///         in the dark: fast and bright when they are critical, slow and dim when they are dead.
/// </summary>
// Raises AfterAutoHandleStateEvent, which the client draws the beacon from.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class KsDistressBeaconComponent : Component
{
    /// <summary>
    ///     What the wearer's state makes the beacon do. Set only by <see cref="KsDistressBeaconSystem"/>, which keeps
    ///         the light and the worn sprite in step with it.
    /// </summary>
    [DataField, AutoNetworkedField, Access(typeof(KsDistressBeaconSystem)), ViewVariables(VVAccess.ReadOnly)]
    public KsDistressBeaconState State;

    /// <summary>
    ///     What the beacon does while the wearer is critical.
    /// </summary>
    [DataField]
    public KsDistressBeaconStateSettings Critical = new()
    {
        BeepInterval = TimeSpan.FromSeconds(0.75),
        LightColor = Color.FromHex("#FF3030"),
        LightRadius = 4.2f,
        LightEnergy = 2.2f,
        PulseRate = 1.5f,
    };

    /// <summary>
    ///     What the beacon does while the wearer is dead.
    /// </summary>
    [DataField]
    public KsDistressBeaconStateSettings Dead = new()
    {
        BeepInterval = TimeSpan.FromSeconds(2),
        LightColor = Color.FromHex("#FF8C1A"),
        LightRadius = 3f,
        LightEnergy = 1.2f,
        PulseRate = 0.3f,
    };

    /// <summary>
    ///     The beep, which carries a long way.
    /// </summary>
    [DataField]
    public SoundSpecifier BeepSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg", AudioParams.Default.WithMaxDistance(25f).WithVolume(-2f));

    /// <summary>
    ///     When it beeps next. Server only.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextBeep;

    /// <summary>
    ///     The settings for a state, or null for <see cref="KsDistressBeaconState.Off"/>.
    /// </summary>
    public KsDistressBeaconStateSettings? SettingsFor(KsDistressBeaconState state)
    {
        return state switch
        {
            KsDistressBeaconState.Critical => Critical,
            KsDistressBeaconState.Dead => Dead,
            _ => null,
        };
    }
}

[Serializable, NetSerializable]
public enum KsDistressBeaconState : byte
{
    Off,
    Critical,
    Dead,
}

/// <summary>
///     How a distress beacon looks and sounds in one state.
/// </summary>
[DataDefinition]
public sealed partial class KsDistressBeaconStateSettings
{
    /// <summary>
    ///     Time between beeps.
    /// </summary>
    [DataField]
    public TimeSpan BeepInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public Color LightColor = Color.White;

    [DataField]
    public float LightRadius = 3f;

    /// <summary>
    ///     Peak light energy, which the client pulses between a faint glow and this.
    /// </summary>
    [DataField]
    public float LightEnergy = 1.5f;

    /// <summary>
    ///     Light pulses per second.
    /// </summary>
    [DataField]
    public float PulseRate = 1f;

    /// <summary>
    ///     Opacity of the outline the wearer gets, at the trough of the light's pulse. It is fully opaque at the peak
    ///         and takes the light's colour.
    /// </summary>
    [DataField]
    public float OutlineMinAlpha = 0.25f;
}
