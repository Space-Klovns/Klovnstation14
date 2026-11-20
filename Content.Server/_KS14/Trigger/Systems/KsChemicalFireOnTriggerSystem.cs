using System.Numerics;
using Content.Server._KS14.Trigger.Components;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Trigger;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._KS14.Trigger.Systems;

public sealed partial class KsChemicalFireOnTriggerSystem : XOnTriggerSystem<KsChemicalFireOnTriggerComponent>
{
    /// <summary>
    ///     How far back along its path a round that went off inside a wall looks for the open tile it came from, in
    ///         tiles, in steps of <see cref="BackStep"/>.
    /// </summary>
    private const float MaxBackDistance = 2f;

    private const float BackStep = 0.25f;

    private static readonly Vector2i[] Neighbours = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    // Scratch for the flood, reused from one round to the next.
    private readonly Queue<Vector2i> _frontier = new();
    private readonly HashSet<Vector2i> _reached = new();

    protected override void OnTrigger(Entity<KsChemicalFireOnTriggerComponent> entity, EntityUid targetUid, ref TriggerEvent args)
    {
        var transformComponent = Transform(targetUid);
        var coordinates = transformComponent.Coordinates;
        if (_transformSystem.GetGrid(coordinates) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return;

        var grid = new Entity<MapGridComponent>(gridUid, gridComponent);
        if (!TryGetOpenTile(grid, targetUid, out var centre))
        {
            args.Handled = true;
            return;
        }

        var radius = Math.Max(0, entity.Comp.Radius);
        var radiusSquared = radius * radius;

        // Out from the open tile it landed on, tile by tile, never through a wall: what is behind one is out of reach
        //      of a splash on this side, however close.
        _frontier.Clear();
        _reached.Clear();
        _frontier.Enqueue(centre);
        _reached.Add(centre);

        while (_frontier.TryDequeue(out var tile))
        {
            _chemicalFireSystem.SpawnChemicalFire(entity.Comp.Fire, (gridUid, gridComponent), tile, entity.Comp.Duration);

            foreach (var step in Neighbours)
            {
                var next = tile + step;
                var offset = next - centre;
                if (offset.X * offset.X + offset.Y * offset.Y > radiusSquared ||
                    _reached.Contains(next) ||
                    _chemicalFireSystem.IsWall((gridUid, gridComponent), next))
                    continue;

                _reached.Add(next);
                _frontier.Enqueue(next);
            }
        }

        args.Handled = true;
    }

    /// <summary>
    ///     The tile the round went off on, or, if that is a wall it struck, the open tile it came from: back along its
    ///         travel, or with no travel to go by, the open tile beside the wall on the side of it the round is nearest. False
    ///         when there is none.
    /// </summary>
    private bool TryGetOpenTile(Entity<MapGridComponent> grid, EntityUid roundUid, out Vector2i tile)
    {
        var localPosition = _mapSystem.WorldToLocal(grid.Owner, grid.Comp, _transformSystem.GetWorldPosition(roundUid));
        tile = _mapSystem.LocalToTile(grid.Owner, grid.Comp, new EntityCoordinates(grid.Owner, localPosition));
        if (!_chemicalFireSystem.IsWall((grid.Owner, grid.Comp), tile))
            return true;

        var wallTile = tile;
        if (_physicsQuery.TryComp(roundUid, out var physicsComponent) && physicsComponent.LinearVelocity.LengthSquared() > 0f)
        {
            // The grid's own frame: a turned grid turns the round's travel with it.
            var back = -(-_transformSystem.GetWorldRotation(grid.Owner)).RotateVec(physicsComponent.LinearVelocity).Normalized();
            for (var distance = BackStep; distance <= MaxBackDistance; distance += BackStep)
            {
                tile = _mapSystem.LocalToTile(grid.Owner, grid.Comp, new EntityCoordinates(grid.Owner, localPosition + back * distance));
                if (!_chemicalFireSystem.IsWall((grid.Owner, grid.Comp), tile))
                    return true;
            }
        }

        // Whichever side of the wall's tile it is nearest, of those open.
        var fromCentre = localPosition - new Vector2(wallTile.X + 0.5f, wallTile.Y + 0.5f) * grid.Comp.TileSize;
        var bestDot = float.MinValue;
        var found = false;
        foreach (var step in Neighbours)
        {
            var neighbour = wallTile + step;
            var dot = Vector2.Dot(fromCentre, (Vector2) step);
            if (dot <= bestDot || _chemicalFireSystem.IsWall((grid.Owner, grid.Comp), neighbour))
                continue;

            bestDot = dot;
            tile = neighbour;
            found = true;
        }

        return found;
    }
}
