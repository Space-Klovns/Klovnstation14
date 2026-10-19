using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met while the owner, or its squad, is still busy with a fight: the owner knows of a threat it has not dealt
///         with yet (<see cref="ThreatKey"/>), a squad member is attacking or had contact within
///         <see cref="Window"/>, the squad still has a recent threat to go to, or the owner's own
///         <see cref="CombatTimeKey"/> is within <see cref="Window"/>. Invert it to ask whether things are quiet.
/// </summary>
public sealed partial class SquadEngagedPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;

    [DataField] public TimeSpan Window = TimeSpan.FromSeconds(90);

    /// <summary>
    ///     The owner's own time of last combat, a <see cref="TimeSpan"/> on the blackboard. Covers NPCs with no squad.
    /// </summary>
    [DataField] public string CombatTimeKey = "TimeOfLastCombat";

    /// <summary>
    ///     Coordinates of a threat the owner knows of and has not yet dealt with - a disturbance it just heard,
    ///         say. Present means engaged: it is set before anything else records the fight, and cleared once the
    ///         threat has been looked into.
    /// </summary>
    [DataField] public string ThreatKey = "LastKnownThreatCoordinates";

    [DataField] public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var engaged = blackboard.ContainsKey(ThreatKey) ||
            _npcSquadSystem.IsEngaged(ownerUid, Window) ||
            _npcSquadCoverSystem.HasThreatObjective(ownerUid) ||
            blackboard.TryGetValue<TimeSpan>(CombatTimeKey, out var combatTime, _entityManager) &&
            _gameTiming.CurTime - combatTime <= Window;

        return engaged != Invert;
    }
}
