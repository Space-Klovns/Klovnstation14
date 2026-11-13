using System.Numerics;
using Content.Client._KS14.Graphics;
using Content.Shared._KS14.Particles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Particles;

/// <summary>
///     Draws every emitter on one <see cref="KsParticleLayer"/>. One subclass per layer, since the overlay manager
///         keeps one overlay per type and an overlay has one ZIndex. Everything to draw was gathered by
///         <see cref="KsParticlesSystem.FrameUpdate"/>; this only culls it per pass and draws.
/// </summary>
public abstract partial class KsParticlesOverlay : Overlay
{
    [Dependency] private KsParticlesSystem _particlesSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    private readonly KsParticleLayer _layer;

    // Scratch, filled by BeforeDraw for the Draw that follows it.
    private readonly List<KsParticleDrawEntry> _lit = new();
    private readonly List<KsParticleDrawEntry> _unshaded = new();
    private float[] _sortKeys = new float[64];
    private int[] _sortIndices = new int[64];

    private ShaderInstance? _unshadedShader;

    protected KsParticlesOverlay(KsParticleLayer layer, int zIndex)
    {
        _layer = layer;
        ZIndex = zIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _lit.Clear();
        _unshaded.Clear();

        if (args.Viewport.Eye == null)
            return false;

        foreach (var entry in _particlesSystem.Layers[(int)_layer])
        {
            var emitter = entry.Emitter;
            if (emitter.MapId != args.MapId || !args.WorldAABB.Intersects(emitter.WorldBounds))
                continue;

            (emitter.Prototype!.Unshaded ? _unshaded : _lit).Add(entry);
        }

        return _lit.Count > 0 || _unshaded.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        var eye = args.Viewport.Eye!;
        var viewPosition = eye.Position.Position + eye.Offset;
        var viewScale = eye.Scale * args.Viewport.RenderScale * new Vector2(EyeManager.PixelsPerMeter, -EyeManager.PixelsPerMeter);
        var viewportSize = (Vector2)args.Viewport.Size;

        if (_lit.Count > 0)
        {
            worldHandle.UseShader(null);
            foreach (var entry in _lit)
                DrawEmitter(worldHandle, entry, eye.Rotation, viewPosition, viewScale, viewportSize);
        }

        if (_unshaded.Count > 0)
        {
            _unshadedShader ??= _prototypeManager.Index(SpriteSystem.UnshadedId).Instance();
            worldHandle.UseShader(_unshadedShader);
            foreach (var entry in _unshaded)
                DrawEmitter(worldHandle, entry, eye.Rotation, viewPosition, viewScale, viewportSize);
        }

        worldHandle.UseShader(null);
        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawEmitter(DrawingHandleWorld worldHandle, in KsParticleDrawEntry entry, Angle eyeRotation, Vector2 viewPosition, Vector2 viewScale, Vector2 viewportSize)
    {
        var emitter = entry.Emitter;
        var prototype = emitter.Prototype!;
        var sprites = emitter.Sprites;
        var modulate = entry.Particles.Color;

        var matrix = emitter.Matrix;
        if (prototype.PixelSnap)
            matrix.Translation += KsPixelSnap.GetOffset(emitter.Origin, viewPosition, eyeRotation, viewScale, viewportSize);
        worldHandle.SetTransform(matrix);

        var step = emitter.TickLength;
        var fraction = prototype.Interpolate ? Math.Clamp(emitter.TimeAccumulator / step, 0f, 1f) : 1f;
        var ageBehind = (1f - fraction) * step;

        var alive = emitter.Alive;
        var particles = emitter.Particles;
        var sorted = emitter.UsesZ;
        if (sorted)
        {
            if (_sortKeys.Length < alive)
            {
                _sortKeys = new float[particles.Length];
                _sortIndices = new int[particles.Length];
            }

            for (var i = 0; i < alive; i++)
            {
                _sortKeys[i] = particles[i].Position.Z;
                _sortIndices[i] = i;
            }

            Array.Sort(_sortKeys, _sortIndices, 0, alive);
        }

        for (var n = 0; n < alive; n++)
        {
            ref var particle = ref particles[sorted ? _sortIndices[n] : n];

            var age = particle.Age - ageBehind;
            var alpha = 1f;
            if (particle.FadeIn > 0f)
                alpha = Math.Min(alpha, age / particle.FadeIn);
            if (particle.Fade > 0f && particle.Lifespan > 0f)
                alpha = Math.Min(alpha, (particle.Lifespan - age) / particle.Fade);

            var color = particle.Color * modulate;
            color.A *= Math.Clamp(alpha, 0f, 1f);
            if (color.A <= 0f)
                continue;

            var position = Vector2.Lerp(new Vector2(particle.PrevPosition.X, particle.PrevPosition.Y), new Vector2(particle.Position.X, particle.Position.Y), fraction);
            var scale = Vector2.Lerp(particle.PrevScale, particle.Scale, fraction);
            var rotation = float.Lerp(particle.PrevRotation, particle.Rotation, fraction);

            var texture = sprites?.GetTexture(particle.Sprite, age, prototype.Animated) ?? Texture.White;
            var box = Box2.CenteredAround(position, (Vector2)texture.Size / EyeManager.PixelsPerMeter * scale);

            if (rotation == 0f)
                worldHandle.DrawTextureRect(texture, box, color);
            else
                worldHandle.DrawTextureRect(texture, new Box2Rotated(box, rotation, position), color);
        }
    }
}

/// <summary>Particles above floor tiles and puddles, below everything standing on the floor.</summary>
public sealed partial class KsParticlesFloorOverlay : KsParticlesOverlay
{
    public KsParticlesFloorOverlay() : base(KsParticleLayer.Floor, (int)Shared.DrawDepth.DrawDepth.HighFloorObjects) { }
}

/// <summary>Particles above objects and items, below mobs.</summary>
public sealed partial class KsParticlesBelowMobsOverlay : KsParticlesOverlay
{
    public KsParticlesBelowMobsOverlay() : base(KsParticleLayer.BelowMobs, (int)Shared.DrawDepth.DrawDepth.Mobs) { }
}

/// <summary>Particles above mobs, doors and gas, with other effects.</summary>
public sealed partial class KsParticlesEffectsOverlay : KsParticlesOverlay
{
    public KsParticlesEffectsOverlay() : base(KsParticleLayer.Effects, (int)Shared.DrawDepth.DrawDepth.Effects) { }
}

/// <summary>Particles above every sprite.</summary>
public sealed partial class KsParticlesTopOverlay : KsParticlesOverlay
{
    public KsParticlesTopOverlay() : base(KsParticleLayer.Top, (int)Shared.DrawDepth.DrawDepth.Overlays + 1) { }
}
