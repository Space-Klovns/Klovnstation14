using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation.Body;

// Every body art names its exhaustion cost; most then scale with the body's stage.

public sealed partial class BodyForgeActionEvent : InstantActionEvent;

public sealed partial class BodyBreathOfRenewalActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 15f;
}

public sealed partial class BodyIronShirtActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 20f;
}

public sealed partial class BodyMountainLeapActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float Exhaustion = 15f;
}

public sealed partial class BodyEarthStompActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 20f;
}

public sealed partial class BodyShatteringFistActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float Exhaustion = 25f;
}

public sealed partial class BodyHundredFistsActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float Exhaustion = 25f;
}

public sealed partial class BodyBoneSettingActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 15f;
}

public enum BodyStance : byte
{
    Thunderclap,
    Mountain,
}

/// <summary>
///     Settle into a stance, or out of it if already in it. With no stance given, step to the next one in turn:
///         none, Thunderclap, Mountain, none.
/// </summary>
public sealed partial class BodyStanceActionEvent : InstantActionEvent
{
    [DataField]
    public BodyStance? Stance;
}

public sealed partial class BodyDiscipleActionEvent : EntityTargetActionEvent;

public sealed partial class BodyRemoldActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 40f;
}

public sealed partial class BodyBullChargeActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float Exhaustion = 30f;
}

public sealed partial class BodyBloodBoilActionEvent : InstantActionEvent
{
    /// <summary>
    ///     The crash when it wears off.
    /// </summary>
    [DataField]
    public float CrashExhaustion = 40f;
}

public sealed partial class BodyFallingMountainActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float Exhaustion = 30f;
}

public sealed partial class BodyMountainHurlActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float Exhaustion = 25f;
}

public sealed partial class BodySkySplittingPalmActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float Exhaustion = 30f;
}

public sealed partial class BodyVajraActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 30f;
}

public sealed partial class BodyHeavenQuakeActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 40f;
}

public sealed partial class BodyPrimordialRoarActionEvent : InstantActionEvent
{
    [DataField]
    public float Exhaustion = 30f;
}

[Serializable, NetSerializable]
public sealed partial class BodyForgeDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class BodyTribulationDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class BodyBreathDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class BodyStanceDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public BodyStance Stance;

    public BodyStanceDoAfterEvent(BodyStance stance)
    {
        Stance = stance;
    }

    public override DoAfterEvent Clone()
    {
        return new BodyStanceDoAfterEvent(Stance);
    }
}

[Serializable, NetSerializable]
public sealed partial class BodyManualDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Raised on a body cultivator after their stage rises.
/// </summary>
[ByRefEvent]
public readonly record struct BodyStageChangedEvent(int OldStage, int NewStage);
