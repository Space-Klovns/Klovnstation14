using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     Captures what is behind a control and composites it back through a shader, the way a browser composites a
///         translucent element over its backdrop. The UI drawn before the control is rendered again into an offscreen target the
///         size of the control, optionally blurred in two passes (tgui's <c>backdrop-filter: blur(12px)</c>, in sRGB), and
///         drawn over the control's box, clipped to its rounded corners, with the control's CSS background colour blended in
///         sRGB on top (<see cref="KsTguiTint"/>) before the control draws itself. The engine has no such filter, and blends
///         in linear light, which is why this exists.
/// </summary>
/// <remarks>
///     Owns up to two render targets and three shader instances. Targets are made at the size the control has and kept until
///         it changes, and everything is let go by <see cref="Dispose"/>. Nothing is done unless the controller allows it: blur
///         needs the blur setting on, the capture alone (<c>blur: false</c>, the window dimmer) only the tgui theme, and
///         neither runs inside another pass.
/// </remarks>
public sealed class KsTguiBackdropBlur(KsTguiBackdropUIController controller) : IDisposable
{
    private readonly List<(Control Control, Vector2i Position)> _earlier = new();

    private IRenderTexture? _scene;
    private IRenderTexture? _scratch;
    private ShaderInstance? _horizontal;
    private ShaderInstance? _vertical;
    private ShaderInstance? _composite;

    public Vector2i TargetSize { get; private set; }

    public bool HasTargets => _scene != null;

    /// <summary>
    ///     Draws the blurred backdrop for a popup.
    /// </summary>
    /// <param name="handle">Where the popup is being drawn.</param>
    /// <param name="anchor">
    ///     The control whose place in the tree says what is behind: everything drawn before it is the backdrop.
    /// </param>
    /// <param name="anchorPosition">Where the anchor is on screen, in pixels.</param>
    /// <param name="box">The popup's box on screen, in pixels, as it is drawn now (scaled if it is mid-animation).</param>
    /// <param name="sourceSize">
    ///     The popup's size when not scaled. The backdrop is made at this size whatever the scale, so it isn't remade on every
    ///         frame of an animation; it is drawn squeezed into <paramref name="box"/>.
    /// </param>
    /// <param name="radius">The popup's corner radius in pixels, at the scale it is drawn.</param>
    /// <param name="modulate">The popup's colour and fade.</param>
    /// <param name="tint">
    ///     The CSS colour and opacity to blend over the backdrop in sRGB, or null to leave the backdrop as it is. The result is
    ///         opaque, so the popup must not draw this background again.
    /// </param>
    /// <param name="blur">False to composite the backdrop unblurred, as the window dimmer does.</param>
    /// <returns>Whether the backdrop was drawn. False means nothing was, and the caller draws its own background.</returns>
    public bool Draw(IRenderHandle handle, Control anchor, Vector2i anchorPosition, UIBox2 box, Vector2i sourceSize,
        float radius, Color modulate, KsTguiTint? tint = null, bool blur = true)
    {
        if (!(blur ? controller.CanBlur : controller.CanCapture) || modulate.A <= 0f || box.Width <= 0f || box.Height <= 0f)
            return false;

        if (KsTguiBackdropMath.Region(box.TopLeft, sourceSize) is not { } region)
            return false;

        EnsureTargets(region.Size, blur);

        KsTguiBackdropMath.EarlierControls(anchor, anchorPosition, _earlier);

        var width = region.Width;
        var height = region.Height;
        var clear = controller.ClearColor;

        controller.RunPass(() => handle.RenderInRenderTarget(_scene!, () => RenderScene(handle, region), clear));

        if (blur)
        {
            handle.RenderInRenderTarget(_scratch!, () => Blit(handle, _scene!, _horizontal!, width, height), Color.Black);
            handle.RenderInRenderTarget(_scene!, () => Blit(handle, _scratch!, _vertical!, width, height), Color.Black);
        }

        var drawing = handle.DrawingHandleScreen;
        var oldTransform = drawing.GetTransform();
        drawing.SetTransform(Matrix3x2.Identity);
        _composite!.SetParameter("boxSize", box.Size);
        _composite.SetParameter("radius", radius);
        _composite.SetParameter("tint", tint?.ShaderValue ?? Vector4.Zero);
        drawing.UseShader(_composite);
        drawing.DrawTextureRect(_scene!.Texture, box, modulate);
        drawing.UseShader(null);
        drawing.SetTransform(oldTransform);
        return true;
    }

