using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._KS14.CloneLocalVisuals;
using Content.Shared.DisplacementMap;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
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
    [Dependency] private IPlacementManager _placementManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    // Must match the uniforms declared by the displacement shaders.
    private const string DisplacementMapParameter = "displacementMap";
    private const string DisplacementUvParameter = "displacementUV";
    private const string DisplacementSizeParameter = "displacementSize";

    /// <summary>
    ///     The displacementSize the shader prototypes in Resources/Prototypes/Shaders/displacement.yml declare. It is
    ///     the range the maps encode their offsets over, measured in texels of the sprite at its native resolution.
    ///     The shader spends it in texels of whatever texture it samples, and ours is the size of the viewport rather
    ///     than of a sprite sheet, so it has to be scaled by <see cref="GetViewportScale"/> before being handed over.
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
    ///     Scratch targets the reference sprite is composited into before being displaced, one per viewport.
    /// </summary>
    /// <remarks>
    ///     One target per viewport is enough for every clone drawn in a frame: they all composite the same sprite, and
    ///     <see cref="DrawingHandleBase.RenderInRenderTarget"/> flushes the render queue before it rebinds, so the
    ///     previous clone's draw has already consumed the contents by the time the next one overwrites them.
    /// </remarks>
    private readonly OverlayResourceCache<CachedRenderTexture> _renderTextures = new();

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

        // Everything below works in the viewport's own screen space, which an eyeless viewport does not have.
        if (args.Viewport.Eye is not { } eye)
            return;

        var referenceEntity = new Entity<SpriteComponent>(referenceUid, referenceSpriteComponent);

        var (oldOffset, oldRotation, oldColor) = (referenceSpriteComponent.Offset, referenceSpriteComponent.Rotation, referenceSpriteComponent.Color);

        // Everything RenderInRenderTarget's closure needs, copied out of the args because a closure cannot capture
        // an 'in' parameter. The reference sprite's original alpha is carried along because the clones tint it, and
        // reading it back off the component afterwards would compound every clone's alpha into the next one's.
        var context = new CloneDrawContext(
            args.Viewport,
            args.WorldHandle,
            args.RenderHandle,
            referenceEntity,
            eye.Rotation,
            GetViewportScale(args.Viewport),
            oldColor.A);

        _drawnCloneUids.Clear();

        var enumerator = _entityManager.EntityQueryEnumerator<CloneLocalVisualsComponent, TransformComponent, SpriteComponent>();
        while (enumerator.MoveNext(out var cloneUid, out var cloneVisualsComponent, out var transformComponent, out var spriteComponent))
        {
            _drawnCloneUids.Add(cloneUid);

            // Clones off this map have no position here to draw at. That is mostly clones in nullspace - the entity
            // spawn menu keeps preview entities there, and so does the placement ghost - which would otherwise all
            // pile onto the origin of whatever map the viewport happens to be showing. The placement ghost is drawn
            // further down instead, where the position it is actually being previewed at is known.
            if (transformComponent.MapID != args.MapId)
                continue;

            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);

            DrawClone(context, cloneUid, cloneVisualsComponent, spriteComponent, worldPosition, worldRotation);
        }

        DrawPlacementGhostClones(context);

        context.WorldHandle.SetTransform(Matrix3x2.Identity);
        context.WorldHandle.UseShader(null);

        _spriteSystem.SetOffset(referenceEntity.AsNullable(), oldOffset);
        _spriteSystem.SetRotation(referenceEntity.AsNullable(), oldRotation);
        _spriteSystem.SetColor(referenceEntity.AsNullable(), oldColor);

        DisposeUndrawnShaders();
    }

    /// <summary>
    ///     Draws the clone the entity placement ghost would have, at every position the ghost is being previewed at.
    /// </summary>
    /// <remarks>
    ///     The ghost is a real entity built from the prototype being placed, so it carries the clone's components -
    ///     but it is parked in nullspace and drawn by <see cref="PlacementMode.Render"/> at coordinates worked out
    ///     from the cursor, so the entity query above cannot place it. This mirrors that method's own loop.
    /// </remarks>
    private void DrawPlacementGhostClones(CloneDrawContext context)
    {
        if (_placementManager is not PlacementManager
            {
                IsActive: true,
                Eraser: false,
                CurrentMode: { } placementMode,
                CurrentPlacementOverlayEntity: { } ghostUid,
            } placementManager)
        {
            return;
        }

        if (!_entityManager.TryGetComponent<CloneLocalVisualsComponent>(ghostUid, out var cloneVisualsComponent))
            return;

        if (!_entityManager.TryGetComponent<SpriteComponent>(ghostUid, out var ghostSpriteComponent) || !ghostSpriteComponent.Visible)
            return;

        var placementCoordinates = placementManager.PlacementType switch
        {
            PlacementManager.PlacementTypes.Line => placementMode.LineCoordinates(),
            PlacementManager.PlacementTypes.Grid => placementMode.GridCoordinates(),
            _ => placementMode.SingleCoordinate(),
        };

        var directionAngle = placementManager.Direction.ToAngle();

        foreach (var coordinates in placementCoordinates)
        {
            if (!coordinates.IsValid(_entityManager))
                return;

            var worldPosition = _transformSystem.ToMapCoordinates(coordinates).Position;
            var worldRotation = _transformSystem.GetWorldRotation(coordinates.EntityId) + directionAngle;

            DrawClone(context, ghostUid, cloneVisualsComponent, ghostSpriteComponent, worldPosition, worldRotation);
        }
    }

    /// <summary>
    ///     Draws the reference sprite once, dressed up as the given clone.
    /// </summary>
    private void DrawClone(
        CloneDrawContext context,
        EntityUid cloneUid,
        CloneLocalVisualsComponent cloneVisualsComponent,
        SpriteComponent cloneSpriteComponent,
        Vector2 worldPosition,
        Angle worldRotation
    )
    {
        var referenceEntity = context.ReferenceEntity;
        var referenceSpriteComponent = referenceEntity.Comp;

        _spriteSystem.SetOffset(referenceEntity.AsNullable(), cloneSpriteComponent.Offset);
        _spriteSystem.SetRotation(referenceEntity.AsNullable(), cloneSpriteComponent.Rotation);
        _spriteSystem.SetColor(referenceEntity.AsNullable(), cloneSpriteComponent.Color.WithAlpha(cloneSpriteComponent.Color.A * context.ReferenceAlpha));

        // The same on-screen angle RenderSprite derives its RSI directions from.
        var angle = (worldRotation + context.EyeRotation).Reduced().FlipPositive();

        // Nothing to displace, so skip the whole render target detour and composite straight into the world.
        if (!TryGetDisplacementTexture(cloneVisualsComponent, referenceSpriteComponent, angle, out var displacementData, out var displacementTexture))
        {
            _spriteSystem.RenderSprite(referenceEntity, context.WorldHandle, context.EyeRotation, worldRotation, worldPosition);
            return;
        }

        var pixelSize = GetSpritePixelSize(referenceSpriteComponent);
        if (pixelSize.X <= 0 || pixelSize.Y <= 0)
            return;

        // The displacement has to be applied to the finished sprite rather than while it is being drawn: a shader
        // set on the drawing handle only survives until the first layer that carries a shader of its own, because
        // SpriteSystem.RenderLayer resets the handle back to no shader after drawing such a layer. Compositing the
        // whole sprite into a render target first turns it into a single texture, so one draw covers every layer.
        //
        // The target is the size of the viewport and the sprite goes into it at the very screen position it occupies
        // in the viewport, rather than into a small target of its own. The lighting the engine applies while the
        // sprite is drawn samples the light map at the fragment's position within the current render target, so a
        // small target would smear the whole viewport's lighting across the sprite. At viewport size that sampling
        // is identical to the real one, and the sprite comes out lit exactly like the one it copies.
        var renderTexture = GetRenderTexture(context.Viewport);

        // The sprite's own scale is kept out of the target and applied to the quad instead. Sprites like the
        // dwarf ones are scaled non-uniformly (1 by 0.5), and baking that in would rasterise the sprite onto
        // pixels that are not square - mixels - which the displacement shader then reads as though they were,
        // stretching every offset it makes along the squashed axis.
        var spriteScale = referenceSpriteComponent.Scale;
        if (spriteScale != Vector2.One)
            _spriteSystem.SetScale(referenceEntity.AsNullable(), Vector2.One);

        // Top down pixels into the render target, which is exactly the viewport's own screen space.
        var screenPosition = context.Viewport.WorldToLocal(worldPosition);

        var referenceUid = referenceEntity.Owner;
        var renderHandle = context.RenderHandle;
        var viewportScale = context.ViewportScale;

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
        var drawRotation = worldRotation + context.EyeRotation;

        context.WorldHandle.RenderInRenderTarget(renderTexture,
            () => renderHandle.DrawEntity(
                referenceUid,
                screenPosition,
                new Vector2(viewportScale, viewportScale),
                drawRotation,
                overrideDirection: referenceSpriteComponent.EnableDirectionOverride ? referenceSpriteComponent.DirectionOverride : null,
                sprite: referenceSpriteComponent
            ),
            Color.Transparent);

        // RenderInRenderTarget runs its action then and there, so the sprite is free again already.
        if (spriteScale != Vector2.One)
            _spriteSystem.SetScale(referenceEntity.AsNullable(), spriteScale);

        // Where the sprite's offset carries it, in screen aligned world metres. RenderSprite rotates the sprite's
        // frame by the eye rotation plus the entity rotation it drew with, which cancels out to nothing for a
        // NoRotation sprite and comes to the on-screen angle otherwise - the offset rides along with that.
        var spriteRotation = referenceSpriteComponent.NoRotation
            ? Angle.Zero
            : angle - (referenceSpriteComponent.SnapCardinals ? angle.RoundToCardinalAngle() : Angle.Zero);

        var screenOffset = spriteRotation.RotateVec(referenceSpriteComponent.Offset);

        // The slice of the target the sprite landed in. Selecting it rather than drawing the whole target keeps the
        // displacement map spread across the sprite the way it is when the engine applies one as a sprite layer:
        // the shader spreads it over the quad it is drawing, so that quad has to be the sprite and nothing else.
        var regionCentre = screenPosition + new Vector2(screenOffset.X, -screenOffset.Y) * EyeManager.PixelsPerMeter * viewportScale;
        var regionHalfSize = (Vector2)pixelSize * viewportScale / 2f;
        var subRegion = new UIBox2(regionCentre - regionHalfSize, regionCentre + regionHalfSize);

        var shaderInstance = GetDisplacementShader(cloneUid, displacementData, displacementTexture, viewportScale);
        context.WorldHandle.UseShader(shaderInstance);
        context.WorldHandle.SetTransform(GetQuadMatrix(referenceSpriteComponent, worldPosition, context.EyeRotation, spriteRotation, screenOffset, spriteScale));

        // The sprite's native size, because the target holds it at native proportions and the quad matrix is what
        // puts the sprite's scale back. Cast, otherwise this is an integer division and sprites that aren't a whole
        // number of tiles get squashed.
        var quadSize = (Vector2)pixelSize / EyeManager.PixelsPerMeter;
        context.WorldHandle.DrawTextureRectRegion(renderTexture.Texture, Box2.CenteredAround(Vector2.Zero, quadSize), subRegion: subRegion);
    }

    /// <summary>
    ///     Builds the transform the composited sprite is drawn back into the world with: screen aligned, at the
    ///     clone's position, carrying the sprite offset and the sprite scale that were kept out of the render target.
    /// </summary>
    private static Matrix3x2 GetQuadMatrix(
        SpriteComponent referenceSpriteComponent,
        Vector2 worldPosition,
        Angle eyeRotation,
        Angle spriteRotation,
        Vector2 screenOffset,
        Vector2 spriteScale
    )
    {
        // The target is screen aligned, so counter-rotating by the eye rotation cancels the one the eye's view
        // matrix adds and lands the target's pixels on screen pixels. The offset is already screen aligned too, and
        // sits outside the scale below because LocalMatrix likewise applies it after the sprite's own scale.
        var quadMatrix = Matrix3x2.CreateTranslation(screenOffset)
            * Matrix3Helpers.CreateTransform(worldPosition, -eyeRotation);

        if (spriteScale == Vector2.One)
            return quadMatrix;

        // SpriteComponent.LocalMatrix is built as scale, then rotation, then offset, so the scale belongs in the
        // frame the sprite had before any of that rotation - otherwise a scaled sprite that is also rotated (a mob
        // lying down is rotated a quarter turn) gets squashed along the wrong axis. Rotating back out of the
        // orientation the target baked in, scaling there, and rotating back into it puts it in that frame.
        var bakedRotation = referenceSpriteComponent.Rotation + spriteRotation;

        return Matrix3Helpers.CreateRotation(-bakedRotation.Theta)
            * Matrix3Helpers.CreateScale(spriteScale)
            * Matrix3Helpers.CreateRotation(bakedRotation.Theta)
            * quadMatrix;
    }

    /// <summary>
    ///     The size of the box the sprite's layers fit in, in texels at the sprite's native resolution.
    /// </summary>
    private static Vector2i GetSpritePixelSize(SpriteComponent referenceSpriteComponent)
    {
        var pixelSize = Vector2i.Zero;
        foreach (var spriteLayer in referenceSpriteComponent.AllLayers)
        {
            if (!spriteLayer.Visible)
                continue;

            pixelSize = Vector2i.ComponentMax(pixelSize, spriteLayer.PixelSize);
        }

        return pixelSize;
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

        // A degenerate eye would leave the sprite and the displacement at a nonsense magnitude.
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    /// <summary>
    ///     Fetches this viewport's scratch target, creating it if it does not exist yet and replacing it whenever the
    ///     viewport is resized.
    /// </summary>
    private IRenderTexture GetRenderTexture(IClydeViewport viewport)
    {
        var cached = _renderTextures.GetForViewport(viewport, static _ => new CachedRenderTexture());

        if (cached.RenderTexture?.Size != viewport.Size)
        {
            cached.RenderTexture?.Dispose();
            cached.RenderTexture = _clyde.CreateRenderTarget(
                viewport.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                name: "clone-local-visuals-target"
            );
        }

        return cached.RenderTexture;
    }

    /// <summary>
    ///     Fetches (creating it if needed) the shader instance that displaces this clone's sprite, with its
    ///     displacement map parameters set for the current frame.
    /// </summary>
    private ShaderInstance GetDisplacementShader(
        EntityUid cloneUid,
        DisplacementData displacementData,
        Texture displacementTexture,
        float viewportScale
    )
    {
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

        // The offsets are spent in texels of the render target, which holds the sprite at the viewport's resolution
        // rather than the sprite's own, so without this the displacement would shrink as the viewport is zoomed in.
        shaderInstance.SetParameter(DisplacementSizeParameter, DisplacementSize * viewportScale);

        return shaderInstance;
    }

    /// <summary>
    ///     Resolves the displacement map's texture for the current animation frame and direction.
    /// </summary>
    /// <returns>False if this clone has no usable displacement map, in which case it draws undisplaced.</returns>
    private bool TryGetDisplacementTexture(
        CloneLocalVisualsComponent cloneVisualsComponent,
        SpriteComponent referenceSpriteComponent,
        Angle angle,
        [NotNullWhen(true)] out DisplacementData? displacementData,
        [NotNullWhen(true)] out Texture? texture
    )
    {
        displacementData = null;
        texture = null;

        if (cloneVisualsComponent.Displacement is not { } data)
            return false;

        if (data.ShaderOverride is null)
            return false;

        if (GetSizeMap(data, referenceSpriteComponent) is not { } layerData)
            return false;

        if (layerData.RsiPath is { } rsiPath && layerData.State is { } stateId)
        {
            var state = _spriteSystem.GetState(new SpriteSpecifier.Rsi(new ResPath(rsiPath), stateId));
            var direction = SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);

            displacementData = data;
            texture = state.GetFrame(direction, GetAnimationFrame(state));
            return true;
        }

        if (layerData.TexturePath is not { } texturePath)
            return false;

        displacementData = data;
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

        _renderTextures.Dispose();
    }

    /// <summary>
    ///     Everything about the frame being drawn that every clone in it shares.
    /// </summary>
    private readonly record struct CloneDrawContext(
        IClydeViewport Viewport,
        DrawingHandleWorld WorldHandle,
        IRenderHandle RenderHandle,
        Entity<SpriteComponent> ReferenceEntity,
        Angle EyeRotation,
        float ViewportScale,
        float ReferenceAlpha
    );

    private sealed class CachedRenderTexture : IDisposable
    {
        public IRenderTexture? RenderTexture;

        public void Dispose()
        {
            RenderTexture?.Dispose();
        }
    }
}
