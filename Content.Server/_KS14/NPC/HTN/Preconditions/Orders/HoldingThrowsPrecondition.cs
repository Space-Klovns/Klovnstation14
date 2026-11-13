using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Orders;

/// <summary>
///     Met when the owner's squad is holding its grenades for a hunt (<see cref="NpcSquadTacticsSystem.IsHoldingThrows"/>):
///         stacked up and about to go in together, a throw is the squad's to make, not one member's.
/// </summary>
public sealed partial class HoldingThrowsPrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        return _npcSquadTacticsSystem.IsHoldingThrows(blackboard.GetOwner());
    }
}
