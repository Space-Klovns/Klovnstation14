using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Drawing helpers the NPC debug overlays share.
/// </summary>
public static class NpcDebugDraw
{
    /// <summary>
    ///     Outlines the first <paramref name="count"/> points of <paramref name="shape"/>.
    /// </summary>
    public static void DrawLoop(DrawingHandleWorld worldHandle, Vector2[] shape, int count, Color color)
    {
        for (var i = 0; i < count; i++)
        {
            worldHandle.DrawLine(shape[i], shape[(i + 1) % count], color);
        }
    }

    public static bool TryGetPosition(IEntityManager entityManager, SharedTransformSystem transformSystem, NetCoordinates netCoordinates, MapId mapId, out Vector2 position)
    {
        return TryGetPosition(entityManager, transformSystem, netCoordinates, mapId, out position, out _);
    }

    /// <summary>
    ///     Where <paramref name="netCoordinates"/> are on the map, and the world rotation of the grid they are on (none
    ///         off a grid). Markers with corners are drawn in a frame turned by that rotation (see
    ///         <see cref="GetMarkerTransform"/>), so they sit square to the grid they mark rather than to the map.
    /// </summary>
    public static bool TryGetPosition(IEntityManager entityManager, SharedTransformSystem transformSystem, NetCoordinates netCoordinates, MapId mapId, out Vector2 position, out Angle gridRotation)
    {
        position = default;
        gridRotation = Angle.Zero;

        if (!entityManager.TryGetEntity(netCoordinates.NetEntity, out _))
            return false;

        var coordinates = entityManager.GetCoordinates(netCoordinates);
        var mapCoordinates = transformSystem.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId != mapId)
            return false;

        position = mapCoordinates.Position;
        if (transformSystem.GetGrid(coordinates) is { } gridUid)
            gridRotation = transformSystem.GetWorldRotation(gridUid);

        return true;
    }

    /// <summary>
    ///     A marker's own frame: its origin at <paramref name="position"/>, its axes along its grid's. Set it on the
    ///         handle with <c>SetTransform</c>, draw the marker around <see cref="Vector2.Zero"/>, and set
    ///         <see cref="Matrix3x2.Identity"/> back after. Clyde applies the model transform to vertices as they are
    ///         queued, so changing it per marker is cheap.
    /// </summary>
    public static Matrix3x2 GetMarkerTransform(Vector2 position, Angle gridRotation)
    {
        return Matrix3Helpers.CreateTransform(position, gridRotation);
    }

    /// <summary>
    ///     A square of <paramref name="halfSize"/> around a marker's origin.
    /// </summary>
    public static Box2 Square(float halfSize)
    {
        return Box2.CenteredAround(Vector2.Zero, new Vector2(halfSize * 2f, halfSize * 2f));
    }
}
