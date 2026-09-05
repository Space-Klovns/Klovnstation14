using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.CloneLocalVisuals;
using Content.Shared.DisplacementMap;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
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
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    // Must match the uniforms declared by the displacement shaders.
    private const string DisplacementMapParameter = "displacementMap";
    private const string DisplacementUvParameter = "displacementUV";

    /// <summary>
    ///     One shader instance per clone entity. Shader parameters are only consumed once the batch they belong to
    ///     is flushed, so sharing a single instance between clones would make them all use the last clone's
    ///     displacement map.
    /// </summary>
    private readonly Dictionary<EntityUid, ShaderInstance> _displacementShaders = new();

    private readonly HashSet<EntityUid> _drawnCloneUids = new();
    private readonly List<EntityUid> _undrawnCloneUids = new();

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

            var displacementShaderInstance = GetDisplacementShader(cloneUid, cloneVisualsComponent, referenceSpriteComponent, angle);
            if (displacementShaderInstance is not null)
                args.WorldHandle.UseShader(displacementShaderInstance);

            _spriteSystem.RenderSprite(referenceEntity, args.WorldHandle, eyeRotation, worldRotation, worldPosition);

            if (displacementShaderInstance is not null)
                args.WorldHandle.UseShader(null);
        }

        _spriteSystem.SetOffset(referenceEntity.AsNullable(), oldOffset);
        _spriteSystem.SetRotation(referenceEntity.AsNullable(), oldRotation);
        _spriteSystem.SetColor(referenceEntity.AsNullable(), oldColor);

        DisposeUndrawnShaders();
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
        Angle angle
    )
    {
        if (cloneVisualsComponent.Displacement is not { } displacementData)
            return null;

        if (displacementData.ShaderOverride is not { } shaderId)
            return null;

        if (!TryGetDisplacementTexture(displacementData, referenceSpriteComponent, angle, out var displacementTexture))
            return null;

        if (!_displacementShaders.TryGetValue(cloneUid, out var shaderInstance))
        {
            shaderInstance = _prototypeManager.Index<ShaderPrototype>(shaderId).InstanceUnique();
            _displacementShaders[cloneUid] = shaderInstance;
        }

        var (sourceTexture, displacementUv) = GetTextureWithUvBounds(displacementTexture);
        shaderInstance.SetParameter(DisplacementMapParameter, sourceTexture);
        shaderInstance.SetParameter(DisplacementUvParameter, displacementUv);

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
    }
}
