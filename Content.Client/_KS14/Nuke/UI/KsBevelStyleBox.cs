using Robust.Client.Graphics;

namespace Content.Client._KS14.Nuke.UI;

/// <summary>
///     A flat box with a two-tone bevel: <see cref="Highlight"/> along the top and left edges,
///     <see cref="Shadow"/> along the bottom and right. Swap the two to make it look pushed in.
/// </summary>
public sealed class KsBevelStyleBox : StyleBox
{
    public Color Face { get; set; }
    public Color Highlight { get; set; }
    public Color Shadow { get; set; }

    /// <summary>
    ///     Thickness of the bevel, in virtual pixels.
    /// </summary>
    public float BevelThickness { get; set; } = 3f;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var bevel = BevelThickness * uiScale;

        handle.DrawRect(box, Face);

        if (bevel <= 0)
            return;

        // Shadow first so the highlight wins the top-right and bottom-left corners.
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - bevel, box.Right, box.Bottom), Shadow);
        handle.DrawRect(new UIBox2(box.Right - bevel, box.Top, box.Right, box.Bottom), Shadow);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right - bevel, box.Top + bevel), Highlight);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + bevel, box.Bottom - bevel), Highlight);
    }

    protected override float GetDefaultContentMargin(Margin margin)
    {
        return BevelThickness;
    }
}
