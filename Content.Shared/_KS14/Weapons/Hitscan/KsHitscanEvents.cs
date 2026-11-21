using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Shared._KS14.Weapons.Hitscan;

/// <summary>
///     Raised on the shooter of a hitscan shot that hit something, just before its damage is applied: what that damage
///         becomes. The hitscan counterpart of the mutable <c>ProjectileHitEvent</c>, which bullets have and hitscan
///         had nothing like. Pure: handlers only scale or replace <see cref="Damage"/>.
/// </summary>
[ByRefEvent]
public record struct KsHitscanDamageModifyEvent(HitscanRaycastFiredData Data, DamageSpecifier Damage);

/// <summary>
///     Raised on the shooter of a hitscan shot after it damaged what it hit.
/// </summary>
[ByRefEvent]
public readonly record struct KsShooterHitscanHitEvent(HitscanRaycastFiredData Data, DamageSpecifier DamageDealt);

/// <summary>
///     Broadcast on the server for a hitscan shot with a shooter, once its ray has been cast and before what it hit is
///         picked from <see cref="Results"/>: what the ray is taken to have crossed, nearest first. The server puts the
///         mobs a lagging shooter aimed at back where that shooter saw them. Pure: handlers only change
///         <see cref="Results"/>, and leave it nearest first.
/// </summary>
/// <param name="Direction">The way the ray goes, normalised.</param>
/// <param name="CollisionMask">What the ray collides with.</param>
[ByRefEvent]
public record struct KsModifyHitscanRaycastResultsEvent(
    EntityUid Shooter,
    MapId MapId,
    Vector2 Origin,
    Vector2 Direction,
    float MaxDistance,
    int CollisionMask,
    List<RayCastResults> Results);
