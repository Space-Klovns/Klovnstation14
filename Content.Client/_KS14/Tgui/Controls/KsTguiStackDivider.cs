using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Stack.Divider: the 2px <c>--divider-border</c> line of a Divider, but with no margin of its own. A Stack's 6px
///         gap either side of an item spaces it, so in a box whose <c>SeparationOverride</c> is
///         <see cref="KsTguiSection.Gap"/> the whole is the 14px a <see cref="KsTguiDivider"/> takes. <see cref="Hidden"/>
///         keeps the item, and so its gaps, but not the line.
/// </summary>
public sealed class KsTguiStackDivider : Control
{
    private const float Thickness = 2f;

    private bool _vertical;
    private bool _hidden;

    /// <summary>
    ///     A vertical line, for a horizontal stack.
    /// </summary>
    public bool Vertical
    {
        get => _vertical;
        set
        {
            _vertical = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>hidden</c>: the line is not drawn, and nor is its border's room.
    /// </summary>
    public bool Hidden
    {
        get => _hidden;
        set
        {
            _hidden = value;
            InvalidateMeasure();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var thickness = _hidden ? 0f : Thickness;
        return _vertical ? new Vector2(thickness, 0f) : new Vector2(0f, thickness);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_hidden)
            return;

        var thickness = MathF.Max(1f, MathF.Round(Thickness * UIScale));
        var line = _vertical
            ? UIBox2.FromDimensions(0f, 0f, thickness, PixelHeight)
            : UIBox2.FromDimensions(0f, 0f, PixelWidth, thickness);
        handle.DrawRect(line, KsTguiPalettes.Border);
    }
}
