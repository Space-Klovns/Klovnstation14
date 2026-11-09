using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's StyleableSection: a Section's title (6px padding, bold 14px text, a 2px line under it) and padded content, but
///         no background of its own, so the surface behind it shows, and a colour for the text and the line. XAML children
///         go to the content.
/// </summary>
public sealed class KsTguiStyleableSection : BoxContainer
{
    private static readonly Thickness ContentPadding = new(6f, 8f, 6f, 8f);

    private readonly PanelContainer _titlePanel;
    private readonly Label _titleLabel;
    private readonly BoxContainer _content;
    private readonly KsTguiStyleBox _titleBox;

    public KsTguiStyleableSection()
    {
        Orientation = LayoutOrientation.Vertical;

        _titleBox = new KsTguiStyleBox(Color.Transparent.WithAlpha(0f))
        {
            BorderColor = KsTguiPalettes.PrimaryBase,
            BorderThickness = new Thickness(0f, 0f, 0f, 2f),
        };
        _titleBox.SetContentMarginOverride(StyleBox.Margin.Top, 6f);
        _titleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        _titleBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 8f);

        _titleLabel = new Label();
        _titleLabel.AddStyleClass(StyleClass.LabelHeading);

        _titlePanel = new PanelContainer { PanelOverride = _titleBox, Visible = false };
        _titlePanel.AddChild(_titleLabel);

        _content = new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = ContentPadding };

        AddChild(_titlePanel);
        AddChild(_content);
        XamlChildren = _content.Children;
    }

    public string? Title
    {
        get => _titleLabel.Text;
        set
        {
            _titleLabel.Text = value;
            _titlePanel.Visible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>
    ///     The colour of the title text; null is white.
    /// </summary>
    public Color? TitleColor
    {
        get => _titleLabel.FontColorOverride;
        set => _titleLabel.FontColorOverride = value;
    }

    /// <summary>
    ///     The line under the title; the primary blue by default.
    /// </summary>
    public Color LineColor
    {
        get => _titleBox.BorderColor;
        set => _titleBox.BorderColor = value;
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        if (newChild == _titlePanel || newChild == _content)
            return;

        newChild.Orphan();
        _content.AddChild(newChild);
    }
}
