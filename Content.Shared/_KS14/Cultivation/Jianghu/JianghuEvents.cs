using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation.Jianghu;

/// <summary>
///     Formally challenge someone to an honor duel. They accept through a verb on the challenger.
/// </summary>
public sealed partial class ChallengeDuelActionEvent : EntityTargetActionEvent;

/// <summary>
///     Concede the honor duel.
/// </summary>

public enum HonorDuelOutcome : byte
{
    Defeated,
    Fled,
    Vanished,
    Draw,
}

/// <summary>
///     Raised broadcast as a duel is decided, before face changes hands, so anything that makes a victory worth more
///         (a rival sect, a tournament) can add to it.
/// </summary>
[ByRefEvent]
public record struct HonorDuelResolvingEvent(EntityUid Winner, EntityUid Loser, int FaceGain);

/// <summary>
///     Raised broadcast once a duel is over. On a draw there is no winner or loser, only the two fighters.
/// </summary>
[ByRefEvent]
public readonly record struct HonorDuelFinishedEvent(EntityUid FighterOne, EntityUid FighterTwo, EntityUid? Winner, EntityUid? Loser, HonorDuelOutcome Outcome);

/// <summary>
///     Found a sect where you stand. The name is asked for in a dialog.
/// </summary>
public sealed partial class FoundSectActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 50f;
}

/// <summary>
///     A telepathic message to every member of your sect.
/// </summary>
public sealed partial class SectTransmissionActionEvent : InstantActionEvent;

/// <summary>
///     As Sect Master, declare the sect of the target (a member, or their plaque) your sworn rival.
/// </summary>
public sealed partial class DeclareRivalryActionEvent : EntityTargetActionEvent;

/// <summary>
///     Kneeling together before the incense to swear brotherhood.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SwornOathDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public enum SwornIncenseVisuals : byte
{
    Lit,
}
