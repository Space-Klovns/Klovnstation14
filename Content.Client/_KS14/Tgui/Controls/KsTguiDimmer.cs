using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Dimmer: a dark layer over everything under it, which takes the clicks so nothing beneath can be pressed,
///         with <see cref="Content"/> in the middle. It covers its parent, so the parent has to stack its children, as a plain
///         <see cref="Control"/> does; <see cref="Over"/> builds that host around the control to cover. Its background is
///         <c>--dimmer-background</c>: the window's grey at lightness 5, three quarters opaque.
/// </summary>
[Virtual]
public class KsTguiDimmer : PanelContainer
{
    private static readonly Color Background = KsTguiPalettes.Translucent(KsTguiColors.FromHsl(0f, 0f, 5f), 0.75f, KsTguiPalettes.Section);

    private Control? _content;
    private readonly StyleBoxFlat _background = new() { BackgroundColor = Background };

    public KsTguiDimmer()
    {
        PanelOverride = _background;
        MouseFilter = MouseFilterMode.Stop;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        // The window theme's, from the sheet.
        _background.BackgroundColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyDimmer, out var dimmer) ? dimmer : Background;
    }

    /// <summary>
    ///     What sits in the middle of the dimmed area.
    /// </summary>
    public Control? Content
    {
        get => _content;
        set
        {
            _content?.Orphan();
            _content = value;
            if (value == null)
                return;

            value.HorizontalAlignment = HAlignment.Center;
            value.VerticalAlignment = VAlignment.Center;
            AddChild(value);
        }
    }

    /// <summary>
    ///     A host holding <paramref name="target"/> and a dimmer over it. Add the host where the target was; show or hide the
    ///         dimmer to lock and unlock it.
    /// </summary>
    public static Control Over(Control target, out KsTguiDimmer dimmer)
    {
        dimmer = new KsTguiDimmer { Visible = false };

        var host = new Control();
        host.AddChild(target);
        host.AddChild(dimmer);
        return host;
    }
}
