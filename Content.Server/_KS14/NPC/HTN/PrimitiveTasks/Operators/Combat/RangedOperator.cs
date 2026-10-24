using Content.Server._KS14.NPC.AttackPatterns;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
///     Starts a ranged attack, optionally with a speech line. Finishes as soon as the attack
///         starts, and fails if it can't (on cooldown, or already used up). See <see cref="NpcCombatRangedPatternSystem"/>.
/// </summary>
public sealed partial class RangedOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcCombatRangedPatternSystem _npcCombatRangedPatternSystem = default!;

    [DataField(required: true)]
    public EntProtoId<NpcRangedAttackPatternComponent> Attack;

    /// <summary>
    ///     Line spoken when the attack starts.
    /// </summary>
    [DataField]
    public string? Speech;

    /// <summary>
    ///     If set, a blackboard key set to true once the attack has fired, after which this never fires it again. For
    ///         attacks made once per fight, like a final one.
    /// </summary>
    [DataField]
    public string? ExecutedFlagKey;

    /// <summary>
    ///     Blackboard key of the entity to aim at. Optional; attacks that need a target don't fire without one.
    /// </summary>
    [DataField]
    public string TargetKey = "Target";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (ExecutedFlagKey != null && blackboard.GetValueOrDefault<bool>(ExecutedFlagKey, _entityManager))
            return HTNOperatorStatus.Failed;

        EntityUid? targetUid = blackboard.TryGetValue<EntityUid>(TargetKey, out var foundTargetUid, _entityManager)
            ? foundTargetUid
            : null;

        if (!_npcCombatRangedPatternSystem.TryStartAttack(owner, Attack, targetUid, speech: Speech))
            return HTNOperatorStatus.Failed;

        if (ExecutedFlagKey != null)
            blackboard.SetValue(ExecutedFlagKey, true);

        return HTNOperatorStatus.Finished;
    }
}
