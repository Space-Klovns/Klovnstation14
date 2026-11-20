using Content.Shared.Actions;
using Content.Shared.Alert;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     The Rally action: marks a zone of <see cref="Radius"/> around the holder (<see cref="Zone"/>, drawn as a ring),
///         and after <see cref="Delay"/> every living friend inside, the holder too, is rallied for
///         <see cref="Duration"/>: faster on their feet, faster on the trigger and hitting harder
///         (<see cref="KsRallyBuffComponent"/>). Worked out on the server (<c>KsRallySystem</c>); the buff itself is shared.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsRallyAbilityComponent : Component
{
    [DataField]
    public float Radius = 3f;

    /// <summary>
    ///     From the call to the rally: how long the ring shows where it will be.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(12);

    [DataField]
    public float SpeedMultiplier = 1.2f;

    [DataField]
    public float FireRateMultiplier = 1.25f;

    [DataField]
    public float DamageMultiplier = 1.2f;

    [DataField(required: true)]
    public EntProtoId<KsRallyZoneComponent> Zone;

    /// <summary>
    ///     Shown, with the time left, to everyone rallied.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype>? Alert;

    [DataField]
    public SoundSpecifier? CallSound = new SoundPathSpecifier("/Audio/Items/Whistle/trench_whistle_1.ogg");

    [DataField]
    public SoundSpecifier? RallySound = new SoundPathSpecifier("/Audio/Effects/chime.ogg");

    [DataField]
    public SoundSpecifier? EndSound = new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");
}

public sealed partial class KsRallyActionEvent : InstantActionEvent;
