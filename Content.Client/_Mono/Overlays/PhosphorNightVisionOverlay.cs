using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Mono.Overlays;

/// <summary>
/// Fullscreen overlay that applies the night-vision shader to the rendered screen.
/// </summary>
public sealed partial class PhosphorNightVisionOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "PhosphorNightVision";

    private readonly ShaderInstance _phosphorNightVisionShader;


    /// <summary>
    /// Sets the base lighting seen by the night vision.
    /// </summary>
    /// <remarks>
    /// This value combines with <see cref="Amplification"/> to create the brightness of the scene. Increasing the
    /// magnitude of this color will result in a brighter scene. Amplification increases that further.
    /// </remarks>
    public Color LightingColor { get; private set; }

    /// <summary>
    /// Sets the phosphor color of the night vision. This will be the color seen by the user.
    /// </summary>
    public Color PhosphorColor { get; private set; }

    /// <summary>
    /// If the goggle shader should be rendered.
    /// </summary>
    public bool GogglesEnabled { get; private set; }

    /// <summary>
    /// Radius of each circle in the goggle overlay, in pixels.
    /// </summary>
    public float ViewCircleRadius { get; private set; }

    /// <summary>
    /// Center-to-center spacing of circles.
    /// </summary>
    public float ViewCircleSpacing { get; private set; }

    /// <summary>
    /// Number of circles to render. Odd numbers will place a circle in the center.
    /// </summary>
    public int ViewCircleCount { get; private set; }

    /// <summary>
    /// Amplification of the ambient light by the nightvision shader.
    /// </summary>
    /// <remarks>
    /// This value is responsible for ensuring that ambient light blows out the night vision.
    /// </remarks>
    public float Amplification { get; private set; }

    /// <summary>
    /// KS14 - cone
    /// </summary>
    public bool IsCone { get; private set; }

    /// <summary>
    /// KS14 - cone slope
    /// </summary>
    public float ConeSlope { get; private set; }

    /// <summary>
    /// KS14 - cone width
    /// </summary>
    public float ConeWidth { get; private set; }

    /// <summary>
    /// KS14 - cone softness
    /// </summary>
    public float ConeSoftness { get; private set; }

    /// <summary>
    /// The space where the night vision fake light is added.
    /// </summary>
    public const OverlaySpace LightSpace = OverlaySpace.BeforeLighting;

    /// <summary>
    /// The space where the goggle shader is applied.
    /// </summary>
    public const OverlaySpace ShaderSpace = OverlaySpace.WorldSpaceBelowFOV;

    /// <summary>
    /// Overlay spaces used by the shader.
    /// </summary>
    public override OverlaySpace Space => LightSpace | ShaderSpace;
    public override bool RequestScreenTexture => true;

    public PhosphorNightVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _phosphorNightVisionShader = _prototypeManager.Index(Shader).InstanceUnique();
        ZIndex = -1;
    }

    public void SetParameters(
        Color lightingColor,
        Color phosphorColor,
        bool gogglesEnabled,
        float viewCircleRadius,
        float viewCircleSpacing,
        int viewCircleCount,
        float amplification,
        bool isCone,
        float coneSlope,
        float coneWidth,
        float coneSoftness)
    {
        LightingColor     = lightingColor;
        PhosphorColor     = phosphorColor;
        GogglesEnabled    = gogglesEnabled;
        ViewCircleRadius  = viewCircleRadius;
        ViewCircleSpacing = viewCircleSpacing;
        ViewCircleCount   = viewCircleCount;
        Amplification     = amplification;
        IsCone            = isCone;
        ConeSlope         = coneSlope;
        ConeWidth         = coneWidth;
        ConeSoftness      = coneSoftness;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;

        switch (args.Space)
        {
            // Add light to the scene even if it's completely dark
            case LightSpace:
                handle.DrawRect(args.WorldBounds, LightingColor);
                break;

            // Draw the goggle overlays (if enabled)
            case ShaderSpace:
                if (!GogglesEnabled)
                    break;

                _phosphorNightVisionShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
                _phosphorNightVisionShader.SetParameter("VIEW_RADIUS", ViewCircleRadius);
                _phosphorNightVisionShader.SetParameter("CIRCLE_COUNT", ViewCircleCount);
                _phosphorNightVisionShader.SetParameter("BASE_COLOR", new Vector3(PhosphorColor.R, PhosphorColor.G, PhosphorColor.B));
                _phosphorNightVisionShader.SetParameter("AMPLIFICATION", Amplification);
                _phosphorNightVisionShader.SetParameter("SPACING", ViewCircleSpacing);
                _phosphorNightVisionShader.SetParameter("IS_CONE", IsCone);
                _phosphorNightVisionShader.SetParameter("CONE_SLOPE", ConeSlope);
                _phosphorNightVisionShader.SetParameter("CONE_WIDTH", ConeWidth);
                _phosphorNightVisionShader.SetParameter("CONE_SOFTNESS", ConeSoftness);

                // Adjusting these weights is somewhat tricky.
                // The offset controls the amount of spacing (in px) of the sample - going further out will result in more blur
                // but also artifacting as you're losing information.
                _phosphorNightVisionShader.SetParameter("BLUR_OFFSET", [0.0f, 1.3846153846f, 3.2307692308f]);

                // Adjusting the weights towards the outside will increase the blurring effect, but will also cause artifacts.
                // weight[0] + 2*weight[1] + 2*weight[2] must equal one.
                // Set weight[0] to 1 and others to zero to remove the blur entirely.
                _phosphorNightVisionShader.SetParameter("BLUR_WEIGHT", [0.2270270270f, 0.3162162162f, 0.0702702703f]);

                handle.UseShader(_phosphorNightVisionShader);
                handle.DrawRect(args.WorldBounds, Color.White);
                handle.UseShader(null);
                break;
        }
    }
}
