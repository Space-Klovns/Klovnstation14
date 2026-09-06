using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;

namespace Content.Server._KS14.NPC.Queries.Considerations;

public sealed partial class HasSolutionCon : UtilityConsideration
{
    [Dependency] private EntityQuery<SolutionManagerComponent> _solutionManagerQuery = default!;
    [Dependency] private EntityQuery<SolutionComponent> _solutionQuery = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
        => (_solutionManagerQuery.HasComponent(targetUid) || _solutionQuery.HasComponent(targetUid)) ? 1f : 0f;
}
