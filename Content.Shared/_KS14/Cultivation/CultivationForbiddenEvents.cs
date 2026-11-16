using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

// The Demonic Path: arts the righteous sects burned every copy of. Each event carries its own numbers so the action
// prototypes can tune them.

public sealed partial class DevouringArtActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float QiCost = 10f;
}

public sealed partial class GuWormActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float QiCost = 20f;

    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromMinutes(5);
}

public sealed partial class StirGuActionEvent : InstantActionEvent
{
    [DataField]
    public float QiCost = 5f;
}

public sealed partial class SoulSearchActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float QiCost = 35f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);
}

public sealed partial class CorpsePuppetActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float QiCost = 50f;

    /// <summary>
    ///     How long the puppet lasts at Golden Core; a minute more for every realm above it.
    /// </summary>
    [DataField]
    public TimeSpan BaseLifetime = TimeSpan.FromMinutes(2);

    [DataField]
    public EntProtoId Puppet = "KsJiangshi";
}

public sealed partial class BloodEscapeActionEvent : InstantActionEvent
{
    [DataField]
    public float QiCost = 30f;

    [DataField]
    public float MinDistance = 8f;

    [DataField]
    public float MaxDistance = 15f;

    /// <summary>
    ///     How much of the way to critical condition the escape costs in blood.
    /// </summary>
    [DataField]
    public float LifeFraction = 0.25f;
}

public sealed partial class ExplodingHeartPalmActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float QiCost = 60f;

    [DataField]
    public int BaseSteps = 5;
}

public sealed partial class TransmitDemonicArtActionEvent : InstantActionEvent
{
    [DataField]
    public float QiCost = 40f;
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
