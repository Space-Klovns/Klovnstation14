using System.Numerics;
using Robust.Shared.Random;

namespace Content.Client._KS14.Visor;

/// <summary>
///     What precipitation reaches the visor this frame: rates at the weather's current strength, and whether the player
///         is under cover from it.
/// </summary>
public readonly record struct KsVisorExposure(float DropsPerSecond, float FlakesPerSecond, float Mist, float DropScale, bool Sheltered)
{
    public static readonly KsVisorExposure None = new(0f, 0f, 0f, 1f, Sheltered: true);
}

/// <summary>
///     One drop of water, or wet snowflake, on the visor. Positions are in screen space, 0 to 1 with y up; sizes are a share
///         of the screen's height, so drops stay round on any aspect ratio.
/// </summary>
public struct KsVisorDrop
{
    public Vector2 Position;
    public float Radius;

    /// <summary>
    ///     Seconds since it landed.
    /// </summary>
    public float Age;

    /// <summary>
    ///     How long it lasts before it has dried off, if it doesn't run off the visor first.
    /// </summary>
    public float Life;

    /// <summary>
    ///     Seconds it clings before sliding, once it is heavy enough to slide at all.
    /// </summary>
    public float SlideDelay;

    /// <summary>
    ///     Screen heights per second, downward, while it slides.
    /// </summary>
    public float Speed;

    /// <summary>
    ///     Length of the wet streak it has left above itself, in screen heights.
    /// </summary>
    public float Trail;

    /// <summary>
    ///     0 for a snowflake just stuck, 1 once it has melted into water.
    /// </summary>
    public float Melt;

    public float Seed;
}

/// <summary>
///     Water on the visor, simulated in screen space: drops landing, clinging, sliding down when heavy enough and leaving a
///         streak, picking up what they run into, and drying off; wet snowflakes melting into drops; and a fine mist that
///         builds up while out in it. Pure state and maths, so that it can be tested without drawing anything.
/// </summary>
public sealed class KsVisorSimulation
{
    /// <summary>
    ///     The most drops on the visor at once. The shader's arrays are this long.
    /// </summary>
    /// <remarks>
    ///     TODO ENGINE: drop the packing once RobustToolbox caches built-in uniform locations as int, not sbyte
    ///         (<c>GLShaderProgram._uniformIntCache</c>).
    ///     Every array element takes a uniform location, and the engine keeps its built-in uniforms' locations in a
    ///         signed byte, so the shader's arrays must stay under about 120 elements together, or those wrap negative
    ///         and are never set. That is why two values share a float (<see cref="Pack"/>).
    /// </remarks>
    public const int MaxDrops = 32;

    /// <summary>
    ///     Packs two values from 0 to 1 into one float for the shader: <paramref name="whole"/> to 1/1023 in the whole
    ///         part, <paramref name="fraction"/> to well under 1/1000 in the fraction. The shader's <c>unpack</c> reads
    ///         them back; <see cref="Unpack"/> is the same in C#.
    /// </summary>
    public static float Pack(float whole, float fraction)
    {
        return MathF.Floor(Math.Clamp(whole, 0f, 1f) * 1023f + 0.5f) + Math.Clamp(fraction, 0f, 1f) * 0.999f;
    }

    /// <summary>
    ///     What the shader reads back from <see cref="Pack"/>.
    /// </summary>
    public static (float Whole, float Fraction) Unpack(float packed)
    {
        var whole = MathF.Floor(packed);
        return (whole / 1023f, Math.Clamp((packed - whole) / 0.999f, 0f, 1f));
    }

    /// <summary>
    ///     Drops this big and over slide, once they have clung a while.
    /// </summary>
    public const float SlideRadius = 0.017f;

    /// <summary>
    ///     A sliding drop that has shed down to this stops.
    /// </summary>
    public const float StopRadius = 0.012f;

    public const float MaxRadius = 0.06f;

    private const float MinDropRadius = 0.022f;
    private const float MaxDropRadius = 0.036f;
    private const float MaxTrail = 0.45f;

