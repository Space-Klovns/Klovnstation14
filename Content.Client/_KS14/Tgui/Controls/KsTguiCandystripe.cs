using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>candystripe</c> as a container: a vertical (or horizontal) stack whose visible children are striped by
///         index, the first, third and so on darkened, as <c>Stack zebra</c> and <c>className="candystripe"</c> do. The stripe
///         spans the whole container, so a child that does not stretch still gets a full-width row.
/// </summary>
[Virtual]
public class KsTguiCandystripe : BoxContainer
{
    private Color? _stripeColor;
    private Color? _evenStripeColor;

    public KsTguiCandystripe()
    {
        Orientation = LayoutOrientation.Vertical;
        AddStyleClass(KsTguiSheetlet.StyleClassCandystripe);
    }

    /// <summary>
    ///     Whether a child is one of the dark rows: counted among the visible children, so hiding one re-alternates the rest.
    /// </summary>
    public bool IsStriped(Control child)
    {
        var index = 0;
        foreach (var other in Children)
        {
            if (!other.Visible)
                continue;

            if (other == child)
                return index % 2 == 0;

            index++;
        }

        return false;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();
        _stripeColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripe, out var color) ? color : null;
        _evenStripeColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripeEven, out var evenColor) && evenColor.A > 0f ? evenColor : null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_stripeColor is not { } oddColor)
            return;

        var index = 0;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            // --candystripe-even is clear unless a theme sets it.
            if ((index++ % 2 == 0 ? oddColor : _evenStripeColor) is not { } color)
                continue;

            var box = Orientation == LayoutOrientation.Vertical
                ? new UIBox2(0f, child.PixelPosition.Y, PixelWidth, child.PixelPosition.Y + child.PixelHeight)
                : new UIBox2(child.PixelPosition.X, 0f, child.PixelPosition.X + child.PixelWidth, PixelHeight);
            handle.DrawRect(box, color);
        }
    }
}
