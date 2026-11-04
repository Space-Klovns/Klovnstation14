using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     The row of buttons at the right of a <see cref="KsTguiSection"/>'s title, tgui's <c>buttons</c> prop.
///         Written as a child of the section in xaml; the section moves it into the title row.
/// </summary>
public sealed class KsTguiSectionButtons : BoxContainer
{
    public KsTguiSectionButtons()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 2;
        VerticalAlignment = VAlignment.Center;
    }
}

/// <summary>
///     tgui's Section: a translucent dark panel with a bold title over a 2px primary-blue rule, buttons at the right of
///         the title, and the body below. In xaml every child goes to the body except a <see cref="KsTguiSectionButtons"/>.
/// </summary>
public sealed partial class KsTguiSection : PanelContainer
{
    private const float TitlePadding = 6f;
    private const float BodyPaddingHorizontal = 6f;
    private const float BodyPaddingVertical = 8f;

    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly BoxContainer _layout;
    private readonly PanelContainer _titleRow;
    private readonly BoxContainer _titleBox;
    private readonly Label _titleLabel;
    private readonly ScrollContainer _scroll;
    private KsTguiSectionButtons? _buttons;

    public KsTguiSection()
    {
        IoCManager.InjectDependencies(this);

        PanelOverride = KsTguiTheme.FlatBox(KsTguiTheme.Section);

        _titleLabel = new Label
        {
            HorizontalExpand = true,
            VAlign = Label.VAlignMode.Center,
            FontOverride = KsTguiTheme.BoldFont(_resourceCache, 14),
            FontColorOverride = KsTguiTheme.Text,
        };

        _titleBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children = { _titleLabel },
        };

        _titleRow = new PanelContainer
        {
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Black.WithAlpha(0f),
                BorderColor = KsTguiTheme.Primary,
                BorderThickness = new Thickness(0f, 0f, 0f, 2f),
                ContentMarginLeftOverride = TitlePadding,
                ContentMarginRightOverride = TitlePadding,
                ContentMarginTopOverride = TitlePadding - 1f,
                ContentMarginBottomOverride = TitlePadding - 1f,
            },
            Children = { _titleBox },
        };

        Body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(BodyPaddingHorizontal, BodyPaddingVertical),
            SeparationOverride = 4,
        };

        _scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            Visible = false,
        };

        _layout = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children = { _titleRow, Body },
        };

        AddChild(_layout);
        XamlChildren = new KsTguiChildRouter(Body, AddRouted);
    }

    /// <summary>
    ///     Where the section's contents go.
    /// </summary>
    public BoxContainer Body { get; }

    public string? Title
    {
        get => _titleLabel.Text;
        set
        {
            _titleLabel.Text = value;
            UpdateTitleRow();
        }
    }

    /// <summary>
    ///     tgui's <c>scrollable</c>: the body scrolls inside whatever height the section gets, instead of sizing the
    ///         section to its contents. Give a scrollable section <c>VerticalExpand</c> or a height.
    /// </summary>
    public bool Scrollable
    {
        get => _scroll.Visible;
        set
        {
            if (_scroll.Visible == value)
                return;

            _scroll.Visible = value;
            Body.Orphan();
            if (value)
            {
                _scroll.AddChild(Body);
                _layout.AddChild(_scroll);
            }
            else
            {
                _scroll.Orphan();
                _layout.AddChild(Body);
            }
        }
    }

    private void AddRouted(Control child)
    {
        if (child is KsTguiSectionButtons buttons)
        {
            SetButtons(buttons);
            return;
        }

        Body.AddChild(child);
    }

    private void SetButtons(KsTguiSectionButtons buttons)
    {
        _buttons?.Orphan();
        _buttons = buttons;
        _titleBox.AddChild(buttons);
        UpdateTitleRow();
    }

    private void UpdateTitleRow()
    {
        _titleRow.Visible = !string.IsNullOrEmpty(_titleLabel.Text) || _buttons != null;
    }
}
