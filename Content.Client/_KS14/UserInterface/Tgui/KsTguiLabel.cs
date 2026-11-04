using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     Text in tgui's face and colours. A plain label would take whatever font the active stylesheet gives labels.
/// </summary>
public sealed partial class KsTguiLabel : Label
{
    [Dependency] private IResourceCache _resourceCache = default!;

    private KsTguiTone _tone;

    public KsTguiLabel()
    {
        IoCManager.InjectDependencies(this);
        VAlign = VAlignMode.Center;
        FontOverride = KsTguiTheme.RegularFont(_resourceCache);
        FontColorOverride = KsTguiTheme.ToneText(_tone);
    }

    public KsTguiTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            FontColorOverride = KsTguiTheme.ToneText(value);
        }
    }
}
