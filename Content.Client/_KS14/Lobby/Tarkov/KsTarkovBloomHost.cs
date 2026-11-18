using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Lobby.Tarkov;

/// <summary>
///     Makes the bright parts of the controls inside it glow, as if the UI were emissive. Each frame, before its children
///     draw, it renders them once more into an offscreen target, keeps only what is bright (white text, the lit plate, a
///     hovered item), blurs that in two levels (a tight halo and a wide haze, at half and quarter resolution) and adds it
///     to the screen. Then the children draw as usual on top, sharp. Dark panels and dim text add nothing.
/// </summary>
/// <remarks>
///     Owns five render targets and five shader instances, made on first use and released when it leaves the tree. The UI
///     has no screen texture to sample, so drawing the children twice is the only way to get at what they look like.
/// </remarks>
public sealed partial class KsTarkovBloomHost : Control
{
    private static readonly ProtoId<ShaderPrototype> BlurShaderId = "KsLobbyBloomBlur";
    private static readonly ProtoId<ShaderPrototype> CompositeShaderId = "KsLobbyBloomComposite";

    /// <summary>Taps either side of the middle one, in sigmas, matching the 12-tap loop in lobby_bloom.swsl.</summary>
    private const float SigmaTaps = 4f;

    private const int MaxSide = 4096;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private IRenderTexture? _scene;
    private IRenderTexture? _tightHorizontal;
    private IRenderTexture? _tight;
    private IRenderTexture? _wideHorizontal;
    private IRenderTexture? _wide;
    private Vector2i _sceneSize;

    private ShaderInstance? _tightHorizontalShader;
    private ShaderInstance? _tightVerticalShader;
    private ShaderInstance? _wideHorizontalShader;
    private ShaderInstance? _wideVerticalShader;
    private ShaderInstance? _compositeTightShader;
    private ShaderInstance? _compositeWideShader;

    public KsTarkovBloomHost()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>Whether the bloom is drawn at all.</summary>
    public bool BloomEnabled { get; set; } = true;

    /// <summary>Linear luminance below which nothing glows.</summary>
    public float Threshold { get; set; } = 0.3f;

    /// <summary>How far above <see cref="Threshold"/> the glow reaches full strength.</summary>
    public float Knee { get; set; } = 0.35f;

    /// <summary>Spread of the tight halo, in virtual pixels.</summary>
    public float TightSigma { get; set; } = 3.5f;

    /// <summary>Spread of the wide haze, in virtual pixels.</summary>
    public float WideSigma { get; set; } = 16f;

    public float TightIntensity { get; set; } = 0.3f;

    public float WideIntensity { get; set; } = 0.18f;

    /// <summary>Colour the glow is multiplied by.</summary>
    public Color Tint { get; set; } = Color.FromHex("#FFF6EC");

    protected override void PreRenderChildren(ref ControlRenderArguments args)
    {
        base.PreRenderChildren(ref args);

        if (!BloomEnabled || args.Modulate.A <= 0.01f)
            return;

        var size = PixelSize;
        if (size.X < 4 || size.Y < 4 || size.X > MaxSide || size.Y > MaxSide)
            return;

        var handle = args.Handle;
        EnsureResources(size);
        UpdateParameters();

        // The children as they will be drawn, over transparent black: empty pixels must add nothing.
        handle.RenderInRenderTarget(_scene!, () => RenderChildren(handle, size), new Color(0f, 0f, 0f, 0f));

        var half = _tight!.Size;
        var quarter = _wide!.Size;
        handle.RenderInRenderTarget(_tightHorizontal!, () => Blit(handle, _scene!, _tightHorizontalShader!, half), Color.Black);
        handle.RenderInRenderTarget(_tight!, () => Blit(handle, _tightHorizontal!, _tightVerticalShader!, half), Color.Black);
        handle.RenderInRenderTarget(_wideHorizontal!, () => Blit(handle, _tight!, _wideHorizontalShader!, quarter), Color.Black);
        handle.RenderInRenderTarget(_wide!, () => Blit(handle, _wideHorizontal!, _wideVerticalShader!, quarter), Color.Black);

        // Add both levels over what is behind the host, under its children.
        var screen = handle.DrawingHandleScreen;
        var oldTransform = screen.GetTransform();
        var oldModulate = screen.Modulate;
        screen.SetTransform(Matrix3x2.Identity);
        screen.Modulate = Color.White;
        var box = UIBox2.FromDimensions(GlobalPixelPosition, size);
        screen.UseShader(_compositeWideShader);
        screen.DrawTextureRect(_wide!.Texture, box);
        screen.UseShader(_compositeTightShader);
        screen.DrawTextureRect(_tight!.Texture, box);
        screen.UseShader(null);
        screen.Modulate = oldModulate;
        screen.SetTransform(oldTransform);
    }

    private void RenderChildren(IRenderHandle handle, Vector2i size)
    {
        var drawing = handle.DrawingHandleScreen;
        var oldTransform = drawing.GetTransform();
        var oldModulate = drawing.Modulate;
        drawing.SetTransform(Matrix3x2.Identity);
        drawing.Modulate = Color.White;

        var total = 0;
        var transform = Matrix3x2.Identity;
        var scissor = UIBox2i.FromDimensions(Vector2i.Zero, size);
        for (var i = 0; i < ChildCount; i++)
        {
            var child = GetChild(i);
            var childArguments = new ControlRenderArguments
            {
                Handle = handle,
                Total = ref total,
                Position = child.PixelPosition,
                Modulate = Color.White,
                ScissorBox = scissor,
                CoordinateTransform = ref transform,
            };

            UserInterfaceManager.RenderControl(in childArguments, child);
        }

        handle.SetScissor(null);
        drawing.Modulate = oldModulate;
        drawing.SetTransform(oldTransform);
    }

