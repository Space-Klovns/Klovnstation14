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
///         <see cref="Jitter"/> either way: with an offset, a time something is due rather than one it happened.
/// </summary>
/// <example>
///     <code>
///     - !type:HTNPrimitiveTask # a new spot is due in 3 to 5 seconds
///       operator: !type:SetTimeOperator
///         key: RepositionDueAt
///         offset: 4s
///         jitter: 1s
///     </code>
/// </example>
public sealed partial class SetTimeOperator : HTNOperator
{
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

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _) => (true, new Dictionary<string, object>()
        {
            {Key, GetTime()}
        });

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        blackboard.SetValue(Key, GetTime());
        return HTNOperatorStatus.Finished;
    }

    private TimeSpan GetTime()
    {
        var time = _gameTiming.CurTime + Offset;
        if (Jitter > TimeSpan.Zero)
            time += Jitter * _robustRandom.NextFloat(-1f, 1f);

        return time;
    }
}
