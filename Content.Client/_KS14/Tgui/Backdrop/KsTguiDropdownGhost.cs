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
///         the tree would be taken for an open one. The contents are drawn straight through the UI manager each frame. The
///         contents' host carries the blur, which then fades and shrinks with them.
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

    public KsTguiDropdownGhost(Popup popup, KsTguiScaleHost host, Vector2i popupPosition)
    {
        Popup = popup;
        Host = host;
        PopupPosition = popupPosition;
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
    ///     Where the popup was, in pixels.
    /// </summary>
    public Vector2i PopupPosition { get; }

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

    protected override void Draw(IRenderHandle handle)
    {
        base.Draw(handle);

        if (Popup.Parent != null || Host.Modulate.A <= 0f)
            return;

        var total = 0;
        var transform = Matrix3x2.Identity;
        var args = new ControlRenderArguments
        {
            Handle = handle,
            Total = ref total,

            // The handle's frame is this control's, which is at the popup layer's corner, usually the screen's.
            Position = PopupPosition + Host.PixelPosition - GlobalPixelPosition,
            Modulate = Color.White,
            CoordinateTransform = ref transform,
        };

        Host.BackdropAnchor = this;
        Host.BackdropAnchorPosition = GlobalPixelPosition;
        Host.BackdropHostPosition = PopupPosition + Host.PixelPosition;
        try
        {
            UserInterfaceManager.RenderControl(in args, Host);
        }
        finally
        {
            Host.BackdropAnchor = null;
        }
    }
}
