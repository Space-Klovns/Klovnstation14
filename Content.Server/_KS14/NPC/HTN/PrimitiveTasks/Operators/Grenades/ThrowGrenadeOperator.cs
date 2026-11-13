using Content.Server._KS14.NPC.Grenades;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Grenades;

/// <summary>
///     Throws the grenade at <see cref="GrenadeKey"/> at the coordinates at <see cref="CoordinatesKey"/>. See
///         <see cref="NpcGrenadeSystem"/>.
///     <para>
///         For a thrower that cooks (<see cref="NpcGrenadeThrowerComponent.Cooks"/>) this task is the hold: it carries on while
///             the grenade is in hand, and is finished once it has been thrown. The hold itself belongs to the system, so
///             this task being cut short in any way (<see cref="TaskShutdown"/>) throws the grenade at once rather than
///             leave a lit one in the hand. While it holds the grenade it steps a little to one side of the line it throws
///             along, where the throw is still clear (<see cref="NpcGrenadeSystem.TryGetCookStep"/>), rather than stand still
///             in the open for the whole of it; the step ends with the task.
///     </para>
/// </summary>
public sealed partial class ThrowGrenadeOperator : HTNOperator
{
    /// <summary>
    ///     Set on the blackboard once this task has begun its throw, so that later updates know they are only waiting.
    ///         Removed in <see cref="TaskShutdown"/>.
    /// </summary>
    private const string BegunKey = "GrenadeThrowBegun";

    /// <summary>
    ///     Set while this task is steering its step aside during a cook, so that <see cref="TaskShutdown"/> stops only steering
    ///         it started.
    /// </summary>
    private const string SteppingKey = "GrenadeCookStepping";

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcGrenadeSystem _npcGrenadeSystem = default!;
    [Dependency] private NPCSteeringSystem _npcSteeringSystem = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;

    [DataField]
    public string GrenadeKey = "ChosenGrenade";

    [DataField]
    public string CoordinatesKey = "GrenadeThrowCoordinates";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var ownerUid = blackboard.GetOwner();

        if (blackboard.ContainsKey(BegunKey))
        {
            if (!_npcGrenadeSystem.IsCooking(ownerUid))
                return HTNOperatorStatus.Finished;

            _npcGrenadeSystem.RenewCook(ownerUid);
            return HTNOperatorStatus.Continuing;
        }

        if (!blackboard.TryGetEntity(GrenadeKey, _entityManager, out var grenadeUid) ||
            !blackboard.TryGetCoordinates(CoordinatesKey, _entityManager, out var coordinates))
            return HTNOperatorStatus.Failed;

        switch (_npcGrenadeSystem.TryBeginThrow(ownerUid, grenadeUid, coordinates))
        {
            case NpcGrenadeThrowResult.Thrown:
                return HTNOperatorStatus.Finished;
            case NpcGrenadeThrowResult.Cooking:
                blackboard.SetValue(BegunKey, true);

                if (_npcGrenadeSystem.TryGetCookStep(ownerUid, coordinates, out var step))
                {
                    var steeringComponent = _npcSteeringSystem.Register(ownerUid, step);
                    steeringComponent.Range = 0.3f;
                    steeringComponent.Flags = _pathfindingSystem.GetFlags(blackboard);
                    blackboard.SetValue(SteppingKey, true);
                }

                return HTNOperatorStatus.Continuing;
            default:
                return HTNOperatorStatus.Failed;
        }
    }

    /// <summary>
    ///     However this task ends - finished, failed, or replaced by a better plan - a grenade still being cooked is thrown now.
    /// </summary>
    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);

        blackboard.Remove<bool>(BegunKey);
        _npcGrenadeSystem.EndCook(blackboard.GetOwner());

        if (blackboard.Remove<bool>(SteppingKey))
            _npcSteeringSystem.Unregister(blackboard.GetOwner());
    }
}
