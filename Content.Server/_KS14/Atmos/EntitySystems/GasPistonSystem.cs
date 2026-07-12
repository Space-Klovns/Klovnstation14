using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos.Components;
using Robust.Server.GameObjects;
using Content.Shared.Atmos;
using Content.Shared._KS14.GenericSpriteFlick;
using Content.Shared._KS14.Atmos.Components;
using Robust.Shared.Physics.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Server.Audio;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Physics;
using Content.Shared._KS14.Atmos.EntitySystems;
using Content.Shared.Throwing;
using Content.Server.Administration.Logs;
using Content.Shared.Database;
using Robust.Shared.Physics.Systems;
using Content.Shared.Damage.Components;
using Robust.Shared.Utility;
using Robust.Shared.Map.Components;
using System.Numerics;
using Content.Shared.Popups;

namespace Content.Server._KS14.Atmos.EntitySystems;

// This could use a cooldown MAYBE but AtmosDeviceUpdateEvent works too and im lazy

public sealed partial class GasPistonSystem : SharedGasPistonSystem
{
    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    [Dependency] private KsGenericSpriteFlickSystem _spriteFlickSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private PhysicsSystem _physicsSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private FixtureSystem _fixtureSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private MapSystem _mapSystem = default!;

    private const LookupFlags InitLookupFlags = LookupFlags.Approximate | LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Uncontained;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasPistonComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasPistonComponent, AtmosDeviceUpdateEvent>(OnUpdate);
    }

    // this horrible approach is taken because startcollideevent and endcollideevent dont like anchored objects
    private void OnMapInit(Entity<GasPistonComponent> entity, ref MapInitEvent args)
    {
        PopulateColliding(entity);
        entity.Comp.CollidingUids.TrimExcess();
    }

    private void PopulateColliding(Entity<GasPistonComponent> entity)
    {
        if (_fixtureSystem.GetFixtureOrNull(entity.Owner, entity.Comp.FixtureId) is not { } fixture)
            return;

        var transformComponent = Transform(entity);
        var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
        var transform = new Transform(worldPosition, worldRotation);

        _entityLookupSystem.GetEntitiesIntersecting(transformComponent.MapID, fixture.Shape, transform, entity.Comp.CollidingUids, flags: InitLookupFlags);
        entity.Comp.CollidingUids.Remove(entity);

        var remQueue = new RemQueue<EntityUid>();
        foreach (var otherUid in entity.Comp.CollidingUids)
        {
            if (CanCollideWith(fixture.CollisionMask, fixture.CollisionLayer, otherUid))
                continue;

            remQueue.Add(otherUid);
        }

        foreach (var removedUid in remQueue)
            entity.Comp.CollidingUids.Remove(removedUid);
    }

    private bool CanCollideWith(int fromMask, int fromLayer, EntityUid toUid)
    {
        if (!TryComp<FixturesComponent>(toUid, out var fixturesComponent))
            return false;

        foreach (var (_, fixture) in fixturesComponent.Fixtures)
        {
            if ((fromMask & fixture.CollisionLayer) == 0 ||
                (fromLayer & fixture.CollisionMask) == 0)
                continue;

            return true;
        }

        return false;
    }

    private void OnStartCollide(Entity<GasPistonComponent> entity, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != entity.Comp.FixtureId)
            return;

        entity.Comp.CollidingUids.Add(args.OtherEntity);
    }

    private void OnEndCollide(Entity<GasPistonComponent> entity, ref EndCollideEvent args)
    {
        if (args.OurFixtureId != entity.Comp.FixtureId)
            return;

        entity.Comp.CollidingUids.Remove(args.OtherEntity);
    }

    private void OnUpdate(Entity<GasPistonComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.InletName, out PipeNode? inlet))
            return;

        var inletAir = inlet.Air;
        var pressure = inletAir.Pressure;
        var minPressure = entity.Comp.PressureRange.X;

        if (entity.Comp.Extended)
        {
            if (pressure < minPressure)
                Retract(entity);
        }
        else if (pressure >= minPressure)
            Extend(entity, inletAir);
    }

    /// <summary>
    ///     Assumes pressure >= minPressure
    /// </summary>
    private void Extend(Entity<GasPistonComponent> entity, GasMixture air)
    {
        var pressure = air.Pressure;
        var minPressure = entity.Comp.PressureRange.X;
        var maxPressure = entity.Comp.PressureRange.Y;

        // fraction to max pressure from 0-1 if capped
        var fraction = (pressure - minPressure) / (maxPressure - minPressure);
        if (entity.Comp.Capped &&
            fraction > 1f)
            fraction = 1f;

        var damage = (entity.Comp.MaximumDamage - entity.Comp.MinimumDamage) * (FixedPoint2)fraction + entity.Comp.MinimumDamage;
        var totalDamage = damage.GetTotal();
        var transformComponent = Transform(entity);

        var throwVector = transformComponent.LocalRotation.ToWorldVec();
        var throwForce = entity.Comp.MaxThrowForce * fraction;

        entity.Comp.CollidingUids.Clear();
        PopulateColliding(entity);

        foreach (var collidingUid in entity.Comp.CollidingUids)
        {
            _damageableSystem.ChangeDamage(collidingUid, damage, origin: entity.Owner);
            _throwingSystem.TryThrow(collidingUid, throwVector, baseThrowSpeed: throwForce, user: entity.Owner, predicted: false);

            _adminLogManager.Add(LogType.Damaged, $"{ToPrettyString(entity.Owner):user} dealt {totalDamage:total} damageto {ToPrettyString(entity.Owner):target} via piston, power-scale: {fraction}x");
        }

        if (entity.Comp.CollidingUids.Count == 0 &&
            totalDamage >= 10)
            _adminLogManager.Add(LogType.Damaged, $"{ToPrettyString(entity.Owner):gas-piston} pushed and damaged {entity.Comp.CollidingUids.Count} entities, power-scale: {fraction}x");

        var blockedTileOffset = transformComponent.LocalRotation.RotateVec(entity.Comp.BlockedTileOffset);
        var blockedTileOffsetInteger = new Vector2i((int)MathF.Round(blockedTileOffset.X), (int)MathF.Round(blockedTileOffset.Y));
        if (AnyAnchoredEntities((entity, transformComponent), blockedTileOffsetInteger))
        {
            _audioSystem.PlayPvs(entity.Comp.BlockedSound, entity.Owner);
            PopupSystem.PopupEntity(Loc.GetString("gas-piston-popup-obstructed"), entity.Owner, type: PopupType.MediumCaution);
        }
        else
        {
            _spriteFlickSystem.TryFlick(entity, entity.Comp.FlickData);
            _audioSystem.PlayPvs(entity.Comp.Sound, entity.Owner);
            SetExtended(entity, true);
        }

        if (entity.Comp.RemovedGasRatio == 0f ||
            _atmosphereSystem.GetContainingMixture(entity.Owner, excite: true) is not { } environmentAir)
            return;

        var removedAir = air.RemoveRatio(entity.Comp.RemovedGasRatio);
        _atmosphereSystem.Merge(environmentAir, removedAir);
    }

    private void Retract(Entity<GasPistonComponent> entity)
    {
        SetExtended(entity, false);
    }

    private void SetExtended(Entity<GasPistonComponent> entity, bool value)
    {
        entity.Comp.Extended = value;
        Dirty(entity);

        SetCollider(entity, value);

        _appearanceSystem.SetData(entity.Owner, GasPistonVisuals.Extended, value);

        if (entity.Comp.FlickData is { } flickData)
            _spriteFlickSystem.ResetFlickFinishState(entity.Owner, flickData);
    }

    private void SetCollider(Entity<GasPistonComponent> entity, bool value)
    {
        if (!TryComp<FixturesComponent>(entity.Owner, out var fixturesComponent) ||
            !fixturesComponent.Fixtures.TryGetValue(entity.Comp.FixtureId, out var fixture))
            return;

        _physicsSystem.SetHard(entity.Owner, fixture, value, manager: fixturesComponent);
    }

    private bool AnyAnchoredEntities(Entity<TransformComponent> entity, Vector2i tileOffset)
    {
        if (entity.Comp.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return false;

        var tileIndices = _mapSystem.CoordinatesToTile(gridUid, grid: gridComponent, coords: entity.Comp.Coordinates) + tileOffset;
        var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, gridComponent, tileIndices);

        while (enumerator.MoveNext(out var otherUid))
        {
            if (otherUid == entity.Owner)
                continue;

            return true;
        }

        return false;
    }
}
