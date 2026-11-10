using System;
using System.Numerics;
using Content.Shared._KS14.Light;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Portal;

/// <summary>
///     Works out what a light shining into one view portal's doorway looks like coming out of the linked one.
/// </summary>
/// <remarks>
///     There is no way to hand the engine a light that starts partway along its own beam, so light leaving a
///         doorway is drawn as ordinary point lights sitting on it, one per sample point along it - the same
///         idea z-levels use for light falling through gaps in a floor (see
///         <c>KsZLevelLightLeak</c>). Each stand-in is picked to arrive, at its own centre, at the brightness
///         the real light has at that point of the doorway, and to run out exactly where the real light's reach
///         would have run out had it carried straight on through.
/// </remarks>
public static class KsPortalLightTransfer
{
    /// <summary>
    ///     The least a stand-in may reach past the doorway, in tiles, before it is not worth drawing.
    /// </summary>
    public const float MinimumReach = 0.25f;

    /// <summary>
    ///     Where along a doorway sample <paramref name="index"/> of <paramref name="count"/> sits, in the
    ///         portal's own frame: evenly spread, each in the middle of its share of the width.
    /// </summary>
    public static Vector2 GetSampleOffset(int index, int count, float width)
    {
        return new Vector2(width * ((index + 0.5f) / count - 0.5f), 0f);
    }

    /// <summary>
    ///     The rotation to give a stand-in light so that its mask - lit above its middle, dark below, as
    ///         drawn - faces <paramref name="exitDirection"/>.
    /// </summary>
    /// <remarks>
    ///     The engine draws a mask with its top along world +Y at rotation zero, turning counter-clockwise from
    ///         there, so +Y is rotated onto the exit direction.
    /// </remarks>
    public static Angle GetMaskRotation(Vector2 exitDirection)
    {
        return new Angle(exitDirection) - Angle.FromDegrees(90);
    }

    /// <summary>
    ///     The radius and energy to give one of <paramref name="sampleCount"/> stand-ins carrying a light
    ///         through a doorway point <paramref name="distanceToDoorway"/> tiles from it.
    /// </summary>
    /// <param name="radius">The real light's radius, in tiles.</param>
    /// <param name="energy">The real light's energy.</param>
    /// <param name="falloff">The real light's falloff, which the stand-in keeps.</param>
    /// <param name="curveFactor">The real light's curve factor, which the stand-in keeps.</param>
    /// <param name="distanceToDoorway">How far the doorway point is from the light, across their floor.</param>
    /// <param name="sampleCount">
    ///     How many stand-ins the doorway is split between. Their energy is shared out, so that from
    ///         well past the doorway they add up to roughly one light rather than one per sample.
    /// </param>
    /// <returns>Whether any of the light gets through at all.</returns>
    public static bool TryGetTransferredLight(
        float radius,
        float energy,
        float falloff,
        float curveFactor,
        float distanceToDoorway,
        int sampleCount,
        out float transferredRadius,
        out float transferredEnergy)
    {
        transferredRadius = 0f;
        transferredEnergy = 0f;

        if (sampleCount <= 0)
            return false;

        var reach = radius - distanceToDoorway;
        if (reach <= MinimumReach)
            return false;

        // Every light hovers over the floor it lights, so a stand-in needs that hover added back to its
        //      radius for its edge to land on the floor where the real light's would have.
        transferredRadius = MathF.Sqrt(reach * reach + KsLightAttenuation.LightingHeightSquared);

        var arriving = energy * KsLightAttenuation.Attenuate(
            KsLightAttenuation.GetDistanceOnOwnPlane(distanceToDoorway),
            radius,
            falloff,
            curveFactor
        );

        var atStandInCentre = KsLightAttenuation.Attenuate(
            KsLightAttenuation.LightingHeight,
            transferredRadius,
            falloff,
            curveFactor
        );

        if (atStandInCentre <= 0f)
            return false;

        transferredEnergy = arriving / atStandInCentre / sampleCount;
        return transferredEnergy > 0f;
    }
}
