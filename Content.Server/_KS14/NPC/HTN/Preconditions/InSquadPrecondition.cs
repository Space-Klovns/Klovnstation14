using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met when the owner is in a squad, or, with <see cref="LeaderOnly"/>, when it leads one.
/// </summary>
public sealed partial class InSquadPrecondition : HTNPrecondition
{
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [DataField] public bool LeaderOnly;

    [DataField] public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var met = LeaderOnly
            ? _npcSquadSystem.IsLeader(ownerUid)
            : _npcSquadSystem.TryGetSquad(ownerUid, out _);

        return met != Invert;
    }
}
