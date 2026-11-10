using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Perception;

/// <summary>
///     The owner has called out the death it confirmed: <c>TargetDownPrecondition</c> stops being met. When the task
///         runs, not when it is planned.
/// </summary>
public sealed partial class AcknowledgeTargetDownOperator : KsInstantOperator
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    protected override void OnStartup(EntityUid ownerUid, NPCBlackboard blackboard)
    {
        _npcPerceptionSystem.ClearPendingKillCallout(ownerUid);
    }
}
