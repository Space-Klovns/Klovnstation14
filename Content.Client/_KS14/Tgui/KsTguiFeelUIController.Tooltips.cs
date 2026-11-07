using System.Numerics;
using Content.Client._KS14.Tgui.Backdrop;
using Content.Client._KS14.Tgui.Tooltips;
using Content.Client.Stylesheets;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tgui's tooltips, for every control in a window: placed against the control instead of at the mouse, flipped and
///         shifted to stay inside the window, opening after the mouse rests 200 ms, and scaling and fading in and out.
///         Tooltips outside windows, such as on the context menu or the HUD, keep the engine's placement.
/// </summary>
/// <remarks>
///     The engine still decides when a tooltip shows and hides and makes it; this takes it over once it's in the popup
///         layer. A hidden tooltip is put back for its fade only while its control isn't hovered, because some controls
///         hand out the same tooltip every time, and the engine can't add one that's still parented.
/// </remarks>
public sealed partial class KsTguiFeelUIController
{
    /// <summary>
    ///     The engine's own delay, so a tooltip of a control that set none is known to be one.
    /// </summary>
    private const float EngineTooltipDelay = 0.25f;

    private static readonly AttachedProperty<bool> DelaySetProperty =
        AttachedProperty<bool>.Create("KsTguiTooltipDelaySet", typeof(KsTguiFeelUIController));

    private readonly List<KsTguiTooltipHost> _tooltipHosts = new();
    private readonly List<(Control Tooltip, Control Target)> _pendingTooltips = new();

    private Control? _restHovered;
    private Vector2 _restMousePosition;
    private float _restSeconds;
    private BaseWindow? _delayWindow;

    /// <summary>
    ///     Takes a tooltip the engine just showed, if it belongs to a control in a window.
    /// </summary>
    private bool TryTakeTooltip(Control control)
    {
        if (control is KsTguiTooltipHost or KsTguiDropdownGhost)
            return false;

        var target = UIManager.CurrentlyHovered;
        if (target == null || FindWindow(target) == null || !IsTooltipOf(control, target))
            return false;

        TakeTooltip(control, target);
        return true;
    }

    /// <summary>
    ///     Puts <paramref name="tooltip"/>, which the engine has just added to the popup layer for <paramref name="target"/>,
    ///         in a host on the next update. Public for tests, which have no mouse to make the engine show one.
    /// </summary>
    public void TakeTooltip(Control tooltip, Control target)
    {
        // Hidden until it's in its host.
        tooltip.Modulate = Color.White.WithAlpha(0f);
        _pendingTooltips.Add((tooltip, target));
    }

    /// <summary>
    ///     The engine shows a tooltip after the mouse has rested on a control for its delay. A drag ghost, the other thing put
    ///         in the popup layer over a window, comes while the mouse moves.
    /// </summary>
    private bool IsTooltipOf(Control control, Control target)
    {
        if (control is Tooltip)
            return true;

        if (target.TooltipSupplier == null && string.IsNullOrWhiteSpace(target.ToolTip))
            return false;

        return _restSeconds + 0.001f >= (target.TooltipDelay ?? EngineTooltipDelay);
    }

