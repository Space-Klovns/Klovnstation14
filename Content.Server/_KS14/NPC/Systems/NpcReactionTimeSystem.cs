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
    [Dependency] private NpcLightDetectionSystem _npcLightDetectionSystem = default!;

    [Dependency] private EntityQuery<NpcReactionTimeComponent> _reactionTimeQuery = default!;

    private readonly List<EntityUid> _scratchForgotten = new();

    /// <summary>
    ///     Records that <paramref name="npcUid"/> can see <paramref name="targetUid"/> now, and returns whether
    ///         it may react to it: always if <paramref name="alert"/> or the NPC has no reaction time, otherwise
    ///         once the target has been in sight for the NPC's reaction time - longer the dimmer the target is,
    ///         with light detection on. Once it has reacted to a target it keeps reacting, until it forgets it.
    /// </summary>
    public bool TrySeeAndReact(EntityUid npcUid, EntityUid targetUid, bool alert)
    {
        if (!_reactionTimeQuery.TryComp(npcUid, out var reactionTimeComponent))
            return true;

        var now = _gameTiming.CurTime;
        Forget(reactionTimeComponent, now);

        if (!reactionTimeComponent.Sightings.TryGetValue(targetUid, out var sighting))
            sighting = new NpcSighting(FirstSeen: now, LastSeen: now, Reacted: false);

        sighting.LastSeen = now;

        if (!sighting.Reacted)
            sighting.Reacted = alert || now - sighting.FirstSeen >= GetReactionTime((npcUid, reactionTimeComponent), targetUid);

        reactionTimeComponent.Sightings[targetUid] = sighting;
        return sighting.Reacted;
    }

    private TimeSpan GetReactionTime(Entity<NpcReactionTimeComponent> npcEntity, EntityUid targetUid)
    {
        var lightLevel = _npcLightDetectionSystem.GetPerceivedLightLevel(npcEntity.Owner, targetUid, npcEntity.Comp.DarknessProximityRange);
        var darkness = 1f - lightLevel;

        return npcEntity.Comp.ReactionTime * (1f + npcEntity.Comp.DarknessReactionScale * darkness);
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
