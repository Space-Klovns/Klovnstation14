namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Raised on a gun once per shot, after everything it fired has been spawned and sent on its way. Unlike
///         <see cref="Content.Shared.Weapons.Ranged.Events.AmmoShotEvent"/>, which a cartridge gun raises more than
///         once for the same projectiles, this is safe to change them from.
/// </summary>
/// <param name="ProjectileUids">Everything that was fired, or thrown by a gun that throws its ammo.</param>
[ByRefEvent]
public readonly record struct KsGunFiredProjectilesEvent(IReadOnlyList<EntityUid> ProjectileUids, EntityUid? ShooterUid);
