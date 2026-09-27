using Content.Server._KS14.NPC.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
///     Tracks how long each NPC with <see cref="NpcReactionTimeComponent"/> has had each target in sight, and
///         says whether it has had long enough to react.
/// </summary>
public sealed partial class NpcReactionTimeSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    [Dependency] private EntityQuery<NpcReactionTimeComponent> _reactionTimeQuery = default!;

    private readonly List<EntityUid> _scratchForgotten = new();

    /// <summary>
    ///     Records that <paramref name="npcUid"/> can see <paramref name="targetUid"/> now, and returns whether
    ///         it may react to it: always if <paramref name="alert"/> or the NPC has no reaction time, otherwise
    ///         once the target has been in sight for the NPC's reaction time.
    /// </summary>
    public bool TrySeeAndReact(EntityUid npcUid, EntityUid targetUid, bool alert)
    {
        if (!_reactionTimeQuery.TryComp(npcUid, out var reactionTimeComponent))
            return true;

        var now = _gameTiming.CurTime;
        Forget(reactionTimeComponent, now);

        var firstSeen = reactionTimeComponent.Sightings.TryGetValue(targetUid, out var sighting)
            ? sighting.FirstSeen
            : now;

        reactionTimeComponent.Sightings[targetUid] = (firstSeen, now);

        return alert || now - firstSeen >= reactionTimeComponent.ReactionTime;
    }

    private void Forget(NpcReactionTimeComponent reactionTimeComponent, TimeSpan now)
    {
        _scratchForgotten.Clear();

        foreach (var (targetUid, sighting) in reactionTimeComponent.Sightings)
        {
            if (now - sighting.LastSeen > reactionTimeComponent.ForgetTime || TerminatingOrDeleted(targetUid))
                _scratchForgotten.Add(targetUid);
        }

        foreach (var targetUid in _scratchForgotten)
        {
            reactionTimeComponent.Sightings.Remove(targetUid);
        }
    }
}
