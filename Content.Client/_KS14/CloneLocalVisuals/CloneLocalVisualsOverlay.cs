using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.CloneLocalVisuals;
using Content.Shared.DisplacementMap;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.CloneLocalVisuals;

public sealed partial class CloneLocalVisualsOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    // Must match the uniforms declared by the displacement shaders.
    private const string DisplacementMapParameter = "displacementMap";
    private const string DisplacementUvParameter = "displacementUV";
    private const string DisplacementSizeParameter = "displacementSize";

    /// <summary>
    ///     The displacementSize the shader prototypes in Resources/Prototypes/Shaders/displacement.yml declare. It is
    ///     the range the maps encode their offsets over, not anything resolution specific, but the shader spends it in
    ///     texels of whatever texture it samples - so it has to grow with the render target the same way
    ///     <see cref="GetViewportScale"/> does.
    /// </summary>
    private const float DisplacementSize = 127f;

    /// <summary>
    ///     One shader instance per clone entity. Shader parameters are only consumed once the batch they belong to
    ///     is flushed, so sharing a single instance between clones would make them all use the last clone's
    ///     displacement map.
    /// </summary>
    private readonly Dictionary<EntityUid, ShaderInstance> _displacementShaders = new();

    private readonly HashSet<EntityUid> _drawnCloneUids = new();
    private readonly List<EntityUid> _undrawnCloneUids = new();

    /// <summary>
    ///     Scratch target the reference sprite is composited into before being displaced, sized to fit that sprite.
    ///     One target is enough for every clone drawn in a frame: they all composite the same sprite, and
    ///     <see cref="DrawingHandleBase.RenderInRenderTarget"/> flushes the render queue before it rebinds, so the
    ///     previous clone's draw has already consumed the contents by the time the next one overwrites them.
    /// </summary>
    private IRenderTexture? _renderTexture;

    public CloneLocalVisualsOverlay()
    {
        ZIndex = (int)Shared.DrawDepth.DrawDepth.OverMobs;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity is not { })
            return false;

        var enumerator = _entityManager.EntityQueryEnumerator<CloneLocalVisualsComponent>();
        return enumerator.MoveNext(out _, out _);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var referenceUid = _playerManager.LocalEntity!.Value;
        if (!_entityManager.TryGetComponent<SpriteComponent>(referenceUid, out var referenceSpriteComponent))
            return;

        var referenceEntity = new Entity<SpriteComponent>(referenceUid, referenceSpriteComponent);
        var eyeRotation = args.Viewport.Eye?.Rotation ?? default;

        // Copied out of the args because RenderInRenderTarget takes a closure, which cannot capture an 'in' parameter.
        var worldHandle = args.WorldHandle;
        var renderHandle = args.RenderHandle;

        var viewportScale = GetViewportScale(args.Viewport);

        var (oldOffset, oldRotation, oldColor) = (referenceSpriteComponent.Offset, referenceSpriteComponent.Rotation, referenceSpriteComponent.Color);

        _drawnCloneUids.Clear();

        var enumerator = _entityManager.EntityQueryEnumerator<CloneLocalVisualsComponent, TransformComponent, SpriteComponent>();
        while (enumerator.MoveNext(out var cloneUid, out var cloneVisualsComponent, out var transformComponent, out var spriteComponent))
        {
            _drawnCloneUids.Add(cloneUid);

            _spriteSystem.SetOffset(referenceEntity.AsNullable(), spriteComponent.Offset);
            _spriteSystem.SetRotation(referenceEntity.AsNullable(), spriteComponent.Rotation);
            _spriteSystem.SetColor(referenceEntity.AsNullable(), spriteComponent.Color.WithAlpha(spriteComponent.Color.A * referenceSpriteComponent.Color.A));

            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);

            // The same on-screen angle RenderSprite derives its RSI directions from.
            var angle = (worldRotation + eyeRotation).Reduced().FlipPositive();

            var displacementShaderInstance = GetDisplacementShader(cloneUid, cloneVisualsComponent, referenceSpriteComponent, angle, viewportScale);

            // Nothing to displace, so skip the whole render target detour and composite straight into the world.
            if (displacementShaderInstance is null)
            {
                _spriteSystem.RenderSprite(referenceEntity, worldHandle, eyeRotation, worldRotation, worldPosition);
                continue;
            }

            if (!TryEnsureRenderTexture(referenceSpriteComponent, viewportScale, out var renderTexture))
                continue;

            // The displacement has to be applied to the finished sprite rather than while it is being drawn: a shader
            // set on the drawing handle only survives until the first layer that carries a shader of its own, because
            // SpriteSystem.RenderLayer resets the handle back to no shader after drawing such a layer. Compositing the
            // whole sprite into a render target first turns it into a single texture, so one draw covers every layer.
            //
            // The eye rotation is folded into the world rotation instead of being handed to DrawEntity as one: it
            // applies the eye rotation to its view with the opposite sign to the real viewport (see the "Maaaaybe
            // this is meant to have a minus sign" in Clyde.RenderHandle.DrawEntity), so passing it would bake the
            // sprite in at 'worldRotation - eyeRotation' and leave the quad to rotate the difference back out. The
            // sprite would end up facing the right way, but the target's pixel grid would be rotated away from the
            // sprite it holds, resampling every pixel twice and visibly skewing the result.
            //
            // With a zero eye rotation and 'worldRotation + eyeRotation' as the world rotation, RenderSprite picks
            // the same RSI direction and cardinal snapping as it would on screen, and the entity matrix it builds
            // already carries the sprite's final on-screen orientation - so the sprite is rasterised once, square to
            // the target's own pixels.

            // The sprite's own scale is kept out of the target and applied to the quad instead. Sprites like the
            // dwarf ones are scaled non-uniformly (1 by 0.5), and baking that in would rasterise the sprite onto
            // pixels that are not square - mixels - which the displacement shader then reads as though they were,
            // stretching every offset it makes along the squashed axis.
            var spriteScale = referenceSpriteComponent.Scale;
            _spriteSystem.SetScale(referenceEntity.AsNullable(), Vector2.One);

            var renderTextureSize = renderTexture.Size;
            worldHandle.RenderInRenderTarget(renderTexture,
                () => renderHandle.DrawEntity(
                    referenceUid,
                    (Vector2)renderTextureSize / 2f,
                    new Vector2(viewportScale, viewportScale),
                    worldRotation + eyeRotation,
                    overrideDirection: referenceSpriteComponent.EnableDirectionOverride ? referenceSpriteComponent.DirectionOverride : null,
                    sprite: referenceSpriteComponent
                ),
                Color.Transparent);

            // RenderInRenderTarget runs its action then and there, so the sprite is free again already.
            _spriteSystem.SetScale(referenceEntity.AsNullable(), spriteScale);

            // The target now holds the sprite exactly as it belongs on screen, so the quad has to be screen aligned
            // for its pixels to land on screen pixels. Counter-rotating by the eye rotation cancels the one the eye's
            // view matrix adds, the same way RenderSprite keeps a NoRotation sprite upright.
            worldHandle.UseShader(displacementShaderInstance);
            worldHandle.SetTransform(GetQuadMatrix(referenceSpriteComponent, worldPosition, eyeRotation, angle, spriteScale));

            // Undoes the scale the target was rendered at, so the quad still covers the sprite's true world size and
            // the target's pixels land on screen pixels one for one. Cast, otherwise this is an integer division and
            // sprites that aren't a whole number of tiles get squashed.
            var quadSize = (Vector2)renderTextureSize / (EyeManager.PixelsPerMeter * viewportScale);
            worldHandle.DrawTextureRect(renderTexture.Texture, Box2.CenteredAround(Vector2.Zero, quadSize));
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);

        _spriteSystem.SetOffset(referenceEntity.AsNullable(), oldOffset);
        _spriteSystem.SetRotation(referenceEntity.AsNullable(), oldRotation);
        _spriteSystem.SetColor(referenceEntity.AsNullable(), oldColor);

        DisposeUndrawnShaders();
    }

    /// <summary>
    ///     Builds the transform the composited sprite is drawn back into the world with: screen aligned, at the
    ///     clone's position, carrying the sprite scale that was kept out of the render target.
    /// </summary>
    private static Matrix3x2 GetQuadMatrix(
        SpriteComponent referenceSpriteComponent,
        Vector2 worldPosition,
        Angle eyeRotation,
        Angle angle,
        Vector2 spriteScale
    )
    {
        // The target is screen aligned, so counter-rotating by the eye rotation cancels the one the eye's view
        // matrix adds and lands the target's pixels on screen pixels.
        var quadMatrix = Matrix3Helpers.CreateTransform(worldPosition, -eyeRotation);

        if (spriteScale == Vector2.One)
            return quadMatrix;

        // SpriteComponent.LocalMatrix is built as scale, then rotation, then offset, so the scale belongs in the
        // frame the sprite had before any of that rotation - otherwise a scaled sprite that is also rotated (a mob
        // lying down is rotated a quarter turn) gets squashed along the wrong axis. Rotating back out of the
        // orientation the target baked in, scaling there, and rotating back into it puts it in that frame.
        var cardinal = referenceSpriteComponent is { NoRotation: false, SnapCardinals: true }
            ? angle.RoundToCardinalAngle()
            : Angle.Zero;

        var bakedRotation = referenceSpriteComponent.Rotation
            + (referenceSpriteComponent.NoRotation ? Angle.Zero : angle - cardinal);

        return Matrix3Helpers.CreateRotation(-bakedRotation.Theta)
            * Matrix3Helpers.CreateScale(spriteScale)
            * Matrix3Helpers.CreateRotation(bakedRotation.Theta)
            * quadMatrix;
    }

    /// <summary>
    ///     How much of a render target pixel the viewport spends on one sprite pixel: 1 when a sprite is drawn at its
    ///     native resolution, 2 when the eye is zoomed to twice that, 0.5 when zoomed out to half, and so on.
    /// </summary>
    /// <remarks>
    ///     The sprite has to be composited at the resolution it will be shown at. Rendering it at its native
    ///     resolution and letting the final draw rescale the target rasterises it twice, at two different pixel
    ///     grids - which is what makes the mixels.
    /// </remarks>
    private static float GetViewportScale(IClydeViewport viewport)
    {
        if (viewport.Eye is not { } eye)
            return 1f;

        // The matrix the viewport itself renders the world through, so this picks up the eye's scale and the
        // viewport's render scale alike. Measuring the transformed x axis rather than reading a single component
        // keeps it right whatever the eye is rotated to.
        eye.GetViewMatrix(out var viewMatrix, viewport.RenderScale);
        var scale = new Vector2(viewMatrix.M11, viewMatrix.M12).Length();

        // A degenerate eye would divide the quad size by zero further down.
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    /// <summary>
    ///     Makes sure <see cref="_renderTexture"/> exists and is large enough to hold the whole reference sprite at
    ///     the resolution the viewport draws at, recreating it whenever either of those changes.
    /// </summary>
    /// <returns>False if the sprite has no visible layers, in which case there is nothing to draw at all.</returns>
    private bool TryEnsureRenderTexture(SpriteComponent referenceSpriteComponent, float viewportScale, [NotNullWhen(true)] out IRenderTexture? renderTexture)
    {
        renderTexture = null;

        var pixelSize = Vector2i.Zero;
        foreach (var spriteLayer in referenceSpriteComponent.AllLayers)
        {
            if (!spriteLayer.Visible)
                continue;

            pixelSize = Vector2i.ComponentMax(pixelSize, spriteLayer.PixelSize);
        }

        // Deliberately without the sprite's own scale: that is applied to the quad instead, so the target always
        // holds the sprite at its native proportions. See GetQuadMatrix.
        var requiredSize = new Vector2i(
            (int)MathF.Ceiling(pixelSize.X * viewportScale),
            (int)MathF.Ceiling(pixelSize.Y * viewportScale));

        if (requiredSize.X <= 0 || requiredSize.Y <= 0)
            return false;

        if (_renderTexture?.Size != requiredSize)
        {
            _renderTexture?.Dispose();
            _renderTexture = _clyde.CreateRenderTarget(
                requiredSize,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                name: "clone-local-visuals-target"
            );
        }

        renderTexture = _renderTexture;
        return true;
    }

    /// <summary>
    ///     Fetches (creating it if needed) the shader instance that displaces this clone's sprite, with its
    ///     displacement map parameters set for the current frame.
    /// </summary>
    /// <returns>Null if this clone has no usable displacement map, in which case it draws undisplaced.</returns>
    private ShaderInstance? GetDisplacementShader(
        EntityUid cloneUid,
        CloneLocalVisualsComponent cloneVisualsComponent,
        SpriteComponent referenceSpriteComponent,
        Angle angle,
        float viewportScale
    )
    {
        if (cloneVisualsComponent.Displacement is not { } displacementData)
            return null;

        if (displacementData.ShaderOverride is null)
            return null;

        if (!TryGetDisplacementTexture(displacementData, referenceSpriteComponent, angle, out var displacementTexture))
            return null;

        if (!_displacementShaders.TryGetValue(cloneUid, out var shaderInstance))
        {
            // Deliberately the unshaded variant, even though ShaderOverride is what decides whether this clone is
            // displaced at all. The sprite is lit while it is composited into the render target, so lighting the
            // quad as well would apply it twice - which is why clones came out darker than the sprite they copy.
            shaderInstance = _prototypeManager.Index<ShaderPrototype>(displacementData.ShaderOverrideUnshaded).InstanceUnique();
            _displacementShaders[cloneUid] = shaderInstance;
        }

        var (sourceTexture, displacementUv) = GetTextureWithUvBounds(displacementTexture);
        shaderInstance.SetParameter(DisplacementMapParameter, sourceTexture);
        shaderInstance.SetParameter(DisplacementUvParameter, displacementUv);

        // The offsets are spent in texels of the render target, which is that much larger than the sprite's own
        // resolution, so without this the displacement would shrink as the viewport is zoomed in.
        shaderInstance.SetParameter(DisplacementSizeParameter, DisplacementSize * viewportScale);

        return shaderInstance;
    }

    /// <summary>
    ///     Resolves the displacement map's texture for the current animation frame and direction.
    /// </summary>
    private bool TryGetDisplacementTexture(
        DisplacementData displacementData,
        SpriteComponent referenceSpriteComponent,
        Angle angle,
        [NotNullWhen(true)] out Texture? texture
    )
    {
        texture = null;

        if (GetSizeMap(displacementData, referenceSpriteComponent) is not { } layerData)
            return false;

        if (layerData.RsiPath is { } rsiPath && layerData.State is { } stateId)
        {
            var state = _spriteSystem.GetState(new SpriteSpecifier.Rsi(new ResPath(rsiPath), stateId));
            var direction = SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);

            texture = state.GetFrame(direction, GetAnimationFrame(state));
            return true;
        }

        if (layerData.TexturePath is not { } texturePath)
            return false;

        texture = _spriteSystem.GetTexture(new SpriteSpecifier.Texture(new ResPath(texturePath)));
        return true;
    }

    /// <summary>
    ///     Picks the displacement map matching the reference sprite's resolution, falling back to the default one.
    /// </summary>
    private static PrototypeLayerData? GetSizeMap(DisplacementData displacementData, SpriteComponent referenceSpriteComponent)
    {
        if (referenceSpriteComponent.BaseRSI is { } baseRsi && displacementData.SizeMaps.TryGetValue(baseRsi.Size.X, out var sizeMap))
            return sizeMap;

        return displacementData.SizeMaps.GetValueOrDefault(EyeManager.PixelsPerMeter);
    }

    /// <summary>
    ///     Which frame of an RSI state should be showing right now.
    /// </summary>
    private int GetAnimationFrame(RSI.State state)
    {
        if (!state.IsAnimated)
            return 0;

        var delays = state.GetDelays();
        var animationTime = (float)(_gameTiming.RealTime.TotalSeconds % state.AnimationLength);

        var elapsed = 0f;
        for (var frame = 0; frame < delays.Length; frame++)
        {
            elapsed += delays[frame];
            if (animationTime < elapsed)
                return frame;
        }

        return delays.Length - 1;
    }

    /// <summary>
    ///     Unwraps an atlased texture into the texture a shader has to sample and the UV bounds selecting it,
    ///     matching what the engine feeds the displacement shader when it is used as a sprite layer.
    /// </summary>
    private static (Texture Texture, Vector4 UvBounds) GetTextureWithUvBounds(Texture texture)
    {
        if (texture is not AtlasTexture atlasTexture)
            return (texture, new Vector4(0f, 0f, 1f, 1f));

        var sourceTexture = atlasTexture.SourceTexture;
        var subRegion = atlasTexture.SubRegion;
        var (width, height) = sourceTexture.Size;

        return (sourceTexture, new Vector4(
            subRegion.Left / width,
            (height - subRegion.Bottom) / height,
            subRegion.Right / width,
            (height - subRegion.Top) / height));
    }

    /// <summary>
    ///     Drops the shader instances of clones that no longer exist.
    /// </summary>
    private void DisposeUndrawnShaders()
    {
        _undrawnCloneUids.Clear();

        foreach (var cloneUid in _displacementShaders.Keys)
        {
            if (!_drawnCloneUids.Contains(cloneUid))
                _undrawnCloneUids.Add(cloneUid);
        }

        foreach (var cloneUid in _undrawnCloneUids)
        {
            _displacementShaders[cloneUid].Dispose();
            _displacementShaders.Remove(cloneUid);
        }
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();

        foreach (var shaderInstance in _displacementShaders.Values)
            shaderInstance.Dispose();

        _displacementShaders.Clear();

        _renderTexture?.Dispose();
        _renderTexture = null;
    }
}
