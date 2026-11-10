using Content.Server._KS14.NPC.AttackPatterns;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met while the owner is part way through a ranged attack pattern (<see cref="NpcCombatRangedPatternSystem"/>),
///         or, inverted, while it isn't.
/// </summary>
public sealed partial class RangedAttackActivePrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcCombatRangedPatternSystem _npcCombatRangedPatternSystem = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetOwner();
        return _npcCombatRangedPatternSystem.IsAttackActive(owner);
    }
}
