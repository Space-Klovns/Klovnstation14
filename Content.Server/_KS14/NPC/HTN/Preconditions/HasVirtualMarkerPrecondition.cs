using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Returns true (or false if inverted) only when the blackboard purportedly
///         has the given virtual marker.
/// </summary>
public sealed partial class HasVirtualMarkerPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    [DataField] public bool Invert;
    [DataField(required: true)] public string Id = "Marker";

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<HashSet<string>>(EnsureVirtualMarkerOperator.MarkerSet, out var tagSet, _entityManager) ||
            !tagSet.Contains(Id))
            return Invert;

        return !Invert;
    }
}
