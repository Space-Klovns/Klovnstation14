using System.Numerics;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Atmos.ChemicalFire;

/// <summary>
///     Draws one half of every chemfire. Chemfires have no sprite of their own: both halves are overlays, so
///         anything drawn at a depth between them - visible gas, mobs, doors - covers or uncovers both alike
///         instead of hiding one half and leaving the other floating on top.
///     Rendering is unshaded, and the animation frame comes from time rather than from a sprite layer, so
///         the two halves of a fire are always on the same frame.
/// </summary>
/// <remarks>
///     One subclass per half, since <see cref="IOverlayManager"/> keys overlays by type.
/// </remarks>
public abstract partial class ChemicalFireOverlay : Overlay
{
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    private const LookupFlags EntityLookupFlags = LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained | LookupFlags.Approximate;

    private readonly ShaderInstance _unshadedShader;

    private readonly HashSet<Entity<ChemicalFireComponent>> _entities = [];
    private List<Entity<MapGridComponent>> _grids = [];
    private readonly List<DrawEntry> _drawEntries = [];

    /// <summary>
    ///     Resolved animations, so the RSI is only walked once per state rather than per fire per frame. A
    ///         state that failed to resolve is cached as null, so it isn't looked up again either.
    /// </summary>
    private readonly Dictionary<(ResPath Rsi, string State), Animation?> _animations = [];

    protected ChemicalFireOverlay(ShaderInstance unshadedShader, int zIndex)
    {
        _unshadedShader = unshadedShader;
        ZIndex = zIndex;
    }

    /// <summary>
    ///     The resolved RSI state this overlay draws for a chemfire.
    /// </summary>
    protected abstract string GetState(ChemicalFireComponent fireComponent);

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye != null &&
            _entityManager.Count<ChemicalFireComponent>() > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = args.WorldBounds;

