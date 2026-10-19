using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Everything that could block sight from one viewpoint within a range, gathered once and indexed by the directions
///         it covers, so that checking many targets from there costs one gathering and a few tests each rather than a
///         tile walk each: a shadowcast, with each target still tested exactly as
///         <see cref="NpcLineOfSightSystem.InLineOfSight(MapCoordinates, MapCoordinates, float)"/> tests it, so the two
///         never disagree. Built by <see cref="NpcLineOfSightSystem.BuildSightField"/> and read with
///         <see cref="NpcLineOfSightSystem.InLineOfSight(NpcSightField, MapCoordinates)"/>.
/// </summary>
/// <remarks>
///     A snapshot: good for as long as nothing that occludes changes, which in practice means within the one update
///         or plan that built it. Reuse one instance; rebuilding it stops allocating once its lists have grown.
/// </remarks>
public sealed class NpcSightField
{
    /// <summary>
    ///     How many slices the directions round the viewpoint are cut into.
    /// </summary>
    internal const int Buckets = 256;

    internal MapCoordinates Origin;
    internal float Range;

    /// <summary>
    ///     Whether this viewpoint is somewhere a field cannot be built for - off a grid, near another one, on a map not
    ///         yet initialised - so each target is checked with an ordinary line of sight check instead.
    /// </summary>
    internal bool Fallback;

    internal EntityUid GridUid;
    internal Matrix3x2 InvWorldMatrix;
    internal Angle GridRotation;

    internal readonly List<(EntityUid Uid, OccluderComponent Component, Box2 Bounds)> Occluders = new();

    /// <summary>
    ///     Occluders round the viewpoint itself, which cover every direction from it.
    /// </summary>
    internal readonly List<int> Surrounding = new();

    /// <summary>
    ///     Where each slice's occluders start in <see cref="BucketItems"/>; slice <c>i</c>'s run to the start of
    ///         <c>i + 1</c>.
    /// </summary>
    internal readonly int[] BucketStarts = new int[Buckets + 1];

    internal readonly List<int> BucketItems = new();
}
