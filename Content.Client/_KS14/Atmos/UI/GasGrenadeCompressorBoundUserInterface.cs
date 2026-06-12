using Content.Client.Atmos.UI;
using Content.Shared._KS14.Atmos.Components;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Atmos.UI;

public sealed class GasGrenadeCompressorBoundUserInterface : BoundUserInterface
{
    private GasGrenadeCompressorWindow? _window;

    public GasGrenadeCompressorBoundUserInterface(EntityUid owner, object uiKey) : base(owner, (Enum) uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new GasGrenadeCompressorWindow();
        _window.OnClose += Close;
        _window.OnTargetPressureChanged += pressure =>
        {
            SendMessage(new GasGrenadeCompressorChangeTargetPressureMessage(pressure));
        };
        _window.OnTogglePressed += enabled =>
        {
            SendMessage(new GasGrenadeCompressorToggleMessage(enabled));
        };
        _window.OnRearmPressed += () =>
        {
            SendMessage(new GasGrenadeCompressorRearmMessage());
        };

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not GasGrenadeCompressorBoundUserInterfaceState cast)
            return;

        _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _window?.Dispose();
        }
    }
}
