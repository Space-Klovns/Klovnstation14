using Content.Server.NPC;
using Robust.Shared.Random;
using Content.Server.NPC.HTN.Preconditions;
using Content.Server._KS14.NPC.Components;

namespace Content.Server._KS14.NPC.Preconditions;

public sealed partial class ProbabilityPrecondition : HTNPrecondition
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IEntityManager _entMan = default!;

    [DataField("probability")] public float Probability = 100f;
    [DataField("useAngerModifier")] public bool UseAngerModifier = false;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var roll = _random.Next(0, 100);
        var finalProbability = Probability;

        if (UseAngerModifier)
        {
            var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
            if (_entMan.TryGetComponent<NpcAngerModifierComponent>(owner, out var anger))
            {
                finalProbability += anger.AngerModifier;
            }
        }

        return roll < finalProbability;
    }
}
