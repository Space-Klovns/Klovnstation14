using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

// The Demonic Path: arts the righteous sects burned every copy of. Each event carries its own numbers so the action
// prototypes can tune them.

public sealed partial class DevouringArtActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 10f;
}

public sealed partial class GuWormActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 20f;

    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromMinutes(5);
}

public sealed partial class StirGuActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 5f;
}

public sealed partial class SoulSearchActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 35f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);
}

public sealed partial class CorpsePuppetActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 50f;

    /// <summary>
    ///     How long the puppet lasts at Golden Core; a minute more for every realm above it.
    /// </summary>
    [DataField]
    public TimeSpan BaseLifetime = TimeSpan.FromMinutes(3);

    [DataField]
    public EntProtoId Puppet = "KsJiangshi";
}

public sealed partial class BloodEscapeActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;

    [DataField]
    public float MinDistance = 8f;

    [DataField]
    public float MaxDistance = 15f;

    /// <summary>
    ///     How much of the way to critical condition the escape costs in blood.
    /// </summary>
    [DataField]
    public float LifeFraction = 0.15f;
}

public sealed partial class ExplodingHeartPalmActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 45f;

    [DataField]
    public int BaseSteps = 5;
}

public sealed partial class TransmitDemonicArtActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 40f;
}

[Serializable, NetSerializable]
public sealed partial class DevouringArtDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class SoulSearchDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class DemonicScriptureDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Raised on each mob a Spiritual Sense sweep touches, so other systems can add what they notice.
/// </summary>
[ByRefEvent]
public record struct CultivationSensedEvent(EntityUid Sensor, bool Found = false);

/// <summary>
///     Raised on a cultivator after each cycle of meditation.
/// </summary>
[ByRefEvent]
public record struct CultivationMeditatedEvent;
