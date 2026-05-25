using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Chemistry.Components.SolutionManager;

namespace Content.Server._KS14.NPC.Queries.Considerations;

public sealed partial class HasSolutionCon : UtilityConsideration
{
    [Dependency] private readonly EntityQuery<SolutionContainerManagerComponent> _solutionContainerQuery = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        return _solutionContainerQuery.HasComponent(targetUid) ? 1f : 0f;
    }
}
