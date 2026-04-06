using Content.Shared._Starlight.Plumbing;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Starlight.Plumbing.UI;

[UsedImplicitly]
public sealed class PlumbingSmartFridgeReagentBoundUserInterface : BoundUserInterface
{
    private PlumbingSmartFridgeWindow? _window;

    public PlumbingSmartFridgeReagentBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PlumbingSmartFridgeWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not PlumbingSmartFridgeBoundUserInterfaceState cast)
            return;

        _window.UpdateState(cast);
    }
}
