using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets the value of the specified key to the current simulation time.
/// </summary>
public sealed partial class SetTimeOperator : HTNOperator
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    [DataField(required: true)] public string Key = "TimeSince";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _) => (true, new Dictionary<string, object>()
        {
            {Key, _gameTiming.CurTime}
        });

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        blackboard.SetValue(Key, _gameTiming.CurTime);
        return HTNOperatorStatus.Finished;
    }
}
