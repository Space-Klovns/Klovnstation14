using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Returns true (or false if inverted) only when the blackboard purportedly
///         has the given virtual marker.
/// </summary>
public sealed partial class HasVirtualMarkerPrecondition : KsInvertiblePrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;

    [DataField(required: true)] public string Id = "Marker";

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        return HasMarker(blackboard, Id, _entityManager);
    }

    public static bool HasMarker(NPCBlackboard blackboard, string id, IEntityManager entityManager)
    {
        return blackboard.TryGetValue<HashSet<string>>(EnsureVirtualMarkerOperator.MarkerSet, out var markerSet, entityManager) &&
            markerSet.Contains(id);
    }
}
