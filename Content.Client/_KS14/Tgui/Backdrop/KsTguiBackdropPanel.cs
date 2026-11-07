using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     A panel that blurs what is behind it, as tgui's popups with a <c>backdrop-filter</c> do (the knob's and the slider's
///         value). It is meant for a popup in the popup layer, whose ancestors draw nothing. The panel's own style box is
///         translucent, and is drawn over the blur.
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

    /// <summary>
    ///     Lets go of the blur's render targets. They are made again if the panel is drawn with a blur.
    /// </summary>
    public void ReleaseBackdrop()
    {
        _blur?.Dispose();
        _blur = null;
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
            _blur.Draw(handle, this, position, box, PixelSize, Radius * UIScale, Color.White);
        }

        base.Draw(handle);
    }
}
