using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

[Serializable, NetSerializable]
public sealed partial class MartialArtReadManualDoAfterEvent : SimpleDoAfterEvent;

#region Qi arts

/// <summary>
///     Eighteen Dragon-Subduing Palms: a palm that hits like a charging dragon. Each palm on the same target within
///         a few seconds hits harder and throws them further.
/// </summary>
public sealed partial class DragonPalmActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 15f;

    [DataField]
    public float BluntDamage = 12f;

    [DataField]
    public float DamagePerStack = 4f;

    [DataField]
    public float ThrowDistance = 2f;

    [DataField]
    public int MaxStacks = 5;

    [DataField]
    public TimeSpan ComboWindow = TimeSpan.FromSeconds(4);
}

/// <summary>
///     Six Meridians Divine Sword: sword qi fired from the fingertips.
/// </summary>
public sealed partial class SixMeridiansActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 10f;

    [DataField]
    public EntProtoId Projectile = "ProjectileSixMeridiansSwordQi";

    [DataField]
    public float Speed = 30f;
}

/// <summary>
///     Northern Darkness Divine Skill: drink in another's qi like the sea takes in rivers. A mortal loses stamina instead.
/// </summary>
public sealed partial class NorthernDarknessActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float QiDrain = 40f;

    [DataField]
    public float MortalStaminaDamage = 25f;

    [DataField]
    public float MortalQiGain = 10f;
}

/// <summary>
///     Star-Absorbing Great Method: a darker, greedier drain that also tears away insight and heals the user, at the
///         risk of the stolen qi clashing in their meridians.
/// </summary>
public sealed partial class StarAbsorbingActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float QiDrain = 60f;

    [DataField]
    public float InsightDrain = 20f;

    [DataField]
    public float Heal = 15f;

    [DataField]
    public float MortalStaminaDamage = 35f;

    [DataField]
    public float MortalQiGain = 15f;

    [DataField]
    public float ClashChance = 0.3f;
}

/// <summary>
///     Great Shift of Heaven and Earth: for a few seconds, projectiles that hit you are turned back.
/// </summary>
public sealed partial class GreatShiftActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(6);

    [DataField]
    public int Charges = 3;
}

#endregion

#region Body arts

/// <summary>
///     Golden Bell Shield: harden the skin against physical blows.
/// </summary>
public sealed partial class GoldenBellActionEvent : InstantActionEvent
{
    [DataField]
    public float StaminaCost = 25f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    [DataField]
    public float DamageMultiplier = 0.6f;
}

/// <summary>
///     Graceful Waves Steps: a burst of light footwork.
/// </summary>
public sealed partial class GracefulStepsActionEvent : InstantActionEvent
{
    [DataField]
    public float StaminaCost = 20f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);

    [DataField]
    public float SpeedMultiplier = 1.35f;
}

/// <summary>
///     Ladder-Cloud Leap: spring off the air itself to land somewhere else.
/// </summary>
public sealed partial class LadderCloudLeapActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float StaminaCost = 20f;

    [DataField]
    public float MaxDistance = 5f;
}

/// <summary>
///     Nine Swords of Dugu: read the next blow and break it.
/// </summary>
public sealed partial class DuguCounterActionEvent : InstantActionEvent
{
    [DataField]
    public float StaminaCost = 20f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(3);
}

#endregion
