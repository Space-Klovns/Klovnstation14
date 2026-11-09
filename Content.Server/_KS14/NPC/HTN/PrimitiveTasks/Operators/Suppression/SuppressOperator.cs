using Content.Server._KS14.NPC.Suppression;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Suppression;

/// <summary>
///     Fires at the coordinates at <see cref="CoordinatesKey"/> for <see cref="Duration"/>, whether or not anything is
///         there. See <see cref="NpcSuppressiveFireSystem"/>.
/// </summary>
public sealed partial class SuppressOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSuppressiveFireSystem _npcSuppressiveFireSystem = default!;

    [DataField(required: true)]
    public string CoordinatesKey = string.Empty;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1.5);

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (blackboard.TryGetValue<EntityCoordinates>(CoordinatesKey, out var coordinates, _entityManager))
            _npcSuppressiveFireSystem.Start(blackboard.GetOwner(), coordinates, Duration);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _npcSuppressiveFireSystem.IsFiring(blackboard.GetOwner())
            ? HTNOperatorStatus.Continuing
            : HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _npcSuppressiveFireSystem.Stop(blackboard.GetOwner());
    }
}
