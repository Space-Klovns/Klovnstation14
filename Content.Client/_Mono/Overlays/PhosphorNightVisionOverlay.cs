using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Content.Shared.MouseRotator;
using Robust.Client.Input;
using Content.Shared._Mono.Overlays;
using Robust.Shared.Map;
using Robust.Shared.Timing; // KS14

namespace Content.Client._Mono.Overlays;

/// <summary>
/// Fullscreen overlay that applies the night-vision shader to the rendered screen.
/// </summary>
public sealed partial class PhosphorNightVisionOverlay : Overlay
{
    [Dependency] private IGameTiming _gameTiming = default!; // KS14
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IEyeManager _eye = default!;
    private SharedTransformSystem _xform;
    private Entity<EyeComponent, PhosphorNightVisionRecipientComponent, TransformComponent>? _eyeEntity;
    private float _animationFraction = 0f; // KS14

    private static ProtoId<ShaderPrototype> Shader = "PhosphorNightVision";

    private ShaderInstance _phosphorNightVisionShader;


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
    /// Amplification of the ambient light by the nightvision shader.
    /// </summary>
    /// <remarks>
    /// This value is responsible for ensuring that ambient light blows out the night vision.
    /// </remarks>
    public float Amplification { get; private set; }

    /// <summary>
    /// KS14 - do we draw the phosphor effect?
    /// </summary>
    public bool PhosphorEffect { get; private set; }

    /// <summary>
    /// KS14 - do we draw the cone or do we do fullview?
    /// </summary>
    public bool IsCone { get; private set; }

    /// <summary>
    /// KS14 - the width of the cone in degrees
    /// </summary>
    public float ConeAngle { get; private set; }

    /// <summary>
    /// KS14 - softness of the edges in degrees - dictates transition smoothness (so we dont get a sharp split between bright and dark)
    /// </summary>
    public float ConeFeather { get; private set; }

    /// <summary>
    /// KS14 - how far does the cone reach out from the player? measured in screens
    /// </summary>
    public float ConeDistance { get; private set; }

    /// <summary>
    /// KS14 - softness of the transition at the cone's outer radius - measured in screens also
    /// </summary>
    public float ConeDistanceFeather { get; private set; }

    /// <summary>
    /// KS14 - rotation of the cone in radians
    /// </summary>
    public float ViewAngle { get; private set; }

    // KS14 Start
    /// <summary>
    ///     Gametime when the animation started.
    /// </summary>
    public TimeSpan? AnimationTime { get; private set; }

    public TimeSpan AnimationDuration { get; private set; }

