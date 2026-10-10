using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Queries;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.Queries.Queries;

/// <summary>
///     The hostiles the owner knows of, from its <see cref="NpcPerceptionComponent"/>, in any of
///         <see cref="States"/>. The perception system has already done the looking - line of sight, light,
///         containers - so this does no lookup of its own.
/// </summary>
public sealed partial class PerceivedContactsQuery : UtilityQuery
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [DataField]
    public List<NpcContactState> States = new() { NpcContactState.Visible };

    /// <summary>
    ///     Only hostiles the owner has had time to react to: one it has only just spotted is not a candidate yet.
    /// </summary>
    [DataField]
    public bool ReactedOnly;

    public override void AddEntities(NPCBlackboard blackboard, EntityUid ownerUid, HashSet<EntityUid> entities)
    {
        _npcPerceptionSystem.GetContacts(ownerUid, States, ReactedOnly, entities);
    }
}
