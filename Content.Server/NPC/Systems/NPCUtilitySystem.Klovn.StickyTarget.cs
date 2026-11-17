using Content.Server.NPC.Queries;
using Content.Shared._KS14.LockOn;
using Content.Server.NPC.Components;

namespace Content.Server.NPC.Systems;

// Keeps a locking shooter on its current target. Partial so the upstream file stays minimal.
public sealed partial class NPCUtilitySystem
{
    [Dependency] private EntityQuery<KsTargetLockOnComponent> _stickyLockOnQuery = default!;
    [Dependency] private EntityQuery<NPCRangedCombatComponent> _stickyRangedQuery = default!;

    /// <summary>
    ///     For an owner with <see cref="KsTargetLockOnComponent.KeepTargetUntilLost"/>: if the blackboard's current
    ///         target is still among <paramref name="candidates"/>, still scores above zero on every consideration,
    ///         and is still in sight, it is the only result, however a rival now ranks. Re-ranking by distance every
    ///         replan would otherwise swap two alternately-closest targets before either lock could finish.
    ///     <para>
    ///         Not, though, a target it is holding fire on because its rounds cannot get there
    ///             (<see cref="NPCRangedCombatComponent.KsTargetInLineOfFire"/>), if another candidate now scores higher: a
    ///             turret watching a hostile behind glass turns to one in the open that is shooting it. Against another it
    ///             cannot reach either, it still keeps the one it has.
    ///     </para>
    /// </summary>
    private bool KsTryKeepCurrentTarget(
        NPCBlackboard blackboard,
        UtilityQueryPrototype query,
        HashSet<EntityUid> candidates,
        out UtilityResult result)
    {
        result = UtilityResult.Empty;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_stickyLockOnQuery.TryComp(owner, out var lockOnComponent) ||
            !lockOnComponent.KeepTargetUntilLost ||
            !blackboard.TryGetValue<EntityUid>("Target", out var currentTarget, EntityManager) ||
            !candidates.Contains(currentTarget))
            return false;

        // The shooter's own sight check of the fight, when there is one, knows glass and the like better than the
        //      occlusion check below.
        if (_stickyRangedQuery.TryComp(owner, out var rangedComponent) &&
            rangedComponent.Target == currentTarget &&
            !rangedComponent.TargetInLOS)
            return false;

        var radius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);

        if (!_examine.InRangeUnOccluded(owner, currentTarget, range: radius + 0.5f))
            return false;

        var score = KsScoreCandidate(blackboard, query, currentTarget, floor: 0f);
        if (score <= 0f)
            return false;

        // Holding fire on it because a round would not get there: kept only while nothing else scores higher
        if (rangedComponent is { KsHoldFireWhenBlocked: true, KsTargetInLineOfFire: false } &&
            rangedComponent.Target == currentTarget)
        {
            foreach (var candidate in candidates)
            {
                if (candidate != currentTarget && KsScoreCandidate(blackboard, query, candidate, floor: score) > score)
                    return false;
            }
        }

        result = new UtilityResult(new Dictionary<EntityUid, float> { { currentTarget, score } });
        return true;
    }

    /// <summary>
    ///     <paramref name="candidate"/>'s score on every consideration of <paramref name="query"/>, as
    ///         <see cref="GetEntities"/> works it out; 0 as soon as it can no longer beat <paramref name="floor"/>.
    /// </summary>
    private float KsScoreCandidate(NPCBlackboard blackboard, UtilityQueryPrototype query, EntityUid candidate, float floor)
    {
        var score = 1f;

        foreach (var consideration in query.Considerations)
        {
            var curveScore = GetScore(consideration.Curve, GetScore(blackboard, candidate, consideration));
            score *= GetAdjustedScore(curveScore, query.Considerations.Count);

            // The running score only goes down
            if (score <= floor)
                return 0f;
        }

        return score;
    }
}
