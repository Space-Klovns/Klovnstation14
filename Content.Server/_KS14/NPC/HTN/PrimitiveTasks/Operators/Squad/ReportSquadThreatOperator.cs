using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;

/// <summary>
///     Tells the owner's squad about a threat the owner has just learned of, at <see cref="CoordinatesKey"/>: the
///         squad goes to it, and does not stand down while it is recent. With <see cref="Contact"/>, the owner has
///         the hostile in its sights, which also makes the rest of the squad alert.
/// </summary>
/// <remarks>
///     Put it where the information arrives - the sighting, the sensor branch - not somewhere that runs again and
///         again on old information, since every report counts as fresh. Reports when planned, like
///         <see cref="SquadCoverOperator"/>: a plan through here means the information is there, whether or not
///         that plan is the one that ends up running.
/// </remarks>
public sealed partial class ReportSquadThreatOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [DataField] public string CoordinatesKey = "TargetCoordinates";

    /// <summary>
    ///     Whether the owner can see the hostile, rather than only knowing where one is.
    /// </summary>
    [DataField] public bool Contact = true;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(CoordinatesKey, out var coordinates, _entityManager))
            return (true, null);

        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (Contact)
            _npcSquadSystem.ReportContact(ownerUid, coordinates);
        else
            _npcSquadSystem.ReportThreat(ownerUid, coordinates);

        return (true, null);
    }
}
