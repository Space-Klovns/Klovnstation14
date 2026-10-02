using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Examine;
using Content.Server._KS14.NPC.Exposure;
using Content.Server._KS14.NPC.KillZones;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Queries;
using Content.Server.NPC.Queries.Curves;
using Content.Server.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Picks a camping/retreat/advance destination for an NPC. Tries an existing mapper-placed marker
/// <see cref="UtilityQueryPrototype"/> first (<see cref="MarkerPrototype"/>) so hand-tuned spots keep priority;
/// if that yields nothing, falls back to a dynamic navmesh-based tactical position query: candidate points are
/// enumerated from the reachable poly graph around <see cref="ReferenceCoordinatesKey"/>
/// (<see cref="PathfindingSystem.GetTacticalCandidates"/>) and scored with the same utility-curve machinery
/// used for markers, plus a reservation-table penalty so concurrent NPCs don't converge on the same spot.
/// </summary>
public sealed partial class TacticalPositionOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private NPCUtilitySystem _npcUtilitySystem = default!;
    [Dependency] private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!;
    [Dependency] private NpcTacticalPositionClaimSystem _npcTacticalPositionClaimSystem = default!;
    [Dependency] private NpcKillZoneSystem _npcKillZoneSystem = default!;
    [Dependency] private NpcExposureSystem _npcExposureSystem = default!;
    [Dependency] private NpcTacticalPositionDebugSystem _npcTacticalPositionDebugSystem = default!;
    [Dependency] private ExamineSystem _examineSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    /// Marker-phase utility query to try first (e.g. an existing ComponentQuery against NpcCampingSpot).
    /// Null/omitted skips the marker phase entirely and always uses the dynamic algorithm.
    /// </summary>
    [DataField("markerProto")]
    public ProtoId<UtilityQueryPrototype>? MarkerPrototype;

    [DataField] public string Key = "TacticalTarget";

    [DataField("keyCoordinates")]
    public string KeyCoordinates = "TacticalTargetCoordinates";

    /// <summary>
    /// Blackboard coordinates the candidate flood originates from, and distance is scored against.
    /// </summary>
    [DataField(required: true)]
    public string ReferenceCoordinatesKey = string.Empty;

    [DataField] public float MaxRange = 15f;

    [DataField] public int MaxCandidates = 64;

    [DataField] public IUtilityCurve DistanceCurve = new PresetCurve { Preset = "KsTargetDistanceLessClose" };

    /// <summary>
    /// If set, adds an LOS consideration between the candidate and this coordinates key. Wrap
    /// <see cref="LosCurve"/> with an InverseBoolCurve in YAML to prefer concealment instead of visibility.
    /// </summary>
    [DataField] public string? LosReferenceCoordinatesKey;

    [DataField] public float LosRadius = 10f;

    [DataField] public IUtilityCurve LosCurve = new BoolCurve();

    /// <summary>
    /// If set, adds a directional-cone consideration (owner facing this coordinates key, is the candidate
    /// within Angle degrees of that direction).
    /// </summary>
    [DataField] public string? FovReferenceCoordinatesKey;

    [DataField] public float FovAngle = 65f;

    [DataField] public IUtilityCurve FovCurve = new BoolCurve();

    /// <summary>
    /// 0 disables the random-jitter consideration entirely.
    /// </summary>
    [DataField] public float RandomProbability;

    [DataField] public float ClaimClearanceRadius = 2.5f;

    /// <summary>
    ///     Whether to keep out of squadmates' lines of fire, and them out of ours - the line from a candidate to
    ///         <see cref="LosReferenceCoordinatesKey"/>. Only applies while <see cref="NpcSquadFireLaneSystem"/>
    ///         is enabled by cvar.
    /// </summary>
    [DataField] public bool AvoidFireLanes = true;

    /// <summary>
    ///     How much to shun kill zones - spots where the owner's own have recently gone down (see
    ///         <see cref="NpcKillZoneSystem"/>) - from 0, not at all, to 1, never stand at a zone's centre. A
    ///         candidate's score is scaled by <c>1 - this × danger</c>.
    /// </summary>
    [DataField] public float KillZoneAvoidance;

    /// <summary>
    ///     If set, scores how well hidden a candidate is from the threat at this key - not only from where it stands
    ///         (which <see cref="LosReferenceCoordinatesKey"/> covers), but from floor it could step to within
    ///         <see cref="ExposureReach"/> steps. A spot just round a corner is hidden from where the threat is, and
    ///         seen the moment it takes a step; this tells the two apart. See <see cref="NpcExposureSystem"/>.
    /// </summary>
    [DataField] public string? ExposureReferenceCoordinatesKey;

    /// <summary>
    ///     How many steps of the threat's approach count. See <see cref="ExposureReferenceCoordinatesKey"/>.
    /// </summary>
    [DataField] public int ExposureReach = 4;

    /// <summary>
    ///     How many places along the threat's approach are checked, its own position included. Each candidate costs a
    ///         line of sight check per probe.
    /// </summary>
    [DataField] public int ExposureProbes = 12;

    /// <summary>
    ///     How far a probe can see, in tiles.
    /// </summary>
    [DataField] public float ExposureRadius = 15f;

    /// <summary>
    ///     Curve over how hidden a candidate is: the share of probes that cannot see it, from 0 (seen from everywhere)
    ///         to 1 (from nowhere). Linear by default.
    /// </summary>
    [DataField] public IUtilityCurve ExposureCurve = new QuadraticCurve();

    /// <summary>
    ///     What to do when weighing exposure would go over this tick's budget for it, shared by every NPC (see
    ///         <see cref="NpcExposureSystem.CanAfford"/>). True: fail the plan, so whatever comes next plans instead
    ///         and this is tried again on a later replan - for a search that can wait, like a new spot to shoot from.
    ///         False: pick without weighing exposure - for one that cannot, like a retreat.
    /// </summary>
    [DataField] public bool DeferWhenOverBudget;

    /// <summary>
    ///     The threat's approach, worked out once per plan and scored against for every candidate. Scoring runs
    ///         without awaiting anything, so plans sharing this operator cannot interleave over it.
    /// </summary>
    private readonly List<MapCoordinates> _exposureProbes = new();

    /// <summary>
    ///     Candidates still in the running, with their score before exposure. Reused, as above.
    /// </summary>
    private readonly List<(PathPoly Candidate, float Score)> _scoredCandidates = new();

    /// <summary>
    /// Blackboard float key read at claim time to size the claim's TTL (e.g. CampingTime/AdvanceTime).
    /// </summary>
    [DataField] public string ClaimDurationKey = "CampingTime";

    [DataField] public float ClaimTtlBuffer = 5f;

    /// <summary>
    /// Mirrors <see cref="UtilityOperator.InvalidatePlanOnNoHighest"/>: if true, the plan is invalidated when
    /// neither the marker phase nor the dynamic phase find anything. Otherwise the plan succeeds with no
    /// target written to the blackboard.
    /// </summary>
    [DataField] public bool InvalidatePlanOnNoHighest = true;

    /// <inheritdoc/>
    public HTNPlanState ShutdownState => HTNPlanState.TaskFinished;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

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
            return (!InvalidatePlanOnNoHighest, new Dictionary<string, object>());

        var candidates = await _pathfindingSystem.GetTacticalCandidates(
            owner, reference, MaxRange, MaxCandidates, cancelToken, _pathfindingSystem.GetFlags(blackboard));

        if (candidates.Count == 0)
            return (!InvalidatePlanOnNoHighest, new Dictionary<string, object>());

        PathPoly? best = null;
        var bestScore = 0f;

        // Lazy: the debug candidate list is only allocated/populated while a debug overlay is actually
        // subscribed - see NpcTacticalPositionDebugSystem. Every NPC replanning this task every tick would
        // otherwise pay for a debug payload nobody is looking at.
        var debugCandidates = _npcTacticalPositionDebugSystem.IsTracking(owner)
            ? new List<TacticalPositionDebugCandidate>(candidates.Count)
            : null;

        _exposureProbes.Clear();
        if (ExposureReferenceCoordinatesKey is not null &&
            blackboard.TryGetValue<EntityCoordinates>(ExposureReferenceCoordinatesKey, out var exposureReference, _entityManager))
            _npcExposureSystem.GetApproachProbes(owner, exposureReference, ExposureReach, ExposureProbes, _exposureProbes);

        // Not even one candidate's worth of this tick's budget left: the search waits, or does without.
        if (_exposureProbes.Count > 0 && !_npcExposureSystem.CanAfford(_exposureProbes.Count))
        {
            if (DeferWhenOverBudget)
                return (false, null);

            _exposureProbes.Clear();
        }

        var considerationCount = GetConsiderationCount();

        // Exposure is by far the dearest consideration, and can only lower a score. So every candidate is scored on the
        //      rest first, and exposure is then weighed best first, until no candidate left could beat the best so far
        //      even unexposed - or this tick's budget runs out, when the best so far stands. Usually only the top few
        //      candidates are weighed at all.
        _scoredCandidates.Clear();
        foreach (var candidate in candidates)
        {
            var score = ScoreCandidate(blackboard, owner, candidate, reference, considerationCount);
            if (score > 0f)
                _scoredCandidates.Add((candidate, score));
            else
                debugCandidates?.Add(new TacticalPositionDebugCandidate(_entityManager.GetNetCoordinates(candidate.Coordinates), 0f));
        }

        if (_exposureProbes.Count > 0)
            _scoredCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var weighing = _exposureProbes.Count > 0;
        foreach (var (candidate, scoreWithoutExposure) in _scoredCandidates)
        {
            var score = scoreWithoutExposure;
            var weighed = false;

            if (weighing)
            {
                if (scoreWithoutExposure <= bestScore || !_npcExposureSystem.CanAfford(_exposureProbes.Count))
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
            if (_exposureProbes.Count > 0 && !weighed)
                continue;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best is null)
        {
            if (debugCandidates is not null)
            {
                _npcTacticalPositionDebugSystem.SendDebugFrame(owner, debugCandidates, null,
                    _npcTacticalPositionClaimSystem.GetAllClaimsForDebug().ConvertAll(ToDebugClaim));
            }

            return (!InvalidatePlanOnNoHighest, new Dictionary<string, object>());
        }

        _npcTacticalPositionClaimSystem.Claim(owner, best.Coordinates, GetClaimTtl(blackboard), ClaimClearanceRadius);

        if (debugCandidates is not null)
        {
            _npcTacticalPositionDebugSystem.SendDebugFrame(owner, debugCandidates, best.Coordinates,
                _npcTacticalPositionClaimSystem.GetAllClaimsForDebug().ConvertAll(ToDebugClaim));
        }

        return (true, new Dictionary<string, object>
        {
            { KeyCoordinates, best.Coordinates },
        });
    }

    private TacticalPositionDebugClaim ToDebugClaim((EntityCoordinates Coordinates, float ClearanceRadius) claim)
    {
        return new TacticalPositionDebugClaim(_entityManager.GetNetCoordinates(claim.Coordinates), claim.ClearanceRadius);
    }

    /// <summary>
    ///     How many considerations a candidate is scored on, which <see cref="NPCUtilitySystem.GetAdjustedScore"/>
    ///         needs to make scores with different numbers of them comparable.
    /// </summary>
    private int GetConsiderationCount()
    {
        return 1 // distance, always applied
            + (AvoidFireLanes && _npcSquadFireLaneSystem.Enabled ? 1 : 0)
            + (LosReferenceCoordinatesKey is not null ? 1 : 0)
            + (FovReferenceCoordinatesKey is not null ? 1 : 0)
            + (RandomProbability > 0f ? 1 : 0)
            + (KillZoneAvoidance > 0f ? 1 : 0)
            + (_exposureProbes.Count > 0 ? 1 : 0)
            + 1; // claim penalty, always applied
    }

    /// <summary>
    ///     What exposure multiplies <paramref name="candidate"/>'s score by, at most 1. Spends a check per probe from this
    ///         tick's budget.
    /// </summary>
    private float GetExposureFactor(PathPoly candidate, int considerationCount)
    {
        var hiddenRaw = 1f - _npcExposureSystem.GetExposure(_transformSystem.ToMapCoordinates(candidate.Coordinates), _exposureProbes, ExposureRadius);
        return _npcUtilitySystem.GetAdjustedScore(_npcUtilitySystem.GetScore(ExposureCurve, hiddenRaw), considerationCount);
    }

    /// <summary>
    ///     <paramref name="candidate"/>'s score on everything but exposure, which <see cref="Plan"/> weighs separately.
    /// </summary>
    private float ScoreCandidate(NPCBlackboard blackboard, EntityUid owner, PathPoly candidate, EntityCoordinates reference, int considerationCount)
    {
        var avoidFireLanes = AvoidFireLanes && _npcSquadFireLaneSystem.Enabled;

        var score = 1f;

        if (!candidate.Coordinates.TryDistance(_entityManager, _transformSystem, reference, out var distance))
            return 0f;

        var visionRadius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(_entityManager), _entityManager);
        var distanceRaw = Math.Clamp(distance / MathF.Max(visionRadius, 0.01f), 0f, 1f);
        score *= _npcUtilitySystem.GetAdjustedScore(_npcUtilitySystem.GetScore(DistanceCurve, distanceRaw), considerationCount);

        if (score <= 0f)
            return 0f;

        if (LosReferenceCoordinatesKey is not null &&
            blackboard.TryGetValue<EntityCoordinates>(LosReferenceCoordinatesKey, out var losReference, _entityManager))
        {
            var losRaw = _examineSystem.InRangeUnOccluded(
                _transformSystem.ToMapCoordinates(candidate.Coordinates),
                _transformSystem.ToMapCoordinates(losReference),
                LosRadius + 0.5f,
                null) ? 1f : 0f;

            score *= _npcUtilitySystem.GetAdjustedScore(_npcUtilitySystem.GetScore(LosCurve, losRaw), considerationCount);

            if (score <= 0f)
                return 0f;
        }

        if (FovReferenceCoordinatesKey is not null &&
            blackboard.TryGetValue<EntityCoordinates>(FovReferenceCoordinatesKey, out var fovReference, _entityManager))
        {
            var fovRaw = EvaluateFov(owner, candidate.Coordinates, fovReference, FovAngle);
            score *= _npcUtilitySystem.GetAdjustedScore(_npcUtilitySystem.GetScore(FovCurve, fovRaw), considerationCount);

            if (score <= 0f)
                return 0f;
        }

        if (RandomProbability > 0f)
        {
            var jitterRaw = _robustRandom.Prob(RandomProbability) ? 1f : 0f;
            score *= _npcUtilitySystem.GetAdjustedScore(_npcUtilitySystem.GetScore(new BoolCurve(), jitterRaw), considerationCount);

            if (score <= 0f)
                return 0f;
        }

        if (avoidFireLanes)
        {
            MapCoordinates? aim = LosReferenceCoordinatesKey is not null &&
                blackboard.TryGetValue<EntityCoordinates>(LosReferenceCoordinatesKey, out var aimCoordinates, _entityManager)
                    ? _transformSystem.ToMapCoordinates(aimCoordinates)
                    : null;

            var fireLanePenalty = _npcSquadFireLaneSystem.GetFireLanePenalty(owner, _transformSystem.ToMapCoordinates(candidate.Coordinates), aim);
            score *= _npcUtilitySystem.GetAdjustedScore(fireLanePenalty, considerationCount);

            if (score <= 0f)
                return 0f;
        }

        if (KillZoneAvoidance > 0f)
        {
            var danger = _npcKillZoneSystem.GetDanger(owner, candidate.Coordinates);
            score *= _npcUtilitySystem.GetAdjustedScore(Math.Clamp(1f - KillZoneAvoidance * danger, 0f, 1f), considerationCount);

            if (score <= 0f)
                return 0f;
        }

        var claimPenalty = _npcTacticalPositionClaimSystem.GetClaimPenalty(candidate.Coordinates, ClaimClearanceRadius);
        score *= _npcUtilitySystem.GetAdjustedScore(claimPenalty, considerationCount);

        return score;
    }

    /// <summary>
    /// Mirrors CoordinatesInFOVCon's dot-product cone check: is the direction from
    /// <paramref name="owner"/> to <paramref name="candidate"/> within <paramref name="angleDegrees"/>
    /// of the direction from <paramref name="owner"/> to <paramref name="reference"/>?
    /// </summary>
    private float EvaluateFov(EntityUid owner, EntityCoordinates candidate, EntityCoordinates reference, float angleDegrees)
    {
        var ownerPosition = _transformSystem.GetWorldPosition(owner);
        var candidatePosition = _transformSystem.ToWorldPosition(candidate);
        var referencePosition = _transformSystem.ToWorldPosition(reference);

        var forward = candidatePosition - ownerPosition;
        Vector2Helpers.Normalize(ref forward);

        var toReference = referencePosition - ownerPosition;
        Vector2Helpers.Normalize(ref toReference);

        var dot = Vector2.Dot(forward, toReference);

        var halfFovRad = MathF.PI * (angleDegrees / 2f) / 180f;
        var threshold = MathF.Cos(halfFovRad);

        return dot >= threshold ? 1f : 0f;
    }

    private TimeSpan GetClaimTtl(NPCBlackboard blackboard)
    {
        var duration = blackboard.GetValueOrDefault<float>(ClaimDurationKey, _entityManager);
        return TimeSpan.FromSeconds(duration + ClaimTtlBuffer);
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        _npcTacticalPositionClaimSystem.ReleaseClaim(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _npcTacticalPositionClaimSystem.ReleaseClaim(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