    /// <summary>
    ///     Whether the main night vision effect is enabled.
    /// </summary>
    public bool Enabled = false;
    // KS14 End

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
        _xform = _ent.System<SharedTransformSystem>();
        ZIndex = -1;
    }

    public void SetParameters(
        Color lightingColor,
        Color phosphorColor,
        float amplification,
        bool phosphorEffect,
        bool isCone,
        float coneAngle,
        float coneFeather,
        float coneDistance,
        float coneDistanceFeather,
        TimeSpan animationDuration // KS14
                                   //float viewAngle
        )
    {
        LightingColor = lightingColor;
        PhosphorColor = phosphorColor;
        Amplification = amplification;
        PhosphorEffect = phosphorEffect;
        IsCone = isCone;
        ConeAngle = coneAngle;
        ConeFeather = coneFeather;
        ConeDistance = coneDistance;
        ConeDistanceFeather = coneDistanceFeather;
        AnimationDuration = animationDuration; // KS14
        //ViewAngle           = viewAngle;
    }

    // KS14
    public void SetAnimationTimeNow()
    {
        AnimationTime = _gameTiming.CurTime;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        if (AnimationTime is { } animationTime &&
            _gameTiming.CurTime > animationTime)
        {
            _animationFraction = (float)((_gameTiming.CurTime - animationTime).TotalSeconds / AnimationDuration.TotalSeconds);
            _animationFraction = MathF.Min(_animationFraction, 1f);

            if (Enabled)
                _animationFraction -= 1;
        }
        else
            _animationFraction = -1f;

        var handle = args.WorldHandle;
        var shouldDrawNv = _animationFraction >= -0.5f &&
            _animationFraction < 0.5f;

        switch (args.Space)
        {
            // Add light to the scene even if it's completely dark
            case LightSpace:
                if (!shouldDrawNv)
                    break;

                handle.DrawRect(args.WorldBounds, LightingColor);
                break;

            // Draw the phosphor effect and viewcone
            case ShaderSpace:
                if (!PhosphorEffect)
                    break;

                if (!shouldDrawNv)
                {
                    // if in standstill states, don't draw shader
                    if (_animationFraction != 1f &&
                       _animationFraction != -1f)
                    {
                        _phosphorNightVisionShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
                        _phosphorNightVisionShader.SetParameter("TIME_FRACTION", _animationFraction);

                        handle.UseShader(_phosphorNightVisionShader);
                        handle.DrawRect(args.WorldBounds, Color.White);
                        handle.UseShader(null);
                    }

                    break;
                }

                var enumerator = _ent.AllEntityQueryEnumerator<EyeComponent, PhosphorNightVisionRecipientComponent, TransformComponent>();
                while (enumerator.MoveNext(out var uid, out var eyeComponent, out var recipientComponent, out var transformComponent))
                {
                    if (args.Viewport.Eye != eyeComponent.Eye)
                        continue;

                    _eyeEntity = (uid, eyeComponent, recipientComponent, transformComponent);
                    break;
                }

                if (_eyeEntity == null)
                    break;

                var eyeAngle = (float)_eyeEntity!.Value.Comp1.Rotation.Theta; //the null check is in beforedraw
                var playerAngle = (float)_xform.GetWorldRotation(_eyeEntity.Value.Comp3).Theta;

                if (_ent.HasComponent<MouseRotatorComponent>(_eyeEntity))
                {
                    var mousePos = _eye.PixelToMap(_input.MouseScreenPosition);
                    if (mousePos.MapId != MapId.Nullspace)
                        playerAngle = (float)(mousePos.Position - _xform.GetMapCoordinates(_eyeEntity.Value).Position).ToAngle().Theta + MathHelper.DegreesToRadians(90f);
                }

                ViewAngle = playerAngle + eyeAngle + MathHelper.DegreesToRadians(180f);

                // KS14 start
                const float flashAmplification = 1500f; // amplif at start of flash
                const float flashDurationMultiplier = 3f;

                var extraAmplification = 0f;
                if (_eyeEntity.Value.Comp2.LastFlashTime is { } lastFlashTime &&
                    lastFlashTime <= _gameTiming.CurTime)
                {
                    extraAmplification = 1f - (float)((_gameTiming.CurTime - lastFlashTime).TotalSeconds / (_eyeEntity.Value.Comp2.LastFlashDuration.TotalSeconds * flashDurationMultiplier));
                    if (extraAmplification < 0f)
                        extraAmplification = 0f;
                    else
                        extraAmplification *= flashAmplification;
                }
                // KS14 end

                _phosphorNightVisionShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
                _phosphorNightVisionShader.SetParameter("BASE_COLOR", new Vector3(PhosphorColor.R, PhosphorColor.G, PhosphorColor.B));
                _phosphorNightVisionShader.SetParameter("AMPLIFICATION", Amplification + extraAmplification /* KS14: extra amplification */);
                _phosphorNightVisionShader.SetParameter("IS_CONE", IsCone);
                _phosphorNightVisionShader.SetParameter("CONE_ANGLE", ConeAngle);
                _phosphorNightVisionShader.SetParameter("CONE_FEATHER", ConeFeather);
                _phosphorNightVisionShader.SetParameter("CONE_DISTANCE", ConeDistance);
                _phosphorNightVisionShader.SetParameter("CONE_DISTANCE_FEATHER", ConeDistanceFeather);
                _phosphorNightVisionShader.SetParameter("VIEW_ANGLE", ViewAngle);
                _phosphorNightVisionShader.SetParameter("TIME_FRACTION", _animationFraction); // KS14
                //ViewAngle);

                // KS14 start
                _phosphorNightVisionShader.SetParameter("ZOOM", _eyeEntity!.Value.Comp1.Zoom);
                // KS14 end

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
                _eyeEntity = null;
                break;
        }
    }
}
