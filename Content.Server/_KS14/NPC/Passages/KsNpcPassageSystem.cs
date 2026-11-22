using System.Numerics;
using Content.Shared._KS14.NPC.Passages;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Passages;

/// <summary>
///     Gets NPCs from one map to another by the passages that lead between them, ladders and portals for two: which
///         one to make for, and having it taken.
/// </summary>
/// <remarks>
///     To make something a passage:
///     <list type="number">
///         <item>Give its prototype a <see cref="KsNpcPassageComponent"/>, with the range it is taken from.</item>
///         <item>Handle <see cref="KsGetNpcPassageDestinationEvent"/> on it, saying where it comes out.</item>
///         <item>If walking into it is not what takes it, handle <see cref="KsNpcTakePassageEvent"/> and have the
///             NPC take it.</item>
///     </list>
///     <c>KsLadderNpcPassageSystem</c> does all three, <c>KsPortalNpcPassageSystem</c> the first two.
///     Deliberately not part of the navmesh. A path is found on one map as ever, to the nearest passage that leads
///         towards the destination's map, and from the far end of it again: see
///         <c>NPCSteeringSystem.KsSteerByPassage</c>. So the way taken is the fewest passages, not the shortest walk,
///         and a passage that can't be walked to is not passed over for one that can. One between two spots on one
///         map is never made for: nothing here takes a shortcut.
/// </remarks>
public sealed partial class KsNpcPassageSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsNpcPassageComponent> _passageQuery = default!;

    /// <summary>
    ///     How long an NPC with no way to its destination's map waits before looking again.
    /// </summary>
    private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(1);

    private readonly List<(Entity<KsNpcPassageComponent> Passage, EntityUid FromMapUid, EntityUid ToMapUid)> _passages = [];
    private readonly Dictionary<EntityUid, int> _passageCounts = [];
    private readonly Queue<EntityUid> _frontier = [];

    /// <summary>
    ///     Whether passages lead from one map to another, over however many maps between.
    /// </summary>
    public bool HasRoute(EntityUid fromMapUid, EntityUid toMapUid)
    {
        return TryFindPassage(fromMapUid, toMapUid, Vector2.Zero, out _);
    }

    /// <summary>
    ///     The passage <paramref name="npcUid"/> makes for to get to <paramref name="destinationMapUid"/>, kept from one
    ///         call to the next for as long as it still leads off its map.
    /// </summary>
    /// <param name="isNew">Whether this is another passage than the call before gave.</param>
    public bool TryGetPassage(
        EntityUid npcUid,
        TransformComponent npcTransform,
        EntityUid destinationMapUid,
        out Entity<KsNpcPassageComponent> passage,
        out bool isNew)
    {
        passage = default;
        isNew = false;

        if (npcTransform.MapUid is not { } npcMapUid)
            return false;

        var routeComponent = EnsureComp<KsNpcPassageRouteComponent>(npcUid);

        if (routeComponent.PassageUid is { } keptUid &&
            routeComponent.DestinationMapUid == destinationMapUid &&
            _passageQuery.TryComp(keptUid, out var keptComponent) &&
            TryGetMaps((keptUid, keptComponent), out var keptMapUid, out _) &&
            keptMapUid == npcMapUid)
        {
            passage = (keptUid, keptComponent);
            return true;
        }

        routeComponent.PassageUid = null;

        if (_gameTiming.CurTime < routeComponent.NextSearch)
            return false;

        routeComponent.DestinationMapUid = destinationMapUid;

        if (!TryFindPassage(npcMapUid, destinationMapUid, _transformSystem.GetWorldPosition(npcTransform), out passage))
        {
            routeComponent.NextSearch = _gameTiming.CurTime + SearchInterval;
            return false;
        }

        routeComponent.PassageUid = passage.Owner;
        isNew = true;
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> is standing still for a passage it is part way through taking.
    /// </summary>
    public bool IsWaiting(EntityUid npcUid)
    {
        return TryComp<KsNpcPassageRouteComponent>(npcUid, out var routeComponent) &&
            _gameTiming.CurTime < routeComponent.WaitingUntil;
    }

    /// <summary>
    ///     Has <paramref name="npcUid"/> take the passage it has come within range of.
    /// </summary>
    /// <returns>
    ///     Whether something took it up. If nothing did, the passage is one that is walked into, and the NPC is to
    ///         walk on.
    /// </returns>
    public bool Take(EntityUid npcUid, EntityUid passageUid)
    {
        var ev = new KsNpcTakePassageEvent(npcUid);
        RaiseLocalEvent(passageUid, ref ev);

        if (ev.Wait > TimeSpan.Zero)
            EnsureComp<KsNpcPassageRouteComponent>(npcUid).WaitingUntil = _gameTiming.CurTime + ev.Wait;

        return ev.Handled;
    }

    /// <summary>
    ///     Forgets the way <paramref name="npcUid"/> was going: it is on its destination's map, or its move is over.
    /// </summary>
    /// <returns>Whether it was going by a passage.</returns>
    public bool Clear(EntityUid npcUid)
    {
        return RemComp<KsNpcPassageRouteComponent>(npcUid);
    }

    /// <summary>
    ///     The map a passage is on and the other map it leads to, if it leads to another map.
    /// </summary>
    private bool TryGetMaps(Entity<KsNpcPassageComponent> passage, out EntityUid fromMapUid, out EntityUid toMapUid)
    {
        fromMapUid = default;
        toMapUid = default;

        if (passage.Comp.LifeStage > ComponentLifeStage.Running || Transform(passage).MapUid is not { } mapUid)
            return false;

        var ev = new KsGetNpcPassageDestinationEvent();
        RaiseLocalEvent(passage, ref ev);

        if (ev.Destination is not { } destination ||
            !_transformSystem.IsValid(destination) ||
            _transformSystem.GetMap(destination) is not { } otherMapUid ||
            otherMapUid == mapUid)
            return false;

        fromMapUid = mapUid;
        toMapUid = otherMapUid;
        return true;
    }

    /// <summary>
    ///     The nearest passage on <paramref name="fromMapUid"/> that begins a way to <paramref name="toMapUid"/> by the
    ///         fewest passages.
    /// </summary>
    private bool TryFindPassage(EntityUid fromMapUid, EntityUid toMapUid, Vector2 fromPosition, out Entity<KsNpcPassageComponent> passage)
    {
        passage = default;
        _passages.Clear();

        var query = EntityQueryEnumerator<KsNpcPassageComponent>();
        while (query.MoveNext(out var candidateUid, out var candidateComponent))
        {
            if (TryGetMaps((candidateUid, candidateComponent), out var mapUid, out var farMapUid))
                _passages.Add(((candidateUid, candidateComponent), mapUid, farMapUid));
        }

        // How many passages each map is from the destination's, outwards from it until this map is reached.
        _passageCounts.Clear();
        _frontier.Clear();
        _passageCounts[toMapUid] = 0;
        _frontier.Enqueue(toMapUid);

        while (_frontier.TryDequeue(out var currentMapUid) && currentMapUid != fromMapUid)
        {
            var count = _passageCounts[currentMapUid] + 1;

            foreach (var link in _passages)
            {
                if (link.ToMapUid == currentMapUid && _passageCounts.TryAdd(link.FromMapUid, count))
                    _frontier.Enqueue(link.FromMapUid);
            }
        }

        if (!_passageCounts.TryGetValue(fromMapUid, out var passages))
            return false;

        var found = false;
        var nearest = float.MaxValue;

        foreach (var link in _passages)
        {
            if (link.FromMapUid != fromMapUid ||
                !_passageCounts.TryGetValue(link.ToMapUid, out var after) ||
                after != passages - 1)
                continue;

            var distance = (_transformSystem.GetWorldPosition(link.Passage.Owner) - fromPosition).LengthSquared();
            if (distance >= nearest)
                continue;

            nearest = distance;
            passage = link.Passage;
            found = true;
        }

        return found;
    }
}
