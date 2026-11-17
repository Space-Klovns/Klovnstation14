using Content.Client._KS14.Tgui.Backdrop;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;

namespace Content.Client.Viewport
{
    /// <summary>
    ///     The UI behind a tgui popup is drawn again to be blurred, and this is drawn as part of it. Rendering the world a
    ///         second time (every z-level and portal pass included) for that would cost a frame's worth of the game, so
    ///         while the pass runs the viewport only draws the picture it made last.
    /// </summary>
    public sealed partial class ScalingViewport
    {
        [Dependency] private IUserInterfaceManager _backdropUiManager = default!;

        private KsTguiBackdropUIController? _backdropController;

        /// <summary>
        ///     Whether a backdrop pass is running on this client.
        /// </summary>
        public bool KsBackdropPassActive =>
            (_backdropController ??= _backdropUiManager.GetUIController<KsTguiBackdropUIController>()).PassActive;

        /// <summary>
        ///     What the viewport draws during a backdrop pass: its last render target, where it is drawn normally. That target
        ///         holds the composited z-levels, since each pass draws over the one before. Overlays are left out.
        /// </summary>
        private void KsDrawBackdropPass(IRenderHandle handle)
        {
            var drawBox = GetDrawBox();

            if (KsDrawShader != null)
                handle.DrawingHandleScreen.UseShader(KsDrawShader);

            handle.DrawingHandleScreen.DrawTextureRect(_viewport!.RenderTarget.Texture, drawBox);

            if (KsDrawShader != null)
                handle.DrawingHandleScreen.UseShader(null);
        }
    }
}
