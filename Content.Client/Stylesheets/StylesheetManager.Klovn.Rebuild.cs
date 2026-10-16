namespace Content.Client.Stylesheets;

// Partial so the fork can rebuild the Nanotrasen sheet at runtime (lobby font switching).
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
}
