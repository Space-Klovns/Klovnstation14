using System.Numerics;
using Content.Shared._KS14.Structures;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.Structures;

/// <summary>
///     Draws <see cref="KsLineSmoothComponent"/> structures as one line: the <c>wire</c> layer is always shown, and each
///         end carries a post (<c>postA</c> at the end the structure faces away from, <c>postB</c> at the other) only
///         when no anchored structure of the same key continues the line there.
/// </summary>
/// <remarks>
///     Redrawn when a structure is set up, torn down, anchored, unanchored, moved or turned, and then for its
///         neighbours. Never per frame.
/// </remarks>
public sealed partial class KsLineSmoothSystem : EntitySystem
{
    public const string WireLayer = "wire";
    public const string PostLayerA = "postA";
    public const string PostLayerB = "postB";

    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<KsLineSmoothComponent> _lineSmoothQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsLineSmoothComponent> entity, ref ComponentStartup args)
    {
        // The far end's post is the same art turned round.
        if (TryComp<SpriteComponent>(entity, out var spriteComponent) &&
            _spriteSystem.LayerMapTryGet((entity.Owner, spriteComponent), PostLayerB, out var postLayerIndex, logMissing: false))
        {
            _spriteSystem.LayerSetDirOffset((entity.Owner, spriteComponent), postLayerIndex, SpriteComponent.DirectionOffset.Flip);
        }

        RefreshWithNeighbours(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsLineSmoothComponent> entity, ref ComponentShutdown args)
    {
        // Still in the tile while it goes, so the neighbours must be told to look past it.
        RefreshNeighbours(entity);
    }

    [SubscribeLocalEvent]
    private void OnAnchorStateChanged(Entity<KsLineSmoothComponent> entity, ref AnchorStateChangedEvent args)
    {
        RefreshWithNeighbours(entity);
    }

    [SubscribeLocalEvent]
    private void OnMove(Entity<KsLineSmoothComponent> entity, ref MoveEvent args)
    {
        RefreshWithNeighbours(entity);
    }

    private void RefreshWithNeighbours(Entity<KsLineSmoothComponent> entity)
    {
        Refresh(entity);
        RefreshNeighbours(entity);
    }

    private void RefreshNeighbours(Entity<KsLineSmoothComponent> entity)
    {
        var transformComponent = Transform(entity);
        if (!TryGetGridTile(transformComponent, out var gridUid, out var gridComponent, out var tile))
            return;

        foreach (var offset in CardinalOffsets)
        {
            foreach (var neighbourUid in _mapSystem.GetAnchoredEntities((gridUid, gridComponent), tile + offset))
            {
                if (neighbourUid != entity.Owner && _lineSmoothQuery.TryComp(neighbourUid, out var neighbourComponent))
                    Refresh((neighbourUid, neighbourComponent));
            }
        }
    }

    private static readonly Vector2i[] CardinalOffsets = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];

    private void Refresh(Entity<KsLineSmoothComponent> entity)
    {
        if (!TryComp<SpriteComponent>(entity, out var spriteComponent))
            return;

        var transformComponent = Transform(entity);
        var axis = GetAxis(transformComponent);

        // An unanchored structure isn't part of any line, so it shows both posts.
        var hasNeighbourA = transformComponent.Anchored && HasNeighbour(entity, transformComponent, axis);
        var hasNeighbourB = transformComponent.Anchored && HasNeighbour(entity, transformComponent, -axis);

        var sprite = (entity.Owner, spriteComponent);
        _spriteSystem.LayerSetVisible(sprite, WireLayer, true);
        _spriteSystem.LayerSetVisible(sprite, PostLayerA, !hasNeighbourA);
        _spriteSystem.LayerSetVisible(sprite, PostLayerB, !hasNeighbourB);
    }

    /// <summary>
    ///     The way the line runs, as a grid offset towards the end that <c>postA</c> caps: a south-facing structure
    ///         runs north to south, with its first post at the north end.
    /// </summary>
    private static Vector2i GetAxis(TransformComponent transformComponent)
    {
        return -transformComponent.LocalRotation.GetCardinalDir().ToIntVec();
    }

    private bool HasNeighbour(Entity<KsLineSmoothComponent> entity, TransformComponent transformComponent, Vector2i axis)
    {
        if (!TryGetGridTile(transformComponent, out var gridUid, out var gridComponent, out var tile))
            return false;

        foreach (var neighbourUid in _mapSystem.GetAnchoredEntities((gridUid, gridComponent), tile + axis))
        {
            if (neighbourUid == entity.Owner ||
                TerminatingOrDeleted(neighbourUid) ||
                !_lineSmoothQuery.TryComp(neighbourUid, out var neighbourComponent) ||
                neighbourComponent.Key != entity.Comp.Key)
            {
                continue;
            }

            // Only a structure running the same way carries the line on.
            var neighbourAxis = GetAxis(Transform(neighbourUid));
            if (neighbourAxis == axis || neighbourAxis == -axis)
                return true;
        }

        return false;
    }

    private bool TryGetGridTile(TransformComponent transformComponent, out EntityUid gridUid, out MapGridComponent gridComponent, out Vector2i tile)
    {
        gridUid = default;
        gridComponent = default!;
        tile = default;

        if (transformComponent.GridUid is not { } foundGridUid || !TryComp(foundGridUid, out MapGridComponent? foundGridComponent))
            return false;

        gridUid = foundGridUid;
        gridComponent = foundGridComponent;
        tile = _mapSystem.TileIndicesFor(foundGridUid, foundGridComponent, transformComponent.Coordinates);
        return true;
    }
}