    /// <summary>
    ///     How fast the mist builds up out in the weather, and clears under cover, per second.
    /// </summary>
    private const float MistGrowth = 0.12f;
    private const float MistDecay = 0.04f;

    private readonly IRobustRandom _random;
    private float _dropDebt;
    private float _flakeDebt;

    public KsVisorSimulation(IRobustRandom random)
    {
        _random = random;
    }

    public readonly List<KsVisorDrop> Drops = new(MaxDrops);

    /// <summary>
    ///     How densely the fine mist covers the visor now, 0 to 1.
    /// </summary>
    public float Mist { get; private set; }

    /// <summary>
    ///     Whether there is anything on the visor to draw.
    /// </summary>
    public bool IsDry => Drops.Count == 0 && Mist <= 0.001f;

    /// <param name="aspect">The screen's width over its height, to judge which drops touch.</param>
    public void Update(float frameTime, KsVisorExposure exposure, float aspect)
    {
        if (frameTime <= 0f)
            return;

        UpdateMist(frameTime, exposure);
        Spawn(frameTime, exposure);
        Age(frameTime, exposure.Sheltered);
        Slide(frameTime);
        Merge(aspect);
    }

    private void UpdateMist(float frameTime, KsVisorExposure exposure)
    {
        var target = exposure.Sheltered ? 0f : Math.Clamp(exposure.Mist, 0f, 1f);
        Mist = Mist < target
            ? MathF.Min(target, Mist + MistGrowth * frameTime)
            : MathF.Max(target, Mist - MistDecay * frameTime);
    }

    private void Spawn(float frameTime, KsVisorExposure exposure)
    {
        if (exposure.Sheltered)
        {
            _dropDebt = 0f;
            _flakeDebt = 0f;
            return;
        }

        _dropDebt += exposure.DropsPerSecond * frameTime;
        _flakeDebt += exposure.FlakesPerSecond * frameTime;

        for (; _dropDebt >= 1f; _dropDebt -= 1f)
            TryAdd(NewDrop(exposure.DropScale));

        for (; _flakeDebt >= 1f; _flakeDebt -= 1f)
            TryAdd(NewFlake());
    }

    private void TryAdd(KsVisorDrop drop)
    {
        if (Drops.Count < MaxDrops)
            Drops.Add(drop);
    }

    private KsVisorDrop NewDrop(float scale)
    {
        // Much of a size, the odd one a bit bigger.
        var size = MathF.Pow(_random.NextFloat(), 1.5f);
        return new KsVisorDrop
        {
            Position = new Vector2(_random.NextFloat(), _random.NextFloat(0.05f, 1f)),
            Radius = MathF.Min(MaxRadius, (MinDropRadius + (MaxDropRadius - MinDropRadius) * size) * scale),
            Life = _random.NextFloat(6f, 14f),
            // Heavy enough to run, it starts down almost at once.
            SlideDelay = _random.NextFloat(0f, 0.15f),
            Melt = 1f,
            Seed = _random.NextFloat(),
        };
    }

    private KsVisorDrop NewFlake()
    {
        return new KsVisorDrop
        {
            Position = new Vector2(_random.NextFloat(), _random.NextFloat(0.05f, 1f)),
            Radius = _random.NextFloat(0.012f, 0.022f),
            Life = _random.NextFloat(8f, 16f),
            SlideDelay = _random.NextFloat(0.5f, 3f),
            Melt = 0f,
            Seed = _random.NextFloat(),
        };
    }

    private void Age(float frameTime, bool sheltered)
    {
        // Under cover the visor dries off quicker, as nothing new is landing to keep it wet.
        var ageing = sheltered ? frameTime * 2f : frameTime;

        for (var i = Drops.Count - 1; i >= 0; i--)
        {
            var drop = Drops[i];
            drop.Age += ageing;

            // A flake melts over a few seconds, shrinking a little into the drop it becomes.
            if (drop.Melt < 1f)
            {
                var melted = MathF.Min(1f, drop.Melt + frameTime / (2f + 2f * drop.Seed));
                drop.Radius *= 1f - 0.3f * (melted - drop.Melt);
                drop.Melt = melted;
            }

            if (drop.Age >= drop.Life || drop.Position.Y < -0.1f)
            {
                Drops.RemoveAt(i);
                continue;
            }

            Drops[i] = drop;
        }
    }

