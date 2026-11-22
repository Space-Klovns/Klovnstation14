using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Raised on whoever is firing a gun, once per real shot, right before the gun gives up any ammo for it. Cancelling
///         calls the whole shot off with the gun untouched: nothing is taken, spawned, spent or ejected. A clumsy
///         backfire happens here.
/// </summary>
/// <remarks>
///     Not raised for a gun with nothing to fire, whose shot is an empty click instead.
/// </remarks>
/// <param name="Gun">The gun being fired.</param>
[ByRefEvent]
public record struct KsShooterFireAttemptEvent(Entity<GunComponent> Gun, bool Cancelled = false);
