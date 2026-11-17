using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Grab;

/// <summary>
///     On an NPC while it is held in place by a grab, or carried: it struggles on its own, as a player would by
///         pressing resist. Added when the hold starts and removed once nothing holds it, so only held NPCs are
///         ever looked at.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsNpcGrabResistSystem))]
public sealed partial class KsNpcResistingComponent : Component
{
    /// <summary>
    ///     Time between two tries. The grab's resist cooldown and the carry's escape do-after are longer and decide how
    ///         often a try lands, so this only bounds the reaction time.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextAttempt;
}
