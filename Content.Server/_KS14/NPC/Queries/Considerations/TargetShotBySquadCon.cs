using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if a squadmate of the owner is shooting the target, 0 if not.
/// </summary>
public sealed partial class TargetShotBySquadCon : UtilityConsideration
{
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [Dependency] private EntityQuery<NPCRangedCombatComponent> _rangedCombatQuery = default!;

    private readonly HashSet<EntityUid> _squadmates = new();

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        _squadmates.Clear();
        _npcSquadSystem.GetSquadmates(ownerUid, _squadmates);

        foreach (var squadmateUid in _squadmates)
        {
            if (_rangedCombatQuery.TryComp(squadmateUid, out var rangedCombatComponent) &&
                rangedCombatComponent.Target == targetUid)
                return 1f;
        }

        return 0f;
    }
}
