using Content.Shared._KS14.Atmos.ChemicalFire;

namespace Content.Server._KS14.Atmos.FoamBlanket;

/// <summary>
///     A chemfire that foam has landed on without putting it out at once: one fighting the foam, or one the foam
///         cannot touch. Holds how the fight is going, and when the players were last told about it.
/// </summary>
/// <remarks>
///     The networked <see cref="ChemicalFireComponent.SmotherProgress"/> is this progress in coarse steps, for the
///         client to draw.
/// </remarks>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(KsFoamBlanketSystem))]
public sealed partial class KsFoamContestComponent : Component
{
    /// <summary>
    ///     How close the foam is to smothering the fire, from 0 to 1.
    /// </summary>
    [ViewVariables]
    public float Progress;

    /// <summary>
    ///     When the fire may next roar against the foam over it.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextResistSoundTime;

    /// <summary>
    ///     When foam landing on the fire may next say so in a popup.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextPopupTime;
}
