using Content.Server._KS14.NPC.Firefighting;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Firefighting;

/// <summary>
///     Sprays the chemical fire tiles in the NPC's reach until none of them needs it. See
///         <see cref="KsNpcFirefightingSystem.UpdateSpraying"/>.
/// </summary>
public sealed partial class KsSprayChemicalFireOperator : HTNOperator
{
    [Dependency] private KsNpcFirefightingSystem _firefightingSystem = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _firefightingSystem.UpdateSpraying(owner) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }
}
