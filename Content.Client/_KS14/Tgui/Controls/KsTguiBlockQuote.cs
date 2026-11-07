using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's BlockQuote: wrapping text in the label colour behind a 2px bar of the same colour, 6px of padding after the
///         bar. The 6px below it (<c>margin-bottom</c>, outside the bar, which ends with the text) are on by default;
///         <see cref="Spaced"/> is off for a quote that is the last child of its container, as CSS's <c>:last-child</c>.
///         XAML children follow <see cref="Text"/>.
/// </summary>
public sealed class KsTguiBlockQuote : PanelContainer
{
    private readonly BoxContainer _content;
    private readonly RichTextLabel _label;
    private readonly StyleBoxFlat _box;
    private const float SpaceBelow = 6f;

    private string? _text;
    private bool _spaced = true;
    private Color _labelColor = KsTguiPalettes.Label;

    public KsTguiBlockQuote()
    {
        _box = new StyleBoxFlat
        {
            BackgroundColor = Color.Transparent,
            BorderColor = KsTguiPalettes.Label,
            BorderThickness = new Thickness(2f, 0f, 0f, 0f),
        };
        _box.SetContentMarginOverride(StyleBox.Margin.Left, 8f);
        PanelOverride = _box;
        Margin = new Thickness(0f, 0f, 0f, SpaceBelow);

        _label = new RichTextLabel { Visible = false };
        _content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _content.AddChild(_label);
        AddChild(_content);

        XamlChildren = _content.Children;
    }

    /// <summary>
    ///     The quoted text, in the label colour.
    /// </summary>
    public string? Text
    {
        get => _text;
        set
        {
            _text = value;
            RefreshText();
        }
    }

    /// <summary>
    ///     Whether the quote leaves 6px below it. tgui drops them for the last child of a container.
    /// </summary>
    public bool Spaced
    {
        get => _spaced;
        set
        {
            _spaced = value;
            Margin = new Thickness(0f, 0f, 0f, value ? SpaceBelow : 0f);
        }
    }

    private void RefreshText()
    {
        _label.SetMessage(FormattedMessage.FromUnformatted(_text ?? string.Empty), defaultColor: _labelColor);
        _label.Visible = !string.IsNullOrEmpty(_text);
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _labelColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyLabelColor, out var color) ? color : KsTguiPalettes.Label;
        _box.BorderColor = _labelColor;
        RefreshText();
    }
}