    private static void Blit(IRenderHandle handle, IRenderTexture source, ShaderInstance shader, Vector2i targetSize)
    {
        var drawing = handle.DrawingHandleScreen;
        var oldTransform = drawing.GetTransform();
        var oldModulate = drawing.Modulate;
        drawing.SetTransform(Matrix3x2.Identity);
        drawing.Modulate = Color.White;
        drawing.UseShader(shader);
        drawing.DrawTextureRect(source.Texture, new UIBox2(0f, 0f, targetSize.X, targetSize.Y));
        drawing.UseShader(null);
        drawing.Modulate = oldModulate;
        drawing.SetTransform(oldTransform);
    }

    private void EnsureResources(Vector2i size)
    {
        if (_tightHorizontalShader == null)
        {
            var blur = _prototypeManager.Index(BlurShaderId);
            _tightHorizontalShader = blur.InstanceUnique();
            _tightVerticalShader = blur.InstanceUnique();
            _wideHorizontalShader = blur.InstanceUnique();
            _wideVerticalShader = blur.InstanceUnique();
            var composite = _prototypeManager.Index(CompositeShaderId);
            _compositeTightShader = composite.InstanceUnique();
            _compositeWideShader = composite.InstanceUnique();
        }

        if (_scene != null && _sceneSize == size)
            return;

        DisposeTargets();
        _sceneSize = size;
        var half = new Vector2i(Math.Max(1, size.X / 2), Math.Max(1, size.Y / 2));
        var quarter = new Vector2i(Math.Max(1, size.X / 4), Math.Max(1, size.Y / 4));
        _scene = CreateTarget(size, "ks-lobby-bloom-scene");
        _tightHorizontal = CreateTarget(half, "ks-lobby-bloom-tight-h");
        _tight = CreateTarget(half, "ks-lobby-bloom-tight");
        _wideHorizontal = CreateTarget(quarter, "ks-lobby-bloom-wide-h");
        _wide = CreateTarget(quarter, "ks-lobby-bloom-wide");
    }

    private IRenderTexture CreateTarget(Vector2i size, string name)
    {
        // Filtered: every pass samples between texels, and the downsampling relies on it.
        return _clyde.CreateRenderTarget(
            size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            name);
    }

    /// <summary>
    ///     Every uniform of every instance is set each frame: an instance with one unset draws with whatever value the
    ///     program last had. The tap step is in the source's texels, so it shrinks by how many screen pixels one covers.
    /// </summary>
    private void UpdateParameters()
    {
        var tight = TightSigma * UIScale;
        var wide = WideSigma * UIScale;

        // Tight: full-size scene to half, then half to half. Wide: from the tight result, half to quarter, quarter to quarter.
        SetBlur(_tightHorizontalShader!, Vector2.UnitX, tight / (SigmaTaps * 1f), Threshold);
        SetBlur(_tightVerticalShader!, Vector2.UnitY, tight / (SigmaTaps * 2f), -1f);
        SetBlur(_wideHorizontalShader!, Vector2.UnitX, wide / (SigmaTaps * 2f), -1f);
        SetBlur(_wideVerticalShader!, Vector2.UnitY, wide / (SigmaTaps * 4f), -1f);

        var tint = new Vector3(Tint.R, Tint.G, Tint.B);
        _compositeTightShader!.SetParameter("intensity", TightIntensity);
        _compositeTightShader.SetParameter("tint", tint);
        _compositeWideShader!.SetParameter("intensity", WideIntensity);
        _compositeWideShader.SetParameter("tint", tint);
    }

    private void SetBlur(ShaderInstance shader, Vector2 direction, float tapStep, float threshold)
    {
        shader.SetParameter("direction", direction);
        shader.SetParameter("tapStep", tapStep);
        shader.SetParameter("sigmaTaps", SigmaTaps);
        shader.SetParameter("threshold", threshold);
        shader.SetParameter("knee", Knee);
    }

    private void DisposeTargets()
    {
        _scene?.Dispose();
        _tightHorizontal?.Dispose();
        _tight?.Dispose();
        _wideHorizontal?.Dispose();
        _wide?.Dispose();
        _scene = null;
        _tightHorizontal = null;
        _tight = null;
        _wideHorizontal = null;
        _wide = null;
        _sceneSize = default;
    }

    private void DisposeResources()
    {
        DisposeTargets();
        _tightHorizontalShader?.Dispose();
        _tightVerticalShader?.Dispose();
        _wideHorizontalShader?.Dispose();
        _wideVerticalShader?.Dispose();
        _compositeTightShader?.Dispose();
        _compositeWideShader?.Dispose();
        _tightHorizontalShader = null;
        _tightVerticalShader = null;
        _wideHorizontalShader = null;
        _wideVerticalShader = null;
        _compositeTightShader = null;
        _compositeWideShader = null;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        DisposeResources();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeResources();

        base.Dispose(disposing);
    }
}
