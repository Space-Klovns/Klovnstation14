using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Tooltips;

/// <summary>
///     Which side of its control a window's tooltip opens on: tgui's <c>Tooltip position</c>. It flips to the other side when
///         there's no room, as tg's does.
/// </summary>
public enum KsTguiTooltipPlacement : byte
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
///     tgui's <c>Tooltip</c>: in a window, a control's tooltip is placed against the control rather than the mouse, and
///         eases in and out. <see cref="KsTguiFeelUIController"/> does that to whatever tooltip the engine shows, so every
///         control in a window gets it with no change to the control.
/// </summary>
public static class KsTguiTooltip
{
    /// <summary>
    ///     tgui-core's <c>max-width: fn.em(250px)</c>.
    /// </summary>
    public const float MaxWidth = 250f;

    /// <summary>
    ///     tgui's Floating <c>contentOffset</c>: the gap between the control and its tooltip.
    /// </summary>
    public const float Offset = 6f;

    /// <summary>
    ///     floating-ui's <c>flip({ padding: 6 })</c>: how near the window's edge a tooltip may come before it flips.
    /// </summary>
    public const float FlipPadding = 6f;

    /// <summary>
    ///     tgui's hover <c>restMs</c>: how long the mouse rests on a control before its tooltip opens.
    /// </summary>
    public const float Delay = 0.2f;

    /// <summary>
    ///     <c>--floating-transition-time</c>, both ways.
    /// </summary>
    public const float TransitionSeconds = 0.2f;

    /// <summary>
    ///     What tg's tooltip scales up from as it opens and down to as it closes.
    /// </summary>
    public const float StartScale = 0.9f;

    private static readonly AttachedProperty<KsTguiTooltipPlacement> PlacementProperty =
        AttachedProperty<KsTguiTooltipPlacement>.Create("KsTguiTooltipPlacement", typeof(KsTguiTooltip));

    public static KsTguiTooltipPlacement GetPlacement(Control control)
    {
        return control.GetValue(PlacementProperty);
    }

    public static void SetPlacement(Control control, KsTguiTooltipPlacement placement)
    {
        control.SetValue(PlacementProperty, placement);
    }

    /// <summary>
    ///     A tooltip holding any control, for a <see cref="Control.TooltipSupplier"/>: the tooltip's box around it. Make a new
    ///         one each time, since the old one may still be fading out.
    /// </summary>
    public static Control Create(Control content)
    {
        var panel = new PanelContainer { MaxWidth = MaxWidth };
        panel.AddStyleClass(StyleClass.TooltipPanel);
        panel.AddChild(content);
        return panel;
    }

    /// <summary>
    ///     A plain text tooltip, as <see cref="Create(Control)"/>.
    /// </summary>
    public static Control Create(string text)
    {
        var label = new RichTextLabel();
        label.SetMessage(text);
        return Create(label);
    }
}
