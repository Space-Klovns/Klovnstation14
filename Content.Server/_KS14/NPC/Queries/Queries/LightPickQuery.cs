using Content.Server._KS14.NPC.Lights;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Queries;

namespace Content.Server._KS14.NPC.Queries.Queries;

/// <summary>
///     The light the owner has picked to shoot out, if it has one (<see cref="NpcLightShooterSystem"/>).
/// </summary>
public sealed partial class LightPickQuery : UtilityQuery
{
    [Dependency] private NpcLightShooterSystem _npcLightShooterSystem = default!;

    public override void AddEntities(NPCBlackboard blackboard, EntityUid ownerUid, HashSet<EntityUid> entities)
    {
        if (_npcLightShooterSystem.TryGetPick(ownerUid, out var lightUid))
            entities.Add(lightUid);
    }
}
