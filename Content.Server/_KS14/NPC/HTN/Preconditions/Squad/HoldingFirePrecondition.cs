using Content.Server._KS14.NPC.HTN;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Squad;

/// <summary>
///     Met while the owner has been holding fire for a friend in its way, where it stands, for long enough to want
///         somewhere else to shoot from. See <see cref="NpcSquadFireLaneSystem"/>.
/// </summary>
public sealed partial class HoldingFirePrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        return _npcSquadFireLaneSystem.IsHoldingFire(blackboard.GetOwner());
    }
}
