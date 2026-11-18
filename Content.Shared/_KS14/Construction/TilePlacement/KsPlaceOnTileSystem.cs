using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Administration.Logs;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Construction.TilePlacement;

/// <summary>
///     Builds a <see cref="KsPlaceOnTileComponent"/>'s structure on the tile its holder clicks. Shared and predicted: the
///         do-after, the stack it uses and the structure (an engine stand-in until the server's arrives) all show on the
///         builder's client at once.
/// </summary>
public sealed partial class KsPlaceOnTileSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private AnchorableSystem _anchorableSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStackSystem _stackSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<KsPlaceOnTileComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        // Clicked off every grid, or on space: nothing to build on, and nothing to say.
        if (!TryGetPlacement(args.ClickLocation, out var grid, out _))
            return;

        args.Handled = true;

        if (!HasEnough(entity))
        {
            _popupSystem.PopupCoordinates(KsPopupMessage.Create(entity.Comp.NotEnoughPopup, ("count", entity.Comp.StackCost)),
                args.ClickLocation, args.User);
            return;
        }

        if (!CanPlace(entity, args.ClickLocation))
        {
            _popupSystem.PopupCoordinates(KsPopupMessage.Create(entity.Comp.BlockedPopup), args.ClickLocation, args.User);
            return;
        }

        if (entity.Comp.DoAfter <= TimeSpan.Zero)
        {
            Place(entity, args.User, args.ClickLocation);
            return;
        }

        // Kept against the grid: the click may be relative to something clicked on, gone by the time the do-after ends.
        var gridCoordinates = _transformSystem.WithEntityId(args.ClickLocation, grid.Owner);
        var doAfterEvent = new KsPlaceOnTileDoAfterEvent(GetNetCoordinates(gridCoordinates));
        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, entity.Comp.DoAfter, doAfterEvent,
            eventTarget: entity.Owner, used: entity.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnDoAfter(Entity<KsPlaceOnTileComponent> entity, ref KsPlaceOnTileDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        var coordinates = GetCoordinates(args.Coordinates);
        if (!HasEnough(entity))
        {
            _popupSystem.PopupCoordinates(KsPopupMessage.Create(entity.Comp.NotEnoughPopup, ("count", entity.Comp.StackCost)),
                coordinates, args.User);
            return;
        }

        // Someone may have built there, or walled it off, while this one was busy.
        if (!CanPlace(entity, coordinates))
        {
            _popupSystem.PopupCoordinates(KsPopupMessage.Create(entity.Comp.BlockedPopup), coordinates, args.User);
            return;
        }

        Place(entity, args.User, coordinates);
    }

    /// <summary>
    ///     Whether <paramref name="entity"/> could build on the tile at <paramref name="coordinates"/> now.
    /// </summary>
    public bool CanPlace(Entity<KsPlaceOnTileComponent> entity, EntityCoordinates coordinates)
    {
        if (!TryGetPlacement(coordinates, out var grid, out var tileRef))
            return false;

        if (!entity.Comp.RequireFreeTile)
            return true;

        // Walls, tables, sandbags and the like: what a mob would bump into.
        if (_turfSystem.IsTileBlocked(tileRef, CollisionGroup.MobMask))
            return false;

        // Anything anchored there that the structure's own hard fixtures would collide with.
        if (ProtoMan.Resolve(entity.Comp.Prototype, out var prototype) &&
            prototype.TryGetComponent<FixturesComponent>(out var fixturesComponent, Factory))
        {
            var (layer, mask) = SharedPhysicsSystem.GetHardCollision(fixturesComponent);
            if ((layer | mask) != 0 && !_anchorableSystem.TileFree(grid, tileRef.GridIndices, layer, mask))
                return false;
        }

        // Nothing stops a second roll of wire on top of the first but this.
        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(grid, grid.Comp, tileRef.GridIndices);
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (MetaData(anchoredUid.Value).EntityPrototype?.ID == entity.Comp.Prototype.Id)
                return false;
        }

        return true;
    }

    private bool TryGetPlacement(EntityCoordinates coordinates, out Entity<MapGridComponent> grid, out TileRef tileRef)
    {
        grid = default;
        tileRef = default;

        // A click is relative to whatever was under the cursor, not necessarily the grid: find the grid by position.
        var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
        if (!_mapSystem.TryFindGridAt(mapCoordinates, out var gridUid, out var gridComponent))
            return false;

        grid = (gridUid, gridComponent);
        tileRef = _mapSystem.GetTileRef(gridUid, gridComponent, _mapSystem.WorldToTile(gridUid, gridComponent, mapCoordinates.Position));
        return !tileRef.Tile.IsEmpty && !_turfSystem.IsSpace(tileRef);
    }

    private bool HasEnough(Entity<KsPlaceOnTileComponent> entity)
    {
        return !_stackQuery.TryComp(entity, out var stackComponent) ||
               stackComponent.Unlimited ||
               stackComponent.Count >= entity.Comp.StackCost;
    }

    private void Place(Entity<KsPlaceOnTileComponent> entity, EntityUid user, EntityCoordinates coordinates)
    {
        if (!TryGetPlacement(coordinates, out var grid, out var tileRef))
            return;

        // Pay first: a stack that can't pay builds nothing.
        var stackComponent = _stackQuery.CompOrNull(entity);
        if (stackComponent != null && !_stackSystem.TryUse((entity.Owner, stackComponent), entity.Comp.StackCost))
            return;

        var spawnCoordinates = entity.Comp.SnapToGrid
            ? _mapSystem.GridTileToLocal(grid, grid.Comp, tileRef.GridIndices)
            : coordinates;

        var builtUid = PredictedSpawnAtPosition(entity.Comp.Prototype, spawnCoordinates);
        if (entity.Comp.SnapToGrid)
            _transformSystem.SetLocalRotation(builtUid, Angle.Zero);

        _adminLogger.Add(LogType.Construction, LogImpact.Low,
            $"{ToPrettyString(user):player} built {ToPrettyString(builtUid):entity} at {spawnCoordinates} with {ToPrettyString(entity.Owner):entity}");

        if (stackComponent == null)
            PredictedQueueDel(entity.Owner);
    }
}

/// <summary>
///     The end of building a <see cref="KsPlaceOnTileComponent"/>'s structure on the tile at <see cref="Coordinates"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KsPlaceOnTileDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetCoordinates Coordinates;

    private KsPlaceOnTileDoAfterEvent()
    {
    }

    public KsPlaceOnTileDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => this;
}
