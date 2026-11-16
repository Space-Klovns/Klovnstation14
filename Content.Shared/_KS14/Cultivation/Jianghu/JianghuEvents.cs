using Content.Shared.Actions;

namespace Content.Shared._KS14.Cultivation.Jianghu;

/// <summary>
///     Formally challenge someone to an honor duel. They accept through a verb on the challenger.
/// </summary>
public sealed partial class ChallengeDuelActionEvent : EntityTargetActionEvent;

/// <summary>
///     Concede the honor duel.
/// </summary>
public sealed partial class YieldDuelActionEvent : InstantActionEvent;

public enum HonorDuelOutcome : byte
{
    Defeated,
    Yielded,
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
