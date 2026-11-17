using System.Linq;
using System.Numerics;
using Content.Client.Atmos.EntitySystems;
using Content.Client.Atmos.Overlays;
using Content.Client.Graphics;
using Content.Shared._KS14.Atmos.Piping.Unary;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Binary.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.CanisterOverlay;

// Obviously does not support any kind of prototype hot-reloading
public sealed partial class CanisterOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawShader = "StencilEqualDraw";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawUnshadedShader = "StencilEqualDrawUnshaded";

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IClyde _clyde = default!;

    private readonly AtmosphereSystem _atmosphereSystem = default!;
    private readonly TransformSystem _transformSystem = default!;
    private readonly SpriteSystem _spriteSystem = default!;
    private readonly AppearanceSystem _appearanceSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private static readonly SpriteSpecifier.Rsi WindowMaskSpriteSpecifier = new(new ResPath("/Textures/_KS14/Structures/Storage/canister.rsi"), "window-mask");
    /// <summary>
    ///     This can be used to make the gas texture appear more 'high-res' by shrinking it,
    ///         but it must fit in the mask texture or something (because im lazy to reposition it) when
    ///         scaled down. The bottom-left corner of the mask texture is always in the
    ///         bottom-left regardless of its size.
    /// </summary>
    private const float WindowMaskSizeMultiplier = 0.8f;

    /// <summary>
    ///     Where the window sits in the mask texture, in tiles from the texture's bottom edge: rows 5 to 10 of its 32
    ///         pixels. Liquid and ice fill the window from its bottom.
    /// </summary>
    private const float WindowBottom = 5f / 32f;
    private const float WindowHeight = 5f / 32f;

    private static readonly Color IceColor = Color.FromHex("#d6efffdd");

    // see: DoAfterOverlay.cs
    private const float Scale = 1f;
    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(Scale, Scale));

    public static readonly Vector2 HalfNegativeVector2 = new(-0.5f, -0.5f);

    /// <summary>
    ///     The gas and fire tile overlays, whose animation frames are drawn inside the canister window
    ///         so that the gas in a canister animates in step with the gas on the tiles around it.
    ///         Looked up in <see cref="BeforeDraw"/>, since their systems add them independently of this one.
    /// </summary>
    private GasTileVisibleGasOverlay? _gasTileOverlay;
    private GasTileFireOverlay? _fireTileOverlay;

    private readonly int _visibleGasCount;
    private readonly float[] _visibleGasMolesVisibleMin = new float[Atmospherics.TotalNumberOfGases];
    private readonly float[] _visibleGasMolesVisibleMax = new float[Atmospherics.TotalNumberOfGases];

    private OverlayResourceCache<OverlayResources> _resources = new();

    /// <summary>
    ///     Stores canister components and their matrix.
    /// </summary>
    private readonly List<(GasCanisterOverlayComponent, Matrix3x2)> _drawDataCache = new();
    public const int CanisterZIndex = (int)Shared.DrawDepth.DrawDepth.Objects; // Under ghosts and fire, above mostly everything else

    public CanisterOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = CanisterZIndex;

        _atmosphereSystem = _entityManager.System<AtmosphereSystem>();
        _transformSystem = _entityManager.System<TransformSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _appearanceSystem = _entityManager.System<AppearanceSystem>();

        if (!_atmosphereSystem.GasPrototypesAreInitialised)
            _atmosphereSystem.InitializeGases();

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var gasPrototype = _atmosphereSystem.GetGas(i);
            if (gasPrototype.GasOverlaySprite is not { })
                continue;

            _visibleGasMolesVisibleMin[_visibleGasCount] = gasPrototype.GasMolesVisible;
            _visibleGasMolesVisibleMax[_visibleGasCount] = gasPrototype.GasMolesVisibleMax;
            _visibleGasCount += 1;
        }
        Array.Resize(ref _visibleGasMolesVisibleMin, _visibleGasCount);
        Array.Resize(ref _visibleGasMolesVisibleMax, _visibleGasCount);
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        base.DisposeBehavior();
    }

    private sealed class OverlayResources : IDisposable
    {
        public IRenderTexture? MaskTarget;

        public void Dispose()
        {
            MaskTarget?.Dispose();
        }
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!OverlayManager.TryGetOverlay(out _gasTileOverlay) ||
            !OverlayManager.TryGetOverlay(out _fireTileOverlay))
            return false;

        return _entityManager.EntityQuery<GasCanisterOverlayComponent>(includePaused: false).Any(); // Don't draw anything if no entities with canistercomponent even exist.
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        worldHandle.SetTransform(Matrix3x2.Identity);

        var resources = _resources.GetForViewport(args.Viewport, static _ => new());
        // update if necessary
        var targetSize = viewport.RenderTarget.Size;
        if (resources.MaskTarget?.Size != targetSize)
        {
            resources.MaskTarget?.Dispose();
            resources.MaskTarget = _clyde.CreateRenderTarget(targetSize, new(RenderTargetColorFormat.Rgba8Srgb), name: "canister-overlay-mask");
        }

        var maskTexture = _spriteSystem.GetState(WindowMaskSpriteSpecifier).Frame0;

        // because canisters always have the same rotation as the camera, we use the camera's rotation
        var rotationMatrix = Matrix3Helpers.CreateRotation(-viewport.Eye?.Rotation ?? Angle.Zero);

        // Draw on the stencil target
        _drawDataCache.Clear();

        var scale = viewport.RenderScale / (Vector2.One / (targetSize / (Vector2)viewport.Size));
        worldHandle.RenderInRenderTarget(resources.MaskTarget, () =>
        {
            var invMatrix = resources.MaskTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);
            var canisterEnumerator = _entityManager.EntityQueryEnumerator<GasCanisterOverlayComponent, TransformComponent>();
            while (canisterEnumerator.MoveNext(out var uid, out var canisterComponent, out var transformComponent))
            {
                // save some performance if we can, because canisters with no moles don't matter
                if (canisterComponent.NetworkedMoles <= float.Epsilon &&
                    canisterComponent.LiquidFill == 0 &&
                    canisterComponent.SolidFill == 0)
                    continue;

                if (_appearanceSystem.TryGetData<bool>(uid, GasCanisterVisuals.TankInserted, out var tankInserted) &&
                    tankInserted)
                    continue;

                var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, Matrix3Helpers.CreateTranslation(_transformSystem.GetWorldPosition(transformComponent)));
                var canisterWorldMatrix = Matrix3x2.Multiply(rotationMatrix, scaledWorld);
                // Apply the inverse matrix to transform to render target space. otherwise, we would be rendering in worldspace
                var canisterRenderTargetMatrix = Matrix3x2.Multiply(canisterWorldMatrix, invMatrix);

                _drawDataCache.Add((canisterComponent, canisterWorldMatrix));
                worldHandle.SetTransform(canisterRenderTargetMatrix);

                // so, draw window mask to stencil target
                worldHandle.DrawTexture(maskTexture, HalfNegativeVector2, modulate: Color.White);
            }
        },
        Color.Transparent);

        // reset after setting transform million times
        worldHandle.SetTransform(Matrix3x2.Identity);

        // oops who cares
        if (_drawDataCache.Count == 0)
        {
            worldHandle.UseShader(null);
            return;
        }

        // Draw stencil target onto stencil mask so we can actually use it
        worldHandle.UseShader(_prototypeManager.Index(StencilMaskShader).Instance());
        worldHandle.DrawTextureRect(resources.MaskTarget.Texture, args.WorldBounds);

        // Finally, draw gas textures on pixels that are white on our stencil mask
        var equalDrawShader = _prototypeManager.Index(StencilEqualDrawShader).Instance();
        worldHandle.UseShader(equalDrawShader);

        var unshadedShader = _prototypeManager.Index(StencilEqualDrawUnshadedShader).Instance();
        var canisterEqe = _entityManager.EntityQueryEnumerator<GasCanisterOverlayComponent, TransformComponent>();
        foreach (var (overlayComponent, canisterWorldMatrix) in _drawDataCache)
        {
            worldHandle.SetTransform(canisterWorldMatrix);
            DrawFluid(worldHandle, overlayComponent);
            for (var i = 0; i < _visibleGasCount; i++)
            {
                // 0 to 1
                var gasPercentage = overlayComponent.AppearanceGasPercentages[i] / (float)byte.MaxValue;

                var gasMoles = gasPercentage * overlayComponent.NetworkedMoles;
                var gasMolesVisibleMin = _visibleGasMolesVisibleMin[i];

                // gas moles below minimum moles to be visible, so who cares
                if (gasMoles < gasMolesVisibleMin)
                    continue;

                var gasMolesVisibleMax = _visibleGasMolesVisibleMax[i];

                // lets hope this is never negative
                var opacity = gasMoles >= gasMolesVisibleMax ?
                    1f :
                    (gasMoles - gasMolesVisibleMin) / (gasMolesVisibleMax - gasMolesVisibleMin);

                var gasTexture = _gasTileOverlay!._frames[i][_gasTileOverlay._frameCounter[i]];
                var textureBox = Box2.FromDimensions(HalfNegativeVector2, gasTexture.Size / (float)EyeManager.PixelsPerMeter * WindowMaskSizeMultiplier);
                worldHandle.DrawTextureRect(
                    gasTexture,
                    textureBox,
                    modulate: Color.White.WithAlpha(opacity)
                );

                // render fire too ig
                if (overlayComponent.FireState != 0)
                {
                    worldHandle.UseShader(unshadedShader);

                    var fireStateIndex = overlayComponent.FireState - 1;
                    var texture = _fireTileOverlay!._frames[fireStateIndex][_fireTileOverlay._frameCounter[fireStateIndex]];
                    worldHandle.DrawTextureRect(texture, textureBox); // yes i am reusing the same box2 joyy

                    worldHandle.UseShader(equalDrawShader);
                }
            }
        }


        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }

    /// <summary>
    ///     Ice at the bottom of the window, liquid over it, each as high as the canister's gauge reads. The stencil keeps
    ///         them inside the window, so they span the tile's width.
    /// </summary>
    private static void DrawFluid(DrawingHandleWorld worldHandle, GasCanisterOverlayComponent overlayComponent)
    {
        var bottom = HalfNegativeVector2.Y + WindowBottom;
        var solidHeight = overlayComponent.SolidFill / (float)byte.MaxValue * WindowHeight;
        var liquidHeight = MathF.Min(overlayComponent.LiquidFill / (float)byte.MaxValue * WindowHeight, WindowHeight - solidHeight);

        if (solidHeight > 0f)
            worldHandle.DrawRect(new Box2(HalfNegativeVector2.X, bottom, -HalfNegativeVector2.X, bottom + solidHeight), IceColor);

        if (liquidHeight > 0f)
        {
            worldHandle.DrawRect(new Box2(HalfNegativeVector2.X, bottom + solidHeight, -HalfNegativeVector2.X, bottom + solidHeight + liquidHeight),
                overlayComponent.LiquidColor.WithAlpha(0.85f));
        }
    }
}
