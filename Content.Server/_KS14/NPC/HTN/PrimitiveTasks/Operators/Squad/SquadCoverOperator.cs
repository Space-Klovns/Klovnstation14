using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;

/// <summary>
///     Picks the owner's spot in its squad's room-cover plan (see <see cref="NpcSquadCoverSystem"/>): where to
///         stand, and which way to face to cover its threshold. Fails if the owner has no squad or the squad is
///         not in anything recognisable as a room, so a following branch can fall back to other positions.
/// </summary>
public sealed partial class SquadCoverOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;

    [DataField] public string KeyCoordinates = "CoverTargetCoordinates";

    /// <summary>
    ///     Written as an <see cref="Angle"/> for <c>RotateToTargetOperator</c>.
    /// </summary>
    [DataField] public string FacingKey = "CoverFacing";

    /// <summary>
    ///     If this coordinates key is set, it is passed on to the squad as the latest threat position first, so
    ///         the thresholds facing it are covered before the others. Passed on as a repeat
    ///         (<see cref="NpcSquadSystem.RepeatThreat"/>): whatever set it reported it fresh when it did.
    /// </summary>
    [DataField] public string? ThreatKey;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (ThreatKey is not null &&
            blackboard.TryGetValue<EntityCoordinates>(ThreatKey, out var threatCoordinates, _entityManager))
            _npcSquadSystem.RepeatThreat(ownerUid, threatCoordinates);

        if (!_npcSquadCoverSystem.TryGetAssignment(ownerUid, out var assignment))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { KeyCoordinates, assignment.Coordinates },
            { FacingKey, assignment.Facing },
        });
    }
}
