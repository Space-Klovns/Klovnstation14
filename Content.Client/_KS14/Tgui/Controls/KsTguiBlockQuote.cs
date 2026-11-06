using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's BlockQuote: wrapping text in the label colour behind a 2px bar of the same colour, 6px of padding after the
///         bar and 6px below, which a quote that is the last child of its container would not have. XAML children follow
///         <see cref="Text"/>.
/// </summary>
public sealed class KsTguiBlockQuote : PanelContainer
{
    private readonly BoxContainer _content;
    private readonly RichTextLabel _label;
    private string? _text;

    public KsTguiBlockQuote()
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = Color.Transparent,
            BorderColor = KsTguiPalettes.Label,
            BorderThickness = new Thickness(2f, 0f, 0f, 0f),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Left, 8f);
        box.SetContentMarginOverride(StyleBox.Margin.Bottom, 6f);
        PanelOverride = box;

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
            _label.SetMessage(FormattedMessage.FromUnformatted(value ?? string.Empty), defaultColor: KsTguiPalettes.Label);
            _label.Visible = !string.IsNullOrEmpty(value);
        }
    }
}
