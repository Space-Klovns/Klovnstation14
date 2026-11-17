using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     A panel that blurs what is behind it, as tgui's popups with a <c>backdrop-filter</c> do (the knob's and the slider's
///         value). It is meant for a popup in the popup layer, whose ancestors draw nothing. When its style box has a
///         <see cref="KsTguiStyleBox.Tint"/> the backdrop pass paints that background itself, in sRGB as CSS does, and the box
///         draws only its border; otherwise the box's own translucent background is drawn over the blur.
/// </summary>
public sealed class KsTguiBackdropPanel : PanelContainer
{
    /// <summary>
    ///     The corner radius, in virtual pixels, which the style box draws with and the blur is clipped to.
    /// </summary>
    public float Radius { get; set; }

    /// <summary>
    ///     Where the blur comes from; null for none.
    /// </summary>
    public KsTguiBackdropUIController? Backdrop { get; set; }

    private KsTguiBackdropBlur? _blur;
    private readonly KsTguiBackdropBackground _background = new();

    /// <summary>
    ///     Whether the panel is leaving its background to the backdrop pass right now. For tests.
    /// </summary>
    public bool BackgroundComposited => _background.Hollowed;

    /// <summary>
    ///     Lets go of the blur's render targets. They are made again if the panel is drawn with a blur.
    /// </summary>
    public void ReleaseBackdrop()
    {
        _blur?.Dispose();
        _blur = null;
        _background.Release();
    }

    protected override void Draw(IRenderHandle handle)
    {
        // Under the panel, so before its own box.
        if (Backdrop != null)
        {
            _blur ??= new KsTguiBackdropBlur(Backdrop);

            // The handle already carries this panel's modulate.
            var position = GlobalPixelPosition;
            var box = UIBox2.FromDimensions(position, PixelSize);
            var tint = _background.FindTint(this);
            var composited = _blur.Draw(handle, this, position, box, PixelSize, Radius * UIScale, Color.White, tint: tint);

            // The pass paints the background, exactly, so the style box draws its border only.
            _background.Apply(composited && tint != null);
        }
        else
        {
            _background.Apply(composited: false);
        }

        base.Draw(handle);
    }
}
