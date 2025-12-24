// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Content.Shared._KS14.Random.Helpers;

/// <summary>
///     Basically SharedRandomExtensions but with more methods.
/// </summary>
// HashCodeCombine overloads were originally taken from https://github.com/space-wizards/space-station-14/ at commit 22fe5185a3c121f50c30afd470d55d969f7f838c , licensed under the MIT license.
public static class KsSharedRandomExtensions
{
    private const int DefaultHash = 5381;

    /// <inheritdoc cref="Shared.Random.Helpers.SharedRandomExtensions.HashCodeCombine(List{int})"/>
    /// <remarks>
    ///     This is a generic <see cref="IEnumerable{T}"/> variant. 
    /// </remarks>
    public static int HashCodeCombine(IEnumerable<int> values)
    {
        var hash = DefaultHash;
        foreach (var value in values)
            hash = (hash << 5) + hash + value;

        return hash;
    }

    /// <inheritdoc cref="Shared.Random.Helpers.SharedRandomExtensions.HashCodeCombine(List{int})"/>
    /// <remarks>
    ///     This is a generic variadic variant. 
    /// </remarks>
    public static int HashCodeCombine(params int[] values)
    {
        var hash = DefaultHash;
        foreach (var value in values)
            hash = (hash << 5) + hash + value;

        return hash;
    }

    /// <summary>
    ///     This is a generic variadic variant of <see cref="Shared.Random.Helpers.SharedRandomExtensions.HashCodeCombine(List{int})"/>
    ///         that returns a new <see cref="System.Random"/> whose seed is the hashcode combined from the given values.  
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Random RandomWithHashCodeCombinedSeed(params int[] values)
        => new(HashCodeCombine(values));

    /// <returns>The <see cref="NetEntity.Id"/> of the given Entity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetNetId(Entity<MetaDataComponent?> entity, EntityManager entityManager)
    {
        if (!entityManager.MetaQuery.Resolve(entity, ref entity.Comp))
            return NetEntity.Invalid.Id;

        return entity.Comp.NetEntity.Id;
    }

    /// <summary>
    ///     Fast-ish implementation of NextVector2. The returned vector
    ///         is slightly biased towards the edges of the unit circle.
    /// </summary>
    /// <returns>A unit <see cref="Vector2"/>.</returns>
    public static Vector2 BiasedNextUnitVector2(this System.Random random)
    {
        var ux = (uint)random.Next(int.MinValue, int.MaxValue);
        var uy = (uint)random.Next(int.MinValue, int.MaxValue);

        var u = ux / (float)uint.MaxValue * 2f - 1f;
        var v = uy / (float)uint.MaxValue * 2f - 1f;

        // Marsaglia transformation to map square to unit circle
        // avoid division by zero if u=v=0
        var s = u * u + v * v;
        if (s == 0f)
            s = 1e-6f;

        var factor = 1f / MathF.Sqrt(s);
        return new Vector2(u * factor, v * factor);
    }

    /// <summary>
    ///     Generic implementation of NextVector2. The returned vector
    ///         is slightly biased towards the edges of the unit circle.
    /// </summary>
    /// <returns>A unit <see cref="Vector2"/>.</returns>
    public static Vector2 NextUnitVector2(this System.Random random)
    {
        var angle = random.NextSingle() * MathF.Tau;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>
    ///     Generic implementation of <see cref="Robust.Shared.Random.IRobustRandom.NextVector2(float)"/>, with a maximum magnitude.
    /// </summary>
    public static Vector2 NextVector2(this System.Random random, float maxMagnitude)
    {
        var bits = (uint)random.Next(int.MinValue, int.MaxValue);
        var angle = bits / (float)uint.MaxValue * MathF.Tau;

        var length = random.NextSingle() * maxMagnitude;
        return new Vector2(length * MathF.Cos(angle), length * MathF.Sin(angle));
    }

    /// <summary>
    ///     Generic implementation of <see cref="Robust.Shared.Random.IRobustRandom.NextVector2(float)"/>, with minimum and maximum magnitudes.
    /// </summary>
    public static Vector2 NextVector2(this System.Random random, float minMagnitude, float maxMagnitude)
    {
        var length = random.NextSingle() * (maxMagnitude - minMagnitude) + minMagnitude;
        return random.NextUnitVector2() * length;
    }
}
