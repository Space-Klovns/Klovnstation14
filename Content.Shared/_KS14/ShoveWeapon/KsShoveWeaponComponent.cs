using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.ShoveWeapon;

/// <summary>
///     Alt-attacking with this held item shoves instead of swinging it. tgstation's <c>disarm_attack</c> element, as
///         shields have.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsShoveWeaponComponent : Component
{
    /// <summary>
    ///     tgstation's item shoves never miss.
    /// </summary>
    [DataField]
    public bool Guaranteed = true;

    [DataField]
    public SoundSpecifier? SuccessSound = new SoundPathSpecifier("/Audio/_KS14/Effects/Shields/glassbash.ogg");

    /// <summary>
    ///     A toggleable item, like an energy shield, can't shove while off.
    /// </summary>
    [DataField]
    public LocId InactivePopup = "ks-shove-weapon-inactive";

    /// <summary>
    ///     tgstation's stagger: a short slowdown on whoever was shoved.
    /// </summary>
    [DataField]
    public EntProtoId StaggerEffect = "KsStatusEffectStaggered";

    [DataField]
    public TimeSpan StaggerDuration = TimeSpan.FromSeconds(3);

    [DataField]
    public float StaggerSpeedModifier = 0.85f;
}
