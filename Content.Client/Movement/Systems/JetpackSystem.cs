// KS14: This file was reverted to https://github.com/LaCumbiaDelCoronavirus/ss14/blob/a21e8dd444ad5643e9e315dc5c4a06e30322276e/
//      which is, `allow jetpacks to stay enabled when on grids and lightly rework jetpack system #37775` on wizden Github

using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client.Movement.Systems;

public sealed class JetpackSystem : SharedJetpackSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JetpackComponent, AppearanceChangeEvent>(OnJetpackAppearance);
    }

    // No predicted atmos so you'd have to do a lot of funny to get this working.
    protected override bool CanEnable(Entity<JetpackComponent> jetpack)
        => false;

    private void OnJetpackAppearance(Entity<JetpackComponent> jetpack, ref AppearanceChangeEvent args)
    {
        var uid = jetpack.Owner;
        Appearance.TryGetData<bool>(uid, JetpackVisuals.Enabled, out var enabled, args.Component);

        if (TryComp<ClothingComponent>(uid, out var clothing))
            _clothing.SetEquippedPrefix(uid, enabled ? "on" : null, clothing);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted ||
            _timing.ApplyingState)
            return;

        // TODO: Please don't copy-paste this I beg
        // make a generic particle emitter system / actual particles instead.
        var query = EntityQueryEnumerator<ActiveJetpackComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var transform = Transform(uid);
            var currentCoords = _transform.GetMoverCoordinates(transform.Coordinates);

            if (comp.LastCoordinates is not { })
            {
                comp.LastCoordinates = currentCoords;
                continue;
            }

            // Only spawn particles if we're far-enough from the last place at which we spawned particles, and a long-enough timespan has passed since then.
            if (_transform.InRange(transform.Coordinates, comp.LastCoordinates.Value, comp.MaxDistance) && _timing.CurTime < comp.TargetTime)
                continue;

            comp.LastCoordinates = currentCoords;
            comp.TargetTime = _timing.CurTime + comp.EffectCooldown;
            CreateParticles(uid, transform);
        }
    }

    private void CreateParticles(EntityUid uid, TransformComponent uidXform)
    {
        // Don't show particles unless the user is moving.
        if (Container.TryGetContainingContainer((uid, uidXform, null), out var container) &&
            TryComp<PhysicsComponent>(container.Owner, out var body) &&
            body.LinearVelocity.LengthSquared() < 1f)
        {
            return;
        }

        var coordinates = uidXform.Coordinates;
        var gridUid = _transform.GetGrid(coordinates);

        if (TryComp<MapGridComponent>(gridUid, out var grid))
        {
            coordinates = new EntityCoordinates(gridUid.Value, _mapSystem.WorldToLocal(gridUid.Value, grid, _transform.ToMapCoordinates(coordinates).Position));
        }
        else if (uidXform.MapUid != null)
        {
            coordinates = new EntityCoordinates(uidXform.MapUid.Value, _transform.GetWorldPosition(uidXform));
        }
        else
            return;

        Spawn("JetpackEffect", coordinates);
    }
}
