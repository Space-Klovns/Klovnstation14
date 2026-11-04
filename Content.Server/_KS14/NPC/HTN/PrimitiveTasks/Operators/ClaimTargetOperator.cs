using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Claims the entity at <see cref="TargetKey"/> for the owner, so that squadmates pass it over (see
///         <see cref="NpcTargetClaimSystem"/>). Acts when the task starts, not when it is planned: a plan that never runs
///         claims nothing.
/// </summary>
public sealed partial class ClaimTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcTargetClaimSystem _npcTargetClaimSystem = default!;

    [DataField]
    public string TargetKey = "Target";

    /// <summary>
    ///     How long the claim lasts, if the owner never gets round to it: long enough to walk there and do the job.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager))
            _npcTargetClaimSystem.Claim(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), targetUid, Duration);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
