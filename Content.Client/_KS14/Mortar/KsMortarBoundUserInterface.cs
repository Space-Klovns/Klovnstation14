using Content.Shared._KS14.Mortar;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Mortar;

/// <summary>
///     The mortar's window: bearing, distance and Fire. The server checks and clamps whatever is sent.
/// </summary>
public sealed partial class KsMortarBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private KsMortarWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsMortarWindow>();
        _window.OnFire += (bearing, distance) => SendMessage(new KsMortarFireMessage(bearing, distance));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is KsMortarBuiState mortarState)
            _window?.UpdateState(mortarState);
    }
}
