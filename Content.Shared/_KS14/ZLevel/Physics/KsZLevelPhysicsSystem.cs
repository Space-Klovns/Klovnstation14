using Content.Shared.Gravity;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.ZLevel.Physics;

/// <summary>
///     Ting go down
/// </summary>
public sealed class KsZLevelPhysicsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private readonly SharedGravitySystem _gravitySystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    [Dependency] private readonly EntityQuery<KsSuspendedZLevelFallComponent> _suspendedFallQuery = default!;
    [Dependency] private readonly EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private readonly EntityQuery<MapComponent> _mapQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsSuspendedZLevelFallComponent, WeightlessnessChangedEvent>(OnWeightlessnessChanged);

        SubscribeLocalEvent<PhysicsComponent, EntParentChangedMessage>(OnPhysicsParentChanged,
            after: [typeof(Shared.Movement.Systems.SharedJetpackSystem)]); // So that you dont fall when using a jetpack
        SubscribeLocalEvent<PhysicsComponent, LandEvent>(OnPhysicsLand);
    }

    private void OnWeightlessnessChanged(Entity<KsSuspendedZLevelFallComponent> entity, ref WeightlessnessChangedEvent args)
    {
        if (!args.Weightless)
            return;

        var transformComponent = Transform(entity);
        Fall((entity.Owner, transformComponent), zLevelEntity: _zLevelSystem.GetZLevel((entity.Owner, transformComponent)));
    }

    private void OnPhysicsParentChanged(Entity<PhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        if (_gameTiming.ApplyingState)
            return;

        var transformComponent = args.Transform;
        if (entity.Comp.BodyStatus == BodyStatus.InAir ||
            _gravitySystem.IsWeightless(entity.Owner))
        {
            if (_zLevelSystem.TryGetZLevel((entity, transformComponent), out _))
                EnsureComp<KsSuspendedZLevelFallComponent>(entity.Owner);
            else if (_suspendedFallQuery.TryGetComponent(entity, out var suspendedZLevelFallComponent))
                RemComp(entity, suspendedZLevelFallComponent);

            return;
        }

        Fall((entity, transformComponent));
    }

    private void OnPhysicsLand(Entity<PhysicsComponent> entity, ref LandEvent args)
    {
        Fall((entity.Owner, Transform(entity)));
    }

    public bool Fall(Entity<TransformComponent> entity, Entity<KsZLevelComponent>? zLevelEntity = null)
    {
        if (entity.Comp.MapID == MapId.Nullspace)
            return false;

        if (zLevelEntity is not { } &&
            !_zLevelSystem.TryGetZLevel(entity!, out zLevelEntity))
            return false;

        if (zLevelEntity.Value.Comp.Node.Previous?.Value is not { } lowerZLevelEntity)
            return false;

        // Make sure we can actually fall
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

        RemComp<KsSuspendedZLevelFallComponent>(entity);
        _popupSystem.PopupClient("You are fallen down", entity.Owner, entity.Owner);
        return true;
    }

    /// <summary>
    ///     Tries to make the fall if it is above a z-level and can fall.
    /// </summary>
    /// <returns>If the entity actually fell down.</returns>
    public bool TryFall(Entity<TransformComponent?> entity)
    {
        entity.Comp ??= Transform(entity.Owner);
        return Fall(entity!);
    }
}
