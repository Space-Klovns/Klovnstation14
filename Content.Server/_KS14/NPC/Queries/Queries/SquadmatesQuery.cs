using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Queries;

namespace Content.Server._KS14.NPC.Queries.Queries;

/// <summary>
///     The owner's squadmates, those that are down included, and never the owner itself.
/// </summary>
public sealed partial class SquadmatesQuery : UtilityQuery
{
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    public override void AddEntities(NPCBlackboard blackboard, EntityUid ownerUid, HashSet<EntityUid> entities)
    {
        _npcSquadSystem.GetSquadmates(ownerUid, entities);
    }
}
