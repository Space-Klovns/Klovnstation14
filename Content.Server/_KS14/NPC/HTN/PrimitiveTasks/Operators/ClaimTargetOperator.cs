using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Claims the entity at <see cref="TargetKey"/> for the owner, so that squadmates pass it over (see
///         <see cref="NpcTargetClaimSystem"/>). Acts when the task runs, not when it is planned: a plan that never runs
///         claims nothing. Fails if a squadmate that planned alongside the owner got there first, so the owner plans again.
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

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return blackboard.TryGetEntity(TargetKey, _entityManager, out var targetUid) &&
            _npcTargetClaimSystem.TryClaim(blackboard.GetOwner(), targetUid, Duration)
                ? HTNOperatorStatus.Finished
                : HTNOperatorStatus.Failed;
    }
}
