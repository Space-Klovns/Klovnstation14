using System.Numerics;
using Robust.Shared.Random;

namespace Content.Shared._KS14.Particles;

/// <summary>
///     How a <see cref="KsParticleGenerator"/> turns its two operands into a value. BYOND's generator types, plus
///         <see cref="Constant"/> for a plain value written without a generator.
/// </summary>
public enum KsParticleGeneratorKind : byte
{
    /// <summary>Always <see cref="KsParticleGenerator.A"/>.</summary>
    Constant,

    /// <summary>A number between A and B. On a vector field, the same number on every component.</summary>
    Num,

    /// <summary>A point on the straight line from A to B: one random fraction, applied to every component.</summary>
    Vector,

    /// <summary>A point inside the box with corners A and B: an independent random fraction per component.</summary>
    Box,

    /// <summary>A point in the xy plane at a distance between A.X and B.X from the origin, in a random direction.</summary>
    Circle,

    /// <summary>A point in space at a distance between A.X and B.X from the origin, in a random direction.</summary>
    Sphere,

    /// <summary>A point on the edge of a square whose half-size is between A.X and B.X.</summary>
    Square,

    /// <summary>A point on the surface of a cube whose half-size is between A.X and B.X.</summary>
    Cube,
}

/// <summary>
///     How the random fraction a <see cref="KsParticleGenerator"/> uses is distributed over [0, 1]. BYOND's
///         <c>UNIFORM_RAND</c>, <c>NORMAL_RAND</c>, <c>LINEAR_RAND</c> and <c>SQUARE_RAND</c>.
/// </summary>
public enum KsParticleDistribution : byte
{
    /// <summary>Every value equally likely.</summary>
    Uniform,

    /// <summary>A bell curve about the middle, clamped to the ends.</summary>
    Normal,

    /// <summary>Likelihood rising linearly towards B. On a circle this spreads points evenly over its area.</summary>
    Linear,

    /// <summary>Likelihood rising with the square towards B. On a sphere this spreads points evenly through its volume.</summary>
    Square,
}

