using Content.Client._KS14.Tgui.Controls;

namespace Content.Client.UserInterface.Controls;

// Partial so the tgui theme, whose notice boxes have no edge lines, can turn off the hardcoded ones.
public sealed partial class StripeBack
{
    /// <summary>
    ///     A style property: when true, no edge lines are drawn or made room for, whatever the edge settings say.
    /// </summary>
    public const string KsStylePropertyFlat = "ks-flat";

    private KsTguiNoticeColor? _ksNoticeColor;

    private bool KsFlat => TryGetStyleProperty<bool>(KsStylePropertyFlat, out var flat) && flat;

    private bool KsDrawTopEdge => HasTopEdge && !KsFlat;

    private bool KsDrawBottomEdge => HasBottomEdge && !KsFlat;

    /// <summary>
    ///     Under the tgui theme, which of tgui's notice colours the stripes are drawn in, as tg's <c>NoticeBox color</c>.
    ///         Null is the default notice colour. Baron has no rules for these, so its stripe back is unchanged.
    /// </summary>
    public KsTguiNoticeColor? KsNoticeColor
    {
        get => _ksNoticeColor;
        set
        {
            if (_ksNoticeColor == value)
                return;

            if (_ksNoticeColor is { } old)
                RemoveStyleClass(KsTguiNoticeBox.ColorClass(old));

            _ksNoticeColor = value;

            if (value is { } color)
                AddStyleClass(KsTguiNoticeBox.ColorClass(color));
        }
    }
}
