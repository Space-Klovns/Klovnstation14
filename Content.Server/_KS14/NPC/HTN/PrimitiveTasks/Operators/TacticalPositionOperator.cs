using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Examine;
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
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

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
    private PathfindingSystem _pathfindingSystem = default!;
    private NPCUtilitySystem _npcUtilitySystem = default!;
    private NpcTacticalPositionClaimSystem _npcTacticalPositionClaimSystem = default!;
    private NpcTacticalPositionDebugSystem _npcTacticalPositionDebugSystem = default!;
    private ExamineSystem _examineSystem = default!;
    private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    /// Marker-phase utility query to try first (e.g. an existing ComponentQuery against NpcCampingSpot).
    /// Null/omitted skips the marker phase entirely and always uses the dynamic algorithm.
    /// </summary>
    [DataField("markerProto", customTypeSerializer: typeof(PrototypeIdSerializer<UtilityQueryPrototype>))]
    public string? MarkerPrototype;

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

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pathfindingSystem = sysManager.GetEntitySystem<PathfindingSystem>();
        _npcUtilitySystem = sysManager.GetEntitySystem<NPCUtilitySystem>();
        _npcTacticalPositionClaimSystem = sysManager.GetEntitySystem<NpcTacticalPositionClaimSystem>();
        _npcTacticalPositionDebugSystem = sysManager.GetEntitySystem<NpcTacticalPositionDebugSystem>();
        _examineSystem = sysManager.GetEntitySystem<ExamineSystem>();
        _transformSystem = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

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

        foreach (var candidate in candidates)
        {
            var score = ScoreCandidate(blackboard, owner, candidate, reference);
            debugCandidates?.Add(new TacticalPositionDebugCandidate(_entityManager.GetNetCoordinates(candidate.Coordinates), score));

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

    private float ScoreCandidate(NPCBlackboard blackboard, EntityUid owner, PathPoly candidate, EntityCoordinates reference)
    {
        var considerationCount = 1 // distance, always applied
            + (LosReferenceCoordinatesKey is not null ? 1 : 0)
            + (FovReferenceCoordinatesKey is not null ? 1 : 0)
            + (RandomProbability > 0f ? 1 : 0)
            + 1; // claim penalty, always applied

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
