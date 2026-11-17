using Content.Shared._KS14.Tgui;

namespace Content.Shared.Store.Components;

public sealed partial class StoreComponent
{
    /// <summary>
    ///     The tgui theme of this store's window. Uplinks are Syndicate, so that is the default, and a prototype that isn't
    ///         one says so.
    /// </summary>
    [DataField]
    public KsTguiWindowTheme KsUiTheme = KsTguiWindowTheme.Syndicate;
}
