using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Time;

/// <summary>
///     If a TimeSpan exists in the blackboard at the specified key, this will be met if the current
///         game-time is at or past that point+<see cref="Delay"/>.
///
///     Will also be met if the TimeSpan doesn't exist.
/// </summary>
public sealed partial class KeyTimePassedPrecondition : KsInvertiblePrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    [DataField(required: true)] public string Key = "TimeAt";
    [DataField] public TimeSpan Delay = TimeSpan.Zero;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<TimeSpan>(Key, out var time, _entManager))
            return true;

        return _gameTiming.CurTime >= time + Delay;
    }
}
