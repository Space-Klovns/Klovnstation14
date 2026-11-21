using Content.Client.UserInterface;
using Content.Shared._KS14.Mortar;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Mortar;

/// <summary>
///     The mortar's window: the plot, bearing, distance and Fire, and the sight. The server checks and clamps whatever is
///         sent. The aim the sight follows is sent once a tick at most, however fast the player drags.
/// </summary>
public sealed partial class KsMortarBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey), IBuiPreTickUpdate
{
    private KsMortarWindow? _window;

    // A mutable struct: kept as a field, never copied.
    private InputCoalescer<(Angle Bearing, float Distance)> _aimCoalescer;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsMortarWindow>();
        _window.OnFire += (bearing, distance) => SendMessage(new KsMortarFireMessage(bearing, distance));
        _window.OnAim += (bearing, distance) => _aimCoalescer.Set((bearing, distance));
    }

    void IBuiPreTickUpdate.PreTickUpdate()
    {
        if (_aimCoalescer.CheckIsModified(out var aim))
            SendMessage(new KsMortarAimMessage(aim.Bearing, aim.Distance));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not KsMortarBuiState mortarState || _window == null)
            return;

        _window.UpdateState(mortarState);

        NetEntity? sightEye = null;
        if (PlayerManager.LocalEntity is { } localUid && mortarState.SightEyes.TryGetValue(EntMan.GetNetEntity(localUid), out var eye))
            sightEye = eye;

        _window.SetSightEye(sightEye);
    }
}
