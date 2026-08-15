using Content.Client.Gameplay;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;

namespace Content.Client._KS14.Language;

[UsedImplicitly]
public sealed partial class KsLanguageMenuUIController : UIController, IOnStateChanged<GameplayState>
{
    private KsLanguageMenuWindow? _window;

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.KsOpenLanguageMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<KsLanguageMenuUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<KsLanguageMenuUIController>();
        _window?.Close();
        _window = null;
    }

    private void ToggleWindow()
    {
        if (_window is { IsOpen: true })
        {
            _window.Close();
            return;
        }

        // The window refreshes itself when it enters the UI tree.
        _window ??= UIManager.CreateWindow<KsLanguageMenuWindow>();
        _window.OpenCentered();
    }
}
