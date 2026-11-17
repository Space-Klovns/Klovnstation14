using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     A dropdown menu that has closed, drawn for the rest of tgui's closing transition. The engine takes a closed popup out of
///         the tree on the spot, so this stands in the popup layer, does not take the mouse, and draws the menu's contents
///         where it was, shrinking to <c>scale(.9)</c> and fading out over <c>--floating-transition-time</c> with CSS
///         <c>ease</c>, as Floating's <c>data-transition="close"</c> does.
/// </summary>
/// <remarks>
///     The popup is not given a parent again: an option button reuses its popup on the next open, and one that is already in
///         the tree would be taken for an open one. What it held, the host, stands in this control for the transition, so
///         it is in the tree and has the real UI scale (out of the tree a control's scale falls back to 1, which drew the
///         closing menu at the wrong place and size on any scaled UI). The host carries the blur, which then fades and
///         shrinks with the contents. <see cref="ReleaseHost"/> gives it back to the popup.
/// </remarks>
public sealed class KsTguiDropdownGhost : Control
{
    /// <summary>
    ///     <c>--floating-transition-time</c>.
    /// </summary>
    public const float Seconds = 0.2f;

    /// <summary>
    ///     Floating's closed state, <c>scale(0.9)</c>.
    /// </summary>
    public const float EndScale = 0.9f;

    private readonly float _startScale;
    private readonly float _startAlpha;
    private readonly Vector2 _hostSize;

    public KsTguiDropdownGhost(Popup popup, KsTguiScaleHost host, Vector2 popupPosition)
    {
        Popup = popup;
        Host = host;
        PopupPosition = popupPosition;
        _hostSize = host.Size;
        _startScale = host.Scale;
        _startAlpha = host.Modulate.A;
        MouseFilter = MouseFilterMode.Ignore;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
    }

    /// <summary>
    ///     The popup that closed. It is not a child of anything.
    /// </summary>
    public Popup Popup { get; }

    /// <summary>
    ///     What the popup holds, and what is scaled and faded.
    /// </summary>
    public KsTguiScaleHost Host { get; }

    /// <summary>
    ///     Where the popup was, in virtual pixels from the corner of the UI root.
    /// </summary>
    public Vector2 PopupPosition { get; }

    /// <summary>
    ///     Takes the host out of the ghost and back into the popup it came from.
    /// </summary>
    public void ReleaseHost()
    {
        if (Host.Parent == this)
            RemoveChild(Host);

        if (Host.Parent == null)
            Popup.AddChild(Host);
    }

    public float Elapsed { get; private set; }

    public bool Finished => Elapsed >= Seconds;

    /// <summary>
    ///     Moves the transition on and sets the host's scale and fade to match.
    /// </summary>
    public void Advance(float deltaSeconds)
    {
        Elapsed += deltaSeconds;

        var eased = KsTguiFeelUIController.CssEase(MathF.Min(Elapsed / Seconds, 1f));
        Host.Scale = _startScale + (EndScale - _startScale) * eased;
        Host.Modulate = Color.White.WithAlpha(_startAlpha * (1f - eased));
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        Host.Measure(_hostSize);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Host.Arrange(UIBox2.FromDimensions(PopupPosition - GlobalPosition, _hostSize));
        return finalSize;
    }
}
