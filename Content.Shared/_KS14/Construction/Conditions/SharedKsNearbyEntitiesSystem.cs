using Content.Shared.Tools.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Construction.Conditions;

/// <summary>
///     Checks <see cref="KsNearbyEntitiesCondition"/> against what item crafting would let the user use. Which
///         entities those are is decided by the server's construction system, so only the server checks it.
/// </summary>
public abstract partial class SharedKsNearbyEntitiesSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;

    public abstract bool AllPresent(EntityUid user, List<KsNearbyEntityEntry> entries);

    protected bool AllPresent(IEnumerable<EntityUid> nearby, List<KsNearbyEntityEntry> entries)
    {
        var nearbyUids = new HashSet<EntityUid>(nearby);
        foreach (var entry in entries)
        {
            var found = false;
            foreach (var nearbyUid in nearbyUids)
            {
                if (!Matches(nearbyUid, entry))
                    continue;

                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }

    private bool Matches(EntityUid uid, KsNearbyEntityEntry entry)
    {
        if (entry.Whitelist != null && !_whitelistSystem.IsWhitelistPass(entry.Whitelist, uid))
            return false;

        if (entry.Tool is { } tool && !_toolSystem.HasQuality(uid, tool.Id))
            return false;

        if (entry.Prototypes is not { } prototypes)
            return true;

        foreach (var wanted in prototypes)
        {
            if (ProtoMan.IsOrInheritsFrom(EntityManager, uid, wanted))
                return true;
        }

        return false;
    }
}
