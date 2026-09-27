using System.Numerics;
using Content.Server._KS14.NPC.Components;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     A line an NPC is (or will be) shooting along: from where it stands to what it covers.
/// </summary>
public readonly record struct NpcFireLane(Vector2 From, Vector2 To);

/// <summary>
///     Keeps squad members out of each other's lines of fire when they choose where to stand, gated behind
///         <see cref="KsCCVars.NpcSquadFireLanes"/>. It works both ways: a spot is penalised if its own line to
///         what it covers passes through a teammate, and if it sits on a teammate's line.
/// </summary>
public sealed partial class NpcSquadFireLaneSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcTacticalPositionClaimComponent> _claimQuery = default!;

    /// <summary>
    ///     Score multiplier for a spot that crosses a line of fire. Soft rather than zero: a crossed line beats
    ///         having nowhere to stand at all.
    /// </summary>
    public const float CrossedLanePenalty = 0.2f;

    /// <summary>
    ///     How close to a line of fire, in tiles, counts as standing in it. Roughly a mob's width plus spread.
    /// </summary>
    public const float LaneRadius = 0.6f;

    /// <summary>
    ///     A miss carries on past what was aimed at, so a line extends this far, in tiles, beyond its end.
    /// </summary>
    public const float LaneOverrun = 2f;

    public bool Enabled { get; private set; }

    private readonly List<NpcFireLane> _scratchLanes = new();
    private readonly List<Vector2> _scratchPositions = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.NpcSquadFireLanes, value => Enabled = value, true);
    }

    /// <summary>
    ///     Score multiplier in (0, 1] for <paramref name="ownerUid"/> standing at <paramref name="candidate"/> and
    ///         covering <paramref name="aim"/>, against where its squadmates are standing and what they cover.
    ///         1 when disabled or when the owner has no squad.
    /// </summary>
    public float GetFireLanePenalty(EntityUid ownerUid, MapCoordinates candidate, MapCoordinates? aim)
    {
        if (!Enabled || !_npcSquadSystem.TryGetSquad(ownerUid, out var squadEntity))
            return 1f;

        _scratchLanes.Clear();
        _scratchPositions.Clear();

        var squadComponent = squadEntity.Value.Comp;
        var threatMap = squadComponent.ThreatCoordinates is { } threatCoordinates
            ? _transformSystem.ToMapCoordinates(threatCoordinates)
            : (MapCoordinates?)null;

        foreach (var memberUid in squadComponent.Members)
        {
            if (memberUid == ownerUid)
                continue;

            // Where a teammate is headed matters more than where it happens to be right now.
            var memberMap = _claimQuery.TryComp(memberUid, out var claimComponent)
                ? _transformSystem.ToMapCoordinates(claimComponent.Coordinates)
                : _transformSystem.GetMapCoordinates(memberUid);

            if (memberMap.MapId != candidate.MapId)
                continue;

            _scratchPositions.Add(memberMap.Position);

            MapCoordinates? memberAim = null;
            if (squadComponent.CoverPlan is { HasRoom: true } plan &&
                plan.Assignments.TryGetValue(memberUid, out var assignment))
                memberAim = _transformSystem.ToMapCoordinates(new EntityCoordinates(plan.GridUid, plan.Thresholds[assignment.ThresholdIndex].AimPoint));
            else
                memberAim = threatMap;

            if (memberAim is { } memberAimMap && memberAimMap.MapId == candidate.MapId)
                _scratchLanes.Add(new NpcFireLane(memberMap.Position, memberAimMap.Position));
        }

        Vector2? aimPosition = aim is { } aimMap && aimMap.MapId == candidate.MapId ? aimMap.Position : null;
        return GetPenalty(candidate.Position, aimPosition, _scratchPositions, _scratchLanes);
    }

    /// <summary>
    ///     The penalty for standing at <paramref name="position"/> and covering <paramref name="aim"/>, given
    ///         where teammates stand and the lanes they cover. All in one coordinate space.
    /// </summary>
    public static float GetPenalty(Vector2 position, Vector2? aim, IReadOnlyList<Vector2> teammatePositions, IReadOnlyList<NpcFireLane> teammateLanes)
    {
        // Our line would go through a teammate...
        if (aim is { } aimPosition)
        {
            foreach (var teammatePosition in teammatePositions)
            {
                if (IsInLane(new NpcFireLane(position, aimPosition), teammatePosition))
                    return CrossedLanePenalty;
            }
        }

        // ...or we would stand in a teammate's line.
        foreach (var lane in teammateLanes)
        {
            if (IsInLane(lane, position))
                return CrossedLanePenalty;
        }

        return 1f;
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is in front of the shooter and within <see cref="LaneRadius"/> of the
    ///         line, up to <see cref="LaneOverrun"/> past its end.
    /// </summary>
    public static bool IsInLane(NpcFireLane lane, Vector2 point)
    {
        var direction = lane.To - lane.From;
        var length = direction.Length();

        if (length < 0.01f)
            return false;

        direction /= length;

        var along = Vector2.Dot(point - lane.From, direction);
        if (along <= 0f || along > length + LaneOverrun)
            return false;

        var closest = lane.From + direction * along;
        return Vector2.DistanceSquared(point, closest) < LaneRadius * LaneRadius;
    }
}
