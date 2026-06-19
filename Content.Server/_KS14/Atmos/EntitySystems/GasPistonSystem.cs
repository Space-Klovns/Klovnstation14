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

namespace Content.Server._KS14.Atmos.EntitySystems;

// This could use a cooldown MAYBE but AtmosDeviceUpdateEvent works too and im lazy

public sealed class GasPistonSystem : SharedGasPistonSystem
{
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly KsGenericSpriteFlickSystem _spriteFlickSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasPistonComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<GasPistonComponent, EndCollideEvent>(OnEndCollide);

        SubscribeLocalEvent<GasPistonComponent, AtmosDeviceUpdateEvent>(OnUpdate);
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
        SetExtended(entity, true);

        var pressure = air.Pressure;
        var minPressure = entity.Comp.PressureRange.X;
        var maxPressure = entity.Comp.PressureRange.Y;

        // fraction to max pressure from 0-1 if capped
        var fraction = (pressure - minPressure) / (maxPressure - minPressure);
        if (entity.Comp.Capped &&
            fraction > 1f)
            fraction = 1f;

        var damage = (entity.Comp.MaximumDamage - entity.Comp.MinimumDamage) * (FixedPoint2)fraction + entity.Comp.MinimumDamage;
        var transformComponent = Transform(entity);

        var throwVector = transformComponent.LocalRotation.ToWorldVec();
        var throwForce = entity.Comp.MaxThrowForce * fraction;

        foreach (var collidingUid in entity.Comp.CollidingUids)
        {
            _damageableSystem.TryChangeDamage(collidingUid, damage, origin: entity.Owner);
            _throwingSystem.TryThrow(collidingUid, throwVector, baseThrowSpeed: throwForce, user: entity.Owner, predicted: false);
        }

        _audioSystem.PlayPvs(entity.Comp.Sound, entity.Owner);
        _spriteFlickSystem.TryFlick(entity, entity.Comp.FlickData);

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
}
