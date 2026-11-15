using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client._KS14.Particles;

/// <summary>
///     A grid with walls near an emitter: its boxes are <c>[BoxStart, BoxStart + BoxCount)</c> in
///         <see cref="KsParticleCollisionCache.Boxes"/>, in the grid's own frame, where they are axis-aligned whichever
///         way the grid is turned.
/// </summary>
public struct KsParticleColliderGrid
{
    public EntityUid Uid;
    public int BoxStart;
    public int BoxCount;

    /// <summary>Map frame to the grid's, refreshed every step, so a moving grid keeps its walls.</summary>
    public Matrix3x2 WorldToLocal;

    /// <summary>The grid's frame to the map's.</summary>
    public Matrix3x2 LocalToWorld;
}

/// <summary>
///     The static geometry about one colliding emitter, gathered by <see cref="KsParticlesSystem"/> every
///         <see cref="KsParticlesSystem.CollisionRefreshInterval"/> or when the particles leave
///         <see cref="Region"/>, never per particle. Grown by doubling and reused, so a warm emitter allocates
///         nothing.
/// </summary>
public sealed class KsParticleCollisionCache
{
    /// <summary>Whether the boxes describe <see cref="Region"/> on <see cref="MapId"/>.</summary>
    public bool Valid;

    public MapId MapId = MapId.Nullspace;

    /// <summary>The map-frame box the walls were gathered in.</summary>
    public Box2 Region;

    /// <summary>The radius the boxes are inflated by, metres.</summary>
    public float Radius;

    /// <summary>Seconds of simulation since the last gather.</summary>
    public float SinceRefresh;

    public KsParticleColliderGrid[] Grids = Array.Empty<KsParticleColliderGrid>();
    public int GridCount;

    /// <summary>Wall fixtures' bounding boxes, each in its grid's frame, already inflated by <see cref="Radius"/>.</summary>
    public Box2[] Boxes = Array.Empty<Box2>();
    public int BoxCount;

    /// <summary>Scratch: each live particle's segment this step, in the map frame, by index.</summary>
    public Vector2[] Starts = Array.Empty<Vector2>();
    public Vector2[] Ends = Array.Empty<Vector2>();

    public void Invalidate()
    {
        Valid = false;
        GridCount = 0;
        BoxCount = 0;
    }
}
