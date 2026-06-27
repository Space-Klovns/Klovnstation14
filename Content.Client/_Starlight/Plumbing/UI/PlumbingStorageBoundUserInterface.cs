using Content.Shared._Starlight.Plumbing;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Starlight.Plumbing.UI;

/// <summary>
///     BUI for plumbing storage (tanks, barrels).
///     NOTE: Named 'PlumbingStorageBui' instead of 'PlumbingStorageBoundUserInterface' to prevent 
///     Robust reflection from suffix-matching this against vanilla 'StorageBoundUserInterface'.
/// </summary>
[UsedImplicitly]
public sealed class PlumbingStorageBui : BoundUserInterface
{
    private PlumbingStorageWindow? _window;

    public PlumbingStorageBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new PlumbingStorageWindow();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not PlumbingStorageBuiState cast)
            return;

        _window.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
