using Content.Shared.Gravity;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared._KS14.ZLevel;

/// <summary>
///     Ting go down
/// </summary>
public sealed class KsZLevelPhysicsSystem : EntitySystem
{
    [Dependency] private readonly KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private readonly SharedGravitySystem _gravitySystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    [Dependency] private readonly EntityQuery<ThrownItemComponent> _thrownItemQuery = default!;
    [Dependency] private readonly EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private readonly EntityQuery<MapComponent> _mapQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhysicsComponent, EntParentChangedMessage>(OnPhysicsParentChanged);
        SubscribeLocalEvent<PhysicsComponent, LandEvent>(OnPhysicsLand);
    }

    private bool Fall(Entity<TransformComponent> entity)
    {
        if (entity.Comp.MapID == MapId.Nullspace)
            return false;

        if (!_zLevelSystem.TryGetStackFromDescendant(entity!, out var zLevelEntity, out _) ||
            zLevelEntity.Comp.Node.Previous?.Value is not { } lowerZLevelEntity)
            return false;

        if (entity.Comp.GridUid is { } gridUid)
        {
            var gridComponent = _mapGridQuery.GetComponent(gridUid);
            var tileRef = _mapSystem.GetTileRef((gridUid, gridComponent)!, entity.Comp.Coordinates);
            if (!tileRef.Tile.IsEmpty)
                return false;
        }

        var lowerMapComponent = _mapQuery.GetComponent(lowerZLevelEntity);
        _transformSystem.SetMapCoordinates(entity, new MapCoordinates(
            _transformSystem.GetWorldPosition(entity.Comp),
            lowerMapComponent.MapId
        ));

        _popupSystem.PopupClient("You are fallen down", entity.Owner, entity.Owner);
        return true;
    }

    private void OnPhysicsParentChanged(Entity<PhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        if (_thrownItemQuery.HasComponent(entity.Owner) ||
            _gravitySystem.IsWeightless(entity.Owner))
            return;

        Fall((entity.Owner, Transform(entity)));
    }

    private void OnPhysicsLand(Entity<PhysicsComponent> entity, ref LandEvent args)
    {
        Fall((entity.Owner, Transform(entity)));
    }
}
