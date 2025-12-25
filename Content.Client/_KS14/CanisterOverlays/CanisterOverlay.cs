// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using System.Linq;
using System.Numerics;
using Content.Client.Atmos.EntitySystems;
using Content.Client.Atmos.Overlays;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Light;

// Obviously does not support any kind of prototype hot-reloading
public sealed class CanisterOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawShader = "StencilEqualDraw";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private readonly TransformSystem _transformSystem = default!;
    private readonly SpriteSystem _spriteSystem = default!;
    private readonly GasTileOverlaySystem _gasTileOverlaySystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public SpriteSpecifier.Rsi WindowMaskSpriteSpecifier;

    // see: DoAfterOverlay.cs
    private const float Scale = 1f;
    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(Scale, Scale));

    public static readonly Vector2 HalfNegativeVector2 = new(-0.5f, -0.5f);

    private readonly GasTileOverlay _gasTileOverlay;
    private readonly int _visibleGasCount;
    private readonly float[] _visibleGasMolesVisibleMin;
    private readonly float[] _visibleGasMolesVisibleMax;

    public CanisterOverlay(SpriteSpecifier.Rsi maskSpriteSpecifier, GasTileOverlay gasTileOverlay /* TODO LCDC: HOLY SHIT THIS IS DEMENTED */)
    {
        WindowMaskSpriteSpecifier = maskSpriteSpecifier;
        _gasTileOverlay = gasTileOverlay;

        IoCManager.InjectDependencies(this);

        _transformSystem = _entityManager.System<TransformSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _gasTileOverlaySystem = _entityManager.System<GasTileOverlaySystem>();

        _visibleGasCount = _gasTileOverlaySystem.VisibleGasId.Length;
        _visibleGasMolesVisibleMin = new float[_visibleGasCount];
        _visibleGasMolesVisibleMax = new float[_visibleGasCount];

        for (var i = 0; i < _visibleGasCount; i++)
        {
            var gasPrototype = _prototypeManager.Index<GasPrototype>(_gasTileOverlaySystem.VisibleGasId[i].ToString());
            _visibleGasMolesVisibleMin[i] = gasPrototype.GasMolesVisible;
            _visibleGasMolesVisibleMax[i] = gasPrototype.GasMolesVisibleMax;
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        /*
            TODO LCDC: FIX THIS SHIT

            I think worldbounds being drawn isn't working properly or something
        */

        var canisterQuery = _entityManager.EntityQuery<GasCanisterComponent>();
        if (!canisterQuery.Any())
            return;

        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        worldHandle.SetTransform(Matrix3x2.Identity);

        var maskTexture = _spriteSystem.GetState(WindowMaskSpriteSpecifier).Frame0;

        var maskShader = _prototypeManager.Index(StencilMaskShader).Instance();
        worldHandle.UseShader(maskShader);
        worldHandle.DrawRect(args.WorldBounds, Color.Black, filled: true);

        var stencilEqualDrawShader = _prototypeManager.Index(StencilEqualDrawShader).Instance();

        // because canisters always have the same rotation as the camera, we use the camera's rotation
        var rotationMatrix = Matrix3Helpers.CreateRotation(-(viewport.Eye?.Rotation ?? Angle.Zero));

        var canisterEnumerator = _entityManager.EntityQueryEnumerator<GasCanisterComponent, TransformComponent>();
        while (canisterEnumerator.MoveNext(out var canisterComponent, out var transformComponent))
        {
            // save some performance if we can
            if (canisterComponent.NetworkedMoles == 0f)
                continue;

            var worldPosition = _transformSystem.GetWorldPosition(transformComponent);

            var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, Matrix3Helpers.CreateTranslation(worldPosition));
            worldHandle.SetTransform(Matrix3x2.Multiply(rotationMatrix, scaledWorld));

            // every iteration of this loop should start with the shader as the mask shader
            // so, draw window mask to stencil buffer
            worldHandle.DrawTexture(maskTexture, HalfNegativeVector2, modulate: Color.White);

            // now, only draw on the window mask
            worldHandle.UseShader(stencilEqualDrawShader);

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
                worldHandle.DrawTexture(_gasTileOverlay._frames[i][_gasTileOverlay._frameCounter[i]], HalfNegativeVector2, Color.White.WithAlpha(opacity));

                // TODO LCDC: render fire textures for overlay
            }

            worldHandle.UseShader(maskShader);
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
