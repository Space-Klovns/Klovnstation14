using System.Numerics;
using Content.Server._KS14.ZLevel.Environment;
using Content.Shared._KS14.Ladders;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Builder;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.ZLevel.Builder;

/// <summary>
///     Runs <see cref="KsZLevelBuilderComponent"/>: a ladder from the user's tile to the spot directly above it on the
///         z-level above, which is created, with the environment of the user's map, when there is none yet.
/// </summary>
public sealed partial class KsZLevelBuilderSystem : EntitySystem
{
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private KsLadderSystem _ladderSystem = default!;
    [Dependency] private KsZLevelEnvironmentSystem _zLevelEnvironmentSystem = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;

    private readonly HashSet<Entity<KsLadderComponent>> _ladders = [];

    // By-value class event: the engine has no Entity<T> handler for those (RA0054).
    [SubscribeLocalEvent]
    private void OnUseInHand(EntityUid uid, KsZLevelBuilderComponent component, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryBuild((uid, component), args.User);
    }

    /// <summary>
    ///     Builds the ladder up from where <paramref name="userUid"/> stands, popping up why not if it can't.
    /// </summary>
    /// <returns>Whether the ladders were built.</returns>
    public bool TryBuild(Entity<KsZLevelBuilderComponent> builder, EntityUid userUid)
    {
        var userTransform = Transform(userUid);
        if (userTransform.GridUid is not { } lowerGridUid ||
            !TryComp<MapGridComponent>(lowerGridUid, out var lowerGridComponent) ||
            userTransform.MapUid is not { } lowerMapUid ||
            _zLevelSystem.IsGap(lowerMapUid))
        {
            Refuse(builder, userUid, "ks-zlevel-builder-no-floor");
            return false;
        }

        var lowerIndices = _mapSystem.TileIndicesFor(lowerGridUid, lowerGridComponent, userTransform.Coordinates);
        var lowerCoordinates = _mapSystem.GridTileToLocal(lowerGridUid, lowerGridComponent, lowerIndices);
        var spot = _transformSystem.ToMapCoordinates(lowerCoordinates);

        if (HasLadderAt(builder, spot))
        {
            Refuse(builder, userUid, "ks-zlevel-builder-ladder-here");
            return false;
        }

        var lowerZLevelComponent = EnsureComp<KsZLevelComponent>(lowerMapUid);
        EntityUid upperMapUid;
        if (_zLevelSystem.TryGetZLevelAbove((lowerMapUid, lowerZLevelComponent), out var aboveEntity))
        {
            upperMapUid = aboveEntity.Value.Owner;
            if (!CanBuildAbove(builder, upperMapUid, spot.Position, out var reason))
            {
                Refuse(builder, userUid, reason);
                return false;
            }
        }
        else
        {
            if (_useDelaySystem.IsDelayed(builder.Owner, builder.Comp.LevelDelayId))
            {
                Refuse(builder, userUid, "ks-zlevel-builder-cooldown");
                return false;
            }

            upperMapUid = _zLevelEnvironmentSystem.CreateZLevelAbove((lowerMapUid, lowerZLevelComponent));
            if (TryComp<UseDelayComponent>(builder, out var useDelayComponent))
                _useDelaySystem.TryResetDelay((builder, useDelayComponent), id: builder.Comp.LevelDelayId);
        }

        var upperCoordinates = ResolveUpperFloor(builder, upperMapUid, spot.Position, _transformSystem.GetWorldRotation(lowerGridUid));

        var lowerLadderUid = Spawn(builder.Comp.LowerLadder, lowerCoordinates);
        var upperLadderUid = Spawn(builder.Comp.UpperLadder, upperCoordinates);
        _ladderSystem.Link(lowerLadderUid, upperLadderUid);

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-zlevel-builder-built"), builder, userUid);
        return true;
    }

    private bool CanBuildAbove(Entity<KsZLevelBuilderComponent> builder, EntityUid upperMapUid, Vector2 position, out string reason)
    {
        reason = string.Empty;
        if (HasLadderAt(builder, new MapCoordinates(position, Comp<MapComponent>(upperMapUid).MapId)))
        {
            reason = "ks-zlevel-builder-ladder-above";
            return false;
        }

        if (_mapSystem.TryFindGridAt(upperMapUid, position, out var upperGridUid, out var upperGridComponent))
        {
            var upperIndices = _mapSystem.WorldToTile(upperGridUid, upperGridComponent, position);
            if (_turfSystem.IsTileBlocked(upperGridUid, upperIndices, CollisionGroup.Impassable, upperGridComponent))
            {
                reason = "ks-zlevel-builder-blocked-above";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     The centre of the tile at <paramref name="position"/> on the z-level above: on the grid already there, or on a
    ///         new static one-tile grid lined up with the lower one.
    /// </summary>
    private EntityCoordinates ResolveUpperFloor(Entity<KsZLevelBuilderComponent> builder, EntityUid upperMapUid, Vector2 position, Angle rotation)
    {
        if (_mapSystem.TryFindGridAt(upperMapUid, position, out var existingGridUid, out var existingGridComponent))
        {
            var existingIndices = _mapSystem.WorldToTile(existingGridUid, existingGridComponent, position);
            return _mapSystem.GridTileToLocal(existingGridUid, existingGridComponent, existingIndices);
        }

        var newGrid = _mapSystem.CreateGridEntity(upperMapUid);

        // Tile (0, 0) spans the grid's origin to (1, 1), so its centre lands on the spot.
        var origin = position - rotation.RotateVec(new Vector2(0.5f, 0.5f));
        _transformSystem.SetWorldPositionRotation(newGrid.Owner, origin, rotation);

        var tileDefinition = _tileDefinitionManager[builder.Comp.Tile];
        _mapSystem.SetTile(newGrid.Owner, newGrid.Comp, Vector2i.Zero, new Tile(tileDefinition.TileId));

        // Static, not dynamic as grids are made: nothing pushes it, nothing sets it drifting.
        _physicsSystem.SetBodyType(newGrid.Owner, BodyType.Static);

        return _mapSystem.GridTileToLocal(newGrid.Owner, newGrid.Comp, Vector2i.Zero);
    }

    private bool HasLadderAt(Entity<KsZLevelBuilderComponent> builder, MapCoordinates coordinates)
    {
        _ladders.Clear();
        _entityLookupSystem.GetEntitiesInRange(coordinates, builder.Comp.LadderClearance, _ladders);
        return _ladders.Count > 0;
    }

    private void Refuse(Entity<KsZLevelBuilderComponent> builder, EntityUid userUid, string messageId)
    {
        _popupSystem.PopupEntity(KsPopupMessage.Create(messageId), builder, userUid);
    }
}
