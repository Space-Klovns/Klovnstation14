namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Checks if there is no value at the specified  <see cref="KeyNotExistsPrecondition.Key"/> in the <see cref="NPCBlackboard"/>.
/// Returns true if there is no value.
/// </summary>
public sealed partial class KeyNotExistsPrecondition : HTNPrecondition
{
    [DataField(required: true), ViewVariables]
    public string Key = string.Empty;

    [Dependency] private IEntityManager _entityManager = default!; // KS14

    public override bool IsMet(NPCBlackboard blackboard)
    {
        // KS14: ContainsKey(Key) -> the entity-aware one: a key naming a deleted entity is not there, as for KeyExistsPrecondition
        return !blackboard.ContainsKey(Key, _entityManager);
    }
}
