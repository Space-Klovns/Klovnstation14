using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Fluids.Components;

/// <summary>
///     Extends <see cref="Content.Shared.Fluids.Components.SpraySafetyComponent"/> to shooting: a spray that is also
///         a gun cannot be fired with its safety on.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
[Access(typeof(KsGunSpraySafetySystem))]
public sealed partial class KsGunSpraySafetyComponent : Component
{
    /// <summary>
    ///     How often the safety popup may show; a held trigger asks to shoot every tick.
    /// </summary>
    [DataField]
    public TimeSpan PopupCooldown = TimeSpan.FromSeconds(1);

    [ViewVariables, AutoPausedField]
    public TimeSpan NextPopupTime = TimeSpan.Zero;
}
