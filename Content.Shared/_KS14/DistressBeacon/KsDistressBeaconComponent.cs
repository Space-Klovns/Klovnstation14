using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.DistressBeacon;

/// <summary>
///     A beacon worn on the neck that strobes and beeps while its wearer is down, so that their side can find them
///         in the dark: fast and bright when they are critical, slow and dim when they are dead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsDistressBeaconComponent : Component
{
    /// <summary>
    ///     What the wearer's state makes the beacon do. Set only by <see cref="KsDistressBeaconSystem"/>, which keeps
    ///         the light and the worn sprite in step with it.
    /// </summary>
    [DataField, AutoNetworkedField, Access(typeof(KsDistressBeaconSystem)), ViewVariables(VVAccess.ReadOnly)]
    public KsDistressBeaconState State;

    /// <summary>
    ///     Time between beeps while the wearer is critical.
    /// </summary>
    [DataField]
    public TimeSpan CritBeepInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Time between beeps while the wearer is dead.
    /// </summary>
    [DataField]
    public TimeSpan DeadBeepInterval = TimeSpan.FromSeconds(6);

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
    ///     Light radius while the wearer is critical.
    /// </summary>
    [DataField]
    public float CritLightRadius = 3f;

    /// <summary>
    ///     Peak light energy while the wearer is critical, which the client pulses between 0 and this.
    /// </summary>
    [DataField]
    public float CritLightEnergy = 1.5f;

    /// <summary>
    ///     Pulses per second while the wearer is critical.
    /// </summary>
    [DataField]
    public float CritPulseRate = 1.5f;

    /// <summary>
    ///     Light radius while the wearer is dead.
    /// </summary>
    [DataField]
    public float DeadLightRadius = 2f;

    /// <summary>
    ///     Peak light energy while the wearer is dead.
    /// </summary>
    [DataField]
    public float DeadLightEnergy = 0.6f;

    /// <summary>
    ///     Pulses per second while the wearer is dead.
    /// </summary>
    [DataField]
    public float DeadPulseRate = 0.3f;
}

[Serializable, NetSerializable]
public enum KsDistressBeaconState : byte
{
    Off,
    Critical,
    Dead,
}
