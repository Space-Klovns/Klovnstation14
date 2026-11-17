using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Time;

/// <summary>
///     Sets the value of the specified key to the current simulation time, plus <see cref="Offset"/> and up to
///         <see cref="Jitter"/> either way: with an offset, a time something is due rather than one it happened. Either can
///         come from the blackboard instead (<see cref="OffsetKey"/>, <see cref="JitterKey"/>), so each mob can have its own.
/// </summary>
/// <example>
///     <code>
///     - !type:HTNPrimitiveTask # a new spot is due in 3 to 5 seconds
///       operator: !type:SetTimeOperator
///         key: RepositionDueAt
///         offset: 4s
///         jitter: 1s
///     </code>
///     <code>
///     - !type:HTNPrimitiveTask # the next peek is due when the mob says, 6s ± 2 if it does not
///       operator: !type:SetTimeOperator
///         key: PeekDueAt
///         offset: 6s
///         offsetKey: PeekInterval
///         jitter: 2s
///         jitterKey: PeekIntervalJitter
///     </code>
/// </example>
public sealed partial class SetTimeOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;

    [DataField(required: true)] public string Key = "TimeSince";

    /// <summary>
    ///     Added to the current time.
    /// </summary>
    [DataField] public TimeSpan Offset = TimeSpan.Zero;

    /// <summary>
    ///     Up to this much more or less, at random, so NPCs that set the same key together do not all come due
    ///         together.
    /// </summary>
    [DataField] public TimeSpan Jitter = TimeSpan.Zero;

    /// <summary>
    ///     If the blackboard has a float under this key, it is the offset in seconds, in place of <see cref="Offset"/>.
    /// </summary>
    [DataField] public string? OffsetKey;

    /// <summary>
    ///     If the blackboard has a float under this key, it is the jitter in seconds, in place of <see cref="Jitter"/>.
    /// </summary>
    [DataField] public string? JitterKey;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _) => (true, new Dictionary<string, object>()
        {
            {Key, GetTime(blackboard)}
        });

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        blackboard.SetValue(Key, GetTime(blackboard));
        return HTNOperatorStatus.Finished;
    }

    private TimeSpan GetTime(NPCBlackboard blackboard)
    {
        var offset = OffsetKey != null && blackboard.TryGetValue<float>(OffsetKey, out var offsetSeconds, _entityManager)
            ? TimeSpan.FromSeconds(offsetSeconds)
            : Offset;
        var jitter = JitterKey != null && blackboard.TryGetValue<float>(JitterKey, out var jitterSeconds, _entityManager)
            ? TimeSpan.FromSeconds(jitterSeconds)
            : Jitter;

        var time = _gameTiming.CurTime + offset;
        if (jitter > TimeSpan.Zero)
            time += jitter * _robustRandom.NextFloat(-1f, 1f);

        return time;
    }
}
