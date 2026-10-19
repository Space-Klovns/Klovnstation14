using Content.Server._KS14.NPC.Doors;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Doors.Components;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors;

/// <summary>
///     Forces a door open with a tool (see <see cref="NpcDoorSystem.TryStartBreach"/>): the tool comes out of wherever
///         it is carried, is used, and goes back where it came from with the weapon back in hand, wielded if it was.
///         If the task is cut short by something more pressing, the breach is stopped and the tool put away all the
///         same: nobody goes through a door holding a crowbar. A door already open counts as forced.
/// </summary>
public sealed partial class BreachDoorOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;

    [Dependency] private EntityQuery<DoorComponent> _doorQuery = default!;

    /// <summary>
    ///     The door to force.
    /// </summary>
    [DataField] public string DoorKey = "OrderTarget";

    /// <summary>
    ///     What to force it with.
    /// </summary>
    [DataField] public string ToolKey = "OrderTool";

    /// <summary>
    ///     Longest to wait for the door to open once the tool has been used, before giving up.
    /// </summary>
    [DataField] public TimeSpan Timeout = NpcDoorSystem.DefaultBreachTimeout;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (blackboard.TryGetValue<EntityUid>(DoorKey, out var doorUid, _entityManager) &&
            blackboard.TryGetValue<EntityUid>(ToolKey, out var toolUid, _entityManager))
            _npcDoorSystem.TryStartBreach(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), doorUid, toolUid, Timeout, fromSteering: false);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(DoorKey, out var doorUid, _entityManager) ||
            !_doorQuery.TryComp(doorUid, out var doorComponent))
            return HTNOperatorStatus.Failed;

        if (doorComponent.State is DoorState.Open or DoorState.Opening)
            return HTNOperatorStatus.Finished;

        // Still at it; or it has ended, with the door still shut.
        return _npcDoorSystem.IsBreaching(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), doorUid)
            ? HTNOperatorStatus.Continuing
            : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _npcDoorSystem.StopBreach(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
