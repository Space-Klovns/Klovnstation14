using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's button colours. <see cref="KsTguiButton.Selected"/> overrides all of them with green.
/// </summary>
public enum KsTguiButtonColor : byte
{
    Default,
    Transparent,
    Good,
    Average,
    Bad,
    Caution,
    Danger,
    Green,
    Blue,
}

/// <summary>
///     tgui's Button: a flat 20px block in the token's background shade, lighter on hover and darker while held,
///         half-transparent when disabled, with an optional icon before the text.
/// </summary>
public sealed partial class KsTguiButton : ContainerButton
{
    private const float HorizontalPadding = 6f;
    private const float IconSlotWidth = 16f;
    private const float IconMargin = 3f;

    private static readonly Color TransparentHover = Color.White.WithAlpha(0.1f);
    private static readonly Color TransparentPressed = Color.White.WithAlpha(0.075f);
    private static readonly Color TransparentSelectedText = Color.FromHex("#22C358");
    private static readonly Color DisabledModulate = Color.White.WithAlpha(0.5f);

    [Dependency] private IResourceCache _resourceCache = default!;

    // Initialized inline: the ContainerButton constructor calls DrawModeChanged before ours runs.
    private readonly BoxContainer _row = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
    };

    private readonly Control _iconSlot = new()
    {
        MinWidth = IconSlotWidth,
        Margin = new Thickness(IconMargin, 0f),
        Visible = false,
    };

    private readonly KsTguiIcon _icon = new()
    {
        SetSize = new Vector2(12f, 12f),
        HorizontalAlignment = HAlignment.Center,
        VerticalAlignment = VAlignment.Center,
    };

    private readonly Label _label = new()
    {
        VAlign = Label.VAlignMode.Center,
        Visible = false,
    };

    private KsTguiButtonColor _buttonColor;
    private bool _selected;
    private Color? _iconColor;
    private int _fontSize = KsTguiTheme.FontSize;

    public KsTguiButton()
    {
        IoCManager.InjectDependencies(this);

        MinHeight = KsTguiTheme.ButtonHeight;
        // The sheet tints every button's box through ModulateSelf; this button picks its own colours.
        ModulateSelfOverride = Color.White;

        _iconSlot.AddChild(_icon);
        _row.AddChild(_iconSlot);
        _row.AddChild(_label);
        AddChild(_row);

        _label.FontOverride = KsTguiTheme.RegularFont(_resourceCache);
        UpdateStyle();
    }

    public string? Text
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            _label.Visible = !string.IsNullOrEmpty(value);
            UpdateStyle();
        }
    }

    public KsTguiIconKind Icon
    {
        get => _icon.Kind;
        set
        {
            _icon.Kind = value;
            _iconSlot.Visible = value != KsTguiIconKind.None;
            UpdateStyle();
        }
    }

    /// <summary>
    ///     The icon's colour, when it isn't the text colour. The reagent dispenser tints its drop icons this way.
    /// </summary>
    public Color? IconColor
    {
        get => _iconColor;
        set
        {
            _iconColor = value;
            UpdateStyle();
        }
    }

    public KsTguiButtonColor ButtonColor
    {
        get => _buttonColor;
        set
        {
            _buttonColor = value;
            UpdateStyle();
        }
    }

    /// <summary>
    ///     tgui's <c>selected</c>: the button shows green whatever its colour. Driven by state, not by clicks.
    /// </summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateStyle();
        }
    }

    /// <summary>
    ///     Text alignment inside a button wider than its text. Left by default, like tgui's fluid buttons.
    /// </summary>
    public Label.AlignMode TextAlign
    {
        get => _label.Align;
        set
        {
            _label.Align = value;
            _label.HorizontalExpand = value != Label.AlignMode.Left || _label.ClipText;
        }
    }

    /// <summary>
    ///     Lets the label cut its text off instead of widening the button. Only for buttons whose width comes from
    ///         their parent: a clipped label measures zero wide.
    /// </summary>
    public bool ClipText
    {
        get => _label.ClipText;
        set
        {
            _label.ClipText = value;
            _label.HorizontalExpand = value || _label.Align != Label.AlignMode.Left;
        }
    }

    public int FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            _label.FontOverride = KsTguiTheme.RegularFont(_resourceCache, value);
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateStyle();
    }

    private void UpdateStyle()
    {
        var drawMode = DrawMode;
        var selected = _selected;

        // A toggled-on toggle button reads as selected, not as held down.
        if (ToggleMode && Pressed)
        {
            selected = true;
            if (drawMode == DrawModeEnum.Pressed)
                drawMode = IsHovered ? DrawModeEnum.Hover : DrawModeEnum.Normal;
        }

        var hasIcon = _icon.Kind != KsTguiIconKind.None;
        var hasText = !string.IsNullOrEmpty(_label.Text);

        var key = (_buttonColor, selected, drawMode, hasIcon, hasText);
        if (!Boxes.TryGetValue(key, out var box))
        {
            box = BuildBox(_buttonColor, selected, drawMode, hasIcon, hasText);
            Boxes.Add(key, box);
        }

        StyleBoxOverride = box;

        var textColor = TextColorFor(_buttonColor, selected, drawMode);
        _label.FontColorOverride = textColor;
        _icon.Color = _iconColor ?? textColor;

        // tgui fades the whole disabled button, label and icon included.
        Modulate = drawMode == DrawModeEnum.Disabled ? DisabledModulate : Color.White;
    }

    /// <summary>
    ///     Every button of a colour shares one box per state, the way stylesheet buttons share theirs.
    /// </summary>
    private static readonly Dictionary<(KsTguiButtonColor, bool, DrawModeEnum, bool, bool), StyleBoxFlat> Boxes = new();

    private static StyleBoxFlat BuildBox(KsTguiButtonColor buttonColor, bool selected, DrawModeEnum drawMode, bool hasIcon, bool hasText)
    {
        // With an icon, the icon's own margin replaces the left padding; an icon-only button is just the icon.
        var box = new StyleBoxFlat
        {
            ContentMarginLeftOverride = hasIcon ? 0f : HorizontalPadding,
            ContentMarginRightOverride = hasText ? HorizontalPadding : 0f,
            ContentMarginTopOverride = 0f,
            ContentMarginBottomOverride = 0f,
        };

        // A selected transparent button stays transparent; only its text turns green.
        if (buttonColor == KsTguiButtonColor.Transparent)
        {
            box.BackgroundColor = drawMode switch
            {
                DrawModeEnum.Hover => TransparentHover,
                DrawModeEnum.Pressed => TransparentPressed,
                _ => Color.Black.WithAlpha(0f),
            };
            return box;
        }

        var background = KsTguiTheme.Background(selected ? KsTguiTheme.Green : TokenFor(buttonColor));
        box.BackgroundColor = drawMode switch
        {
            DrawModeEnum.Hover => KsTguiTheme.AdjustLightness(background, 10f),
            DrawModeEnum.Pressed => KsTguiTheme.AdjustLightness(background, -7.5f),
            _ => background,
        };
        return box;
    }

    private static Color TokenFor(KsTguiButtonColor buttonColor)
    {
        return buttonColor switch
        {
            KsTguiButtonColor.Good => KsTguiTheme.Good,
            KsTguiButtonColor.Average => KsTguiTheme.Average,
            KsTguiButtonColor.Bad => KsTguiTheme.Bad,
            KsTguiButtonColor.Caution => KsTguiTheme.Yellow,
            KsTguiButtonColor.Danger => KsTguiTheme.Red,
            KsTguiButtonColor.Green => KsTguiTheme.Green,
            KsTguiButtonColor.Blue => KsTguiTheme.Blue,
            _ => KsTguiTheme.Primary,
        };
    }

    private static Color TextColorFor(KsTguiButtonColor buttonColor, bool selected, DrawModeEnum drawMode)
    {
        if (buttonColor != KsTguiButtonColor.Transparent)
            return KsTguiTheme.Text;

        if (selected)
            return TransparentSelectedText;

        return drawMode == DrawModeEnum.Hover ? KsTguiTheme.Text : KsTguiTheme.TextTranslucent;
    }
}
