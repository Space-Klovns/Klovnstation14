using Content.Server._KS14.NPC.Recovery;
using Content.Server.NPC;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Recovery;

/// <summary>
///     Starts the owner waiting for the meds it has just used to work (see <see cref="NpcRecoverySystem"/>). Goes after the
///         task injecting them, so a plan cut short before that starts nothing.
/// </summary>
public sealed partial class StartRecoveryOperator : KsInstantOperator
{
    [Dependency] private NpcRecoverySystem _npcRecoverySystem = default!;

    protected override void OnStartup(EntityUid ownerUid, NPCBlackboard blackboard)
    {
        _npcRecoverySystem.StartRecovery(ownerUid);
    }
}
