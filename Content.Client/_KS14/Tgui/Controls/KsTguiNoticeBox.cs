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
}

/// <summary>
///     tgui's NoticeBox: bold italic text on a striped coloured box. XAML children are shown after <see cref="Text"/>.
///         The text wraps.
/// </summary>
public sealed class KsTguiNoticeBox : PanelContainer
{
    public const string StyleClassNoticeBox = "KsTguiNoticeBox";
    public const string StyleClassNoticeText = "KsTguiNoticeText";

    private readonly BoxContainer _content;
    private readonly RichTextLabel _label;
    private string? _text;

    private KsTguiNoticeColor _noticeColor;

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
            _label.SetMessage(FormattedMessage.FromUnformatted(value ?? string.Empty), Color.White);
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
        }
    }

    public static string ColorClass(KsTguiNoticeColor color)
    {
        return "KsTguiNotice" + color.ToString();
    }
}
