using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets the value of the target key to the current simulation time..
/// </summary>
public sealed partial class CurTimeOperator : HTNOperator
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    [DataField(required: true)] public string Key = "Origin";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _) => (true, new Dictionary<string, object>()
        {
            {Key, _gameTiming.CurTime}
        });
}
