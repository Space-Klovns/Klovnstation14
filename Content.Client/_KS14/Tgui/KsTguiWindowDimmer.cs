using Content.Client._KS14.Tgui.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tg's <c>Window__dimmer</c>: <c>inset: 0</c> over the whole window, the window's base lightened by 15 at .25 opacity, and
///         <c>pointer-events: none</c>, so the window can still be dragged and closed. The colour is the sheet's, so a themed
///         window gets its own.
/// </summary>
public sealed class KsTguiWindowDimmer : Control
{
    private static readonly Color Fallback = KsTguiPalettes.Translucent(KsTguiColors.FromHsl(0f, 0f, 30f), 0.25f, KsTguiPalettes.Base);

    private Color _color = Fallback;

    public KsTguiWindowDimmer()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    public Color Color => _color;

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _color = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyWindowDimmer, out var color) ? color : Fallback;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(new UIBox2(0f, 0f, PixelSize.X, PixelSize.Y), _color);
    }
}
