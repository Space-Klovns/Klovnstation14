using Content.Shared._KS14.Mobs.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Mobs.Components;

/// <summary>
///     Splits <see cref="Content.Shared.Mobs.MobState.Critical"/> into soft and hard crit, like /tg/.
///     Right after going critical the mob is in soft crit, still floored and unable to use its hands,
///         but it can slowly crawl, turn and whisper. Past <see cref="HardCritFraction"/> of the way
///         from the crit threshold to the dead threshold it's in hard crit, which is plain crit.
///     The mob state itself is never changed, so everything that checks for crit keeps working as is.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(KsSoftCritSystem))]
public sealed partial class KsSoftCritComponent : Component
{
    /// <summary>
    ///     How far from the crit threshold to the dead threshold soft crit lasts, from 0 to 1.
    /// </summary>
    [DataField]
    public float HardCritFraction = 0.3f;

    /// <summary>
    ///     Movement speed multiplier while crawling in soft crit.
    /// </summary>
    [DataField]
    public float CrawlSpeedModifier = 0.3f;

    /// <summary>
    ///     Chance for each letter to get swallowed when speaking, right after going critical.
    /// </summary>
    [DataField]
    public float MinLetterDropChance = 0.1f;

    /// <summary>
    ///     Chance for each letter to get swallowed when speaking, right before hard crit.
    /// </summary>
    [DataField]
    public float MaxLetterDropChance = 0.35f;

    /// <summary>
    ///     Whether the mob is currently in soft crit.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Active;
}
