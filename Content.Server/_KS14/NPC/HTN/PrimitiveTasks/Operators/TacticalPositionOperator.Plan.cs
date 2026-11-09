using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class TacticalPositionOperator
{
    /// <inheritdoc/>
    public HTNPlanState ShutdownState => HTNPlanState.TaskFinished;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetOwner();

        // Phase 1: markers-first. Reuses the existing marker utilityQuery prototype unmodified so mapper
        // intent always wins when a marker scores > 0; no claim is registered for marker picks, since
        // anti-stacking for the marker pool is out of scope (mappers already space markers out).
        if (MarkerPrototype is not null)
        {
            var markerResult = _npcUtilitySystem.GetEntities(blackboard, MarkerPrototype);
            var markerTarget = markerResult.GetHighest();

            if (markerTarget.IsValid())
            {
                return (true, new Dictionary<string, object>
                {
                    { Key, markerTarget },
                    { KeyCoordinates, new EntityCoordinates(markerTarget, Vector2.Zero) },
                });
            }
        }

        // Phase 2: dynamic fallback via the pathfinding poly graph.
        if (!blackboard.TryGetValue<EntityCoordinates>(ReferenceCoordinatesKey, out var reference, _entityManager))
            return NoSpot();

        var maxRange = GetKeyedValue(blackboard, MaxRangeKey, MaxRange);
        var maxCandidates = (int) GetKeyedValue(blackboard, MaxCandidatesKey, MaxCandidates);

        var candidates = await _pathfindingSystem.GetTacticalCandidates(
            owner, reference, maxRange, maxCandidates, cancelToken, _pathfindingSystem.GetFlags(blackboard));

        if (candidates.Count == 0)
            return NoSpot();

        // Lazy: the debug candidate list is only allocated/populated while a debug overlay is actually
        // subscribed - see NpcTacticalPositionDebugSystem. Every NPC replanning this task every tick would
        // otherwise pay for a debug payload nobody is looking at.
        var debugCandidates = _npcTacticalPositionDebugSystem.IsTracking(owner)
            ? new List<TacticalPositionDebugCandidate>(candidates.Count)
            : null;

        // Everything from here on runs without awaiting anything, so the per-plan state is safe to write.
        _plan.ExposureProbes.Clear();
        if (ExposureReferenceCoordinatesKey is not null &&
            blackboard.TryGetValue<EntityCoordinates>(ExposureReferenceCoordinatesKey, out var exposureReference, _entityManager))
            _npcExposureSystem.GetApproachProbes(owner, exposureReference, ExposureReach, ExposureProbes, _plan.ExposureProbes);

        // Not even one candidate's worth of this tick's budget left: the search waits, or does without.
        if (_plan.ExposureProbes.Count > 0 && !_npcExposureSystem.CanAfford(_plan.ExposureProbes.Count))
        {
            if (DeferWhenOverBudget)
                return (false, null);

            _plan.ExposureProbes.Clear();
        }

        SetUpPlanState(blackboard, owner);

        var considerationCount = GetConsiderationCount();

        // Exposure is by far the dearest consideration, and can only lower a score. So every candidate is scored on the
        //      rest first, and exposure is then weighed best first, until no candidate left could beat the best so far
        //      even unexposed - or this tick's budget runs out, when the best so far stands. Usually only the top few
        //      candidates are weighed at all.
        _plan.ScoredCandidates.Clear();
        foreach (var candidate in candidates)
        {
            var score = ScoreCandidate(blackboard, owner, candidate, reference, considerationCount);
            if (score > 0f)
                _plan.ScoredCandidates.Add((candidate, score));
            else
                debugCandidates?.Add(new TacticalPositionDebugCandidate(_entityManager.GetNetCoordinates(candidate.Coordinates), 0f));
        }

        var best = PickBest(considerationCount, debugCandidates);

        if (best is null)
        {
            SendDebugFrame(owner, debugCandidates, null);
            return NoSpot();
        }

        _npcTacticalPositionClaimSystem.Claim(owner, best.Coordinates, GetClaimTtl(blackboard), ClaimClearanceRadius);

        SendDebugFrame(owner, debugCandidates, best.Coordinates);

        return (true, new Dictionary<string, object>
        {
            { KeyCoordinates, best.Coordinates },
        });
    }

    /// <summary>
    ///     Reads what stays the same for every candidate of this plan, once. Nothing writes to the owner's blackboard while
    ///         the candidates are scored, so none of it can change in between.
    /// </summary>
    private void SetUpPlanState(NPCBlackboard blackboard, EntityUid owner)
    {
        _plan.LosRadius = GetKeyedValue(blackboard, LosRadiusKey, LosRadius);
        _plan.CoverOnly = CoverOnlyKey is not null && blackboard.TryGetValue<bool>(CoverOnlyKey, out var coverOnly, _entityManager) && coverOnly;

        _plan.LosReference = LosReferenceCoordinatesKey is not null &&
            blackboard.TryGetValue<EntityCoordinates>(LosReferenceCoordinatesKey, out var losReference, _entityManager)
                ? _transformSystem.ToMapCoordinates(losReference)
                : null;

        _plan.VisionRadius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(_entityManager), _entityManager);
        _plan.AvoidFireLanes = AvoidFireLanes && _npcSquadFireLaneSystem.Enabled;

        _plan.ScoreCoverStep = false;
        if ((CoverStepCurve is not null || _plan.CoverOnly) && _plan.LosReference is { } coverStepReference)
        {
            _plan.ScoreCoverStep = true;
            // Past the candidates' own reach by a step, so every tile next to one is in range.
            _npcLineOfSightSystem.BuildSightField(coverStepReference, _plan.LosRadius + 2f, _plan.CoverStepSightField);
        }

        if (LineOfFire)
            _plan.ShotMask = _npcLineOfFireSystem.GetShotMask(owner);

        GatherCrossfire(blackboard, owner);
    }

    /// <summary>
    ///     The best of the scored candidates once exposure is weighed, best first, or null if none is worth it.
    /// </summary>
    private PathPoly? PickBest(int considerationCount, List<TacticalPositionDebugCandidate>? debugCandidates)
    {
        var exposureProbeCount = _plan.ExposureProbes.Count;

        if (exposureProbeCount > 0)
            _plan.ScoredCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        PathPoly? best = null;
        var bestScore = 0f;

        var weighing = exposureProbeCount > 0;
        foreach (var (candidate, scoreWithoutExposure) in _plan.ScoredCandidates)
        {
            var score = scoreWithoutExposure;
            var weighed = false;

            if (weighing)
            {
                if (scoreWithoutExposure <= bestScore || !_npcExposureSystem.CanAfford(exposureProbeCount))
                {
                    weighing = false; // nothing further down can win, or there is no budget left to tell
                }
                else
                {
                    score *= GetExposureFactor(candidate, considerationCount);
                    weighed = true;
                }
            }

            // Candidates never weighed show their score without exposure: the most they could have scored.
            debugCandidates?.Add(new TacticalPositionDebugCandidate(_entityManager.GetNetCoordinates(candidate.Coordinates), score));

            // An unweighed score cannot be compared with weighed ones.
            if (exposureProbeCount > 0 && !weighed)
                continue;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    ///     Nowhere to go: the plan fails, or succeeds with no target written, as <see cref="InvalidatePlanOnNoHighest"/> says.
    /// </summary>
    private (bool Valid, Dictionary<string, object>? Effects) NoSpot()
    {
        return (!InvalidatePlanOnNoHighest, new Dictionary<string, object>());
    }

    /// <summary>
    ///     Sends what was scored to whoever is watching. Nothing to do when <paramref name="debugCandidates"/> is null,
    ///         which it is while no overlay is tracking <paramref name="owner"/>.
    /// </summary>
    private void SendDebugFrame(EntityUid owner, List<TacticalPositionDebugCandidate>? debugCandidates, EntityCoordinates? chosen)
    {
        if (debugCandidates is null)
            return;

        _npcTacticalPositionDebugSystem.SendDebugFrame(owner, debugCandidates, chosen,
            _npcTacticalPositionClaimSystem.GetAllClaimsForDebug().ConvertAll(ToDebugClaim));
    }

    /// <summary>
    ///     The blackboard float at <paramref name="key"/> if the owner has it, or <paramref name="fallback"/>.
    /// </summary>
    private float GetKeyedValue(NPCBlackboard blackboard, string? key, float fallback)
    {
        return blackboard.GetFloatOr(key, fallback, _entityManager);
    }

    private TacticalPositionDebugClaim ToDebugClaim((EntityCoordinates Coordinates, float ClearanceRadius) claim)
    {
        return new TacticalPositionDebugClaim(_entityManager.GetNetCoordinates(claim.Coordinates), claim.ClearanceRadius);
    }

    private TimeSpan GetClaimTtl(NPCBlackboard blackboard)
    {
        var duration = blackboard.GetValueOrDefault<float>(ClaimDurationKey, _entityManager);
        return TimeSpan.FromSeconds(duration + ClaimTtlBuffer);
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        ReleaseClaim(blackboard);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        ReleaseClaim(blackboard);
    }

    private void ReleaseClaim(NPCBlackboard blackboard)
    {
        _npcTacticalPositionClaimSystem.ReleaseClaim(blackboard.GetOwner());
    }
}
