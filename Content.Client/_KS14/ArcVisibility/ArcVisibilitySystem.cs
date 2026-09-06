using System.Numerics;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;

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
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<OccluderComponent> _occluderQuery = default!;

    /// <summary>
    ///     Fraction of the half-arc that stays fully opaque before the fade towards the edge of the arc starts,
    ///         based on <see cref="KsCCVars.ArcVisibilityFeather"/>.
    /// </summary>
    private float _feather = 0.65f;

    /// <summary>
    ///     Smallest feather we will actually divide by, since the cvar is free to be set to zero.
    /// </summary>
    private const float MinimumFeather = 0.001f;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.ArcVisibilityFeather, (feather) => _feather = feather, invokeImmediately: true);
    }

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
    ///     Is the tile an entity sits on blocking sight? Nothing mounted on something you can see straight through
    ///         has a hidden side, so there is no reason to fade it out at all.
    /// </summary>
    /// <remarks>
    ///     This walks every entity anchored to the tile, which is not cheap - only call it for things actually being drawn.
    /// </remarks>
    public bool IsOnOccludedTile(TransformComponent transformComponent)
    {
        if (transformComponent.GridUid is not { } gridUid ||
            !_mapGridQuery.TryGetComponent(gridUid, out var mapGridComponent))
            return false;

        var tileIndices = _mapSystem.CoordinatesToTile(gridUid, mapGridComponent, transformComponent.Coordinates);
        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, mapGridComponent, tileIndices);

        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (_occluderQuery.TryGetComponent(anchoredUid, out var occluderComponent) && occluderComponent.Enabled)
                return true;
        }

        return false;
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
    /// <param name="feather">Overrides <see cref="KsCCVars.ArcVisibilityFeather"/> for this one call.</param>
    /// <returns>True if the eye is inside the arc, i.e. the thing should be drawn at all.</returns>
    public bool TryGetArcAlpha(
        in ArcVisibilityEyeState eyeState,
        Vector2 worldPosition,
        Angle worldRotation,
        Angle arcDirection,
        Angle arc,
        float opaqueAlpha,
        out float alpha,
        float? feather = null)
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
        var featherValue = Math.Clamp(feather ?? _feather, MinimumFeather, 1f);
        var edgeFraction = (float)(Math.Abs(angleBetween.Theta) / halfArc);
        var opaque = edgeFraction < featherValue;
        var fadeFraction = opaque ? 0f : edgeFraction - featherValue;
        var fadeTarget = opaque ? 0f : opaqueAlpha / featherValue;
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
