using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Owns the tgui toggle: restyles the running UI when <see cref="KsCCVars.TguiStyle"/> changes. Lives for the process,
///         so its subscriptions need no teardown.
/// </summary>
public sealed partial class KsTguiThemeUIController : UIController
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IStylesheetManager _stylesheetManager = default!;
    [Dependency] private FontTagHijackHolder _fontTagHijackHolder = default!;
    [Dependency] private KsTguiFonts _tguiFonts = default!;

    /// <summary>
    ///     Whether the tgui style is on. The stylesheets read the cvar for themselves, each time one is built.
    /// </summary>
    public bool Enabled { get; private set; }

    private bool _restylePending;

    public override void Initialize()
    {
        base.Initialize();

        Enabled = _configurationManager.GetCVar(KsCCVars.TguiStyle);

        // Installed now rather than on the first change, or [bold] markup stays Baron's until someone flips the option.
        _fontTagHijackHolder.Hijack = HijackFont;

        // Not invokeImmediately: every controller initializes before the stylesheets exist.
        _configurationManager.OnValueChanged(KsCCVars.TguiStyle, OnToggled);
    }

    /// <summary>
    ///     Just notes the change. A cvar's callback runs on whichever thread set it, and a stylesheet can only be built on the
    ///         main one, so the work waits for the next frame.
    /// </summary>
    private void OnToggled(bool enabled)
    {
        Enabled = enabled;
        _restylePending = true;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_restylePending)
            return;

        _restylePending = false;
        Restyle();
    }

    private void Restyle()
    {
        if (_stylesheetManager is StylesheetManager stylesheetManager)
        {
            var oldSystemSheet = stylesheetManager.SheetSystem;
            stylesheetManager.KsRebuildAll();
            ReplaceStylesheet(oldSystemSheet, stylesheetManager.SheetSystem);
        }

        _fontTagHijackHolder.HijackUpdated();

        UIManager.GetUIController<KsTguiChromeUIController>().SyncOpenWindows();
        UIManager.GetUIController<KsTguiFeelUIController>().ThemeChanged();
    }

    /// <summary>
    ///     Moves the controls that hold a sheet of their own onto its replacement. Setting the UI's stylesheet skips them.
    /// </summary>
    private void ReplaceStylesheet(Stylesheet oldSheet, Stylesheet newSheet)
    {
        var queue = new Queue<Control>();
        foreach (var root in UIManager.AllRoots)
            queue.Enqueue(root);

        while (queue.TryDequeue(out var control))
        {
            if (ReferenceEquals(control.Stylesheet, oldSheet))
                control.Stylesheet = newSheet;

            foreach (var child in control.Children)
                queue.Enqueue(child);
        }
    }

    /// <summary>
    ///     Points the markup font prototypes at the tgui stack. Returning null leaves their paths in fonts.yml, Baron's.
    /// </summary>
    private Font? HijackFont(ProtoId<FontPrototype> fontPrototypeId, int size)
    {
        if (!Enabled)
            return null;

        return fontPrototypeId.Id switch
        {
            FontTag.DefaultFont => _tguiFonts.GetFont(size),
            BoldTag.BoldFont => _tguiFonts.GetFont(size, FontKind.Bold),
            ItalicTag.ItalicFont => _tguiFonts.GetFont(size, FontKind.Italic),
            BoldItalicTag.BoldItalicFont => _tguiFonts.GetFont(size, FontKind.BoldItalic),
            _ => null,
        };
    }
}
