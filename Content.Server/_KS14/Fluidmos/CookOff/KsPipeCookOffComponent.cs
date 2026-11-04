using Content.Shared._KS14.Fluidmos;

namespace Content.Server._KS14.Fluidmos.CookOff;

/// <summary>
///     A pipe rattling as the explosive fluid in its net cooks off, until it ruptures. Added and removed by
///         <see cref="KsPipeCookOffSystem"/> only. All runtime state: a saved pipe starts over.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsPipeCookOffSystem))]
public sealed partial class KsPipeCookOffComponent : Component
{
    /// <summary>
    ///     How far along the cook-off is, from 0 to 1, at which its net's chain of ruptures starts.
    /// </summary>
    [ViewVariables]
    public float Progress;

    /// <summary>
    ///     When this pipe ruptures, once its net's chain has started; null until then.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan? RuptureAt;

    /// <summary>
    ///     The explosive power this pipe's rupture explodes with.
    /// </summary>
    [ViewVariables]
    public float Power;

    /// <summary>
    ///     The explosive fluid this pipe's rupture sprays onto its tile, and its temperature.
    /// </summary>
    [ViewVariables]
    public KsFluidMixture? Spill;

    [ViewVariables]
    public float SpillTemperature;
}
