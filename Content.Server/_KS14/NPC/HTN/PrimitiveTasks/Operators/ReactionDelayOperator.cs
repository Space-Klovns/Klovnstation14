using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.HTN.Preconditions;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Fails until the owner has had <see cref="TargetKey"/> in sight for its reaction time (see
///         <see cref="NpcReactionTimeSystem"/>), so a freshly spotted target is not engaged instantly. An alert
///         owner reacts at once: one carrying <see cref="AlertMarker"/>, or whose squad is fighting.
/// </summary>
/// <remarks>
///     Put it straight after the task that picks the target, before anything that commits to engaging it.
/// </remarks>
public sealed partial class ReactionDelayOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcReactionTimeSystem _npcReactionTimeSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [DataField] public string TargetKey = "Target";

    /// <summary>
    ///     A virtual marker meaning the owner is already in combat, and so alert. Null to ignore markers.
    /// </summary>
    [DataField] public string? AlertMarker = "OpInCombat";

    /// <summary>
    ///     How recently the squad must have had contact for the owner to count as alert.
    /// </summary>
    [DataField] public TimeSpan SquadAlertWindow = TimeSpan.FromSeconds(20);

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager))
            return (false, null);

        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var alert = AlertMarker is not null && HasVirtualMarkerPrecondition.HasMarker(blackboard, AlertMarker, _entityManager) ||
            _npcSquadSystem.IsEngaged(ownerUid, SquadAlertWindow);

        return (_npcReactionTimeSystem.TrySeeAndReact(ownerUid, targetUid, alert), null);
    }
}
