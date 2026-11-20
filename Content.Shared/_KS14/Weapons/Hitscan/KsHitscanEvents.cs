using Content.Shared.Damage;
using Content.Shared.Weapons.Hitscan.Events;

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
