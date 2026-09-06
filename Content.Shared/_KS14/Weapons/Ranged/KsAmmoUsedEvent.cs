namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Raised on an ammo entity after it is spent to fire (a) projectile(s).
/// </summary>
/// <param name="ProjectileUids">List of projectiles that were fired.</param>
[ByRefEvent]
public readonly record struct KsAmmoUsedEvent(IReadOnlyList<EntityUid> ProjectileUids, EntityUid? ShooterUid);
