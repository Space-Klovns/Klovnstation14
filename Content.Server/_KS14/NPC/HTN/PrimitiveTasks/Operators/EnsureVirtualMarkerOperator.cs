using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Ensures a VIRTUAL (blackboard-only, not actual game-tag) tag exists in the blackboard.
/// </summary>
public sealed partial class EnsureVirtualMarkerOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public const string MarkerSet = "Ks_MarkerSet";
    [DataField(required: true)] public string Id = "Marker";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _)
    {
        HashSet<string> set;
        if (blackboard.TryGetValue<HashSet<string>>(MarkerSet, out var bbSet, _entityManager))
            set = [.. bbSet]; // intentionally clone it
        else
            set = [Id];

        return (true, new() { [MarkerSet] = set });
    }
}
