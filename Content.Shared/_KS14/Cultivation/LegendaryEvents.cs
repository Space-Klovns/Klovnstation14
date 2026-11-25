using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     The Heaven Reliant Sword's Heaven-Cleaving Stroke: a blade of light down a long line.
/// </summary>
public sealed partial class HeavenCleavingStrokeActionEvent : WorldTargetActionEvent;

/// <summary>
///     The Ruyi Jingu Bang stretches to the heavens and comes crashing down.
/// </summary>
public sealed partial class RuyiSlamActionEvent : WorldTargetActionEvent;

/// <summary>
///     Pluck a few hairs and blow on them: copies of the wielder leap out to fight someone.
/// </summary>
public sealed partial class RuyiPluckHairsActionEvent : EntityTargetActionEvent;

/// <summary>
///     Point the Purple-Gold Gourd at someone and call their name. If they answer, they're inside.
/// </summary>
public sealed partial class GourdCallActionEvent : EntityTargetActionEvent;

/// <summary>
///     The Plantain Fan's hurricane, blowing one way.
/// </summary>
public sealed partial class PlantainGustActionEvent : WorldTargetActionEvent;

/// <summary>
///     The Plantain Fan, fanned the other way: a fire whipped into a firestorm.
/// </summary>
public sealed partial class PlantainFirestormActionEvent : WorldTargetActionEvent;

/// <summary>
///     Everything in the Qiankun Pouch, hurled out at a spot.
/// </summary>
public sealed partial class QiankunReleaseActionEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class GourdEscapeDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     The Nine Yin White Bone Claw: five bone-white fingers rake someone beside you.
/// </summary>
public sealed partial class NineYinClawActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float Damage = 25f;

    [DataField]
    public float StaminaDamage = 35f;
}

/// <summary>
///     The Command of the Wumu Yishu: rally those around you into an unbreakable formation.
/// </summary>
public sealed partial class WumuCommandActionEvent : InstantActionEvent
{
    [DataField]
    public int Soldiers = 3;

    [DataField]
    public float Range = 7f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(30);
}

/// <summary>
///     The Seven-Star Sword, all seven stars lit: a river of starlight pours out down a line.
/// </summary>
public sealed partial class SevenStarsActionEvent : WorldTargetActionEvent;

/// <summary>
///     Swallow Heaven and Earth: every loose item nearby flies into the Qiankun Pouch.
/// </summary>
