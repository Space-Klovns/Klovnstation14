namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Raised on a gun when something ends its shooting outright, such as a clumsy backfire, after its trigger was
///         released and its burst cut short. Whatever keeps a gun firing on its own stops here.
/// </summary>
/// <seealso cref="Content.Shared.Weapons.Ranged.Systems.SharedGunSystem.KsInterruptShooting"/>
[ByRefEvent]
public readonly record struct KsGunShootingInterruptedEvent;
