using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     The bookkeeping for a popup whose translucent background is painted by its backdrop pass
///         (<see cref="KsTguiBackdropBlur"/>) and so must not be drawn a second time by its panel. Finds the popup's panel, reads
///         the CSS tint off its style box (<see cref="KsTguiStyleBox.Tint"/>), and while the pass has drawn the backdrop this
///         frame swaps the panel's style box for the same box without a background, so the border still draws. When the pass
///         didn't run (blur off, inside another pass, a theme without tints) the panel draws as it always did.
/// </summary>
/// <remarks>
///     The swap is done with <see cref="PanelContainer.PanelOverride"/>, per panel, because the sheet's style box is shared
///         by every panel of the kind. The decision is made again on every draw, so it follows the cvar and a theme change.
/// </remarks>
public sealed class KsTguiBackdropBackground
{
    private PanelContainer? _panel;
    private StyleBox? _savedOverride;
    private bool _hollowed;
    private KsTguiStyleBox? _hollow;
    private KsTguiStyleBox? _hollowSource;

    /// <summary>
    ///     The panel found, for tests.
    /// </summary>
    public PanelContainer? Panel => _panel;

    /// <summary>
    ///     Whether the panel is drawing without its background right now, for tests.
    /// </summary>
    public bool Hollowed => _hollowed;

    /// <summary>
    ///     The tint of the first panel at or under <paramref name="root"/> that has one, or null if there is none (yet: a
    ///         panel that hasn't been styled has no style box).
    /// </summary>
    public KsTguiTint? FindTint(Control root)
    {
        if (_panel == null || _panel.Disposed || !IsUnder(_panel, root) || RealBox(_panel)?.Tint == null)
        {
            // Back to how it was, before looking at another.
            Apply(composited: false);
            _panel = Find(root);
        }

        return _panel == null ? null : RealBox(_panel)?.Tint;
    }

    /// <summary>
    ///     Draws the panel without its background while <paramref name="composited"/>, and as it was otherwise.
    /// </summary>
    public void Apply(bool composited)
    {
        if (_panel == null || _panel.Disposed)
        {
            _hollowed = false;
            return;
        }

        if (!composited || RealBox(_panel) is not { Tint: not null } real)
        {
            if (_hollowed)
            {
                _panel.PanelOverride = _savedOverride;
                _savedOverride = null;
                _hollowed = false;
            }

            return;
        }

        if (!_hollowed)
        {
            _savedOverride = _panel.PanelOverride;
            _hollowed = true;
        }

        if (_hollow == null || !ReferenceEquals(_hollowSource, real))
        {
            _hollow = real.WithoutBackground();
            _hollowSource = real;
        }

        _panel.PanelOverride = _hollow;
    }

    /// <summary>
    ///     Puts the panel back as it was and forgets it.
    /// </summary>
    public void Release()
    {
        Apply(composited: false);
        _panel = null;
        _hollow = null;
        _hollowSource = null;
    }

    private KsTguiStyleBox? RealBox(PanelContainer panel)
    {
        // While hollowed, the override is ours and what was there before is the real one.
        var overridden = _hollowed ? _savedOverride : panel.PanelOverride;
        if (overridden != null)
            return overridden as KsTguiStyleBox;

        return panel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var box) ? box as KsTguiStyleBox : null;
    }

    private PanelContainer? Find(Control root)
    {
        if (root is PanelContainer panel && RealBox(panel)?.Tint != null)
            return panel;

        foreach (var child in root.Children)
        {
            if (Find(child) is { } found)
                return found;
        }

        return null;
    }

    private static bool IsUnder(Control control, Control root)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current == root)
                return true;
        }

        return false;
    }
}
