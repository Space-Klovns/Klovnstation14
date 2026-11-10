using Content.Server._KS14.NPC.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met when the owner has sensor data waiting to be pulled in by
///         <see cref="PrimitiveTasks.Operators.HandleSensorsOperator"/>.
/// </summary>
/// <remarks>
///     Gate the pull on this. An ungated pull always plans, so it makes every branch after it unreachable.
/// </remarks>
public sealed partial class HasPendingSensorDataPrecondition : KsInvertiblePrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private EntityQuery<NpcSensorsComponent> _sensorsQuery = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        return blackboard.TryGetOwner(_entityManager, out var ownerUid) &&
            _sensorsQuery.TryGetComponent(ownerUid, out var sensorsComponent) &&
            sensorsComponent.AggregatedEffects.Count > 0;
    }
}