    private void RenderScene(IRenderHandle handle, UIBox2i region)
    {
        var drawing = handle.DrawingHandleScreen;
        var oldTransform = drawing.GetTransform();
        var oldModulate = drawing.Modulate;
        drawing.SetTransform(Matrix3x2.Identity);
        drawing.Modulate = Color.White;

        // Only what reaches the region is worth the work.
        var scissor = UIBox2i.FromDimensions(Vector2i.Zero, region.Size);
        var total = 0;
        var transform = Matrix3x2.Identity;
        foreach (var (control, position) in _earlier)
        {
            var args = new Control.ControlRenderArguments
            {
                Handle = handle,
                Total = ref total,
                Position = position - region.TopLeft,
                Modulate = Color.White,
                ScissorBox = scissor,
                CoordinateTransform = ref transform,
            };

            controller.UiManager.RenderControl(in args, control);
        }

        handle.SetScissor(null);
        drawing.Modulate = oldModulate;
        drawing.SetTransform(oldTransform);
    }

    private static void Blit(IRenderHandle handle, IRenderTexture source, ShaderInstance shader, int width, int height)
    {
        var drawing = handle.DrawingHandleScreen;
        var oldTransform = drawing.GetTransform();
        var oldModulate = drawing.Modulate;
        drawing.SetTransform(Matrix3x2.Identity);
        drawing.Modulate = Color.White;
        drawing.UseShader(shader);
        drawing.DrawTextureRect(source.Texture, new UIBox2(0f, 0f, width, height));
        drawing.UseShader(null);
        drawing.Modulate = oldModulate;
        drawing.SetTransform(oldTransform);
    }

    private void EnsureTargets(Vector2i size, bool blur)
    {
        if (_scene == null || TargetSize != size)
        {
            _scene?.Dispose();
            _scratch?.Dispose();
            _scratch = null;
            _scene = controller.CreateTarget(size, "ks-tgui-backdrop-scene");
            TargetSize = size;
        }

        // The second target only a blur needs.
        if (blur && _scratch == null)
            _scratch = controller.CreateTarget(size, "ks-tgui-backdrop-scratch");

        if (_horizontal != null)
            return;

        _horizontal = controller.CreateShader(KsTguiBackdropUIController.BlurShader);
        _vertical = controller.CreateShader(KsTguiBackdropUIController.BlurShader);
        _composite = controller.CreateShader(KsTguiBackdropUIController.CompositeShader);

        // Every uniform of each instance is set, since an instance with none draws with the last values the program had.
        foreach (var (shader, direction) in new[] { (_horizontal, Vector2.UnitX), (_vertical, Vector2.UnitY) })
        {
            shader.SetParameter("direction", direction);
            shader.SetParameter("tapStep", KsTguiBackdropMath.TapStep);
            shader.SetParameter("sigmaTaps", KsTguiBackdropMath.SigmaTaps);
        }

        _composite.SetParameter("boxSize", Vector2.One);
        _composite.SetParameter("radius", 0f);
        _composite.SetParameter("tint", Vector4.Zero);
    }

    public void Dispose()
    {
        _scene?.Dispose();
        _scratch?.Dispose();
        _horizontal?.Dispose();
        _vertical?.Dispose();
        _composite?.Dispose();
        _scene = null;
        _scratch = null;
        _horizontal = null;
        _vertical = null;
        _composite = null;
        TargetSize = default;
    }
}
