using Content.Shared.Actions;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DeviceLinking;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     A wearable reservoir that periodically injects its contents into its wearer.
/// </summary>
/// <remarks>
///     Injection itself is server-authoritative - see <see cref="SharedIvDripSystem.Update"/> for why.
///         Everything the client needs to draw the window and the hotbar action is networked here.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class IvDripComponent : Component
{
    /// <summary>
    ///     Name of the solution this drip pumps out of, on this same entity.
    /// </summary>
    [DataField]
    public string SolutionName = "ivDrip";

    /// <summary>
    ///     Whether the pump is currently running. Set this through
    ///         <see cref="SharedIvDripSystem.SetInjectionEnabled"/> rather than directly, so that the
    ///         hotbar action and the window stay in step with it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool InjectionEnabled;

    /// <summary>
    ///     Device-link sink port that flips <see cref="InjectionEnabled"/>.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> TogglePort = "Toggle";

    /// <summary>
    ///     Device-link sink port that turns injection on.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> OnPort = "On";

    /// <summary>
    ///     Device-link sink port that turns injection off.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> OffPort = "Off";

    /// <summary>
    ///     Action granted to the wearer while this drip is worn, which toggles <see cref="InjectionEnabled"/>.
    /// </summary>
    [DataField]
    public EntProtoId ToggleAction = "ActionToggleIvDrip";

    /// <summary>
    ///     The live action entity spawned from <see cref="ToggleAction"/>, or null while unworn.
    /// </summary>
    /// <remarks>
    ///     Networked because the client resolves it to draw the action button's toggled state.
    /// </remarks>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;

    /// <summary>
    ///     Units of solution moved into the wearer per injection.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 InjectionAmount = FixedPoint2.New(1);

    /// <summary>
    ///     Seconds between injections.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float InjectionInterval = 1f;

    /// <summary>
    ///     Who is currently wearing this drip, or null while unworn.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? WearerUid;

    /// <summary>
    ///     <see cref="IGameTiming.CurTime"/> at which the next injection is due.
    /// </summary>
    /// <remarks>
    ///     Deliberately neither a datafield nor networked: it is an absolute time against the server
    ///         clock, so map-serializing it or replicating it to a client means nothing.
    /// </remarks>
    [ViewVariables]
    public TimeSpan NextInjection;

    /// <summary>
    ///     Whether the wearer may change <see cref="InjectionAmount"/> through the window.
    /// </summary>
    [DataField]
    public bool CanSetInjectionAmount = true;

    /// <summary>
    ///     Whether the wearer may change <see cref="InjectionInterval"/> through the window.
    /// </summary>
    [DataField]
    public bool CanSetInjectionInterval = true;

    /// <summary>
    ///     Lower bound <see cref="InjectionAmount"/> is clamped to.
    /// </summary>
    [DataField]
    public FixedPoint2 MinimumInjectionAmount = FixedPoint2.New(0.01);

    /// <summary>
    ///     Upper bound <see cref="InjectionAmount"/> is clamped to.
    /// </summary>
    [DataField]
    public FixedPoint2 MaximumInjectionAmount = FixedPoint2.New(30);

    /// <summary>
    ///     Lower bound, in seconds, <see cref="InjectionInterval"/> is clamped to.
    /// </summary>
    [DataField]
    public float MinimumInjectionInterval = 0.1f;

    /// <summary>
    ///     Upper bound, in seconds, <see cref="InjectionInterval"/> is clamped to.
    /// </summary>
    [DataField]
    public float MaximumInjectionInterval = 10f;

    /// <summary>
    ///     Whether damage to the wearer spills fluid from this drip.
    /// </summary>
    [DataField]
    public bool SpillOnWearerAttacked;

    /// <summary>
    ///     Damage types that can tear the line out. Any one of them landing on the wearer spills the drip.
    /// </summary>
    /// <remarks>
    ///     Types rather than a <c>DamageGroupPrototype</c>, which is obsolete for anything but grouping
    ///         damage in UIs.
    /// </remarks>
    [DataField]
    // ReSharper disable once UseCollectionExpression - a non-empty collection expression on a List<T>
    // lowers to CollectionsMarshal.SetCount, which the content sandbox rejects.
    public List<ProtoId<DamageTypePrototype>> SpillDamageTypes = new() { "Blunt", "Slash", "Piercing" };

    /// <summary>
    ///     Units of solution dumped on the floor per spill.
    /// </summary>
    [DataField]
    public FixedPoint2 SpillAmount = FixedPoint2.New(5);
}

