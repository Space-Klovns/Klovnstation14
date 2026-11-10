using Content.Server._KS14.NPC.Suppression;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Suppression;

/// <summary>
///     Fires at the coordinates at <see cref="CoordinatesKey"/> for <see cref="Duration"/>, whether or not anything is
///         there, keeping its shots off friends. See <see cref="NpcSuppressiveFireSystem"/>.
/// </summary>
public sealed partial class SuppressOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSuppressiveFireSystem _npcSuppressiveFireSystem = default!;

    [DataField(required: true)]
    public string CoordinatesKey = string.Empty;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     The hostile that went there, if it is one: where it could be by now does as well as the spot, round a friend in the
    ///         way. Without one it has to be that very spot, as a window to break is: a round beside it hits the wall. With
    ///         nowhere clear, it holds fire.
    /// </summary>
    [DataField]
    public string? TargetKey;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (!blackboard.TryGetCoordinates(CoordinatesKey, _entityManager, out var coordinates))
            return;

        EntityUid? targetUid = TargetKey is not null && blackboard.TryGetEntity(TargetKey, _entityManager, out var uid) ? uid : null;
        _npcSuppressiveFireSystem.Start(blackboard.GetOwner(), coordinates, Duration, targetUid);
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
