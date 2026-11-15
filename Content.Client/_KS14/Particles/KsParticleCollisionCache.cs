using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client._KS14.Particles;

/// <summary>
///     A grid with walls near an emitter: its colliders are <c>[ColliderStart, ColliderStart + ColliderCount)</c> in
///         <see cref="KsParticleCollisionCache.Colliders"/>, in the grid's own frame.
/// </summary>
public struct KsParticleColliderGrid
{
    public EntityUid Uid;
    public int ColliderStart;
    public int ColliderCount;

    /// <summary>Map frame to the grid's, refreshed every step, so a moving grid keeps its walls.</summary>
    public Matrix3x2 WorldToLocal;

    /// <summary>The grid's frame to the map's.</summary>
    public Matrix3x2 LocalToWorld;
}

/// <summary>
///     One side of a convex wall, in its grid's frame: a point is on the wall's side of it when
///         <c>Vector2.Dot(Normal, point) &lt; Offset</c>. The offset already includes the particles' radius.
/// </summary>
public struct KsParticleColliderPlane
{
    public Vector2 Normal;
    public float Offset;
}

/// <summary>
///     One wall fixture's real shape, in its grid's frame, grown by the particles' radius: a convex polygon (the
///         planes <c>[PlaneStart, PlaneStart + PlaneCount)</c> of <see cref="KsParticleCollisionCache.Planes"/>, its
///         edges plus a bevel at each corner, so the grown corners stay close to round), or a circle.
/// </summary>
public struct KsParticleCollider
{
    /// <summary>The grown shape's bounding box, for the quick reject.</summary>
    public Box2 Bounds;

    public int PlaneStart;

    /// <summary>Zero for a circle.</summary>
    public int PlaneCount;

    public Vector2 Center;

    /// <summary>The grown circle's radius; unused for a polygon.</summary>
    public float CircleRadius;
}

/// <summary>
///     The static geometry about one colliding emitter, gathered by <see cref="KsParticlesSystem"/> every
///         <see cref="KsParticlesSystem.CollisionRefreshInterval"/> or when the particles leave
///         <see cref="Region"/>, never per particle. Grown by doubling and reused, so a warm emitter allocates
///         nothing.
/// </summary>
public sealed class KsParticleCollisionCache
{
    /// <summary>Whether the colliders describe <see cref="Region"/> on <see cref="MapId"/>.</summary>
    public bool Valid;

    public MapId MapId = MapId.Nullspace;

    /// <summary>The map-frame box the walls were gathered in.</summary>
    public Box2 Region;

    /// <summary>The radius the shapes are grown by, metres.</summary>
    public float Radius;

    /// <summary>Seconds of simulation since the last gather.</summary>
    public float SinceRefresh;

    public KsParticleColliderGrid[] Grids = Array.Empty<KsParticleColliderGrid>();
    public int GridCount;

    public KsParticleCollider[] Colliders = Array.Empty<KsParticleCollider>();
    public int ColliderCount;

    public KsParticleColliderPlane[] Planes = Array.Empty<KsParticleColliderPlane>();
    public int PlaneCount;

    /// <summary>
    ///     A <see cref="Content.Shared._KS14.Particles.KsParticleSpace.Local"/> emitter's matrix as of the last step,
    ///         so a particle's motion is swept from where it was in the world, with the emitter's own motion.
    /// </summary>
    public Matrix3x2 PreviousMatrix = Matrix3x2.Identity;

    public bool HasPreviousMatrix;

    /// <summary>Scratch: each live particle's segment this step, in the map frame, by index.</summary>
    public Vector2[] Starts = Array.Empty<Vector2>();
    public Vector2[] Ends = Array.Empty<Vector2>();

    /// <summary>Scratch: one fixture's corners while it is turned into planes.</summary>
    public Vector2[] Corners = new Vector2[8];

    public void Invalidate()
    {
        Valid = false;
        GridCount = 0;
        ColliderCount = 0;
        PlaneCount = 0;
    }
}
