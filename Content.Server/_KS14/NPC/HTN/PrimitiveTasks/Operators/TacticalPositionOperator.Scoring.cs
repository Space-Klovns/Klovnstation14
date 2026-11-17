using Content.Server.NPC;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Queries.Curves;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class TacticalPositionOperator
{
    /// <summary>
    ///     How many considerations a candidate is scored on, which <see cref="NPCUtilitySystem.GetAdjustedScore"/>
    ///         needs to make scores with different numbers of them comparable.
    ///     Keep in step with the guards in <see cref="ScoreCandidate"/> (and <see cref="GetExposureFactor"/>): a
    ///         consideration is counted here exactly when it is applied there. The cover step is counted on its curve
    ///         alone, as it is only applied as a consideration when there is one.
    /// </summary>
    private int GetConsiderationCount()
    {
        return 1 // distance, always applied
            + (_plan.AvoidFireLanes ? 1 : 0)
            + (LosReferenceCoordinatesKey is not null ? 1 : 0)
            + (CoverStepCurve is not null && LosReferenceCoordinatesKey is not null ? 1 : 0)
            + (WallCurve is not null ? 1 : 0)
            + (FovReferenceCoordinatesKey is not null ? 1 : 0)
            + (RandomProbability > 0f ? 1 : 0)
            + (KillZoneAvoidance > 0f ? 1 : 0)
            + (_plan.CrossfireDirections.Count > 0 ? 1 : 0)
            + (_plan.ExposureProbes.Count > 0 ? 1 : 0)
            + 1; // claim penalty, always applied
    }

    /// <summary>
    ///     Multiplies <paramref name="score"/> by <paramref name="factor"/>, adjusted for the number of considerations.
    ///         False if that leaves nothing, when scoring can stop.
    /// </summary>
    private bool TryApply(ref float score, float factor, int considerationCount)
    {
        score *= _npcUtilitySystem.GetAdjustedScore(factor, considerationCount);
        return score > 0f;
    }

    /// <summary>
    ///     As <see cref="TryApply"/>, for a <paramref name="raw"/> value run through <paramref name="curve"/> first.
    /// </summary>
    private bool TryApplyCurve(ref float score, IUtilityCurve curve, float raw, int considerationCount)
    {
        return TryApply(ref score, _npcUtilitySystem.GetScore(curve, raw), considerationCount);
    }

    /// <summary>
    ///     What exposure multiplies <paramref name="candidate"/>'s score by, at most 1. Spends a check per probe from this
    ///         tick's budget.
    /// </summary>
    private float GetExposureFactor(PathPoly candidate, int considerationCount)
    {
        var hiddenRaw = 1f - _npcExposureSystem.GetExposure(_transformSystem.ToMapCoordinates(candidate.Coordinates), _plan.ExposureProbes, ExposureRadius);
        return _npcUtilitySystem.GetAdjustedScore(_npcUtilitySystem.GetScore(ExposureCurve, hiddenRaw), considerationCount);
    }

    /// <summary>
    ///     <paramref name="candidate"/>'s score on everything but exposure, which <see cref="Plan"/> weighs separately.
    ///         Considerations are applied cheapest first, and scoring stops at the first that leaves nothing: the order
    ///         matters, as the random jitter only draws from the RNG if it is reached.
    /// </summary>
    private float ScoreCandidate(NPCBlackboard blackboard, EntityUid owner, PathPoly candidate, EntityCoordinates reference, int considerationCount)
    {
        var score = 1f;

        if (!candidate.Coordinates.TryDistance(_entityManager, _transformSystem, reference, out var distance))
            return 0f;

        var distanceRaw = Math.Clamp(distance / MathF.Max(_plan.VisionRadius, 0.01f), 0f, 1f);
        if (!TryApplyCurve(ref score, DistanceCurve, distanceRaw, considerationCount))
            return 0f;

        var candidateMapCoordinates = _transformSystem.ToMapCoordinates(candidate.Coordinates);

        if (_plan.LosReference is { } losMapCoordinates &&
            !TryApplyCurve(ref score, LosCurve, GetLineOfSightRaw(candidateMapCoordinates, losMapCoordinates), considerationCount))
            return 0f;

        if (_plan.ScoreCoverStep)
        {
            var hasCover = HasCoverAStepAway(candidate);
            if (_plan.CoverOnly && (!hasCover || !IsBesideSomething(candidate)))
                return 0f;

            if (CoverStepCurve is not null && !TryApplyCurve(ref score, CoverStepCurve, hasCover ? 1f : 0f, considerationCount))
                return 0f;
        }

        if (WallCurve is not null && !TryApplyCurve(ref score, WallCurve, GetWallRaw(candidate), considerationCount))
            return 0f;

        if (FovReferenceCoordinatesKey is not null &&
            blackboard.TryGetValue<EntityCoordinates>(FovReferenceCoordinatesKey, out var fovReference, _entityManager) &&
            !TryApplyCurve(ref score, FovCurve, EvaluateFov(owner, candidate.Coordinates, fovReference, FovAngle), considerationCount))
            return 0f;

        if (RandomProbability > 0f &&
            !TryApplyCurve(ref score, JitterCurve, _robustRandom.Prob(RandomProbability) ? 1f : 0f, considerationCount))
            return 0f;

        if (_plan.AvoidFireLanes)
        {
            var fireLanePenalty = _npcSquadFireLaneSystem.GetFireLanePenalty(owner, candidateMapCoordinates, _plan.LosReference);
            if (!TryApply(ref score, fireLanePenalty, considerationCount))
                return 0f;
        }

        if (KillZoneAvoidance > 0f)
        {
            var danger = _npcKillZoneSystem.GetDanger(owner, candidate.Coordinates);
            if (!TryApply(ref score, Math.Clamp(1f - KillZoneAvoidance * danger, 0f, 1f), considerationCount))
                return 0f;
        }

        if (_plan.CrossfireDirections.Count > 0 &&
            !TryApply(ref score, GetCrossfireFactor(candidateMapCoordinates), considerationCount))
            return 0f;

        if (ExclusiveClaims)
        {
            if (_npcTacticalPositionClaimSystem.GetClaimPenalty(candidate.Coordinates, ClaimClearanceRadius, ignoreClaimantUid: owner) < 1f)
                return 0f;
        }
        else
        {
            var claimPenalty = _npcTacticalPositionClaimSystem.GetClaimPenalty(candidate.Coordinates, ClaimClearanceRadius);
            TryApply(ref score, claimPenalty, considerationCount); // the last one: what is left is the score either way
        }

        return score;
    }

    /// <summary>
    ///     1 if <paramref name="candidate"/> sees <paramref name="losReference"/> (and, with <see cref="LineOfFire"/>,
    ///         could hit it from there), else 0.
    /// </summary>
    private float GetLineOfSightRaw(MapCoordinates candidate, MapCoordinates losReference)
    {
        var inLineOfSight = _npcLineOfSightSystem.InLineOfSight(candidate, losReference, _plan.LosRadius + 0.5f);
        if (inLineOfSight && LineOfFire)
            inLineOfSight = _npcLineOfFireSystem.IsClear(candidate, losReference, _plan.ShotMask);

        return inLineOfSight ? 1f : 0f;
    }
}
