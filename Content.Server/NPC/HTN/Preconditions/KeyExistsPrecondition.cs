namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Checks for the presence of the value by the specified <see cref="KeyExistsPrecondition.Key"/> in the <see cref="NPCBlackboard"/>.
/// Returns true if there is a value.
/// </summary>
public sealed partial class KeyExistsPrecondition : HTNPrecondition
{
    [DataField(required: true), ViewVariables]
    public string Key = string.Empty;

    [DataField] public bool Inverted = false; // KS14: ANK: added inverted

    [Dependency] private IEntityManager _entityManager = default!; // KS14

    public override bool IsMet(NPCBlackboard blackboard)
    {
        // KS14: ContainsKey(Key) -> the entity-aware one: a key naming a deleted entity is not there, as for the read after it
        return blackboard.ContainsKey(Key, _entityManager) ^ Inverted /* KS14: ANK: added inverted */;
    }
}
