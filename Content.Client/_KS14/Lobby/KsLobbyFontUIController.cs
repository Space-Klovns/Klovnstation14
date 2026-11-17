using Content.Client.Stylesheets;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.Lobby;

/// <summary>
///     Rebuilds the UI stylesheet when <see cref="KsCCVars.LobbyFont"/> changes, so the font switches live.
///     Lives for the process, so the cvar subscription needs no teardown.
/// </summary>
public sealed partial class KsLobbyFontUIController : UIController
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IStylesheetManager _stylesheetManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.LobbyFont, _ => OnLobbyFontChanged());
    }

    private void OnLobbyFontChanged()
    {
        if (_stylesheetManager is StylesheetManager stylesheetManager)
            stylesheetManager.KsRebuildNanotrasen();
    }
}
