using Content.Server._KS14.NPC.AttackPatterns;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met while the owner is part way through a ranged attack pattern (<see cref="NpcCombatRangedPatternSystem"/>),
///         or, inverted, while it isn't.
/// </summary>
public sealed partial class RangedAttackActivePrecondition : HTNPrecondition
{
    [Dependency] private NpcCombatRangedPatternSystem _npcCombatRangedPatternSystem = default!;

    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _npcCombatRangedPatternSystem.IsAttackActive(owner) ^ Invert;
    }
}
