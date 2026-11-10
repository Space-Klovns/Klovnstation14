using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Removes a virtual marker set by <see cref="EnsureVirtualMarkerOperator"/>.
/// </summary>
public sealed partial class RemoveVirtualMarkerOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;

    [DataField(required: true)] public string Id = "Marker";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _)
    {
        if (!blackboard.TryGetValue<HashSet<string>>(EnsureVirtualMarkerOperator.MarkerSet, out var markerSet, _entityManager) ||
            !markerSet.Contains(Id))
            return (true, null);

        // Cloned, as EnsureVirtualMarkerOperator does: the set may be shared with a blackboard being planned against.
        var newMarkerSet = new HashSet<string>(markerSet);
        newMarkerSet.Remove(Id);

        return (true, new Dictionary<string, object> { [EnsureVirtualMarkerOperator.MarkerSet] = newMarkerSet });
    }
}
