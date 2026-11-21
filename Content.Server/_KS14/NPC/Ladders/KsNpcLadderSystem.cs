using System.Numerics;
using Content.Shared._KS14.Ladders;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Ladders;

/// <summary>
///     Gets NPCs from one map to another by ladder: which ladder to make for, and climbing it.
/// </summary>
/// <remarks>
///     Deliberately not part of the navmesh. A path is found on one map as ever, to the nearest ladder that leads
///         towards the destination's map, and from the far end of it again: see
///         <c>NPCSteeringSystem.KsSteerByLadder</c>. So the way taken is the fewest ladders, not the shortest walk, and
///         a ladder that can't be walked to is not passed over for one that can.
/// </remarks>
public sealed partial class KsNpcLadderSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsLadderSystem _ladderSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsLadderComponent> _ladderQuery = default!;

    /// <summary>
    ///     How close to a ladder an NPC gets to climb it. Over a tile, as a ladder may be on a wall, which it can only
    ///         stand beside.
    /// </summary>
    public const float ClimbRange = 1.25f;

    /// <summary>
    ///     How long an NPC with no way to its destination's map waits before looking again.
    /// </summary>
    private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long past the climb's own time it waits for the climb to have happened.
    /// </summary>
    private static readonly TimeSpan ClimbMargin = TimeSpan.FromSeconds(0.25);

    private readonly List<(Entity<KsLadderComponent> Ladder, EntityUid FromMapUid, EntityUid ToMapUid)> _links = [];
    private readonly Dictionary<EntityUid, int> _ladderCounts = [];
    private readonly Queue<EntityUid> _frontier = [];

    /// <summary>
    ///     Whether ladders lead from one map to another, over however many maps between.
    /// </summary>
    public bool HasRoute(EntityUid fromMapUid, EntityUid toMapUid)
    {
        return TryFindLadder(fromMapUid, toMapUid, Vector2.Zero, out _);
    }

    /// <summary>
    ///     The ladder <paramref name="npcUid"/> makes for to get to <paramref name="destinationMapUid"/>, kept from one
    ///         call to the next for as long as it still leads there.
    /// </summary>
    /// <param name="isNew">Whether this is another ladder than the call before gave.</param>
    public bool TryGetLadder(
        EntityUid npcUid,
        TransformComponent npcTransform,
        EntityUid destinationMapUid,
        out Entity<KsLadderComponent> ladder,
        out bool isNew)
    {
        ladder = default;
        isNew = false;

        if (npcTransform.MapUid is not { } npcMapUid)
            return false;

        var routeComponent = EnsureComp<KsNpcLadderRouteComponent>(npcUid);

        if (routeComponent.LadderUid is { } keptUid &&
            routeComponent.DestinationMapUid == destinationMapUid &&
            _ladderQuery.TryComp(keptUid, out var keptComponent) &&
            Transform(keptUid).MapUid == npcMapUid &&
            _ladderSystem.TryGetDestination((keptUid, keptComponent), out _))
        {
            ladder = (keptUid, keptComponent);
            return true;
        }

        routeComponent.LadderUid = null;

        if (_gameTiming.CurTime < routeComponent.NextSearch)
            return false;

        routeComponent.DestinationMapUid = destinationMapUid;

        if (!TryFindLadder(npcMapUid, destinationMapUid, _transformSystem.GetWorldPosition(npcTransform), out ladder))
        {
            routeComponent.NextSearch = _gameTiming.CurTime + SearchInterval;
            return false;
        }

        routeComponent.LadderUid = ladder.Owner;
        isNew = true;
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> is part way through a climb that takes time.
    /// </summary>
    public bool IsClimbing(EntityUid npcUid)
    {
        return TryComp<KsNpcLadderRouteComponent>(npcUid, out var routeComponent) &&
            _gameTiming.CurTime < routeComponent.ClimbingUntil;
    }

    /// <summary>
    ///     Has <paramref name="npcUid"/> climb <paramref name="ladder"/>, which it stands at.
    /// </summary>
    public void Climb(EntityUid npcUid, Entity<KsLadderComponent> ladder)
    {
        if (!_ladderSystem.TryStartClimb(ladder, npcUid) || ladder.Comp.UseDelay is not { } delay)
            return;

        EnsureComp<KsNpcLadderRouteComponent>(npcUid).ClimbingUntil = _gameTiming.CurTime + delay + ClimbMargin;
    }

    /// <summary>
    ///     Forgets the way <paramref name="npcUid"/> was going: its move is over.
    /// </summary>
    public void Clear(EntityUid npcUid)
    {
        RemComp<KsNpcLadderRouteComponent>(npcUid);
    }

    /// <summary>
    ///     The nearest ladder on <paramref name="fromMapUid"/> that begins a way to <paramref name="toMapUid"/> by the
    ///         fewest ladders.
    /// </summary>
    private bool TryFindLadder(EntityUid fromMapUid, EntityUid toMapUid, Vector2 fromPosition, out Entity<KsLadderComponent> ladder)
    {
        ladder = default;
        _links.Clear();

        var query = EntityQueryEnumerator<KsLadderComponent, TransformComponent>();
        while (query.MoveNext(out var ladderUid, out var ladderComponent, out var ladderTransform))
        {
            if (ladderTransform.MapUid is not { } mapUid ||
                !_ladderSystem.TryGetDestination((ladderUid, ladderComponent), out var destination) ||
                _transformSystem.GetMap(destination) is not { } linkedMapUid ||
                linkedMapUid == mapUid)
                continue;

            _links.Add(((ladderUid, ladderComponent), mapUid, linkedMapUid));
        }

        // How many ladders each map is from the destination's, outwards from it until this map is reached.
        _ladderCounts.Clear();
        _frontier.Clear();
        _ladderCounts[toMapUid] = 0;
        _frontier.Enqueue(toMapUid);

        while (_frontier.TryDequeue(out var currentMapUid) && currentMapUid != fromMapUid)
        {
            var count = _ladderCounts[currentMapUid] + 1;

            foreach (var link in _links)
            {
                if (link.ToMapUid == currentMapUid && _ladderCounts.TryAdd(link.FromMapUid, count))
                    _frontier.Enqueue(link.FromMapUid);
            }
        }

        if (!_ladderCounts.TryGetValue(fromMapUid, out var ladders))
            return false;

        var found = false;
        var nearest = float.MaxValue;

        foreach (var link in _links)
        {
            if (link.FromMapUid != fromMapUid ||
                !_ladderCounts.TryGetValue(link.ToMapUid, out var after) ||
                after != ladders - 1)
                continue;

            var distance = (_transformSystem.GetWorldPosition(link.Ladder.Owner) - fromPosition).LengthSquared();
            if (distance >= nearest)
                continue;

            nearest = distance;
            ladder = link.Ladder;
            found = true;
        }

        return found;
    }
}
