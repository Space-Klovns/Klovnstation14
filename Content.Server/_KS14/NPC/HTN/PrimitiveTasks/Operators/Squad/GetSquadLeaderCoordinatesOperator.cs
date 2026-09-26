using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;

/// <summary>
///     Writes the owner's squad leader's coordinates to <see cref="Key"/>, or the owner's own coordinates if it
///         has no squad, so squad behaviour degrades to solo behaviour rather than failing.
/// </summary>
public sealed partial class GetSquadLeaderCoordinatesOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [DataField] public string Key = "SquadLeaderCoordinates";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var leaderUid = _npcSquadSystem.TryGetSquad(ownerUid, out var squadEntity) && squadEntity.Value.Comp.Leader is { } squadLeaderUid
            ? squadLeaderUid
            : ownerUid;

        return (true, new Dictionary<string, object>
        {
            { Key, _entityManager.GetComponent<TransformComponent>(leaderUid).Coordinates },
        });
    }
}