/// <summary>
///     A value, or a rule for drawing one, for a particle property: BYOND's <c>generator()</c>. One struct serves every
///         property type; the operands are held in a <see cref="Vector4"/> and the caller takes the components it
///         wants (<see cref="SampleFloat"/>, <see cref="SampleVector2"/>, <see cref="SampleVector3"/>,
///         <see cref="SampleColor"/>).
/// </summary>
/// <remarks>
///     Read from YAML by <see cref="KsParticleGeneratorSerializer"/>: a scalar (<c>5</c>), a vector
///         (<c>[ 0, 0.5 ]</c>) or a colour (<c>"#ff8800"</c>) is a <see cref="KsParticleGeneratorKind.Constant"/>; a
///         mapping <c>{ kind: circle, a: 0, b: 0.5, dist: normal }</c> is any other kind.
/// </remarks>
public readonly struct KsParticleGenerator
{
    public readonly KsParticleGeneratorKind Kind;
    public readonly KsParticleDistribution Distribution;

    /// <summary>First operand. The value itself for <see cref="KsParticleGeneratorKind.Constant"/>.</summary>
    public readonly Vector4 A;

    /// <summary>Second operand. Unused for <see cref="KsParticleGeneratorKind.Constant"/>.</summary>
    public readonly Vector4 B;

    /// <summary>
    ///     Whether the operands were written as colours, so they are written back as colours and
    ///         <see cref="SampleColor"/> is the natural read.
    /// </summary>
    public readonly bool IsColor;

    /// <summary>
    ///     How many components the YAML gave (1 for a scalar, 2 or 3 for a list, 4 for a colour). Only affects how
    ///         the value is written back out.
    /// </summary>
    public readonly byte Components;

    public KsParticleGenerator(KsParticleGeneratorKind kind, Vector4 a, Vector4 b, KsParticleDistribution distribution = KsParticleDistribution.Uniform, bool isColor = false, byte components = 1)
    {
        Kind = kind;
        A = a;
        B = b;
        Distribution = distribution;
        IsColor = isColor;
        Components = components;
    }

    public static KsParticleGenerator Constant(float value) => new(KsParticleGeneratorKind.Constant, new Vector4(value), Vector4.Zero);
    public static KsParticleGenerator Constant(Vector2 value) => new(KsParticleGeneratorKind.Constant, new Vector4(value, 0f, 0f), Vector4.Zero, components: 2);
    public static KsParticleGenerator Constant(Vector3 value) => new(KsParticleGeneratorKind.Constant, new Vector4(value, 0f), Vector4.Zero, components: 3);
    public static KsParticleGenerator Constant(Color value) => new(KsParticleGeneratorKind.Constant, ToVector(value), Vector4.Zero, isColor: true, components: 4);

    public static readonly KsParticleGenerator Zero = Constant(0f);
    public static readonly KsParticleGenerator One = Constant(1f);
    public static readonly KsParticleGenerator White = Constant(Color.White);

    /// <summary>Whether this can only ever produce zero, so callers can skip the work it would drive.</summary>
    public bool IsAlwaysZero => Kind == KsParticleGeneratorKind.Constant && A == Vector4.Zero;

    /// <summary>Whether this can produce a non-zero z component (the third one).</summary>
    public bool CanProduceZ => Kind switch
    {
        KsParticleGeneratorKind.Constant => A.Z != 0f,
        KsParticleGeneratorKind.Sphere or KsParticleGeneratorKind.Cube => B.X != 0f || A.X != 0f,
        KsParticleGeneratorKind.Circle or KsParticleGeneratorKind.Square => false,
        _ => A.Z != 0f || B.Z != 0f,
    };

    public static Vector4 ToVector(Color color) => new(color.R, color.G, color.B, color.A);
    public static Color ToColor(Vector4 vector) => new(vector.X, vector.Y, vector.Z, vector.W);

    public float SampleFloat(System.Random random) => Sample(random).X;
    public Vector2 SampleVector2(System.Random random) { var v = Sample(random); return new Vector2(v.X, v.Y); }
    public Vector3 SampleVector3(System.Random random) { var v = Sample(random); return new Vector3(v.X, v.Y, v.Z); }
    public Color SampleColor(System.Random random) => ToColor(Vector4.Clamp(Sample(random), Vector4.Zero, Vector4.One));

    /// <summary>
    ///     Draws a value. Allocation-free; the distribution and shape are a couple of switches.
    /// </summary>
    public Vector4 Sample(System.Random random)
    {
        switch (Kind)
        {
            case KsParticleGeneratorKind.Constant:
                return A;

            case KsParticleGeneratorKind.Num:
            case KsParticleGeneratorKind.Vector:
                return Vector4.Lerp(A, B, Fraction(random));

            case KsParticleGeneratorKind.Box:
                return new Vector4(
                    float.Lerp(A.X, B.X, Fraction(random)),
                    float.Lerp(A.Y, B.Y, Fraction(random)),
                    float.Lerp(A.Z, B.Z, Fraction(random)),
                    float.Lerp(A.W, B.W, Fraction(random)));

            case KsParticleGeneratorKind.Circle:
            {
                var radius = float.Lerp(A.X, B.X, Fraction(random));
                var angle = random.NextFloat() * MathF.Tau;
                return new Vector4(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0f, 0f);
            }

            case KsParticleGeneratorKind.Sphere:
            {
                var radius = float.Lerp(A.X, B.X, Fraction(random));
                // Uniform direction: z uniform in [-1, 1], azimuth uniform.
                var z = random.NextFloat() * 2f - 1f;
                var azimuth = random.NextFloat() * MathF.Tau;
                var planar = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
                return new Vector4(MathF.Cos(azimuth) * planar * radius, MathF.Sin(azimuth) * planar * radius, z * radius, 0f);
            }

            case KsParticleGeneratorKind.Square:
            {
                var halfSize = float.Lerp(A.X, B.X, Fraction(random));
                var along = (random.NextFloat() * 2f - 1f) * halfSize;
                return (random.Next(4)) switch
                {
                    0 => new Vector4(along, halfSize, 0f, 0f),
                    1 => new Vector4(along, -halfSize, 0f, 0f),
                    2 => new Vector4(halfSize, along, 0f, 0f),
                    _ => new Vector4(-halfSize, along, 0f, 0f),
                };
            }

            case KsParticleGeneratorKind.Cube:
            {
                var halfSize = float.Lerp(A.X, B.X, Fraction(random));
                var u = (random.NextFloat() * 2f - 1f) * halfSize;
                var v = (random.NextFloat() * 2f - 1f) * halfSize;
                return (random.Next(6)) switch
                {
                    0 => new Vector4(u, v, halfSize, 0f),
                    1 => new Vector4(u, v, -halfSize, 0f),
                    2 => new Vector4(u, halfSize, v, 0f),
                    3 => new Vector4(u, -halfSize, v, 0f),
                    4 => new Vector4(halfSize, u, v, 0f),
                    _ => new Vector4(-halfSize, u, v, 0f),
                };
            }

            default:
                return A;
        }
    }

    /// <summary>
    ///     A fraction in [0, 1] drawn with <see cref="Distribution"/>.
    /// </summary>
    public float Fraction(System.Random random) => Fraction(random, Distribution);

    public static float Fraction(System.Random random, KsParticleDistribution distribution)
    {
        var uniform = random.NextFloat();
        return distribution switch
        {
            KsParticleDistribution.Normal => Math.Clamp((float)random.NextGaussian(0.5, 1.0 / 6.0), 0f, 1f),
            KsParticleDistribution.Linear => MathF.Sqrt(uniform),
            KsParticleDistribution.Square => MathF.Cbrt(uniform),
            _ => uniform,
        };
    }
}
