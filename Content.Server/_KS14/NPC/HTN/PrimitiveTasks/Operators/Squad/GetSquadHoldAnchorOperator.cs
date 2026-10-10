using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;

/// <summary>
///     Writes where the owner should hold when there is no room to cover. In a squad, that is the squad's anchor
///         (see <see cref="NpcSquadCoverSystem.GetHoldAnchor"/>): the threat it is going to, otherwise its leader.
///         On its own, it is the threat the owner knows of at <see cref="ThreatKey"/>, otherwise where it stands.
/// </summary>
public sealed partial class GetSquadHoldAnchorOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [DataField] public string Key = "SquadHoldAnchorCoordinates";

    /// <summary>
    ///     Where a squadless owner goes instead of staying put, if it knows of a threat.
    /// </summary>
    [DataField] public string ThreatKey = "LastKnownThreatCoordinates";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var anchor = !_npcSquadSystem.TryGetSquad(ownerUid, out _) &&
            blackboard.TryGetValue<EntityCoordinates>(ThreatKey, out var threatCoordinates, _entityManager)
                ? threatCoordinates
                : _npcSquadCoverSystem.GetHoldAnchor(ownerUid);

        return (true, new Dictionary<string, object>
        {
            { Key, anchor },
        });
    }
}
