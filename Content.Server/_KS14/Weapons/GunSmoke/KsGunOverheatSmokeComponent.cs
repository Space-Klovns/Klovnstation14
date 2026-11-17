namespace Content.Server._KS14.Weapons.GunSmoke;

/// <summary>
///     A purely visual heat: each shot adds <see cref="HeatPerShot"/>, it cools by <see cref="CoolPerSecond"/>, and from
///         <see cref="SmokeAt"/> until it cools below <see cref="StopBelow"/> the gun's <c>KsParticles</c> emits, as
///         TGMC's overheated guns smoke (<c>/particles/overheat_smoke</c>). Nothing about the gun changes.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsGunOverheatSmokeSystem))]
public sealed partial class KsGunOverheatSmokeComponent : Component
{
    [DataField]
    public float HeatPerShot = 1f;

    [DataField]
    public float CoolPerSecond = 4f;

    [DataField]
    public float SmokeAt = 24f;

    [DataField]
    public float StopBelow = 8f;

    /// <summary>
    ///     The heat as of <see cref="LastShot"/>; it is cooled lazily from then.
    /// </summary>
    [ViewVariables]
    public float Heat;

    [ViewVariables, AutoPausedField]
    public TimeSpan LastShot;

    /// <summary>
    ///     When the smoke stops, if no more shots come; null while not smoking.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan? SmokeUntil;
}
