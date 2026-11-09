using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     What a window's title bar says about whether its user can use it, tgui's <c>status</c> prop.
/// </summary>
public enum KsTguiWindowStatus
{
    /// <summary>
    ///     <c>UI_INTERACTIVE</c>: the user can use the window.
    /// </summary>
    Interactive,

    /// <summary>
    ///     <c>UI_UPDATE</c>: the user sees the window update but cannot use it.
    /// </summary>
    Update,

    /// <summary>
    ///     <c>UI_DISABLED</c>: the window is shown and the server refuses everything.
    /// </summary>
    Disabled,
}

/// <summary>
///     tgui's <c>TitleBar__statusIcon</c>: a 45px cell at the left of the title bar, a 20px Font Awesome glyph centred in it, whose
///         colour eases to a new status over <c>--transition-time-slow</c> (0.5 s). Takes no mouse input, so the bar under it drags
///         the window, as tgui's drag zone does.
/// </summary>
public sealed class KsTguiTitleStatusIcon : Control
{
    private const float CellWidth = 45f;

    /// <summary>
    ///     20px in ems of the 12px base.
    /// </summary>
    private const float GlyphSize = 20f / 12f;

    /// <summary>
    ///     <c>--transition-time-slow</c>.
    /// </summary>
    public const float TransitionSeconds = 0.5f;

    private readonly KsTguiIcon _icon = new() { IconSize = GlyphSize, HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Center, MouseFilter = MouseFilterMode.Ignore };

    private KsTguiWindowStatus _status;
    private Color _from;
    private Color _to;
    private float _elapsed = TransitionSeconds;

    public KsTguiTitleStatusIcon(KsTguiWindowStatus status)
    {
        MouseFilter = MouseFilterMode.Ignore;
        MinSize = new Vector2(CellWidth, KsTguiGeometry.TitleBarHeight);
        SetSize = MinSize;
        HorizontalExpand = false;
        AddChild(_icon);

        _status = status;
        _icon.Glyph = GlyphFor(status);
        _from = _to = ColorFor(status);
        _icon.IconColor = _to;
    }

    public KsTguiWindowStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            // A CSS transition told a new target part way restarts from the colour it has reached, for the whole time.
            _from = Current;
            _to = ColorFor(value);
            _elapsed = 0f;
            _status = value;
            _icon.Glyph = GlyphFor(value);
        }
    }

    /// <summary>
    ///     Shows a status at once, with no transition.
    /// </summary>
    public void Place(KsTguiWindowStatus status)
    {
        _status = status;
        _icon.Glyph = GlyphFor(status);
        _from = _to = ColorFor(status);
        _elapsed = TransitionSeconds;
        _icon.IconColor = _to;
    }

    /// <summary>
    ///     The glyph shown.
    /// </summary>
    public string? Glyph => _icon.Glyph;

    /// <summary>
    ///     The colour drawn now, part way through a transition.
    /// </summary>
    public Color Current
    {
        get
        {
            if (_elapsed >= TransitionSeconds)
                return _to;

            // CSS transitions colours in sRGB with a linear ease-in-out of time ("ease"); the components here are sRGB already.
            var t = _elapsed / TransitionSeconds;
            return new Color(
                MathHelper.Lerp(_from.R, _to.R, t),
                MathHelper.Lerp(_from.G, _to.G, t),
                MathHelper.Lerp(_from.B, _to.B, t),
                MathHelper.Lerp(_from.A, _to.A, t));
        }
    }

    private static string GlyphFor(KsTguiWindowStatus status)
    {
        return status switch
        {
            KsTguiWindowStatus.Disabled => KsTguiIcons.EyeSlash,
            _ => KsTguiIcons.Eye,
        };
    }

    private static Color ColorFor(KsTguiWindowStatus status)
    {
        return status switch
        {
            KsTguiWindowStatus.Interactive => KsTguiPalettes.Good,
            KsTguiWindowStatus.Update => KsTguiPalettes.Average,
            _ => KsTguiPalettes.Bad,
        };
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_elapsed >= TransitionSeconds)
            return;

        _elapsed += args.DeltaSeconds;
        _icon.IconColor = Current;
    }
}
