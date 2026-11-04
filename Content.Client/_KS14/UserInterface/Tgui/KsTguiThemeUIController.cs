using Content.Client.Stylesheets;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     Rebuilds the UI stylesheet when <see cref="KsCCVars.TguiTheme"/> changes, so the global tgui restyle switches
///         live. Lives for the process, so the cvar subscription needs no teardown.
/// </summary>
public sealed partial class KsTguiThemeUIController : UIController
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IStylesheetManager _stylesheetManager = default!;

    private bool _rebuildPending;

    public override void Initialize()
    {
        base.Initialize();

        // Only flag it: a cvar callback runs on whichever thread set the cvar, and building a stylesheet needs the
        //      UI thread's IoC context. The rebuild happens on the next frame.
        _configurationManager.OnValueChanged(KsCCVars.TguiTheme, _ => _rebuildPending = true);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_rebuildPending)
            return;

        _rebuildPending = false;
        if (_stylesheetManager is StylesheetManager stylesheetManager)
            stylesheetManager.KsRebuildNanotrasen();
    }
}
