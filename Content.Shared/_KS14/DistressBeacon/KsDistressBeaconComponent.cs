using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.DistressBeacon;

/// <summary>
///     A beacon worn on the neck that strobes and beeps while its wearer is down, so that their side can find them
///         in the dark: fast and bright when they are critical, slow and dim when they are dead.
/// </summary>
// Raises AfterAutoHandleStateEvent, which the client draws the beacon from.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
[Access(typeof(KsDistressBeaconSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class KsDistressBeaconComponent : Component
{
    /// <summary>
    ///     The wearer's state, as the beacon follows it: <see cref="MobState.Alive"/> while it is not worn. The beacon
    ///         is on in any state <see cref="States"/> has settings for. Set only by <see cref="KsDistressBeaconSystem"/>,
    ///         which keeps the light and the worn sprite in step with it.
    /// </summary>
    [DataField, AutoNetworkedField, Access(typeof(KsDistressBeaconSystem)), ViewVariables(VVAccess.ReadOnly)]
    public MobState State = MobState.Alive;

    /// <summary>
    ///     What the beacon does in each state of its wearer it is on for. Read-only: the settings may be shared with
    ///         the prototype and other beacons.
    /// </summary>
    [DataField]
    public Dictionary<MobState, KsDistressBeaconStateSettings> States = new()
    {
        [MobState.Critical] = new()
        {
            BeepInterval = TimeSpan.FromSeconds(0.75),
            LightColor = Color.FromHex("#FF3030"),
            LightRadius = 4.5f,
            LightEnergy = 2.2f,
            PulseRate = 1.5f,
        },
        [MobState.Dead] = new()
        {
            BeepInterval = TimeSpan.FromSeconds(2),
            LightColor = Color.FromHex("#FF8C1A"),
            LightRadius = 4.5f,
            LightEnergy = 2.2f,
            PulseRate = 0.3f,
        },
    };

    /// <summary>
    ///     The beep, which carries a long way.
    /// </summary>
    [DataField]
    public SoundSpecifier BeepSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg", AudioParams.Default.WithMaxDistance(25f).WithVolume(-2f));

    /// <summary>
    ///     The settings for a state, or null if the beacon is off in it.
    /// </summary>
    public KsDistressBeaconStateSettings? SettingsFor(MobState state)
    {
        return States.GetValueOrDefault(state);
    }
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
