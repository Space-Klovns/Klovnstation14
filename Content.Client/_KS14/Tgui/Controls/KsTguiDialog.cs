using Content.Client.Stylesheets.Fonts;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Dialog (<c>Dialog.js</c>, <c>Dialog.scss</c>): a layer over the whole window, black at half opacity, with
///         a centred panel of the window's grey. The panel has a header (the title in italic at a third of the opacity, and a
///         close button), a body with <see cref="BodyMargin"/> around it, and a footer of buttons pushed to the right. It
///         covers its parent, so add it to a host that stacks its children, as <see cref="KsTguiDimmer"/> does. All the
///         lengths are in ems of the dialog's 14px font.
/// </summary>
public sealed partial class KsTguiDialog : Control
{
    /// <summary>
    ///     <c>.Dialog__content</c>'s font size.
    /// </summary>
    public const int FontSize = 14;

    /// <summary>
    ///     The default <c>width</c> of the panel.
    /// </summary>
    public const float DefaultWidth = 370f;

    /// <summary>
    ///     <c>.Dialog__header</c>: <c>height: 2em</c>.
    /// </summary>
    public const float HeaderHeight = 28f;

    /// <summary>
    ///     <c>.Dialog__title</c>: <c>margin-left: var(--space-xl)</c>, one em.
    /// </summary>
    public const float TitleMarginLeft = 14f;

    /// <summary>
    ///     <c>.Dialog__title</c>: <c>margin-right: var(--space-xxl)</c>, two ems.
    /// </summary>
    public const float TitleMarginRight = 28f;

    /// <summary>
    ///     <c>.Dialog__footer</c>'s padding, one em.
    /// </summary>
    public const float FooterPadding = 14f;

    /// <summary>
    ///     The close button's box: <c>width: 26px</c>, <c>lineHeight: 22px</c>.
    /// </summary>
    public static readonly Vector2 CloseButtonSize = new(26f, 22f);

    /// <summary>
    ///     <c>.Dialog__body</c>: <c>margin: var(--space-xxl) var(--space-xl)</c>.
    /// </summary>
    public static readonly Thickness BodyMargin = new(14f, 28f, 14f, 28f);

    /// <summary>
    ///     <c>--dialog-background</c>: black at half opacity.
    /// </summary>
    public static readonly Color Dimming = KsTguiPalettes.Translucent(Color.Black, 0.5f, KsTguiPalettes.Base);

    /// <summary>
    ///     The footer's wash, the same black at a quarter.
    /// </summary>
    public static readonly Color FooterWash = KsTguiPalettes.Translucent(Color.Black, 0.25f, KsTguiPalettes.Base);

    [Dependency] private KsTguiFonts _fonts = default!;

    private readonly PanelContainer _panel;
    private readonly Label _titleLabel;
    private readonly KsTguiButton _closeButton;
    private readonly Control _bodySlot;
    private readonly BoxContainer _footer;

    private float _dialogWidth = DefaultWidth;
    private float _dialogHeight;

    /// <summary>
    ///     Raised by the close button, and by <see cref="Close"/>.
    /// </summary>
    public event Action? OnClose;

    public KsTguiDialog()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Stop;

