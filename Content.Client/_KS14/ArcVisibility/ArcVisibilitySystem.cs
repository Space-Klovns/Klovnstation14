using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.ArcVisibility;

/// <summary>
///     Math for fading out things that only face a limited arc, based on where they sit relative to the
///         center of a viewport's eye. Used by directional wallmounts and by stains drawn on top of them.
/// </summary>
/// <remarks>
///     Everything here works in screen space, so a caller drawing many things in one frame should grab a
///         single <see cref="ArcVisibilityEyeState"/> up front and reuse it for every one of them.
/// </remarks>
public sealed partial class ArcVisibilitySystem : EntitySystem
{
    /// <summary>
    ///     Fraction of the half-arc that stays fully opaque before the fade towards the edge of the arc starts.
    /// </summary>
    public const float DefaultFeather = 0.65f;

    /// <summary>
    ///     Grabs the per-viewport values <see cref="TryGetArcAlpha"/> needs, so they only get calculated once per draw.
    /// </summary>
    /// <returns>False if the viewport has no eye, in which case nothing can be faded.</returns>
    public bool TryGetEyeState(IClydeViewport viewport, out ArcVisibilityEyeState eyeState)
    {
        eyeState = default;

        if (viewport.Eye is not { } eye)
            return false;

        var worldToLocalMatrix = viewport.GetWorldToLocalMatrix();

        eyeState = new ArcVisibilityEyeState(
            worldToLocalMatrix,
            Vector2.Transform(eye.Position.Position, worldToLocalMatrix), // there is surely a better way to get this value from somewhere
            eye.Rotation
        );

        return true;
    }

    /// <summary>
    ///     Works out how visible something with a limited facing arc is, from the eye described by <paramref name="eyeState"/>.
    /// </summary>
    /// <param name="worldPosition">World position of the thing being faded.</param>
    /// <param name="worldRotation">World rotation of the thing being faded.</param>
    /// <param name="arcDirection">Direction the arc faces, relative to <paramref name="worldRotation"/>. Zero is south.</param>
    /// <param name="arc">Total width of the arc. Anything outside of it is not visible at all.</param>
    /// <param name="opaqueAlpha">Alpha the thing is drawn with while it is dead-on facing the eye.</param>
    /// <param name="alpha">Alpha the thing should be drawn with. Only meaningful when this returns true.</param>
    /// <param name="feather">See <see cref="DefaultFeather"/>.</param>
    /// <returns>True if the eye is inside the arc, i.e. the thing should be drawn at all.</returns>
    public bool TryGetArcAlpha(
        in ArcVisibilityEyeState eyeState,
        Vector2 worldPosition,
        Angle worldRotation,
        Angle arcDirection,
        Angle arc,
        float opaqueAlpha,
        out float alpha,
        float feather = DefaultFeather)
    {
        alpha = 0f;

        var halfArc = arc.Theta / 2d;
        if (halfArc <= 0d)
            return false;

        // we figure out what should be visible based on its direction & rotation adjusted for eye rotation
        // + its position relative to the viewport center's screencoords (the four quadrants surrounding it)
        var screenRotation = worldRotation + eyeState.EyeRotation + arcDirection;

        var screenPosition = Vector2.Transform(worldPosition, eyeState.WorldToLocalMatrix);
        var distance = screenPosition - eyeState.EyeScreenPosition;

        // measure how much the angle is 'facing' the viewport center
        // if its inside the arc then it should be visible
        // i have no fucking idea why i need to flip x, genuinely
        // but it fixes the math. it worked fine vertically
        var distanceAngle = (distance with { X = -distance.X }).ToWorldAngle();
        var angleBetween = Angle.ShortestDistance(distanceAngle, screenRotation);

        // opaque until `feather` of the way to the edge of the arc, then linearly down to (nearly) nothing at the edge
        var edgeFraction = (float)(Math.Abs(angleBetween.Theta) / halfArc);
        var opaque = edgeFraction < feather;
        var fadeFraction = opaque ? 0f : edgeFraction - feather;
        var fadeTarget = opaque ? 0f : opaqueAlpha / feather;
        alpha = float.Lerp(opaqueAlpha, 0f - fadeTarget, Math.Min(fadeFraction, 1f));

        return angleBetween > -halfArc && angleBetween < halfArc;
    }
}

/// <summary>
///     Screen-space state of a viewport's eye, for <see cref="ArcVisibilitySystem"/>.
/// </summary>
public readonly record struct ArcVisibilityEyeState(
    Matrix3x2 WorldToLocalMatrix,
    Vector2 EyeScreenPosition,
    Angle EyeRotation
);
