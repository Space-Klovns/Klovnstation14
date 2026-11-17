using System.Numerics;
using Content.Server._KS14.NPC.Utility;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class TacticalPositionOperator
{
    private static readonly Vector2i[] CoverStepOffsets =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    ];

    /// <summary>
    ///     Whether a free floor tile next to <paramref name="candidate"/>'s, diagonals included, is out of sight of
    ///         <see cref="LosReferenceCoordinatesKey"/>. See <see cref="CoverStepCurve"/>.
    /// </summary>
    private bool HasCoverAStepAway(PathPoly candidate)
    {
        if (!_npcGridSystem.TryGetGrid(candidate.GraphUid, out var grid))
            return false;

        var tile = _mapSystem.TileIndicesFor(grid, candidate.Coordinates);

        foreach (var offset in CoverStepOffsets)
        {
            if (!IsFreeFloor(grid, tile + offset))
                continue;

            var neighbourCoordinates = new EntityCoordinates(grid.Owner, _mapSystem.TileCenterToVector(grid, tile + offset));

            if (!_npcLineOfSightSystem.InLineOfSight(_plan.CoverStepSightField, _transformSystem.ToMapCoordinates(neighbourCoordinates)))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether any tile next to <paramref name="candidate"/>'s, diagonals included, is one nothing can stand on.
    /// </summary>
    private bool IsBesideSomething(PathPoly candidate)
    {
        if (!_npcGridSystem.TryGetGrid(candidate.GraphUid, out var grid))
            return false;

        var tile = _mapSystem.TileIndicesFor(grid, candidate.Coordinates);

        foreach (var offset in CoverStepOffsets)
        {
            if (!IsFreeFloor(grid, tile + offset))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether something can stand on <paramref name="tile"/>: no wall, door, table or anything else the navmesh has there.
    /// </summary>
    private bool IsFreeFloor(Entity<MapGridComponent> grid, Vector2i tile)
    {
        return _pathfindingSystem.GetPoly(new EntityCoordinates(grid, _mapSystem.TileCenterToVector(grid, tile))) is { } poly &&
            poly.Data.IsFreeSpace &&
            poly.Data.CollisionLayer == 0 &&
            poly.Data.CollisionMask == 0;
    }

    /// <summary>
    ///     How tucked in <paramref name="candidate"/> is. See <see cref="WallCurve"/>.
    /// </summary>
    private float GetWallRaw(PathPoly candidate)
    {
        if (!_npcGridSystem.TryGetGrid(candidate.GraphUid, out var grid))
            return 0f;

        var tile = _mapSystem.TileIndicesFor(grid, candidate.Coordinates);

        var blockedAlongX = !IsFreeFloor(grid, tile + new Vector2i(1, 0)) || !IsFreeFloor(grid, tile + new Vector2i(-1, 0));
        var blockedAlongY = !IsFreeFloor(grid, tile + new Vector2i(0, 1)) || !IsFreeFloor(grid, tile + new Vector2i(0, -1));

        if (blockedAlongX && blockedAlongY)
            return 1f;

        return blockedAlongX || blockedAlongY ? 0.5f : 0f;
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

        return NpcCoordinates.IsInCone(ownerPosition, candidatePosition, referencePosition, angleDegrees) ? 1f : 0f;
    }
}