        _titleLabel = new Label
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            FontOverride = _fonts.GetMonoFont(FontSize, FontKind.Italic),
            // Opacity .33 over the panel's grey.
            FontColorOverride = KsTguiPalettes.Translucent(Color.White, 0.33f, KsTguiPalettes.Base),
            Margin = new Thickness(TitleMarginLeft, 0f, TitleMarginRight, 0f),
            ClipText = true,
        };

        _closeButton = new KsTguiButton
        {
            Color = KsTguiColor.Transparent,
            Icon = KsTguiIcons.Times,
            MinSize = CloseButtonSize,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("ks-tgui-dialog-close"),
        };
        _closeButton.OnPressed += _ => Close();

        // The header's close button sits in a Box with mr=2 (12px), and has mr=-3px.
        var closeHolder = new Control
        {
            Margin = new Thickness(0f, 0f, 12f, 0f),
            VerticalAlignment = VAlignment.Center,
        };
        _closeButton.Margin = new Thickness(0f, 0f, -3f, 0f);
        closeHolder.AddChild(_closeButton);

        var headerRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        headerRow.AddChild(_titleLabel);
        headerRow.AddChild(closeHolder);

        var header = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Dimming },
            MinHeight = HeaderHeight,
        };
        header.AddChild(headerRow);

        _bodySlot = new Control { VerticalExpand = true, Margin = BodyMargin };

        _footer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Right,
        };
        var footerPanel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = FooterWash } };
        var footerPadding = new Control { Margin = new Thickness(FooterPadding) };
        footerPadding.AddChild(_footer);
        footerPanel.AddChild(footerPadding);

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        column.AddChild(header);
        column.AddChild(_bodySlot);
        column.AddChild(footerPanel);

        _panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = KsTguiPalettes.Base },
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            SetWidth = _dialogWidth,
        };
        _panel.AddChild(column);
        AddChild(_panel);
    }

    /// <summary>
    ///     The text in the header.
    /// </summary>
    public string? Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    /// <summary>
    ///     tgui's <c>width</c>: the panel's width in pixels, <see cref="DefaultWidth"/> by default.
    /// </summary>
    public float DialogWidth
    {
        get => _dialogWidth;
        set
        {
            _dialogWidth = value > 0f ? value : DefaultWidth;
            _panel.SetWidth = _dialogWidth;
        }
    }

    /// <summary>
    ///     tgui's <c>height</c>: the panel's height in pixels. Zero fits the contents.
    /// </summary>
    public float DialogHeight
    {
        get => _dialogHeight;
        set
        {
            _dialogHeight = value;
            _panel.SetHeight = value > 0f ? value : float.NaN;
        }
    }

    /// <summary>
    ///     What sits in the body, with <see cref="BodyMargin"/> around it.
    /// </summary>
    public Control? Body
    {
        get => _bodySlot.ChildCount > 0 ? _bodySlot.GetChild(0) : null;
        set
        {
            _bodySlot.RemoveAllChildren();
            if (value != null)
                _bodySlot.AddChild(value);
        }
    }

    public KsTguiButton CloseButton => _closeButton;

    public Control Panel => _panel;

    /// <summary>
    ///     Adds a <see cref="KsTguiDialogButton"/> to the footer.
    /// </summary>
    public KsTguiDialogButton AddButton(string text, Action? onPressed = null)
    {
        var button = new KsTguiDialogButton { Text = text };
        if (onPressed != null)
            button.OnPressed += _ => onPressed();

        // The 1em margin is on a holder: the tgui sheet gives every Button its own 2px margin, which beats one set here.
        var holder = new Control { Margin = new Thickness(14f, 0f) };
        holder.AddChild(button);
        button.Holder = holder;
        _footer.AddChild(holder);
        return button;
    }

    /// <summary>
    ///     What the close button does: raises <see cref="OnClose"/>, which the owner answers by removing the dialog.
    /// </summary>
    public void Close()
    {
        OnClose?.Invoke();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Dimming);
    }
}

/// <summary>
///     tgui's Dialog.Button: a Button with <c>margin: 0 1em</c> (14px either side), 24px tall and at least 72px wide, its text
///         centred. The margin is a holder's, kept at 14px net of whatever margin the sheet gives the button itself.
/// </summary>
public sealed class KsTguiDialogButton : KsTguiButton
{
    public static readonly Vector2 MinimumSize = new(72f, 24f);

    public KsTguiDialogButton()
    {
        MinSize = MinimumSize;
        TextAlign = Label.AlignMode.Center;

        // .Dialog__content sets the mono family and 14px for everything inside, and a Button sets neither of its own.
        Label.FontOverride = IoCManager.Resolve<KsTguiFonts>().GetMonoFont(KsTguiDialog.FontSize);
    }

    internal Control? Holder { get; set; }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        if (Holder != null)
            Holder.Margin = new Thickness(14f - Margin.Left, -Margin.Top, 14f - Margin.Right, -Margin.Bottom);
    }
}
