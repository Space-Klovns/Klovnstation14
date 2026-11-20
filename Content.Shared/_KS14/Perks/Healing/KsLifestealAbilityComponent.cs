using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Healing;

/// <summary>
///     Heals the holder by <see cref="Fraction"/> of the damage they deal to living mobs that aren't on their side,
///         by any means: guns held, bullets fired, blows struck; not from one they are healing with a medical beam at
///         the time. Worked out on the server (<c>KsLifestealSystem</c>).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class KsLifestealAbilityComponent : Component
{
    [DataField]
    public float Fraction = 0.2f;

    /// <summary>
    ///     The holder is told they drained life at most this often, however many hits land.
    /// </summary>
    [DataField]
    public TimeSpan PopupInterval = TimeSpan.FromSeconds(2);

    [ViewVariables, AutoPausedField]
    public TimeSpan NextPopup;
}