        _grids.Clear();
        _mapSystem.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true);
        if (_grids.Count == 0)
            return;

        var worldHandle = args.WorldHandle;
        var transformQuery = _entityManager.TransformQuery;
        var eye = args.Viewport.Eye!;
        var eyeRotation = eye.Rotation;
        var realSeconds = _gameTiming.RealTime.TotalSeconds;

        var viewPosition = eye.Position.Position + eye.Offset;
        var viewScale = eye.Scale * args.Viewport.RenderScale * new Vector2(EyeManager.PixelsPerMeter, -EyeManager.PixelsPerMeter);
        var viewportSize = (Vector2)args.Viewport.Size;

        worldHandle.UseShader(_unshadedShader);

        foreach (var grid in _grids)
        {
            var (_, gridRotation, worldMatrix, invWorldMatrix) = _transformSystem.GetWorldPositionRotationMatrixWithInv(grid.Owner);

            _entities.Clear();
            _entityLookupSystem.GetLocalEntitiesIntersecting(grid.Owner, invWorldMatrix.TransformBox(bounds), _entities, flags: EntityLookupFlags);

            if (_entities.Count == 0)
                continue;

            var localEyeRotation = eyeRotation + gridRotation;

            _drawEntries.Clear();
            foreach (var entity in _entities)
            {
                if (!TryGetAnimation(entity.Comp, out var animation))
                    continue;

                var texture = animation.GetFrame(realSeconds, entity.Comp.AnimationPhase);

                var transformComponent = transformQuery.GetComponent(entity.Owner);
                var position = transformComponent.LocalPosition;

                // Snapped exactly as a snapCardinals sprite would be, which is also what the lateral smoothing
                //     in ChemicalFireVisualsSystem assumes the fire looks like on screen.
                var drawRotation = ChemicalFireVisualsSystem.GetDrawRotation(transformComponent.LocalRotation, localEyeRotation);
                var size = texture.Size / (float)EyeManager.PixelsPerMeter;

                // Foam smothering the fire squashes the flame down onto its base and fades it.
                var smothered = ChemicalFireComponent.SmotheredShrink * entity.Comp.SmotherProgress;
                var height = size.Y * (1f - smothered);
                var origin = position + drawRotation.RotateVec(entity.Comp.SpriteOffset - new Vector2(0f, (size.Y - height) / 2f));
                var color = entity.Comp.Color.WithAlpha(entity.Comp.Color.A * (1f - smothered));

                _drawEntries.Add(new DrawEntry(
                    entity.Owner,
                    texture,
                    new Box2Rotated(Box2.CenteredAround(origin, new Vector2(size.X, height)), drawRotation, origin),
                    color,
                    localEyeRotation.RotateVec(position).Y
                ));
            }

            // Back to front, the way the sprite renderer y-sorts: whatever is lower on screen is drawn later,
            //     so a fire overlaps the one behind it rather than the other way around.
            _drawEntries.Sort(static (a, b) =>
            {
                var order = b.ScreenHeight.CompareTo(a.ScreenHeight);
                return order != 0 ? order : a.Uid.Id.CompareTo(b.Uid.Id);
            });

            // Snapped the same way the engine snaps a grid's sprites and tiles, so fires don't shimmer against
            //     the floor they sit on while the eye moves.
            worldMatrix.Translation += GetPixelSnapOffset(worldMatrix.Translation, viewPosition, eyeRotation, viewScale, viewportSize);
            worldHandle.SetTransform(worldMatrix);

            foreach (var entry in _drawEntries)
                worldHandle.DrawTextureRectRegion(entry.Texture, entry.Quad, modulate: entry.Color);
        }

        _drawEntries.Clear();

        worldHandle.UseShader(null);
        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    ///     The world-space nudge that puts <paramref name="worldPosition"/> on a whole screen pixel.
    /// </summary>
    /// <remarks>
    ///     A copy of the engine's <c>Clyde.GetPixelSnapOffset</c>, which it applies to every grid before drawing
    ///         that grid's sprites and tiles. It is internal, so it can't be called from here.
    /// </remarks>
    private static Vector2 GetPixelSnapOffset(
        Vector2 worldPosition,
        Vector2 viewPosition,
        Angle viewRotation,
        Vector2 viewScale,
        Vector2 viewportSize)
    {
        var viewPositionRelative = viewRotation.RotateVec(worldPosition - viewPosition);
        var screenPosition = viewPositionRelative * viewScale + viewportSize / 2f;
        var screenOffset = screenPosition.Rounded() - screenPosition;
        var viewOffset = screenOffset / viewScale;
        return (-viewRotation).RotateVec(viewOffset);
    }

    private bool TryGetAnimation(ChemicalFireComponent fireComponent, out Animation animation)
    {
        animation = default;

        var stateName = GetState(fireComponent);
        if (string.IsNullOrEmpty(stateName))
            return false;

        var key = (fireComponent.Sprite, stateName);

        // GetValueRefOrAddDefault isnt sandboxed, so this is two lookups.
        if (!_animations.TryGetValue(key, out var cachedAnimation))
        {
            cachedAnimation = ResolveAnimation(fireComponent.Sprite, stateName);
            _animations[key] = cachedAnimation;
        }

        if (cachedAnimation is not { } resolvedAnimation)
            return false;

        animation = resolvedAnimation;
        return true;
    }

    private Animation? ResolveAnimation(ResPath rsiPath, string stateName)
    {
        if (!_resourceCache.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / rsiPath, out var rsiResource) ||
            !rsiResource.RSI.TryGetState(stateName, out var state))
            return null;

        var frames = state.GetFrames(RsiDirection.South);
        if (frames.Length == 0)
            return null;

        return new Animation(frames, state.GetDelays(), state.TotalDelay);
    }

    private readonly record struct DrawEntry(EntityUid Uid, Texture Texture, Box2Rotated Quad, Color Color, float ScreenHeight);

    private readonly record struct Animation(Texture[] Frames, float[] Delays, float TotalDelay)
    {
        /// <param name="seconds">Any steadily increasing clock, in seconds.</param>
        /// <param name="phase">How far into the loop to start, as a fraction of it.</param>
        public Texture GetFrame(double seconds, float phase)
        {
            if (Frames.Length == 1 || TotalDelay <= 0f)
                return Frames[0];

            // Reduced in double first: the clock is large enough that a float would lose the frame timing.
            var time = (float)((seconds + phase * TotalDelay) % TotalDelay);

            for (var i = 0; i < Delays.Length && i < Frames.Length; i++)
            {
                time -= Delays[i];
                if (time < 0f)
                    return Frames[i];
            }

            return Frames[^1];
        }
    }
}

/// <summary>
///     The half of a chemfire behind whatever is standing in it.
/// </summary>
public sealed partial class ChemicalFireUnderOverlay(ShaderInstance unshadedShader)
    : ChemicalFireOverlay(unshadedShader, ContentZIndex)
{
    /// <summary>
    ///     Where an object on the floor would draw: drawn before every sprite at this depth or above, so mobs and
    ///         items in the fire cover it.
    /// </summary>
    public const int ContentZIndex = (int)Shared.DrawDepth.DrawDepth.Objects;

    protected override string GetState(ChemicalFireComponent fireComponent) => fireComponent.UnderState;
}

/// <summary>
///     The half of a chemfire in front of whatever is standing in it.
/// </summary>
public sealed partial class ChemicalFireOverOverlay(ShaderInstance unshadedShader)
    : ChemicalFireOverlay(unshadedShader, ContentZIndex)
{
    /// <summary>
    ///     Above mobs and doors, so flames cover what is standing in them, but below visible gas.
    /// </summary>
    /// <remarks>
    ///     The gas overlay draws at <see cref="Shared.DrawDepth.DrawDepth.Gasses"/>, above the <c>under</c> half.
    ///         Drawing this half above gas as well would leave gas on the tile hiding only the <c>under</c>
    ///         half, and chemfires make or burn in visible gas themselves (water vapour, plasma).
    /// </remarks>
    public const int ContentZIndex = (int)Shared.DrawDepth.DrawDepth.Overdoors;

    protected override string GetState(ChemicalFireComponent fireComponent) => fireComponent.OverState;
}
