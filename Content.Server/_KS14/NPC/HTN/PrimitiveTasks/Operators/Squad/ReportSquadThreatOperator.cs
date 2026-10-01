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
///     Put it where the information arrives - the sensor branch - not somewhere that runs again and again on old
///         information, since every report counts as fresh. Sightings need no operator: NpcPerceptionSystem reports
///         those itself. Reports when it runs, not when planned: planning has no side effects (CONTRIBUTING.md).
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

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (!blackboard.TryGetValue<EntityCoordinates>(CoordinatesKey, out var coordinates, _entityManager))
            return;

        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (Contact)
            _npcSquadSystem.ReportContact(ownerUid, coordinates);
        else
            _npcSquadSystem.ReportThreat(ownerUid, coordinates);
    }
}
