using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ThrowMode;

/// <summary>
///     tgstation's throw mode: while on, clicking somewhere throws whatever is in the active hand there, until it is
///         toggled off. Throwing the hand that holds someone in a grab, or on your back, throws them.
///     Toggled from its HUD button beside the hotbar or its own key (unbound by default; Ctrl+Q still throws at once).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsThrowModeSystem))]
public sealed partial class KsThrowModeComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled;
}
