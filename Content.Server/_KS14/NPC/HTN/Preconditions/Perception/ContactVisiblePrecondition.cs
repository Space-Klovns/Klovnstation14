using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Perception;

/// <summary>
///     Met while the owner can see the entity at <see cref="TargetKey"/> (see <see cref="NpcPerceptionSystem"/>).
///         Put it on a task that should stop the moment its target goes out of sight, with
///         <c>recheckPreconditions: true</c> so it is checked while the task runs and not only when it is planned.
/// </summary>
public sealed partial class ContactVisiblePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [DataField]
    public string TargetKey = "Target";

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager))
            return false;

        return _npcPerceptionSystem.IsVisible(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), targetUid);
    }
}
