using Content.Shared._KS14.Particles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Random;

namespace Content.Client._KS14.Particles;

/// <summary>
///     A <see cref="KsParticlesPrototype"/>'s sprites, resolved once and shared by every emitter of that prototype:
///         the RSI states, their weights, and each state's frame timings, so picking a frame by a particle's age is a
///         division and a short scan.
/// </summary>
public sealed class KsParticleSpriteSet
{
    public readonly IRsiStateLike[] States;

    /// <summary>Running totals of the weights, for a weighted pick with one random number.</summary>
    private readonly float[] _cumulativeWeights;
    private readonly float _totalWeight;

    /// <summary>Per state, the time at which each frame ends, from the state's start.</summary>
    private readonly float[][] _frameEnds;
    private readonly float[] _totalDelays;

    /// <summary>
    ///     Half the largest frame's larger side, in metres, at scale 1: how far a particle can reach past its position.
    /// </summary>
    public readonly float MaxExtent;

    public KsParticleSpriteSet(KsParticlesPrototype prototype, SpriteSystem spriteSystem, ISawmill sawmill)
    {
        var count = prototype.Sprites.Count;
        States = new IRsiStateLike[count];
        _cumulativeWeights = new float[count];
        _frameEnds = new float[count][];
        _totalDelays = new float[count];

        var maxExtent = 0.5f / EyeManager.PixelsPerMeter; // the white pixel
        for (var i = 0; i < count; i++)
        {
            var sprite = prototype.Sprites[i];
            IRsiStateLike state;
            try
            {
                state = spriteSystem.RsiStateLike(sprite.Sprite);
            }
            catch (Exception e)
            {
                sawmill.Error($"Particle prototype {prototype.ID} sprite {i} ({sprite.Sprite}) could not be loaded: {e.Message}");
                state = Texture.White;
            }

            States[i] = state;
            _totalWeight += MathF.Max(0f, sprite.Weight);
            _cumulativeWeights[i] = _totalWeight;

            var frames = state.IsAnimated ? state.AnimationFrameCount : 1;
            var ends = new float[frames];
            var total = 0f;
            for (var frame = 0; frame < frames; frame++)
            {
                total += state.IsAnimated ? MathF.Max(0f, state.GetDelay(frame)) : 0f;
                ends[frame] = total;
            }

            _frameEnds[i] = ends;
            _totalDelays[i] = total;

            var size = state.Default.Size;
            maxExtent = MathF.Max(maxExtent, 0.5f * Math.Max(size.X, size.Y) / EyeManager.PixelsPerMeter);
        }

        MaxExtent = maxExtent;
    }

    /// <summary>
    ///     Picks a sprite by weight, or -1 when the prototype has none.
    /// </summary>
    public int Pick(System.Random random)
    {
        if (States.Length == 0 || _totalWeight <= 0f)
            return -1;

        var roll = random.NextFloat() * _totalWeight;
        for (var i = 0; i < _cumulativeWeights.Length; i++)
        {
            if (roll < _cumulativeWeights[i])
                return i;
        }

        return States.Length - 1;
    }

    /// <summary>
    ///     The texture a particle shows at <paramref name="age"/> seconds.
    /// </summary>
    public Texture GetTexture(int sprite, float age, bool animated)
    {
        if (sprite < 0 || sprite >= States.Length)
            return Texture.White;

        var state = States[sprite];
        var total = _totalDelays[sprite];
        if (!animated || !state.IsAnimated || total <= 0f)
            return state.Default;

        var time = age % total;
        if (time < 0f)
            time += total;

        var ends = _frameEnds[sprite];
        for (var frame = 0; frame < ends.Length; frame++)
        {
            if (time < ends[frame])
                return state.GetFrame(RsiDirection.South, frame);
        }

        return state.GetFrame(RsiDirection.South, ends.Length - 1);
    }
}
