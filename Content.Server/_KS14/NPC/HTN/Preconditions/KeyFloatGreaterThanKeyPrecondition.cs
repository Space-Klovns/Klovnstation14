using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met when the float at <see cref="Key"/> is greater than the float at <see cref="ValueKey"/> - a threshold that
///         differs between NPCs, set on each one's blackboard. Like <c>KeyFloatGreaterPrecondition</c>, which only takes
///         a fixed value. Not met if either is missing.
/// </summary>
public sealed partial class KeyFloatGreaterThanKeyPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;

    [DataField(required: true)]
    public string Key = string.Empty;

    [DataField(required: true)]
    public string ValueKey = string.Empty;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return blackboard.TryGetValue<float>(Key, out var value, _entityManager) &&
            blackboard.TryGetValue<float>(ValueKey, out var threshold, _entityManager) &&
            value > threshold;
    }
}
