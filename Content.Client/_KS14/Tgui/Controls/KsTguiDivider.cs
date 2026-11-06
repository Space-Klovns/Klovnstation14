using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Divider: a 2px line of <see cref="KsTguiPalettes.Border"/> with 6px of space either side of it, across the
///         container (<see cref="Vertical"/> for a column divider between side-by-side items).
/// </summary>
public sealed class KsTguiDivider : Control
{
    private const float Thickness = 2f;
    private const float Space = 6f;

    /// <summary>
    ///     A vertical line, for a horizontal row of items.
    /// </summary>
    public bool Vertical { get; set; }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return Vertical
            ? new Vector2(Thickness + Space * 2f, 0f)
            : new Vector2(0f, Thickness + Space * 2f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var thickness = MathF.Max(1f, MathF.Round(Thickness * UIScale));
        var space = Space * UIScale;
        var line = Vertical
            ? UIBox2.FromDimensions(space, 0f, thickness, PixelHeight)
            : UIBox2.FromDimensions(0f, space, PixelWidth, thickness);
        handle.DrawRect(line, KsTguiPalettes.Border);
    }
}
