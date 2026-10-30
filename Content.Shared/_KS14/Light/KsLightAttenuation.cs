namespace Content.Shared._KS14.Light;

/// <summary>
///     RobustToolbox's own point-light attenuation, in C#.
/// </summary>
/// <remarks>
///     A port of the fragment stage of RobustToolbox/Resources/Shaders/Internal/light_shared.swsl, which stays
///         the authority: if its curve changes, this has to change with it, or anything derived from it stops
///         matching what is actually drawn. The shader says as much itself.
///     The engine keeps a port of its own in LightLevelSystem.GetColourFromLight, which is private - so this
///         is a third copy of one curve, and a change to the shader has to reach all three. That one also
///         clamps the radius to CVars.MaxLightRadius; this one deliberately does not, because Clyde hands the
///         shader the light's radius unclamped and this has to agree with what is on screen.
/// </remarks>
public static class KsLightAttenuation
{
    /// <summary>
    ///     The square of how far above its own plane a point light is treated as hovering, in tiles.
    /// </summary>
    /// <remarks>
    ///     This is the shader's LIGHTING_HEIGHT verbatim. It is squared because the shader adds it straight to
    ///         the squared distance - `dot(diff, diff) + LIGHTING_HEIGHT` - rather than to the distance.
    /// </remarks>
    public const float LightingHeightSquared = 1f;

    /// <summary>
    ///     How far above its own plane a point light is treated as hovering, in tiles.
    /// </summary>
    public static readonly float LightingHeight = MathF.Sqrt(LightingHeightSquared);

    /// <summary>
    ///     The fraction of a light's energy that arrives at a point <paramref name="distance"/> away from it -
    ///         the shader's own `val`, before it is multiplied by the light's energy and by the light mask.
    /// </summary>
    /// <param name="distance">
    ///     Distance in three dimensions, in tiles. For a point on the light's own plane this is not the planar
    ///         distance: see <see cref="GetDistanceOnOwnPlane"/>.
    /// </param>
    public static float Attenuate(float distance, float radius, float falloff, float curveFactor)
    {
        if (radius <= 0f)
            return 0f;

        var scaled = Math.Clamp(distance / radius, 0f, 1f);
        var squared = scaled * scaled;

        // Lerps between an inverse-shaped curve and an inverse-quadratic-shaped one.
        var curve = MathHelper.Lerp(scaled, squared, Math.Clamp(curveFactor, 0f, 1f));

        return Math.Clamp((1f - squared) * (1f - squared) / (1f + falloff * curve), 0f, 1f);
    }

    /// <summary>
    ///     The distance <see cref="Attenuate"/> actually sees for a point <paramref name="planarDistance"/>
    ///         away from the light across the plane the light itself sits on.
    /// </summary>
    /// <remarks>
    ///     Always at least <see cref="LightingHeight"/>: a light is never level with what it lights, which is
    ///         why even the tile directly under a light is not at full energy.
    /// </remarks>
    public static float GetDistanceOnOwnPlane(float planarDistance)
    {
        return MathF.Sqrt(planarDistance * planarDistance + LightingHeightSquared);
    }
}
