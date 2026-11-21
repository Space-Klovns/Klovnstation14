using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Cultivation.Body;

/// <summary>
///     In the air from a body art; what happens when they come down.
/// </summary>
[RegisterComponent, Access(typeof(BodyArtSystem))]
public sealed partial class BodyLandingComponent : Component
{
    /// <summary>
    ///     Who threw or launched them, for whose art the landing counts.
    /// </summary>
    [DataField]
    public EntityUid? Source;

    [DataField]
    public float Radius;

    [DataField]
    public float Damage;

    /// <summary>
    ///     Extra damage to whoever is right at the centre (Falling Mountain Descent).
    /// </summary>
    [DataField]
    public float CentreDamage;

    [DataField]
    public TimeSpan Knockdown;

    /// <summary>
    ///     Structural damage to everything breakable around the landing.
    /// </summary>
    [DataField]
    public float Smash;

    /// <summary>
    ///     Mountain-Toppling Throw: the one landing is the victim, and takes the damage themselves.
    /// </summary>
    [DataField]
    public bool HurtSelf;

    [DataField]
    public string ArtName = string.Empty;
}

/// <summary>
///     Heaven-Shaking Quake in progress: the ground pulses a few more times.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(BodyArtSystem))]
public sealed partial class BodyQuakeComponent : Component
{
    [DataField]
    public int PulsesLeft;

    [DataField]
    public float Radius;

    [DataField]
    public float Damage;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPulse;
}

/// <summary>
///     Breath of Renewal in progress: breaths left after this one.
/// </summary>
[RegisterComponent, Access(typeof(BodyArtSystem))]
public sealed partial class BodyBreathingComponent : Component
{
    [DataField]
    public int BreathsLeft;

    [DataField]
    public float ExhaustionPerBreath;
}

/// <summary>
///     Hundred Fists in progress: blows land one after another in quick succession, the last one hurling the target.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(BodyArtSystem))]
public sealed partial class BodyFlurryComponent : Component
{
    [DataField]
    public EntityUid Target;

    [DataField]
    public int BlowsLeft;

    [DataField]
    public float PerBlow;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBlow;
}
