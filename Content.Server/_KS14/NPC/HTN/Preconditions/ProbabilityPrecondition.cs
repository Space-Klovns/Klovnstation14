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
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    /// <summary>
    ///     Chance, from 0 to 100.
    /// </summary>
    [DataField]
    public float Probability = 100f;

    /// <summary>
    ///     A blackboard key holding the chance in place of <see cref="Probability"/>, if the NPC has it, for one that varies
    ///         from NPC to NPC.
    /// </summary>
    [DataField]
    public string? ProbabilityKey;

    /// <summary>
    ///     A meter on the NPC, such as anger, each point of which adds a percent to the chance.
    /// </summary>
    [DataField]
    public ProtoId<NpcMeterPrototype>? Meter;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var probability = blackboard.GetFloatOr(ProbabilityKey, Probability, _entityManager);

        if (Meter is { } meter)
            probability += _npcMeterSystem.GetValue(blackboard.GetOwner(), meter);

        return _random.Next(0, 100) < probability;
    }
}
