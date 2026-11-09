using System.Numerics;
using Content.Server._KS14.NPC.Exposure;
using Content.Server._KS14.NPC.KillZones;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Utility;
using Content.Shared._KS14.NPC;
using Content.Shared.Physics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Queries;
using Content.Server.NPC.Queries.Curves;
using Content.Server.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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
    [Dependency] private NpcGridSystem _npcGridSystem = default!;
    [Dependency] private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcTacticalPositionClaimSystem _npcTacticalPositionClaimSystem = default!;
    [Dependency] private NpcKillZoneSystem _npcKillZoneSystem = default!;
    [Dependency] private NpcExposureSystem _npcExposureSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcTacticalPositionDebugSystem _npcTacticalPositionDebugSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
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

    /// <summary>
    ///     A blackboard float that overrides <see cref="MaxRange"/> if the owner has it, for reach that goes by the mob
    ///         rather than the task.
    /// </summary>
    [DataField] public string? MaxRangeKey;

    [DataField] public int MaxCandidates = 64;

    /// <summary>
    ///     A blackboard float that overrides <see cref="MaxCandidates"/> if the owner has it. The candidates are half the
    ///         nearest tiles and half spread over the rest of the flood, so in a big room any one corner is rarely among
    ///         them: more of them cost more, and miss fewer.
    /// </summary>
    [DataField] public string? MaxCandidatesKey;

    [DataField] public IUtilityCurve DistanceCurve = new PresetCurve { Preset = "KsTargetDistanceLessClose" };

    /// <summary>
    /// If set, adds an LOS consideration between the candidate and this coordinates key. Wrap
    /// <see cref="LosCurve"/> with an InverseBoolCurve in YAML to prefer concealment instead of visibility.
    /// </summary>
    [DataField] public string? LosReferenceCoordinatesKey;

    [DataField] public float LosRadius = 10f;

    /// <summary>
    ///     A blackboard float that overrides <see cref="LosRadius"/> if the owner has it.
    /// </summary>
    [DataField] public string? LosRadiusKey;

    [DataField] public IUtilityCurve LosCurve = new BoolCurve();

    /// <summary>
    ///     Whether line of sight here is for shooting: a candidate only counts as seeing <see cref="LosReferenceCoordinatesKey"/>
    ///         if what the owner is holding could hit it from there. A spot that sees it through a window is no use to a
    ///         gun that the window stops. See <see cref="NpcLineOfFireSystem"/>.
    /// </summary>
    [DataField] public bool LineOfFire;

    /// <summary>
    ///     If set, scores whether a candidate is a step from cover from <see cref="LosReferenceCoordinatesKey"/>: whether
    ///         a free floor tile next to it is out of that one's sight. For a spot that sees the target, that makes it a
    ///         corner - one step from out of the fight - rather than open floor, which the threat's whole approach sees
    ///         alike, so exposure alone barely tells them apart. The curve runs over 0 (no cover a step away) and 1.
    ///         Needs <see cref="LosReferenceCoordinatesKey"/>.
    /// </summary>
    [DataField] public IUtilityCurve? CoverStepCurve;

    /// <summary>
    ///     A blackboard bool that, true on the owner, rules out every candidate without cover a step away (see
    ///         <see cref="CoverStepCurve"/>) rather than only scoring it down, for an NPC that would sooner have no spot
    ///         than one in the open. The candidate must also be right beside something: the shadow of a pillar runs on
    ///         across a big room, and the edge of it is a step from being out of sight with nothing to stand behind.
    ///         Works with or without the curve. Needs <see cref="LosReferenceCoordinatesKey"/>.
    /// </summary>
    [DataField] public string? CoverOnlyKey;

    /// <summary>
    ///     If set, scores how tucked in a candidate is, with no threat needed: 0 on open floor, 0.5 beside something it
    ///         cannot stand on, 1 in the corner of two such sides. For somewhere to wait with nothing to take cover from.
    /// </summary>
    [DataField] public IUtilityCurve? WallCurve;

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
    ///     Whether anywhere within <see cref="ClaimClearanceRadius"/> of a spot another NPC has claimed is ruled out,
    ///         rather than only scored down the nearer it gets. For spots only one may hold - peeking a corner, say -
    ///         where a second NPC taking the next best spot beside the first is the thing to prevent. The owner's own
    ///         claim never rules out its own spot.
    /// </summary>
    [DataField] public bool ExclusiveClaims;

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
    ///     A blackboard key holding the hostile being shot. Set, and with the squad's
    ///         <see cref="Squad.Tactics.NpcSquadTacticsSettings.Crossfire"/> on, a spot is worth more the wider the angle at
    ///         the hostile between it and every squadmate already shooting it, so a squad closes in from several sides
    ///         rather than bunching up on one.
    /// </summary>
    [DataField] public string? CrossfireTargetKey;

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

    /// <summary>
    ///     This operator's state for the plan in progress. One operator instance is shared by every NPC on the same HTN
    ///         prototype, and <see cref="Plan"/> awaits only once, so all of this is written after that await and read
    ///         before the next plan can start: plans sharing the operator cannot interleave over it. The one instance
    ///         is reused, so nothing here is allocated per plan.
    /// </summary>
    private sealed class PlanState
    {
        /// <summary>
        ///     What this plan's shots collide with, if it goes by <see cref="LineOfFire"/>.
        /// </summary>
        public CollisionGroup ShotMask;

        /// <summary>
        ///     What <see cref="LosReferenceCoordinatesKey"/> can see, built once per plan for
        ///         <see cref="CoverStepCurve"/>.
        /// </summary>
        public readonly NpcSightField CoverStepSightField = new();

        /// <summary>
        ///     Whether this plan's candidates are scored on <see cref="CoverStepCurve"/>, with
        ///         <see cref="CoverStepSightField"/>.
        /// </summary>
        public bool ScoreCoverStep;

        /// <summary>
        ///     This plan's reach for line of sight, and whether <see cref="CoverOnlyKey"/> is on.
        /// </summary>
        public float LosRadius;
        public bool CoverOnly;

        /// <summary>
        ///     Where <see cref="LosReferenceCoordinatesKey"/> is, if the owner has it. Nothing moves it while the
        ///         candidates are scored.
        /// </summary>
        public MapCoordinates? LosReference;

        /// <summary>
        ///     The owner's vision radius, which distance is scored against.
        /// </summary>
        public float VisionRadius;

        /// <summary>
        ///     Whether <see cref="AvoidFireLanes"/> is on and the squad's fire lanes are enabled.
        /// </summary>
        public bool AvoidFireLanes;

        /// <summary>
        ///     The threat's approach, scored against for every candidate.
        /// </summary>
        public readonly List<MapCoordinates> ExposureProbes = new();

        /// <summary>
        ///     Candidates still in the running, with their score before exposure.
        /// </summary>
        public readonly List<(PathPoly Candidate, float Score)> ScoredCandidates = new();

        /// <summary>
        ///     How much crossfire counts in this search, and the directions, from the hostile, of the squadmates
        ///         shooting it.
        /// </summary>
        public float Crossfire;
        public Vector2 CrossfireTargetPosition;
        public readonly List<Vector2> CrossfireDirections = new();
        public readonly HashSet<EntityUid> CrossfireSquadmates = new();
    }

    private readonly PlanState _plan = new();

    /// <summary>
    ///     Shared by every plan: the curve for the random jitter consideration, which has no data field.
    /// </summary>
    private static readonly BoolCurve JitterCurve = new();
}
