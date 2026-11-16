namespace Content.Shared._KS14.Tgui;

/// <summary>
///     The tgui theme of the windows this entity opens, for a bound user interface that has no data of its own to say
///         so: a syndicate communications console is Syndicate, a wizard's is Wizard. Prototype data, read by the client,
///         so it isn't networked.
/// </summary>
[RegisterComponent]
public sealed partial class KsTguiUiThemeComponent : Component
{
    [DataField]
    public KsTguiWindowTheme Theme = KsTguiWindowTheme.Nanotrasen;
}
