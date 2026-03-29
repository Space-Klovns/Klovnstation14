using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets the value of the target key to that of the origin key (which must be present).
///         Doesnt actually copy unless the copied data is by-value.
/// </summary>
public sealed partial class CopyKeyOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    [DataField(required: true)] public string OriginKey = "Origin";
    [DataField(required: true)] public string TargetKey = "Target";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _) => (true, new Dictionary<string, object>()
        {
            {TargetKey, blackboard.GetValueOrDefault<object>(OriginKey, _entityManager)!}
        });
}
