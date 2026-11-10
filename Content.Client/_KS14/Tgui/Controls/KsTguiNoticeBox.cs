using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Controls;

public enum KsTguiNoticeColor : byte
{
    Default,
    Info,
    Success,
    Warning,
    Danger,

    // The rest of tgui's colour names; Info, Success, Warning and Danger are the blue, green, orange and red.
    Yellow,
    Olive,
    Teal,
    Violet,
    Purple,
    Pink,
    Brown,
    Gold,
    Black,
    White,
    Grey,
    LightGrey,
    Primary,
    Good,
    Average,
    Bad,
    Label,
}

/// <summary>
///     tgui's NoticeBox: bold italic text on a striped coloured box. XAML children are shown after <see cref="Text"/>.
///         The text wraps. <see cref="Spaced"/> gives it tgui's 6px margin below.
/// </summary>
public sealed class KsTguiNoticeBox : PanelContainer
{
    public const string StyleClassNoticeBox = "KsTguiNoticeBox";
    public const string StyleClassNoticeText = "KsTguiNoticeText";

    private readonly BoxContainer _content;
    private readonly RichTextLabel _label;
    private string? _text;

    private KsTguiNoticeColor _noticeColor;
    private bool _spaced;

    public KsTguiNoticeBox()
    {
        AddStyleClass(StyleClassNoticeBox);
        AddStyleClass(ColorClass(_noticeColor));

        _label = new RichTextLabel { Visible = false };
        _label.AddStyleClass(StyleClassNoticeText);

        _content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _content.AddChild(_label);
        AddChild(_content);

        XamlChildren = _content.Children;
    }

    public string? Text
    {
        get => _text;
        set
        {
            _text = value;
            _label.SetMessage(FormattedMessage.FromUnformatted(value ?? string.Empty), TextColor(_noticeColor));
            _label.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public KsTguiNoticeColor NoticeColor
    {
        get => _noticeColor;
        set
        {
            if (_noticeColor == value)
                return;

            RemoveStyleClass(ColorClass(_noticeColor));
            _noticeColor = value;
            AddStyleClass(ColorClass(value));

            // Its text colour follows the box's.
            if (!string.IsNullOrEmpty(_text))
                _label.SetMessage(FormattedMessage.FromUnformatted(_text), TextColor(value));
        }
    }

    /// <summary>
    ///     tgui's <c>margin-bottom: var(--space-m)</c>, 6px under the box. It is off by default because a window built from
    ///         boxes with a gap between them already has that space; turn it on where the box sits in a container that doesn't.
    /// </summary>
    public bool Spaced
    {
        get => _spaced;
        set
        {
            _spaced = value;
            Margin = value ? new Thickness(0f, 0f, 0f, 6f) : default;
        }
    }

    /// <summary>
    ///     Black on the low-contrast colours, as <c>$low-contrast-color-map</c> sets <c>--notice-box-color</c>.
    /// </summary>
    public static Color TextColor(KsTguiNoticeColor color)
    {
        return color is KsTguiNoticeColor.Yellow or KsTguiNoticeColor.White ? Color.Black : Color.White;
    }

    /// <summary>
    ///     The token colour a notice colour is drawn from, which <c>NoticeBox.scss</c> then darkens in oklch. Null for the
    ///         default, whose token is the theme's.
    /// </summary>
    public static KsTguiColor? TokenOf(KsTguiNoticeColor color)
    {
        return color switch
        {
            KsTguiNoticeColor.Default => null,
            KsTguiNoticeColor.Info => KsTguiColor.Blue,
            KsTguiNoticeColor.Success => KsTguiColor.Green,
            KsTguiNoticeColor.Warning => KsTguiColor.Orange,
            KsTguiNoticeColor.Danger => KsTguiColor.Red,
            KsTguiNoticeColor.Yellow => KsTguiColor.Yellow,
            KsTguiNoticeColor.Olive => KsTguiColor.Olive,
            KsTguiNoticeColor.Teal => KsTguiColor.Teal,
            KsTguiNoticeColor.Violet => KsTguiColor.Violet,
            KsTguiNoticeColor.Purple => KsTguiColor.Purple,
            KsTguiNoticeColor.Pink => KsTguiColor.Pink,
            KsTguiNoticeColor.Brown => KsTguiColor.Brown,
            KsTguiNoticeColor.Gold => KsTguiColor.Gold,
            KsTguiNoticeColor.Black => KsTguiColor.Black,
            KsTguiNoticeColor.White => KsTguiColor.White,
            KsTguiNoticeColor.Grey => KsTguiColor.Grey,
            KsTguiNoticeColor.LightGrey => KsTguiColor.LightGrey,
            KsTguiNoticeColor.Primary => KsTguiColor.Primary,
            KsTguiNoticeColor.Good => KsTguiColor.Good,
            KsTguiNoticeColor.Average => KsTguiColor.Average,
            KsTguiNoticeColor.Bad => KsTguiColor.Bad,
            _ => KsTguiColor.Label,
        };
    }

    /// <summary>
    ///     The background of a box of this colour: <c>oklch(from token calc(l * 0.825) calc(c * 0.75) h)</c>.
    /// </summary>
    public static Color BackgroundOf(KsTguiNoticeColor color, Color defaultBackground)
    {
        return TokenOf(color) is { } token
            ? KsTguiThemePalette.OklchScale(KsTguiColors.Shade(token, 0f), 0.825f, 0.75f)
            : defaultBackground;
    }

    public static string ColorClass(KsTguiNoticeColor color)
    {
        return "KsTguiNotice" + color.ToString();
    }
}
