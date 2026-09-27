using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;

/// <summary>
///     Tells the owner's squad it is in contact, with a hostile at <see cref="CoordinatesKey"/>: the rest of the
///         squad becomes alert, and does not stand down while the contact is recent.
/// </summary>
/// <remarks>
///     Reports when planned, like <see cref="SquadCoverOperator"/>: a plan through here means a hostile is in
///         sight, whether or not that plan is the one that ends up running.
/// </remarks>
public sealed partial class ReportSquadContactOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [DataField] public string CoordinatesKey = "TargetCoordinates";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (blackboard.TryGetValue<EntityCoordinates>(CoordinatesKey, out var coordinates, _entityManager))
            _npcSquadSystem.ReportContact(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), coordinates);

        return (true, null);
    }
}
