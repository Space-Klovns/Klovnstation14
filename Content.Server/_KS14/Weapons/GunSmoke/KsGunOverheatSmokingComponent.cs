namespace Content.Server._KS14.Weapons.GunSmoke;

/// <summary>
///     Marks a <see cref="KsGunOverheatSmokeComponent"/> gun that is smoking now, so only those are checked each tick.
/// </summary>
[RegisterComponent, Access(typeof(KsGunOverheatSmokeSystem))]
public sealed partial class KsGunOverheatSmokingComponent : Component;
