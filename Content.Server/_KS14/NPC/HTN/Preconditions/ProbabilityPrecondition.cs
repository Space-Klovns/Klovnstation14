using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met with a chance of <see cref="Probability"/> percent, plus the value of <see cref="Meter"/> if set.
/// </summary>
public sealed partial class ProbabilityPrecondition : HTNPrecondition
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    /// <summary>
    ///     Chance, from 0 to 100.
    /// </summary>
    [DataField]
    public float Probability = 100f;

    /// <summary>
    ///     A meter on the NPC, such as anger, each point of which adds a percent to the chance.
    /// </summary>
    [DataField]
    public ProtoId<NpcMeterPrototype>? Meter;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var probability = Probability;
        if (Meter is { } meter)
            probability += _npcMeterSystem.GetValue(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), meter);

        return _random.Next(0, 100) < probability;
    }
}
