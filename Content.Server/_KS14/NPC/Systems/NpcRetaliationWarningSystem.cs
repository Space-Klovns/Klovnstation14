using Content.Server._KS14.NPC.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Collections;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

public sealed class NpcRetaliationWarningSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private readonly NPCRetaliationSystem _retaliationSystem = default!;

    public const string SensorKey = "__Sensor__WarningRetaliation";

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var removed = new ValueList<EntityUid>();
        var eqe = EntityQueryEnumerator<NpcRetaliationWarningComponent>();
        while (eqe.MoveNext(out var sourceUid /* the issuer of the warning, duh */, out var component))
        {
            var ourMapPos = _transformSystem.GetMapCoordinates(sourceUid);
            foreach (var (warnedUid, time) in component.ExpiryTimes)
            {
                if (TerminatingOrDeleted(warnedUid))
                    goto removeIt;

                var otherMapPos = _transformSystem.GetMapCoordinates(warnedUid);
                if (ourMapPos.MapId != otherMapPos.MapId ||
                    (otherMapPos.Position - ourMapPos.Position).LengthSquared() > component.DistanceThresholdSq)
                {
                    goto removeIt;
                }

                if (_gameTiming.CurTime < time)
                    continue;

                if (!TryComp<NPCRetaliationComponent>(sourceUid, out var retaliationComponent))
                    goto removeIt;

                _retaliationSystem.TryRetaliate((sourceUid, retaliationComponent), warnedUid, tryWarn: false);
            removeIt:
                removed.Add(warnedUid);
            }

            foreach (var removedUid in removed)
                component.ExpiryTimes.Remove(removedUid);

            removed.Clear();
        }
    }

    public bool TryWarn(Entity<NpcRetaliationWarningComponent?> entity, EntityUid warnedUid, TimeSpan delay)
    {
        if (!Resolve(entity.Owner, ref entity.Comp) ||
            !entity.Comp.ExpiryTimes.TryAdd(warnedUid, _gameTiming.CurTime + delay))
            return false;

        _npcSensorSystem.AddEffect(entity.Owner, SensorKey, true /* dummy value */);
        return true;
    }
}
