using Content.Shared._KS14.Light;

namespace Content.Shared._KS14.ZLevel.Light;

/// <summary>
///     Works out what a light on one z-level looks like from a z-level below it.
/// </summary>
/// <remarks>
///     There is no way to give the engine a light with a height, so a leak is drawn as an ordinary point light
///         sitting on the lower z-level - and the job here is picking the radius and energy that make that
///         stand-in arrive at the same brightness the real one would have.
///     Two things are matched exactly, because they are the two a viewer actually reads: how far the light
///         reaches across the lower floor, and how bright it is directly underneath. In between, the stand-in
///         runs slightly darker than the truth, because a light seen from further away falls off more gently
///         than any light on the plane itself can - which is not something radius and energy alone can express.
/// </remarks>
public static class KsZLevelLightLeak
{
    /// <summary>
    ///     The least a leak may reach across the lower floor, in tiles, before it is not worth drawing.
    /// </summary>
    /// <remarks>
    ///     Also keeps <see cref="TryGetLeakedLight"/> away from the point where it is solving 0/0: as the
    ///         vertical distance approaches the light's own radius, both the energy that arrives below and the
    ///         energy the stand-in would arrive with go to zero together.
    /// </remarks>
    public const float MinimumReach = 0.25f;

    /// <summary>
    ///     How much of a light reaches a gap in the floor it stands on, measured against what reaches the
    ///         floor directly underneath it.
    /// </summary>
    /// <remarks>
    ///     Light does not go through a floor, it goes through the gaps in one, so a gap across the room passes
    ///         less of a light than one underfoot. This is that difference, and it is exactly one for a gap
    ///         directly below - which is what makes <see cref="TryGetLeakedLight"/> on its own the answer for
    ///         a light shining straight down.
    /// </remarks>
    /// <param name="distanceToHole">How far the gap is from the light, across their shared floor.</param>
    public static float GetHoleFactor(float distanceToHole, float radius, float falloff, float curveFactor)
    {
        var atLight = KsLightAttenuation.Attenuate(
            KsLightAttenuation.LightingHeight,
            radius,
            falloff,
            curveFactor
        );

        if (atLight <= 0f)
            return 0f;

        var atHole = KsLightAttenuation.Attenuate(
            KsLightAttenuation.GetDistanceOnOwnPlane(distanceToHole),
            radius,
            falloff,
            curveFactor
        );

        return atHole / atLight;
    }

    /// <summary>
    ///     The radius and energy to give a stand-in light on a floor <paramref name="verticalDistance"/> tiles
    ///         below a real one.
    /// </summary>
    /// <param name="radius">The real light's radius, in tiles.</param>
    /// <param name="energy">The real light's energy.</param>
    /// <param name="falloff">The real light's falloff, which the stand-in keeps.</param>
    /// <param name="curveFactor">The real light's curve factor, which the stand-in keeps.</param>
    /// <param name="verticalDistance">
    ///     How far the lit floor sits below the floor the light belongs to, in tiles. The light's own hover
    ///         height is added to this here, so a light and a floor on the same z-level is zero.
    /// </param>
    /// <returns>Whether any of the light reaches that far down at all.</returns>
    public static bool TryGetLeakedLight(
        float radius,
        float energy,
        float falloff,
        float curveFactor,
        float verticalDistance,
        out float leakedRadius,
        out float leakedEnergy)
    {
        leakedRadius = 0f;
        leakedEnergy = 0f;

        // The light hovers its usual height over the floor it belongs to, and the floor being lit is the
        //      vertical distance below that one.
        var height = verticalDistance + KsLightAttenuation.LightingHeight;

        // Spend the whole radius going straight down and there is nothing left to spread sideways with.
        var reachSquared = radius * radius - height * height;
        if (reachSquared <= MinimumReach * MinimumReach)
            return false;

        // A light on the lower floor runs out at the same place this one does, as long as its radius covers
        //      that reach plus its own hover height - which is the same sum, from the other side.
        leakedRadius = MathF.Sqrt(reachSquared + KsLightAttenuation.LightingHeightSquared);

        // Then it is given exactly the energy that reproduces, at its own centre, what arrives from above.
        var arriving = energy * KsLightAttenuation.Attenuate(height, radius, falloff, curveFactor);
        var atStandInCentre = KsLightAttenuation.Attenuate(
            KsLightAttenuation.LightingHeight,
            leakedRadius,
            falloff,
            curveFactor
        );

        if (atStandInCentre <= 0f)
            return false;

        leakedEnergy = arriving / atStandInCentre;
        return true;
    }
}
