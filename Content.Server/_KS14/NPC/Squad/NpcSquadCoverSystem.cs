using System.Numerics;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Examine;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Works out how a squad covers the room its leader is in: each member takes one of the room's thresholds
///         and holds a spot with line of sight to it, off its axis, backed onto a wall and away from windows.
///         The plan is squad-wide and cached on <see cref="NpcSquadComponent.CoverPlan"/>, so every member
///         reads the same answer and replanning NPCs do not redo the work.
/// </summary>
public sealed partial class NpcSquadCoverSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcTacticalPositionClaimSystem _npcTacticalPositionClaimSystem = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;

    [Dependency] private EntityQuery<NpcSquadMemberComponent> _squadMemberQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;

    /// <summary>
    ///     A failed room search is retried sooner than a found room is refreshed, since the leader may just be
    ///         passing through a doorway.
    /// </summary>
    private static readonly TimeSpan NoRoomLifetime = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Returns <paramref name="memberUid"/>'s cover assignment, working out the squad's plan first if it is
    ///         missing or stale. False if the member has no squad, or the squad is not in a room.
    /// </summary>
    public bool TryGetAssignment(EntityUid memberUid, out NpcSquadCoverAssignment assignment)
    {
        assignment = default;

        if (!_npcSquadSystem.TryGetSquad(memberUid, out var squadEntity) ||
            EnsurePlan(squadEntity.Value) is not { HasRoom: true } plan)
            return false;

        return plan.Assignments.TryGetValue(memberUid, out assignment);
    }

    /// <summary>
    ///     Where <paramref name="memberUid"/>'s squad should hold when it has no room to cover: the threat it is
    ///         going to, if there is one, otherwise its leader. The member's own position if it has no squad.
    /// </summary>
    public EntityCoordinates GetHoldAnchor(EntityUid memberUid)
    {
        if (!_npcSquadSystem.TryGetSquad(memberUid, out var squadEntity) ||
            squadEntity.Value.Comp.Leader is not { } leaderUid)
            return Transform(memberUid).Coordinates;

        if (_squadMemberQuery.TryComp(leaderUid, out var leaderSquadMemberComponent) &&
            TryGetThreatObjective(squadEntity.Value, leaderSquadMemberComponent.Cover, out var threatCoordinates))
            return threatCoordinates;

        return Transform(leaderUid).Coordinates;
    }

    /// <summary>
    ///     Whether <paramref name="memberUid"/>'s squad still has a threat recent enough to go to.
    /// </summary>
    public bool HasThreatObjective(EntityUid memberUid)
    {
        return _npcSquadSystem.TryGetSquad(memberUid, out var squadEntity) &&
            squadEntity.Value.Comp.Leader is { } leaderUid &&
            _squadMemberQuery.TryComp(leaderUid, out var leaderSquadMemberComponent) &&
            TryGetThreatObjective(squadEntity.Value, leaderSquadMemberComponent.Cover, out _);
    }

    /// <summary>
    ///     The squad's threat, if it was reported recently enough to still be worth going to.
    /// </summary>
    private bool TryGetThreatObjective(Entity<NpcSquadComponent> squadEntity, NpcSquadCoverSettings settings, out EntityCoordinates threatCoordinates)
    {
        threatCoordinates = default;

        if (squadEntity.Comp.ThreatCoordinates is not { } coordinates ||
            _gameTiming.CurTime > squadEntity.Comp.ThreatReportedAt + TimeSpan.FromSeconds(settings.ThreatObjectiveLifetime))
            return false;

        threatCoordinates = coordinates;
        return true;
    }

    private NpcSquadCoverPlan? EnsurePlan(Entity<NpcSquadComponent> squadEntity)
    {
        if (squadEntity.Comp.Leader is not { } leaderUid ||
            !_squadMemberQuery.TryComp(leaderUid, out var leaderSquadMemberComponent))
            return null;

        var settings = leaderSquadMemberComponent.Cover;

        if (squadEntity.Comp.CoverPlan is { } existingPlan && !IsStale(squadEntity, leaderUid, existingPlan, settings))
            return existingPlan;

        var plan = BuildPlan(squadEntity, leaderUid, settings);
        squadEntity.Comp.CoverPlan = plan;
        return plan;
    }

    private bool IsStale(Entity<NpcSquadComponent> squadEntity, EntityUid leaderUid, NpcSquadCoverPlan plan, NpcSquadCoverSettings settings)
    {
        if (_gameTiming.CurTime >= plan.ExpiresAt || plan.SquadRevision != squadEntity.Comp.Revision)
            return true;

        if (squadEntity.Comp.ThreatCoordinates is { } threatCoordinates)
        {
            if (plan.ThreatCoordinates is not { } plannedThreatCoordinates ||
                !threatCoordinates.TryDistance(EntityManager, _transformSystem, plannedThreatCoordinates, out var threatMoved) ||
                threatMoved > settings.ThreatMoveTolerance)
                return true;
        }

        if (!plan.HasRoom)
            return false;

        // A room chosen because the threat is in it stays chosen while the squad makes its way there; the
        //      threat moving is what retargets it, checked above.
        if (plan.SeededFromThreat)
            return !TryGetThreatObjective(squadEntity, settings, out _);

        var leaderTransform = Transform(leaderUid);
        if (leaderTransform.GridUid != plan.GridUid || !_mapGridQuery.TryComp(plan.GridUid, out var mapGridComponent))
            return true;

        // Members walk to their spots inside the room, so the leader only leaves it when something else in its
        //      plan (combat, a retreat) takes it out - which is exactly when the old room stops mattering.
        var leaderTile = _mapSystem.TileIndicesFor((plan.GridUid, mapGridComponent), leaderTransform.Coordinates);
        return !plan.RoomTiles.Contains(leaderTile);
    }

    private NpcSquadCoverPlan BuildPlan(Entity<NpcSquadComponent> squadEntity, EntityUid leaderUid, NpcSquadCoverSettings settings)
    {
        var now = _gameTiming.CurTime;
        var plan = new NpcSquadCoverPlan
        {
            SquadRevision = squadEntity.Comp.Revision,
            ThreatCoordinates = squadEntity.Comp.ThreatCoordinates,
            ExpiresAt = now + NoRoomLifetime,
        };

        var leaderTransform = Transform(leaderUid);
        if (leaderTransform.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return plan;

        var gridEntity = new Entity<MapGridComponent>(gridUid, mapGridComponent);

        var (collisionLayer, collisionMask) = _fixturesQuery.TryComp(leaderUid, out var fixturesComponent)
            ? _physicsSystem.GetHardCollision(leaderUid, fixturesComponent)
            : (0, 0);

        // With nothing to collide with, every wall would count as floor and the whole grid as one room.
        if (collisionLayer == 0 && collisionMask == 0)
            (collisionLayer, collisionMask) = ((int)CollisionGroup.MobLayer, (int)CollisionGroup.MobMask);

        // Go to the threat, if the squad knows of one on this grid; otherwise hold where the leader is.
        var leaderTile = _mapSystem.TileIndicesFor(gridEntity, leaderTransform.Coordinates);
        var seedTile = leaderTile;

        if (TryGetThreatObjective(squadEntity, settings, out var threatCoordinates) &&
            _transformSystem.GetGrid(threatCoordinates) == gridUid)
        {
            seedTile = _mapSystem.TileIndicesFor(gridEntity, threatCoordinates);
            plan.SeededFromThreat = true;
        }

        var exposureTiles = new List<Vector2i>();

        if (!TryAnalyseRoom(gridEntity, seedTile, leaderTile, collisionLayer, collisionMask, settings, plan, exposureTiles))
        {
            plan.RoomTiles.Clear();
            plan.Thresholds.Clear();
            return plan;
        }

        AssignCover(squadEntity, gridEntity, plan, settings, exposureTiles);

        if (plan.Assignments.Count == 0)
            return plan;

        plan.HasRoom = true;
        plan.ExpiresAt = now + TimeSpan.FromSeconds(settings.PlanLifetime);
        return plan;
    }

    #region Assignment

    private void AssignCover(
        Entity<NpcSquadComponent> squadEntity,
        Entity<MapGridComponent> gridEntity,
        NpcSquadCoverPlan plan,
        NpcSquadCoverSettings settings,
        List<Vector2i> exposureTiles)
    {
        // Otherwise the squad's claims from its previous plan push every member off the spot it already holds.
        foreach (var memberUid in squadEntity.Comp.Members)
        {
            _npcTacticalPositionClaimSystem.ReleaseClaim(memberUid);
        }

        var candidatesPerThreshold = new List<(Vector2i Tile, float Score)>[plan.Thresholds.Count];
        for (var i = 0; i < plan.Thresholds.Count; i++)
        {
            candidatesPerThreshold[i] = ScoreCandidates(gridEntity, plan, plan.Thresholds[i], settings, exposureTiles);
        }

        var thresholdOrder = GetThresholdPriority(squadEntity, gridEntity, plan);
        var unassignedUids = new List<EntityUid>(squadEntity.Comp.Members);
        var claimTtl = TimeSpan.FromSeconds(settings.PlanLifetime + 5f);

        // Grid-local, like everything else here. Filled as members are placed, so each later pick keeps out of
        //      the lines of fire of those before it, and keeps them out of its own.
        var assignedPositions = new List<Vector2>();
        var assignedLanes = new List<NpcFireLane>();

        // Round-robin over the thresholds in priority order, so each gets one member before any gets two, and
        //      surplus members double up on the most important ones. Each claim spreads the next pick apart.
        while (unassignedUids.Count > 0)
        {
            var assignedAny = false;

            foreach (var thresholdIndex in thresholdOrder)
            {
                if (unassignedUids.Count == 0)
                    break;

                var threshold = plan.Thresholds[thresholdIndex];

                if (!TryPickCandidate(gridEntity, candidatesPerThreshold[thresholdIndex], threshold, settings, assignedPositions, assignedLanes, out var coverCoordinates))
                    continue;

                var memberUid = TakeNearest(unassignedUids, coverCoordinates);
                assignedPositions.Add(coverCoordinates.Position);
                assignedLanes.Add(new NpcFireLane(coverCoordinates.Position, threshold.AimPoint));

                var facingLocal = threshold.Center - coverCoordinates.Position;
                var facingWorld = _transformSystem.GetWorldRotation(gridEntity).RotateVec(facingLocal);

                _npcTacticalPositionClaimSystem.Claim(memberUid, coverCoordinates, claimTtl, settings.ClaimClearanceRadius);
                plan.Assignments[memberUid] = new NpcSquadCoverAssignment(thresholdIndex, coverCoordinates, facingWorld.ToWorldAngle());
                assignedAny = true;
            }

            if (!assignedAny)
                break;
        }
    }

    /// <summary>
    ///     Thresholds nearest the last known threat come first; with no threat, those nearest the leader.
    /// </summary>
    private List<int> GetThresholdPriority(Entity<NpcSquadComponent> squadEntity, Entity<MapGridComponent> gridEntity, NpcSquadCoverPlan plan)
    {
        var reference = squadEntity.Comp.ThreatCoordinates is { } threatCoordinates &&
            _transformSystem.GetGrid(threatCoordinates) == gridEntity.Owner
                ? threatCoordinates
                : Transform(squadEntity.Comp.Leader!.Value).Coordinates;

        var referenceLocal = _transformSystem.WithEntityId(reference, gridEntity.Owner).Position;

        var order = new List<int>(plan.Thresholds.Count);
        for (var i = 0; i < plan.Thresholds.Count; i++)
        {
            order.Add(i);
        }

        order.Sort((a, b) =>
            Vector2.DistanceSquared(plan.Thresholds[a].Center, referenceLocal)
                .CompareTo(Vector2.DistanceSquared(plan.Thresholds[b].Center, referenceLocal)));

        return order;
    }

    private bool TryPickCandidate(
        Entity<MapGridComponent> gridEntity,
        List<(Vector2i Tile, float Score)> candidates,
        NpcSquadThreshold threshold,
        NpcSquadCoverSettings settings,
        List<Vector2> assignedPositions,
        List<NpcFireLane> assignedLanes,
        out EntityCoordinates coverCoordinates)
    {
        var avoidFireLanes = _npcSquadFireLaneSystem.Enabled;

        coverCoordinates = default;
        var bestScore = 0f;

        foreach (var (tile, baseScore) in candidates)
        {
            if (baseScore <= bestScore)
                continue;

            var coordinates = new EntityCoordinates(gridEntity, _mapSystem.TileCenterToVector(gridEntity, tile));
            var score = baseScore * _npcTacticalPositionClaimSystem.GetClaimPenalty(coordinates, settings.ClaimClearanceRadius);

            if (avoidFireLanes)
                score *= NpcSquadFireLaneSystem.GetPenalty(coordinates.Position, threshold.AimPoint, assignedPositions, assignedLanes);

            if (score <= bestScore)
                continue;

            bestScore = score;
            coverCoordinates = coordinates;
        }

        return bestScore > 0f;
    }

    private EntityUid TakeNearest(List<EntityUid> memberUids, EntityCoordinates coordinates)
    {
        var target = _transformSystem.ToMapCoordinates(coordinates);
        var bestIndex = 0;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < memberUids.Count; i++)
        {
            var memberCoordinates = _transformSystem.GetMapCoordinates(memberUids[i]);
            var distance = memberCoordinates.MapId == target.MapId
                ? Vector2.DistanceSquared(memberCoordinates.Position, target.Position)
                : float.MaxValue;

            if (distance >= bestDistance)
                continue;

            bestIndex = i;
            bestDistance = distance;
        }

        var memberUid = memberUids[bestIndex];
        memberUids.RemoveAt(bestIndex);
        return memberUid;
    }

    #endregion

    #region Scoring

    /// <summary>
    ///     Scores every room tile as a place to cover <paramref name="threshold"/> from, before claims. Tiles
    ///         without line of sight to it, or outside the standoff band, are left out.
    /// </summary>
    private List<(Vector2i Tile, float Score)> ScoreCandidates(
        Entity<MapGridComponent> gridEntity,
        NpcSquadCoverPlan plan,
        NpcSquadThreshold threshold,
        NpcSquadCoverSettings settings,
        List<Vector2i> exposureTiles)
    {
        var candidates = new List<(Vector2i, float)>();
        var aimMap = _transformSystem.ToMapCoordinates(new EntityCoordinates(gridEntity, threshold.AimPoint));

        foreach (var tile in plan.RoomTiles)
        {
            var position = _mapSystem.TileCenterToVector(gridEntity, tile);
            var offset = position - threshold.Center;
            var distance = offset.Length();

            if (distance < settings.MinStandoff || distance > settings.MaxStandoff)
                continue;

            var score = ScoreDistance(distance, settings) *
                (plan.IsHallway ? 1f : ScoreAngle(offset / distance, threshold.InwardNormal, settings)) *
                ScoreExposure(position, gridEntity, exposureTiles, settings) *
                ScoreWalls(plan, tile, settings);

            if (score <= 0f)
                continue;

            var positionMap = _transformSystem.ToMapCoordinates(new EntityCoordinates(gridEntity, position));
            if (!_examineSystem.InRangeUnOccluded(positionMap, aimMap, settings.MaxStandoff + 1f, null))
                continue;

            candidates.Add((tile, score));
        }

        return candidates;
    }

    private static float ScoreDistance(float distance, NpcSquadCoverSettings settings)
    {
        var span = MathF.Max(settings.IdealStandoff - settings.MinStandoff, settings.MaxStandoff - settings.IdealStandoff);
        return MathF.Max(0.2f, 1f - MathF.Abs(distance - settings.IdealStandoff) / MathF.Max(span, 0.01f));
    }

    /// <summary>
    ///     Favours standing at an angle to the threshold (slicing the pie), and punishes standing on its axis,
    ///         where whoever comes through sees you first.
    /// </summary>
    private static float ScoreAngle(Vector2 direction, Vector2 inwardNormal, NpcSquadCoverSettings settings)
    {
        var angle = MathF.Acos(Math.Clamp(Vector2.Dot(direction, inwardNormal), -1f, 1f)) * 180f / MathF.PI;

        if (angle < settings.FunnelAngle)
            return 0.15f;

        if (angle < settings.MinPreferredAngle)
            return float.Lerp(0.15f, 1f, (angle - settings.FunnelAngle) / MathF.Max(settings.MinPreferredAngle - settings.FunnelAngle, 0.01f));

        if (angle <= settings.MaxPreferredAngle)
            return 1f;

        // Hugging the threshold's own wall: still covering it, but only just.
        return float.Lerp(1f, 0.5f, Math.Clamp((angle - settings.MaxPreferredAngle) / 25f, 0f, 1f));
    }

    private float ScoreExposure(Vector2 position, Entity<MapGridComponent> gridEntity, List<Vector2i> exposureTiles, NpcSquadCoverSettings settings)
    {
        var nearest = float.MaxValue;

        foreach (var exposureTile in exposureTiles)
        {
            nearest = MathF.Min(nearest, Vector2.Distance(position, _mapSystem.TileCenterToVector(gridEntity, exposureTile)));
        }

        if (nearest >= settings.ExposureAvoidRange)
            return 1f;

        return float.Lerp(0.15f, 1f, Math.Clamp((nearest - 1f) / MathF.Max(settings.ExposureAvoidRange - 1f, 0.01f), 0f, 1f));
    }

    /// <summary>
    ///     See <see cref="NpcSquadCoverSettings.WallPreference"/>. Three or more solid neighbours out of eight
    ///         counts as fully walled.
    /// </summary>
    private static float ScoreWalls(NpcSquadCoverPlan plan, Vector2i tile, NpcSquadCoverSettings settings)
    {
        const float fullyWalledNeighbours = 3f;

        var walled = MathF.Min(CountSolidNeighbours(plan, tile), fullyWalledNeighbours) / fullyWalledNeighbours;
        var preference = Math.Clamp(settings.WallPreference, -1f, 1f);

        return preference >= 0f
            ? 1f - preference * (1f - walled)
            : 1f + preference * walled;
    }

    #endregion
}
