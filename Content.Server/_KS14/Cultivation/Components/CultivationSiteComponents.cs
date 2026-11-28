using Content.Server._KS14.Cultivation.Systems;

namespace Content.Server._KS14.Cultivation.Components;

/// <summary>
///     A cultivation mat. Meditating on one lets the surroundings matter: the five elements around it feed or starve
///         the cultivator's laws.
/// </summary>
[RegisterComponent, Access(typeof(CultivationSiteSystem))]
public sealed partial class CultivationMatComponent : Component;

/// <summary>
///     A seven-string zither. Playing it is cultivation: the player and every cultivator listening nearby slowly gain
///         insight, and their qi settles.
/// </summary>
[RegisterComponent, Access(typeof(GuqinSystem))]
public sealed partial class GuqinComponent : Component
{
    /// <summary>
    ///     Seconds of continuous playing between waves of music.
    /// </summary>
    [DataField]
    public float PulseInterval = 20f;

    [DataField]
    public float PlayTime;

    [DataField]
    public float Range = 6f;

    /// <summary>
    ///     Insight a wave gives, before the player's realm adds to it.
    /// </summary>
    [DataField]
    public float Insight = 2f;

    /// <summary>
    ///     Every this many waves the player's torn meridians settle a little.
    /// </summary>
    [DataField]
    public int PulsesPerInjuryHeal = 3;

    [DataField]
    public int Pulses;

    /// <summary>
    ///     When each listener last learned from the music, so standing in two rooms' worth doesn't stack.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> ListenedAt = new();

    [DataField]
    public TimeSpan ListenerCooldown = TimeSpan.FromSeconds(40);
}
