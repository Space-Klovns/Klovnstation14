using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Raised on whoever fired a gun, once per shot, after everything it fired has been spawned and sent on its way:
///         the holder-side counterpart of <see cref="KsGunFiredProjectilesEvent"/>, so abilities of the shooter
///         rather than of the gun can change what was fired. Raised for hitscan shots too, with no projectiles.
/// </summary>
/// <param name="Gun">The gun that fired.</param>
/// <param name="ProjectileUids">The projectiles fired; empty for a hitscan shot.</param>
[ByRefEvent]
public readonly record struct KsShooterFiredGunEvent(Entity<GunComponent> Gun, IReadOnlyList<EntityUid> ProjectileUids);