    private void Slide(float frameTime)
    {
        for (var i = 0; i < Drops.Count; i++)
        {
            var drop = Drops[i];
            var sliding = drop.Melt >= 1f && drop.Radius >= StopRadius && drop.Age >= drop.SlideDelay &&
                          (drop.Speed > 0f || drop.Radius >= SlideRadius);

            if (sliding)
            {
                // Heavier drops run faster, slowing and picking up again as the visor's surface drags at them.
                var drag = 0.6f + 0.4f * (0.5f + 0.5f * MathF.Sin(drop.Age * 1.7f + drop.Seed * 23f));
                var targetSpeed = drop.Radius * 2.5f * drag;
                drop.Speed += (targetSpeed - drop.Speed) * MathF.Min(1f, frameTime * 2f);
                drop.Speed = MathF.Max(drop.Speed, 0.0001f);

                var moved = drop.Speed * frameTime;
                drop.Position.Y -= moved;
                drop.Trail = MathF.Min(MaxTrail, drop.Trail + moved);

                // It leaves some of itself behind on the way down.
                drop.Radius = MathF.Max(0f, drop.Radius - moved * 0.03f);
                if (drop.Radius < StopRadius)
                    drop.Speed = 0f;
            }
            else
            {
                drop.Speed = 0f;
                // A streak dries faster than a drop.
                drop.Trail = MathF.Max(0f, drop.Trail - frameTime * 0.03f);
            }

            Drops[i] = drop;
        }
    }

    /// <summary>
    ///     A sliding drop picks up any drop it runs into, and grows by its water.
    /// </summary>
    private void Merge(float aspect)
    {
        for (var i = 0; i < Drops.Count; i++)
        {
            var runner = Drops[i];
            if (runner.Speed <= 0f)
                continue;

            for (var j = Drops.Count - 1; j >= 0; j--)
            {
                if (j == i)
                    continue;

                var other = Drops[j];
                var offset = runner.Position - other.Position;
                offset.X *= aspect;
                var reach = runner.Radius + other.Radius * 0.8f;
                if (offset.LengthSquared() > reach * reach)
                    continue;

                // Areas add, as the water does.
                runner.Radius = MathF.Min(MaxRadius, MathF.Sqrt(runner.Radius * runner.Radius + other.Radius * other.Radius));
                runner.Life = MathF.Max(runner.Life, other.Life);
                Drops.RemoveAt(j);
                if (j < i)
                    i--;
            }

            Drops[i] = runner;
        }
    }

    /// <summary>
    ///     How opaque a drop is now: it fades in as it lands, and out as it dries.
    /// </summary>
    public static float GetAlpha(in KsVisorDrop drop)
    {
        var landed = Math.Clamp(drop.Age / 0.04f, 0f, 1f);
        var drying = Math.Clamp((drop.Life - drop.Age) / (drop.Life * 0.3f), 0f, 1f);
        return landed * drying;
    }

    /// <summary>
    ///     How far a drop has gathered from the splat it landed as into a rounded running drop, 0 to 1: over its first
    ///         four drop-widths of running, and never all the way.
    /// </summary>
    public static float GetGathered(in KsVisorDrop drop)
    {
        // Never quite all the way: a running drop keeps a little of its splat.
        return 0.7f * Math.Clamp(drop.Trail / (drop.Radius * 4f), 0f, 1f);
    }

    /// <summary>
    ///     How big a drop is drawn now: just landed, it spreads out to its size, easing in, never past it.
    /// </summary>
    public static float GetDrawnRadius(in KsVisorDrop drop)
    {
        var spread = Math.Clamp(drop.Age / 0.1f, 0f, 1f);
        var eased = 1f - (1f - spread) * (1f - spread);
        return drop.Radius * (0.75f + 0.25f * eased);
    }
}