    /// <summary>
    ///     The tgui window a control is in. KS14's own diegetic windows (the nuke, the instrument) aren't tgui, so their tooltips
    ///         stay the engine's.
    /// </summary>
    private static BaseWindow? FindWindow(Control control)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current is BaseWindow window)
                return window is Content.Client._KS14.Shuttles.UI.KsBaseWindow ? null : window;
        }

        return null;
    }

    /// <summary>
    ///     Public for tests. Runs before the engine's tooltip timer in the same frame, so a fading tooltip is let go before the engine adds it
    ///         again.
    /// </summary>
    public void UpdateWindowTooltips(float deltaSeconds)
    {
        UpdateRest(deltaSeconds);

        foreach (var (tooltip, target) in _pendingTooltips)
            HostTooltip(tooltip, target);

        _pendingTooltips.Clear();

        for (var i = _tooltipHosts.Count - 1; i >= 0; i--)
        {
            var host = _tooltipHosts[i];
            if (!AdvanceHost(host, deltaSeconds))
            {
                DropHost(host);
                _tooltipHosts.RemoveAt(i);
            }
        }
    }

    private void UpdateRest(float deltaSeconds)
    {
        var hovered = UIManager.CurrentlyHovered;
        var mousePosition = UIManager.MousePositionScaled.Position;
        if (hovered != _restHovered || mousePosition != _restMousePosition)
        {
            _restSeconds = 0f;
            _restMousePosition = mousePosition;

            if (hovered != _restHovered && hovered != null)
                ShortenTooltipDelays(hovered);

            _restHovered = hovered;
            return;
        }

        _restSeconds += deltaSeconds;
    }

    /// <summary>
    ///     Gives tgui's 200 ms to every control in the window being moved over. The engine reads a control's delay as the mouse
    ///         enters it, so this is done ahead of time: on entering a window, and again on entering a control still without
    ///         one, which means the window has new contents.
    /// </summary>
    private void ShortenTooltipDelays(Control hovered)
    {
        if (!_configurationManager.GetCVar(KsCCVars.TguiStyle) || FindWindow(hovered) is not { } window)
        {
            _delayWindow = null;
            return;
        }

        if (window == _delayWindow && (hovered.TooltipDelay != null || !HasTooltip(hovered)))
            return;

        var hoveredHadDelay = hovered.TooltipDelay != null;
        _delayWindow = window;
        SetTooltipDelays(window);

        // The engine has already read this control's delay, the default, as the mouse entered it. Entering it again makes
        //      it read ours, so the first control hovered in new contents waits tg's 200 ms too. This happens in the frame the
        //      mouse arrived, before any tooltip is up, and the exit and entry cancel out: hover sounds restart, they don't
        //      stack, and the button's draw mode ends where it was.
        if (!hoveredHadDelay && hovered.TooltipDelay != null && UIManager.CurrentlyHovered == hovered)
        {
            UIManager.SetHovered(null);
            UIManager.SetHovered(hovered);
        }
    }

    private static bool HasTooltip(Control control)
    {
        return control.TooltipSupplier != null || !string.IsNullOrWhiteSpace(control.ToolTip);
    }

    public static void SetTooltipDelays(Control control)
    {
        if (control.TooltipDelay == null && HasTooltip(control))
        {
            control.TooltipDelay = KsTguiTooltip.Delay;
            control.SetValue(DelaySetProperty, true);
        }

        foreach (var child in control.Children)
            SetTooltipDelays(child);
    }

    public static void ResetTooltipDelays(Control control)
    {
        if (control.GetValue(DelaySetProperty))
        {
            control.TooltipDelay = null;
            control.SetValue(DelaySetProperty, false);
        }

        foreach (var child in control.Children)
            ResetTooltipDelays(child);
    }

    private void HostTooltip(Control tooltip, Control target)
    {
        // The engine may have hidden it already, or shown it somewhere else.
        if (tooltip.Parent != UIManager.PopupRoot)
            return;

        UIManager.PopupRoot.RemoveChild(tooltip);
        tooltip.Modulate = Color.White;

        if (tooltip is Tooltip engineTooltip)
            engineTooltip.Tracking = false;

        if (tooltip is Tooltip || tooltip.HasStyleClass(StyleClass.TooltipPanel))
            tooltip.MaxWidth = MathF.Min(tooltip.MaxWidth, KsTguiTooltip.MaxWidth);

        var host = new KsTguiTooltipHost(tooltip, target)
        {
            Placement = KsTguiTooltip.GetPlacement(target),
            Stylesheet = tooltip.Stylesheet,
            Backdrop = UIManager.GetUIController<KsTguiBackdropUIController>(),
        };

        UIManager.PopupRoot.AddChild(host);
        _tooltipHosts.Add(host);
        PlaceHost(host);
    }

    /// <returns>False once the host is done with.</returns>
    private bool AdvanceHost(KsTguiTooltipHost host, float deltaSeconds)
    {
        var tooltip = host.Tooltip;

        // Its control went, with its window or otherwise: nothing to point at.
        if (!host.Target.VisibleInTree)
            return false;

        if (!host.Closing && tooltip.Parent != host)
        {
            // Hidden by the engine. Fade out only what nothing else holds, of a control that is still there and not hovered.
            if (tooltip.Parent != null || UIManager.CurrentlyHovered == host.Target)
                return false;

            host.AddChild(tooltip);
            host.Closing = true;
        }

        if (host.Closing && UIManager.CurrentlyHovered == host.Target)
            return false;

        host.Progress += (host.Closing ? -deltaSeconds : deltaSeconds) / KsTguiTooltip.TransitionSeconds;
        host.Progress = Math.Clamp(host.Progress, 0f, 1f);
        if (host.Closing && host.Progress <= 0f)
            return false;

        var eased = CssEase(host.Progress);
        host.Modulate = Color.White.WithAlpha(eased);
        host.Scale = KsTguiTooltip.StartScale + (1f - KsTguiTooltip.StartScale) * eased;

        PlaceHost(host);
        return true;
    }

    private void DropHost(KsTguiTooltipHost host)
    {
        host.ReleaseBackdrop();

        if (host.Tooltip.Parent == host)
            host.RemoveChild(host.Tooltip);

        host.Orphan();
    }

    private void DropAllTooltipHosts()
    {
        foreach (var host in _tooltipHosts)
            DropHost(host);

        _tooltipHosts.Clear();
        _pendingTooltips.Clear();

        foreach (var window in UIManager.WindowRoot.Children)
            ResetTooltipDelays(window);

        _delayWindow = null;
    }

    /// <summary>
    ///     floating-ui's <c>offset(6)</c>, <c>flip({ padding: 6 })</c> and <c>shift()</c>, inside the control's window, since a
    ///         tg tooltip can't leave its window's browser. Re-done each frame, so it follows a tooltip that changes size.
    /// </summary>
    private void PlaceHost(KsTguiTooltipHost host)
    {
        if (FindWindow(host.Target) is not { } window)
            return;

        host.Tooltip.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var size = host.Tooltip.DesiredSize;
        var targetBox = UIBox2.FromDimensions(host.Target.GlobalPosition, host.Target.Size);
        var bounds = UIBox2.FromDimensions(window.GlobalPosition, window.Size);

        var (topLeft, placement) = Place(size, targetBox, bounds, host.Placement);

        var pivot = placement switch
        {
            KsTguiTooltipPlacement.Top => new Vector2(size.X / 2f, size.Y),
            KsTguiTooltipPlacement.Left => new Vector2(size.X, size.Y / 2f),
            KsTguiTooltipPlacement.Right => new Vector2(0f, size.Y / 2f),
            _ => new Vector2(size.X / 2f, 0f),
        };

        LayoutContainer.SetPosition(host, topLeft + pivot * (1f - host.Scale));
    }

    /// <summary>
    ///     Where a tooltip of <paramref name="size"/> goes, and on which side it ended up.
    /// </summary>
    public static (Vector2 TopLeft, KsTguiTooltipPlacement Placement) Place(Vector2 size, UIBox2 targetBox, UIBox2 bounds,
        KsTguiTooltipPlacement preferred)
    {
        var placement = preferred;
        var preferredOverflow = Overflow(size, targetBox, bounds, preferred);
        if (preferredOverflow > 0f)
        {
            var opposite = Opposite(preferred);
            var oppositeOverflow = Overflow(size, targetBox, bounds, opposite);

            // floating-ui's bestFit: the side that fits, or failing that, the one that overflows least.
            if (oppositeOverflow < preferredOverflow)
                placement = opposite;
        }

        var topLeft = MainAxisPosition(size, targetBox, placement);

        // shift(): slides along the side it's on to stay in the window, the far edge first so the near one wins.
        if (placement is KsTguiTooltipPlacement.Bottom or KsTguiTooltipPlacement.Top)
            topLeft.X = MathF.Max(MathF.Min(topLeft.X, bounds.Right - size.X), bounds.Left);
        else
            topLeft.Y = MathF.Max(MathF.Min(topLeft.Y, bounds.Bottom - size.Y), bounds.Top);

        return (topLeft, placement);
    }

    private static Vector2 MainAxisPosition(Vector2 size, UIBox2 targetBox, KsTguiTooltipPlacement placement)
    {
        var center = targetBox.Center;
        return placement switch
        {
            KsTguiTooltipPlacement.Top => new Vector2(center.X - size.X / 2f, targetBox.Top - KsTguiTooltip.Offset - size.Y),
            KsTguiTooltipPlacement.Left => new Vector2(targetBox.Left - KsTguiTooltip.Offset - size.X, center.Y - size.Y / 2f),
            KsTguiTooltipPlacement.Right => new Vector2(targetBox.Right + KsTguiTooltip.Offset, center.Y - size.Y / 2f),
            _ => new Vector2(center.X - size.X / 2f, targetBox.Bottom + KsTguiTooltip.Offset),
        };
    }

    /// <summary>
    ///     How far past the window, less flip's padding, the tooltip reaches on the side it opens towards.
    /// </summary>
    private static float Overflow(Vector2 size, UIBox2 targetBox, UIBox2 bounds, KsTguiTooltipPlacement placement)
    {
        var topLeft = MainAxisPosition(size, targetBox, placement);
        return placement switch
        {
            KsTguiTooltipPlacement.Top => bounds.Top + KsTguiTooltip.FlipPadding - topLeft.Y,
            KsTguiTooltipPlacement.Left => bounds.Left + KsTguiTooltip.FlipPadding - topLeft.X,
            KsTguiTooltipPlacement.Right => topLeft.X + size.X - (bounds.Right - KsTguiTooltip.FlipPadding),
            _ => topLeft.Y + size.Y - (bounds.Bottom - KsTguiTooltip.FlipPadding),
        };
    }

    private static KsTguiTooltipPlacement Opposite(KsTguiTooltipPlacement placement)
    {
        return placement switch
        {
            KsTguiTooltipPlacement.Top => KsTguiTooltipPlacement.Bottom,
            KsTguiTooltipPlacement.Left => KsTguiTooltipPlacement.Right,
            KsTguiTooltipPlacement.Right => KsTguiTooltipPlacement.Left,
            _ => KsTguiTooltipPlacement.Top,
        };
    }

    /// <summary>
    ///     CSS <c>ease</c>, <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c>: x solved for the progress by Newton's method, then y.
    /// </summary>
    public static float CssEase(float progress)
    {
        if (progress <= 0f)
            return 0f;

        if (progress >= 1f)
            return 1f;

        const float x1 = 0.25f, y1 = 0.1f, x2 = 0.25f, y2 = 1f;

        var t = progress;
        for (var i = 0; i < 8; i++)
        {
            var x = Bezier(t, x1, x2) - progress;
            var slope = BezierSlope(t, x1, x2);
            if (MathF.Abs(slope) < 1e-6f)
                break;

            t = Math.Clamp(t - x / slope, 0f, 1f);
        }

        return Bezier(t, y1, y2);
    }

    private static float Bezier(float t, float p1, float p2)
    {
        var u = 1f - t;
        return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
    }

    private static float BezierSlope(float t, float p1, float p2)
    {
        var u = 1f - t;
        return 3f * u * u * p1 + 6f * u * t * (p2 - p1) + 3f * t * t * (1f - p2);
    }
}
