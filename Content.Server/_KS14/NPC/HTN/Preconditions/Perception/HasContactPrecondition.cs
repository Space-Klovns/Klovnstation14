using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Perception;

/// <summary>
///     Met when the owner knows of a hostile in any of <see cref="States"/> (see <see cref="NpcPerceptionSystem"/>),
///         last seen or called out no longer than <see cref="MaxAge"/> ago if set.
/// </summary>
public sealed partial class HasContactPrecondition : HTNPrecondition
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [DataField(required: true)]
    public List<NpcContactState> States = new();

    [DataField]
    public TimeSpan? MaxAge;

    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _npcPerceptionSystem.HasContact(ownerUid, States, MaxAge) != Invert;
    }
}
