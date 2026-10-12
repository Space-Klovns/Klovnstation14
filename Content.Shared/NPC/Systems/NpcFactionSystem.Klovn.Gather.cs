// KS14: added in this fork
using System.Numerics;
using Content.Shared.NPC.Components;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    private readonly HashSet<Entity<NpcFactionMemberComponent>> _klovnNearbyMembers = new();

    /// <summary>
    ///     <see cref="GetNearbyHostiles(Entity{NpcFactionMemberComponent?, FactionExceptionComponent?}, float)"/>,
    ///         filling <paramref name="results"/> instead of building a chain of enumerators, for callers that run
    ///         it several times a second. Contained entities are left out, and so is anything in the aggro set that
    ///         is out of range, which the original does not range-check.
    /// </summary>
    /// <remarks>
    ///     <paramref name="results"/> is not cleared first.
    /// </remarks>
    public void GetNearbyHostiles(Entity<NpcFactionMemberComponent?, FactionExceptionComponent?> entity,
        float range,
        HashSet<EntityUid> results)
    {
        if (!Resolve(entity, ref entity.Comp1, false))
            return;

        var mapCoordinates = _xform.GetMapCoordinates(entity.Owner);

        // A box and a distance check rather than GetEntitiesInRange, which builds a new circle shape for every call.
        _klovnNearbyMembers.Clear();
        _lookup.GetEntitiesIntersecting(mapCoordinates.MapId,
            Box2.CenteredAround(mapCoordinates.Position, new Vector2(range * 2f, range * 2f)),
            _klovnNearbyMembers,
            LookupFlags.Approximate | LookupFlags.Uncontained);

        Resolve(entity, ref entity.Comp2, false);

        foreach (var member in _klovnNearbyMembers)
        {
            // The same tests as GetNearbyFactions and IsEntityFriendly, without HashSet.Overlaps: it takes an
            //      IEnumerable, so it boxes the other set's enumerator on every call.
            if (member.Owner == entity.Owner ||
                (_xform.GetMapCoordinates(member.Owner).Position - mapCoordinates.Position).LengthSquared() > range * range ||
                !AnyIn(member.Comp.Factions, entity.Comp1.HostileFactions) ||
                AnyIn(member.Comp.Factions, entity.Comp1.Factions) ||
                AnyIn(member.Comp.Factions, entity.Comp1.FriendlyFactions) ||
                entity.Comp2 != null && entity.Comp2.Ignored.Contains(member.Owner))
                continue;

            results.Add(member.Owner);
        }

        if (entity.Comp2 == null)
            return;

        foreach (var hostileUid in entity.Comp2.Hostiles)
        {
            if (hostileUid == entity.Owner || entity.Comp2.Ignored.Contains(hostileUid) || TerminatingOrDeleted(hostileUid))
                continue;

            var hostileCoordinates = _xform.GetMapCoordinates(hostileUid);
            if (hostileCoordinates.MapId != mapCoordinates.MapId ||
                (hostileCoordinates.Position - mapCoordinates.Position).LengthSquared() > range * range)
                continue;

            results.Add(hostileUid);
        }
    }

    private static bool AnyIn<T>(HashSet<T> items, HashSet<T> set)
    {
        foreach (var item in items)
        {
            if (set.Contains(item))
                return true;
        }

        return false;
    }
}
