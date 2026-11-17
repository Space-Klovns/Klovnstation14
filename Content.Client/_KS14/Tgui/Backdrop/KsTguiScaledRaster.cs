using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     Draws a popup host's contents scaled and faded about a pivot at subpixel precision, as a browser does to an element with
///         a CSS <c>transform</c> and <c>opacity</c> transition. The engine can't: it places each child at
///         <c>(Vector2i)Vector2.Transform(child.PixelPosition, coordinateTransform)</c>, so scaling a subtree through the
///         coordinate transform moves every control in whole-pixel steps while the scale changes smoothly, and the popup
///         visibly jitters. So while a popup is animating its children are rendered once at scale 1 into an offscreen target
///         and that picture is drawn, scaled, to a float box. The host itself stays where it will end up, on whole pixels.
/// </summary>
/// <remarks>
///     Owns one render target and one shader instance, made on first use and let go by <see cref="Dispose"/>. The target is
///         rendered again on every animated frame, since the contents can change. Nothing is done once the animation is over:
///         the host draws normally then.
/// </remarks>
public sealed class KsTguiScaledRaster(KsTguiBackdropUIController controller) : IDisposable
{
    /// <summary>
    ///     How near 1 a scale or opacity may be and count as settled.
    /// </summary>
    public const float Epsilon = 1e-4f;

    private IRenderTexture? _target;
    private ShaderInstance? _shader;

    /// <summary>
    ///     Whether a popup at this scale and opacity is drawn through the texture. Only while animating: a settled popup is
    ///         drawn as any control, with no extra render.
    /// </summary>
    public static bool ShouldRasterize(float scale, float alpha)
    {
        return scale < 1f - Epsilon || alpha < 1f - Epsilon;
    }

    /// <summary>
    ///     How far the scaled box's top left corner moves from the unscaled one, so that <paramref name="pivot"/> (a fraction of
    ///         the size: (0.5, 0) is the middle of the top edge) stays still. Not rounded.
    /// </summary>
    public static Vector2 PivotShift(Vector2 size, Vector2 pivot, float scale)
    {
        return new Vector2(size.X * pivot.X, size.Y * pivot.Y) * (1f - scale);
    }

    /// <summary>
    ///     The box the scaled contents are drawn into, in float pixels.
    /// </summary>
    public static UIBox2 ScaledBox(Vector2 topLeft, Vector2 size, Vector2 pivot, float scale)
    {
        return UIBox2.FromDimensions(topLeft + PivotShift(size, pivot, scale), size * scale);
    }

    public bool HasTarget => _target != null;

    /// <summary>
    ///     Renders <paramref name="host"/>'s children at scale 1 and draws them scaled and faded. The caller must then not
    ///         render the children itself (<c>RenderChildOverride</c>).
    /// </summary>
    /// <param name="host">The control whose children are drawn.</param>
    /// <param name="args">As given to <c>PreRenderChildren</c>; its modulate's alpha is the fade.</param>
    /// <param name="hostPosition">
    ///     Where the host is on screen, in pixels. Not <c>args.Position</c>, which the engine leaves unset there.
    /// </param>
    /// <param name="pivot">Fractions of the host's size that stay still.</param>
    /// <param name="scale">The scale.</param>
    public void Draw(Control host, in Control.ControlRenderArguments args, Vector2 hostPosition, Vector2 pivot, float scale)
    {
        var size = host.PixelSize;
        if (size.X <= 0 || size.Y <= 0 || size.X > KsTguiBackdropMath.MaxSide || size.Y > KsTguiBackdropMath.MaxSide)
            return;

        var handle = args.Handle;
        var fade = args.Modulate.A;
        EnsureTarget(size);

        // Transparent black and not Color.Transparent, which is white: what the contents leave empty must add no colour.
        handle.RenderInRenderTarget(_target!, () =>
        {
            var drawing = handle.DrawingHandleScreen;
            var oldTransform = drawing.GetTransform();
            var oldModulate = drawing.Modulate;
            drawing.SetTransform(Matrix3x2.Identity);
            drawing.Modulate = Color.White;

            var total = 0;
            var transform = Matrix3x2.Identity;
            var scissor = UIBox2i.FromDimensions(Vector2i.Zero, size);
            for (var i = 0; i < host.ChildCount; i++)
            {
                var child = host.GetChild(i);
                var childArguments = new Control.ControlRenderArguments
                {
                    Handle = handle,
                    Total = ref total,
                    Position = child.PixelPosition,
                    Modulate = Color.White,
                    ScissorBox = scissor,
                    CoordinateTransform = ref transform,
                };

                controller.UiManager.RenderControl(in childArguments, child);
            }

            handle.SetScissor(null);
            drawing.Modulate = oldModulate;
            drawing.SetTransform(oldTransform);
        }, new Color(0f, 0f, 0f, 0f));

        var screen = handle.DrawingHandleScreen;
        var savedTransform = screen.GetTransform();
        var savedModulate = screen.Modulate;
        screen.SetTransform(Matrix3x2.Identity);
        screen.Modulate = Color.White;
        screen.UseShader(_shader);
        screen.DrawTextureRect(_target!.Texture, ScaledBox(hostPosition, (Vector2)size, pivot, scale), new Color(1f, 1f, 1f, fade));
        screen.UseShader(null);
        screen.Modulate = savedModulate;
        screen.SetTransform(savedTransform);
    }

    private void EnsureTarget(Vector2i size)
    {
        _shader ??= controller.CreateShader(KsTguiBackdropUIController.RasterShader);

        if (_target != null && _target.Size == size)
            return;

        _target?.Dispose();
        _target = controller.CreateTarget(size, "ks-tgui-popup-raster");
    }

    public void Dispose()
    {
        _target?.Dispose();
        _shader?.Dispose();
        _target = null;
        _shader = null;
    }
}
