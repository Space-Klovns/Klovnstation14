using System.Numerics;
using Content.Shared._KS14.DodgingEffect;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.GunDodger;

public sealed partial class GunDodgerSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private DodgingEffectSystem _dodgingEffectSystem = default!;

    [Dependency] private EntityQuery<GunDodgerComponent> _dodgerQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    private static readonly LocId PopupLocId = "gun-dodger-dodge-popup";

    private const CollisionGroup DefaultCollisionGroup = CollisionGroup.Impassable | CollisionGroup.BulletImpassable;
    private static readonly QueryFilter DefaultQueryFilter = new() { LayerBits = 0L, Flags = QueryFlags.Dynamic, MaskBits = (long)DefaultCollisionGroup };

    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<GunDodgerComponent> entity, ref PreventCollideEvent args)
    {
        if (args.Cancelled ||
            !_projectileQuery.HasComponent(args.OtherEntity) ||
            !_mobStateSystem.IsAlive(entity.Owner))
            return;

        // No roll of its own: a projectile passes through only while a dodge that TryDodge rolled for its shot is
        //      under way. Anything a projectile carries can't be rolled on, since a predicted projectile's id differs
        //      between client and server, and rolling separately here would let the dodger be thrown clear of a shot
        //      and hit by it anyway. At a chance of 1 every roll succeeds, so there is nothing to wait for.
        if (entity.Comp.DodgeChance < 1f && _gameTiming.CurTime >= entity.Comp.DodgeEndTime)
            return;

        args.Cancelled = true;
    }

    // This needs to be improved in the future but idgaf
    [SubscribeLocalEvent]
    private void OnGunShot(ref GunShotEvent args)
    {
        // gundodgers can not dodge bullets shot by other gundodgers, or if they arent alive
        if (_dodgerQuery.HasComponent(args.User))
            return;

        var userTransformComponent = Transform(args.User);
        var fromPosition = _transformSystem.ToWorldPosition(args.FromCoordinates);

        var delta = _transformSystem.ToWorldPosition(args.ToCoordinates) - fromPosition;
        Vector2Helpers.Normalize(ref delta);
        delta *= 25f;

        var rayResults = _rayCastSystem.CastRayClosest(userTransformComponent.MapID, fromPosition, delta, DefaultQueryFilter);
        if (rayResults.Results.Count == 0)
            return;

        var rayResult = rayResults.Results[0];
        TryDodge(rayResult.Entity, rayResult.LocalNormal, args.User);
    }

    [SubscribeLocalEvent]
    private void OnAttemptHitscan(ref AttemptHitscanRaycastFiredEvent args)
    {
        if (args.Cancelled ||
            args.Data.HitEntity is not { } hitUid ||
            !_dodgerQuery.TryGetComponent(hitUid, out var dodgerComponent) ||
            !_mobStateSystem.IsAlive(hitUid))
            return;

        // again: gundodgers can not dodge bullets shot by other gundodgers
        if (args.Data.Shooter is { } shooterUid &&
            _dodgerQuery.HasComponent(shooterUid))
            return;

        // Same seed as the roll in TryDodge, which runs this same tick, so a hitscan shot is only let through when
        //      the dodger wasn't also thrown out of its way.
        if (!RollDodge((hitUid, dodgerComponent), (int)_gameTiming.CurTick.Value).Dodged)
            return;

        // This is alredy dodged in GunShotEvent

        args.Cancelled = true;
    }

    private void TryDodge(Entity<GunDodgerComponent?> dodgerEntity, Vector2 localNormal, EntityUid? userUid)
    {
        if (!_dodgerQuery.Resolve(dodgerEntity.Owner, ref dodgerEntity.Comp, logMissing: false) ||
            !_mobStateSystem.IsAlive(dodgerEntity.Owner))
            return;

        var (dodged, predictedRandom) = RollDodge((dodgerEntity.Owner, dodgerEntity.Comp), (int)_gameTiming.CurTick.Value);
        if (!dodged)
            return;

        dodgerEntity.Comp.DodgeEndTime = _gameTiming.CurTime + dodgerEntity.Comp.DodgeWindow;
        Dirty(dodgerEntity.Owner, dodgerEntity.Comp);

        // Everyone who can see the dodger, predicted for whoever fired.
        _popupSystem.PopupEntity(Loc.GetString(PopupLocId, ("name", Identity.Name(dodgerEntity.Owner, EntityManager))), dodgerEntity.Owner, type: PopupType.Small);
        _dodgingEffectSystem.AddEffect(dodgerEntity.Owner, TimeSpan.FromSeconds(0.01d), TimeSpan.FromSeconds(0.7d));

        // only dodge perpendicularly to the shooting direction. The normal has to be transformed because LocalNormal
        //      IS NOT ACTUALLY LOCAL
        var invMatrix = _transformSystem.GetWorldMatrix(Transform(dodgerEntity.Owner).ParentUid);
        localNormal = Vector2.TransformNormal(localNormal, invMatrix);

        // equal random direction perpendicular to normal
        var throwDirection = (predictedRandom.NextDouble() < 0.5d) ? new Vector2(-localNormal.Y, localNormal.X) : new Vector2(localNormal.Y, -localNormal.X);

        _throwingSystem.TryThrow(dodgerEntity.Owner, throwDirection, baseThrowSpeed: dodgerEntity.Comp.ThrowSpeed, user: userUid, pushbackRatio: 0f, predicted: true);
    }

    /// <summary>
    ///     Rolls <see cref="GunDodgerComponent.DodgeChance"/> from a seed of the dodger and <paramref name="salt"/>,
    ///         so the client and server agree on it. The same dodger and salt always give the same result.
    /// </summary>
    /// <returns>
    ///     Whether the dodge succeeded, and the seeded random with the chance already drawn from it, for any further
    ///         predicted rolls.
    /// </returns>
    private (bool Dodged, System.Random PredictedRandom) RollDodge(Entity<GunDodgerComponent> dodgerEntity, int salt)
    {
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(KsSharedRandomExtensions.GetNetId(dodgerEntity.Owner, EntityManager), salt);
        var dodged = predictedRandom.NextDouble() < (double)dodgerEntity.Comp.DodgeChance;

        return (dodged, predictedRandom);
    }
}
