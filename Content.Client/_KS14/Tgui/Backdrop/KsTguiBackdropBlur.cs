using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     tgui's <c>backdrop-filter: blur(12px)</c> for one popup: draws what is behind it, blurred, clipped to its rounded box.
///         The engine has no such filter, so the UI drawn before the popup is rendered again into an offscreen target the
///         size of the popup, blurred in two passes, and drawn over the popup's box before the popup draws itself.
/// </summary>
/// <remarks>
///     Owns two render targets and three shader instances. Targets are made at the size the popup has and kept until it
///         changes, and everything is let go by <see cref="Dispose"/>. Nothing is done unless the controller says blurring is
///         on and no other pass is running.
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
    public void Draw(IRenderHandle handle, Control anchor, Vector2i anchorPosition, UIBox2 box, Vector2i sourceSize,
        float radius, Color modulate)
    {
        if (!controller.CanBlur || modulate.A <= 0f || box.Width <= 0f || box.Height <= 0f)
            return;

        if (KsTguiBackdropMath.Region(box.TopLeft, sourceSize) is not { } region)
            return;

        EnsureTargets(region.Size);

        KsTguiBackdropMath.EarlierControls(anchor, anchorPosition, _earlier);

        var width = region.Width;
        var height = region.Height;
        var clear = controller.ClearColor;

        controller.RunPass(() => handle.RenderInRenderTarget(_scene!, () => RenderScene(handle, region), clear));

        handle.RenderInRenderTarget(_scratch!, () => Blit(handle, _scene!, _horizontal!, width, height), Color.Black);
        handle.RenderInRenderTarget(_scene!, () => Blit(handle, _scratch!, _vertical!, width, height), Color.Black);

        var drawing = handle.DrawingHandleScreen;
        var oldTransform = drawing.GetTransform();
        drawing.SetTransform(Matrix3x2.Identity);
        _composite!.SetParameter("boxSize", box.Size);
        _composite.SetParameter("radius", radius);
        drawing.UseShader(_composite);
        drawing.DrawTextureRect(_scene!.Texture, box, modulate);
        drawing.UseShader(null);
        drawing.SetTransform(oldTransform);
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

    private void EnsureTargets(Vector2i size)
    {
        if (_scene != null && TargetSize == size)
            return;

        _scene?.Dispose();
        _scratch?.Dispose();
        _scene = controller.CreateTarget(size, "ks-tgui-backdrop-scene");
        _scratch = controller.CreateTarget(size, "ks-tgui-backdrop-scratch");
        TargetSize = size;

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
