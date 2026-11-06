namespace Content.Client.Stylesheets;

// Partial so the fork can rebuild the Nanotrasen sheet at runtime (lobby font switching, the tgui toggle).
public sealed partial class StylesheetManager
{
    /// <summary>
    ///     Rebuilds <see cref="SheetNanotrasen"/> and makes it the UI's stylesheet. Windows that hold
    ///     <see cref="SheetSystem"/> are not affected; only roots without their own stylesheet pick the new one up.
    /// </summary>
    public void KsRebuildNanotrasen()
    {
        var baseSheet = new Stylesheets.NanotrasenStylesheet(new BaseStylesheet.NoConfig(), this);
        Stylesheets[baseSheet.StylesheetName] = baseSheet.Stylesheet;
        SheetNanotrasen = baseSheet.Stylesheet;
        _userInterfaceManager.Stylesheet = SheetNanotrasen;
    }

    /// <summary>
    ///     Rebuilds both live sheets. Controls that hold the old <see cref="SheetSystem"/> keep it until they're moved off it,
    ///         so a caller that needs them restyled has to do that.
    /// </summary>
    public void KsRebuildAll()
    {
        var systemSheet = new Stylesheets.SystemStylesheet(new BaseStylesheet.NoConfig(), this);
        Stylesheets[systemSheet.StylesheetName] = systemSheet.Stylesheet;
        SheetSystem = systemSheet.Stylesheet;

        KsRebuildNanotrasen();
    }
}
