using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met when the owner is in a squad, or, with <see cref="LeaderOnly"/>, when it leads one. With
///         <see cref="OrAlone"/>, also met when it is in no squad at all. <c>invert</c> flips the whole answer, so
///         <c>orAlone</c> with <c>invert</c> is met only by a squad member that does not match: not the leader with
///         <c>leaderOnly</c>, any member without.
/// </summary>
public sealed partial class InSquadPrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [DataField] public bool LeaderOnly;

    /// <summary>
    ///     Also met when the owner is in no squad. <c>{ leaderOnly: true, orAlone: true }</c> is "the leader, or on its
    ///         own": one voice per squad.
    /// </summary>
    [DataField] public bool OrAlone;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        var ownerUid = blackboard.GetOwner();

        var inSquad = _npcSquadSystem.TryGetSquad(ownerUid, out _);

        if (OrAlone && !inSquad)
            return true;

        return LeaderOnly
            ? _npcSquadSystem.IsLeader(ownerUid)
            : inSquad;
    }
}
