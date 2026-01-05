// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

// This was originally licensed mpl but i stole some code from upstream so it's MIT now

using System.Linq;
using System.Numerics;
using Content.Client.Atmos.EntitySystems;
using Content.Client.Atmos.Overlays;
using Content.Client.Graphics;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.CanisterOverlay;

// Obviously does not support any kind of prototype hot-reloading
public sealed class CanisterOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawShader = "StencilEqualDraw";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IClyde _clyde = default!;

    private readonly AtmosphereSystem _atmosphereSystem = default!;
    private readonly TransformSystem _transformSystem = default!;
    private readonly SpriteSystem _spriteSystem = default!;
    private readonly GasTileOverlaySystem _gasTileOverlaySystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public SpriteSpecifier.Rsi WindowMaskSpriteSpecifier;

    // see: DoAfterOverlay.cs
    private const float Scale = 1f;
    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(Scale, Scale));

    public static readonly Vector2 HalfNegativeVector2 = new(-0.5f, -0.5f);

    private readonly GasTileOverlay _gasTileOverlay;
    private readonly int _visibleGasCount;
    private readonly float[] _visibleGasMolesVisibleMin;
    private readonly float[] _visibleGasMolesVisibleMax;

    private OverlayResourceCache<OverlayResources> _resources = new();

    /// <summary>
    ///     Stores canister components and their matrix.
    /// </summary>
    private readonly List<(GasCanisterComponent, Matrix3x2)> _drawDataCache = new();

    public CanisterOverlay(SpriteSpecifier.Rsi maskSpriteSpecifier, GasTileOverlay gasTileOverlay /* TODO LCDC: HOLY SHIT THIS IS DEMENTED */)
    {
        WindowMaskSpriteSpecifier = maskSpriteSpecifier;
        _gasTileOverlay = gasTileOverlay;

        IoCManager.InjectDependencies(this);

        _atmosphereSystem = _entityManager.System<AtmosphereSystem>();
        _transformSystem = _entityManager.System<TransformSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _gasTileOverlaySystem = _entityManager.System<GasTileOverlaySystem>();

        _visibleGasCount = _gasTileOverlaySystem.VisibleGasId.Length;
        _visibleGasMolesVisibleMin = new float[_visibleGasCount];
        _visibleGasMolesVisibleMax = new float[_visibleGasCount];

        for (var i = 0; i < _visibleGasCount; i++)
        {
            var gasPrototype = _atmosphereSystem.GetGas(_gasTileOverlaySystem.VisibleGasId[i]);
            _visibleGasMolesVisibleMin[i] = gasPrototype.GasMolesVisible;
            _visibleGasMolesVisibleMax[i] = gasPrototype.GasMolesVisibleMax;
        }
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

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Don't draw anything if no entities with canistercomponent even exist.
        if (!_entityManager.EntityQuery<GasCanisterComponent>(includePaused: false).Any())
            return;

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
        // TODO LCDC: maybe this shouldnt be negative maybe it should IDFK.
        var rotationMatrix = Matrix3Helpers.CreateRotation(-viewport.Eye?.Rotation ?? Angle.Zero);

        // Draw on the stencil target
        _drawDataCache.Clear();
        worldHandle.RenderInRenderTarget(resources.MaskTarget, () =>
        {
            var canisterEnumerator = _entityManager.EntityQueryEnumerator<GasCanisterComponent, TransformComponent>();
            while (canisterEnumerator.MoveNext(out var canisterComponent, out var transformComponent))
            {
                // save some performance if we can, because canisters with no moles don't matter
                if (canisterComponent.NetworkedMoles == 0f)
                    continue;

                var worldPosition = _transformSystem.GetWorldPosition(transformComponent);

                var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, Matrix3Helpers.CreateTranslation(worldPosition));
                var canisterTransform = Matrix3x2.Multiply(rotationMatrix, scaledWorld);

                _drawDataCache.Add((canisterComponent, canisterTransform));
                worldHandle.SetTransform(canisterTransform);

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

        var maskShader = _prototypeManager.Index(StencilMaskShader).Instance();
        worldHandle.UseShader(maskShader);

        // Draw stencil target onto stencil mask so we can actually use it
        worldHandle.DrawTextureRect(resources.MaskTarget.Texture, args.WorldBounds);

        // Finally, draw gas textures on pixels that are white on our stencil mask
        var stencilEqualDrawShader = _prototypeManager.Index(StencilEqualDrawShader).Instance();
        worldHandle.UseShader(stencilEqualDrawShader);
        foreach (var (canisterComponent, canisterMatrix) in _drawDataCache)
        {
            worldHandle.SetTransform(canisterMatrix);
            for (var i = 0; i < _visibleGasCount; i++)
            {
                // 0 to 1
                var gasPercentage = canisterComponent.AppearanceGasPercentages[i] / (float)byte.MaxValue;

                var gasMoles = gasPercentage * canisterComponent.NetworkedMoles;
                var gasMolesVisibleMin = _visibleGasMolesVisibleMin[i];

                // gas moles below minimum moles to be visible, so who cares
                if (gasMoles < gasMolesVisibleMin)
                    continue;

                var gasMolesVisibleMax = _visibleGasMolesVisibleMax[i];

                // lets hope this is never negative
                var opacity = gasMoles >= gasMolesVisibleMax ?
                    1f :
                    (gasMoles - gasMolesVisibleMin) / (gasMolesVisibleMax - gasMolesVisibleMin);

                // TODO LCDC MAYBE: find a way to scale this down so it's higher resolution
                worldHandle.DrawTexture(_gasTileOverlay._frames[i][_gasTileOverlay._frameCounter[i]], HalfNegativeVector2, modulate: Color.White.WithAlpha(opacity));

                // TODO LCDC: render fire textures for overlay
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
