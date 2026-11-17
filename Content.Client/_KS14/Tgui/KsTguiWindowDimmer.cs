using Content.Client._KS14.Tgui.Backdrop;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tg's <c>Window__dimmer</c>: <c>inset: 0</c> over the whole window, the window's base lightened by 15 at .25 opacity, and
///         <c>pointer-events: none</c>, so the window can still be dragged and closed. The colour is the sheet's, so a themed
///         window gets its own.
/// </summary>
/// <remarks>
///     A browser lays that colour over the window in sRGB: <c>out = .25 * colour + .75 * behind</c> on the encoded values, so a
///         pixel of 16 becomes 31 and one of 26 becomes 38. A plain alpha in this engine's linear blending gives 36 and 40, and
///         no single alpha is right for both. So the dimmer captures what is behind it (the window's own background and
///         contents, which are its earlier siblings, and everything under the window) through
///         <see cref="KsTguiBackdropBlur"/> without a blur, and blends the colour in the shader. It draws that way only while it
///         is visible, that is while the window is dimmed. Where a capture can't run (inside another popup's backdrop pass) it
///         falls back to the flat <see cref="Color"/>, which is exact over the window's base and near elsewhere.
///         The window must draw nothing of its own: its background is a child panel, which is why the dimmer has to be its
///         last child (<see cref="KsTguiWindowDimmerUIController"/> keeps it so).
/// </remarks>
public sealed class KsTguiWindowDimmer : Control
{
    private static readonly KsTguiTint FallbackTint = KsTguiThemePalette.Nanotrasen.WindowDimmerTint;
    private static readonly Color Fallback = FallbackTint.Approximate(KsTguiPalettes.Base);

    private Color _color = Fallback;
    private KsTguiTint _tint = FallbackTint;
    private KsTguiBackdropBlur? _capture;

    public KsTguiWindowDimmer()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>
    ///     The flat approximation, drawn when the backdrop can't be captured.
    /// </summary>
    public Color Color => _color;

    /// <summary>
    ///     The CSS colour and opacity that is blended in sRGB over what is behind.
    /// </summary>
    public KsTguiTint Tint => _tint;

    /// <summary>
    ///     Whether the last draw composited over the captured backdrop, for tests.
    /// </summary>
    public bool Composited { get; private set; }

    /// <summary>
    ///     Lets go of the render target. Made again if the dimmer is drawn again.
    /// </summary>
    public void ReleaseCapture()
    {
        _capture?.Dispose();
        _capture = null;
        Composited = false;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _color = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyWindowDimmer, out var color) ? color : Fallback;
        _tint = TryGetStyleProperty<KsTguiTint>(KsTguiSheetlet.StylePropertyWindowDimmerTint, out var tint) ? tint : FallbackTint;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        ReleaseCapture();
    }

    protected override void Draw(IRenderHandle handle)
    {
        base.Draw(handle);

        // Not laid out yet: no box to capture, and an inverted one would trip the engine's asserts.
        if (PixelSize.X > 0 && PixelSize.Y > 0)
        {
            var controller = UserInterfaceManager.GetUIController<KsTguiBackdropUIController>();
            _capture ??= new KsTguiBackdropBlur(controller);

            // The handle already carries this control's modulate, which the composite is drawn with.
            var position = GlobalPixelPosition;
            Composited = _capture.Draw(handle, this, position, UIBox2.FromDimensions(position, PixelSize), PixelSize, 0f,
                Color.White, tint: _tint, blur: false);
            if (Composited)
                return;
        }

        Composited = false;
        handle.DrawingHandleScreen.DrawRect(new UIBox2(0f, 0f, PixelSize.X, PixelSize.Y), _color);
    }
}
