using Content.Server._KS14.NPC.Doors;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Doors;

/// <summary>
///     Met when a door the owner believed it could open has just refused it, and it has not said so yet (see
///         <see cref="NpcDoorSystem.ReportRefused"/>). Pair with <c>AcknowledgeDoorRefusedOperator</c>, which clears it.
/// </summary>
public sealed partial class DoorRefusedPrecondition : HTNPrecondition
{
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;

    /// <summary>
    ///     A refusal older than this goes unsaid: the moment has passed.
    /// </summary>
    [DataField]
    public TimeSpan Within = TimeSpan.FromSeconds(5);

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _npcDoorSystem.HasPendingRefusal(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), Within);
    }
}
