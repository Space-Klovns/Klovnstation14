using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's LabeledControls.Item: a control with its label under it in the label colour, both centred. The column is as
///         tall as its row and spaces its three flex items (an empty one, the content, the label) between top and bottom, so
///         the label sits at the bottom and the content halfway between the top and the label.
/// </summary>
public sealed class KsTguiLabeledControlsItem : Control
{
    private readonly Label _label = new() { FontColorOverride = KsTguiPalettes.Label };
    private Control? _content;

    public KsTguiLabeledControlsItem()
    {
        AddChild(_label);
    }

    /// <summary>
    ///     The label under the content.
    /// </summary>
    public string? Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>
    ///     The control above the label.
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

            AddChild(value);
        }
    }

    /// <summary>
    ///     Where the content sits vertically in a column this tall.
    /// </summary>
    public static float ContentTop(float height, float contentHeight, float labelHeight)
    {
        return (height - contentHeight - labelHeight) / 2f;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _content?.Measure(new Vector2(float.PositiveInfinity, availableSize.Y));
        _label.Measure(new Vector2(float.PositiveInfinity, availableSize.Y));

        var contentSize = _content?.DesiredSize ?? Vector2.Zero;
        return new Vector2(MathF.Max(contentSize.X, _label.DesiredSize.X), contentSize.Y + _label.DesiredSize.Y);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var labelSize = _label.DesiredSize;
        var contentSize = _content?.DesiredSize ?? Vector2.Zero;

        _content?.Arrange(UIBox2.FromDimensions(
            (finalSize.X - contentSize.X) / 2f,
            ContentTop(finalSize.Y, contentSize.Y, labelSize.Y),
            contentSize.X,
            contentSize.Y));
        _label.Arrange(UIBox2.FromDimensions((finalSize.X - labelSize.X) / 2f, finalSize.Y - labelSize.Y, labelSize.X, labelSize.Y));

        return finalSize;
    }
}
