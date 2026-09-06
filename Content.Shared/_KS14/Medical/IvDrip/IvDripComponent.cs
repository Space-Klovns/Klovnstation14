using Content.Shared.Actions;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     A wearable reservoir that periodically injects its contents into its wearer.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class IvDripComponent : Component
{
    [DataField(required: true)]
    public string SolutionName = "ivDrip";

    [DataField, AutoNetworkedField]
    public bool InjectionEnabled;

    [DataField]
    public EntProtoId ToggleAction = "ActionToggleIvDrip";

    public EntityUid? ToggleActionEntity;

    [DataField, AutoNetworkedField]
    public FixedPoint2 InjectionAmount = FixedPoint2.New(1);

    /// <summary>
    ///     Seconds between injections.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float InjectionInterval = 1f;

    [DataField, AutoNetworkedField]
    public EntityUid? Wearer;

    [DataField, AutoNetworkedField]
    public TimeSpan NextInjection;

    [DataField]
    public bool CanSetInjectionAmount = true;

    [DataField]
    public bool CanSetInjectionInterval = true;

    [DataField]
    public FixedPoint2 MinimumInjectionAmount = FixedPoint2.New(0.01);

    [DataField]
    public FixedPoint2 MaximumInjectionAmount = FixedPoint2.New(30);

    [DataField]
    public float MinimumInjectionInterval = 0.1f;

    [DataField]
    public float MaximumInjectionInterval = 10f;

    /// <summary>
    ///     Whether damage to the wearer spills fluid from this drip.
    /// </summary>
    [DataField]
    public bool SpillOnWearerAttacked;

    [DataField]
    public FixedPoint2 SpillAmount = FixedPoint2.New(5);
}

[Serializable, NetSerializable]
public enum IvDripUiKey : byte
{
    Key
}

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
    public bool InjectionEnabled = injectionEnabled;
    public FixedPoint2 InjectionAmount = injectionAmount;
    public float InjectionInterval = injectionInterval;
    public FixedPoint2 SolutionVolume = solutionVolume;
    public FixedPoint2 SolutionMaxVolume = solutionMaxVolume;
    public bool CanSetInjectionAmount = canSetInjectionAmount;
    public bool CanSetInjectionInterval = canSetInjectionInterval;
    public FixedPoint2 MinimumInjectionAmount = minimumInjectionAmount;
    public FixedPoint2 MaximumInjectionAmount = maximumInjectionAmount;
    public float MinimumInjectionInterval = minimumInjectionInterval;
    public float MaximumInjectionInterval = maximumInjectionInterval;
}

[Serializable, NetSerializable]
public sealed class IvDripSetEnabledMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class IvDripSetAmountMessage(FixedPoint2 amount) : BoundUserInterfaceMessage
{
    public FixedPoint2 Amount = amount;
}

[Serializable, NetSerializable]
public sealed class IvDripSetIntervalMessage(float interval) : BoundUserInterfaceMessage
{
    public float Interval = interval;
}

[Serializable, NetSerializable]
public sealed partial class ToggleIvDripActionEvent : InstantActionEvent;