/// <summary>
///     UI key for the IV drip configuration window.
/// </summary>
[Serializable, NetSerializable]
public enum IvDripUiKey : byte
{
    Key
}

/// <summary>
///     Everything the IV drip window draws.
/// </summary>
/// <remarks>
///     The bounds and the two "can set" flags are static per prototype and are resent on every update.
///         That is eleven small fields on a window one player at a time has open, which is not worth a
///         second message type to avoid.
/// </remarks>
[Serializable, NetSerializable]
public sealed class IvDripBoundUserInterfaceState(
    bool injectionEnabled,
    FixedPoint2 injectionAmount,
    float injectionInterval,
    FixedPoint2 solutionVolume,
    FixedPoint2 solutionMaxVolume,
    bool canSetInjectionAmount,
    bool canSetInjectionInterval,
    FixedPoint2 minimumInjectionAmount,
    FixedPoint2 maximumInjectionAmount,
    float minimumInjectionInterval,
    float maximumInjectionInterval) : BoundUserInterfaceState
{
    /// <inheritdoc cref="IvDripComponent.InjectionEnabled"/>
    public bool InjectionEnabled = injectionEnabled;

    /// <inheritdoc cref="IvDripComponent.InjectionAmount"/>
    public FixedPoint2 InjectionAmount = injectionAmount;

    /// <inheritdoc cref="IvDripComponent.InjectionInterval"/>
    public float InjectionInterval = injectionInterval;

    /// <summary>
    ///     Current contents of the drip's solution, in units.
    /// </summary>
    public FixedPoint2 SolutionVolume = solutionVolume;

    /// <summary>
    ///     Capacity of the drip's solution, in units.
    /// </summary>
    public FixedPoint2 SolutionMaxVolume = solutionMaxVolume;

    /// <inheritdoc cref="IvDripComponent.CanSetInjectionAmount"/>
    public bool CanSetInjectionAmount = canSetInjectionAmount;

    /// <inheritdoc cref="IvDripComponent.CanSetInjectionInterval"/>
    public bool CanSetInjectionInterval = canSetInjectionInterval;

    /// <inheritdoc cref="IvDripComponent.MinimumInjectionAmount"/>
    public FixedPoint2 MinimumInjectionAmount = minimumInjectionAmount;

    /// <inheritdoc cref="IvDripComponent.MaximumInjectionAmount"/>
    public FixedPoint2 MaximumInjectionAmount = maximumInjectionAmount;

    /// <inheritdoc cref="IvDripComponent.MinimumInjectionInterval"/>
    public float MinimumInjectionInterval = minimumInjectionInterval;

    /// <inheritdoc cref="IvDripComponent.MaximumInjectionInterval"/>
    public float MaximumInjectionInterval = maximumInjectionInterval;
}

/// <summary>
///     Sent by the window to start or stop injection.
/// </summary>
[Serializable, NetSerializable]
public sealed class IvDripSetEnabledMessage(bool enabled) : BoundUserInterfaceMessage
{
    /// <inheritdoc cref="IvDripComponent.InjectionEnabled"/>
    public bool Enabled = enabled;
}

/// <summary>
///     Sent by the window to change how much is injected per tick. Clamped server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class IvDripSetAmountMessage(FixedPoint2 amount) : BoundUserInterfaceMessage
{
    /// <inheritdoc cref="IvDripComponent.InjectionAmount"/>
    public FixedPoint2 Amount = amount;
}

/// <summary>
///     Sent by the window to change how often injection happens. Clamped server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class IvDripSetIntervalMessage(float interval) : BoundUserInterfaceMessage
{
    /// <inheritdoc cref="IvDripComponent.InjectionInterval"/>
    public float Interval = interval;
}

/// <summary>
///     Raised on the worn drip's action when the wearer presses it, to toggle injection.
/// </summary>
/// <remarks>
///     Partial because the prototype names it through <c>!type:</c>, which makes it a data definition.
/// </remarks>
public sealed partial class ToggleIvDripActionEvent : InstantActionEvent;
