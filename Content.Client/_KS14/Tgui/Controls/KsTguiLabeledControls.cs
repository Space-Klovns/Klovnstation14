using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's LabeledControls: a row of <see cref="KsTguiLabeledControlsItem"/>s spread edge to edge
///         (<c>justify="space-between"</c>), every item as tall as the row (<c>align="stretch"</c>). The row has a margin of
///         -0.5 (-3px) either side, and every item one of 1 (6px), so the first and last items sit 3px in from the edges.
///         <see cref="Wrap"/> is tgui's <c>wrap</c>: items that no longer fit start a new line.
/// </summary>
public sealed class KsTguiLabeledControls : Control
{
    /// <summary>
    ///     An item's <c>mx=1</c>, in pixels (one unit is 0.5rem).
    /// </summary>
    public const float ItemMargin = 6f;

    /// <summary>
    ///     The row's <c>mx=-0.5</c>, as a positive number of pixels.
    /// </summary>
    public const float RowOverhang = 3f;

    private bool _wrap;

    /// <summary>
    ///     Lets items that do not fit the width go on to another line.
    /// </summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            _wrap = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     Adds an item with this label under <paramref name="content"/>.
    /// </summary>
    public KsTguiLabeledControlsItem AddItem(string label, Control content)
    {
        var item = new KsTguiLabeledControlsItem { Text = label, Content = content };
        AddChild(item);
        return item;
    }

    /// <summary>
    ///     The visible items of each line, for a line this wide (the row's width plus its overhang). Without
    ///         <see cref="Wrap"/> there is one line.
    /// </summary>
    private List<List<Control>> BuildLines(float flexWidth)
    {
        var lines = new List<List<Control>> { new() };
        var used = 0f;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            var outer = child.DesiredSize.X + ItemMargin * 2f;
            if (_wrap && lines[^1].Count > 0 && used + outer > flexWidth)
            {
                lines.Add(new List<Control>());
                used = 0f;
            }

            lines[^1].Add(child);
            used += outer;
        }

        return lines;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var child in Children)
        {
            // An item takes the width it needs: the row never narrows it.
            child.Measure(new Vector2(float.PositiveInfinity, availableSize.Y));
        }

        var flexWidth = _wrap && float.IsFinite(availableSize.X) ? availableSize.X + RowOverhang * 2f : float.PositiveInfinity;
        var width = 0f;
        var height = 0f;
        foreach (var line in BuildLines(flexWidth))
        {
            var lineWidth = 0f;
            var lineHeight = 0f;
            foreach (var child in line)
            {
                lineWidth += child.DesiredSize.X + ItemMargin * 2f;
                lineHeight = MathF.Max(lineHeight, child.DesiredSize.Y);
            }

            width = MathF.Max(width, lineWidth);
            height += lineHeight;
        }

        return new Vector2(MathF.Max(0f, width - RowOverhang * 2f), height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var flexWidth = finalSize.X + RowOverhang * 2f;
        var lines = BuildLines(flexWidth);

        var y = 0f;
        foreach (var line in lines)
        {
            var lineWidth = 0f;
            var lineHeight = 0f;
            foreach (var child in line)
            {
                lineWidth += child.DesiredSize.X + ItemMargin * 2f;
                lineHeight = MathF.Max(lineHeight, child.DesiredSize.Y);
            }

            // One line fills the row's height (stretch); several take what each needs.
            if (lines.Count == 1)
                lineHeight = finalSize.Y;

            var gap = line.Count > 1 ? MathF.Max(0f, (flexWidth - lineWidth) / (float)(line.Count - 1)) : 0f;
            var x = -RowOverhang;
            foreach (var child in line)
            {
                child.Arrange(UIBox2.FromDimensions(x + ItemMargin, y, child.DesiredSize.X, lineHeight));
                x += child.DesiredSize.X + ItemMargin * 2f + gap;
            }

            y += lineHeight;
        }

        return finalSize;
    }
}
