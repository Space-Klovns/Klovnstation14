using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Targeting;

/// <summary>
///     The target's <see cref="KsNpcTargetPriorityComponent.Priority"/>, or <see cref="DefaultPriority"/> for one
///         without it, clamped to 0..1. Every target without the component scores the same, so adding this to a query
///         changes nothing among them: it only lifts those that have one above the rest.
/// </summary>
public sealed partial class KsTargetPriorityCon : UtilityConsideration
{
    [DataField]
    public float DefaultPriority = 0.1f;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        var priority = EntityManager.TryGetComponent<KsNpcTargetPriorityComponent>(targetUid, out var priorityComponent)
            ? priorityComponent.Priority
            : DefaultPriority;

        return Math.Clamp(priority, 0f, 1f);
    }
}
