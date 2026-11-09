using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Recovery;

/// <summary>
///     Decides how long an NPC with <see cref="NpcRecoveryComponent"/> waits for its meds to work. It starts when the NPC
///         uses them, and ends once it has its health back, after long enough, or if it goes down. It ends for good: an
///         NPC that has recovered and takes one more scratch goes on fighting, rather than running back into hiding.
///         HTN does the waiting.
/// </summary>
public sealed partial class NpcRecoverySystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [Dependency] private EntityQuery<NpcRecoveringComponent> _recoveringQuery = default!;
    [Dependency] private EntityQuery<NpcRecoveryComponent> _recoveryQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        if (!NpcThrottle.TryConsume(ref _nextUpdate, now, UpdateInterval))
            return;

        UpdateNow(now);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is waiting for its meds to work.
    /// </summary>
    public bool IsRecovering(EntityUid uid)
    {
        return _recoveringQuery.HasComp(uid);
    }

    /// <summary>
    ///     Starts <paramref name="entity"/> waiting for its meds to work, or restarts the wait if it already is. Does
    ///         nothing for an NPC without <see cref="NpcRecoveryComponent"/>.
    /// </summary>
    public void StartRecovery(Entity<NpcRecoveryComponent?> entity)
    {
        if (!_recoveryQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        EnsureComp<NpcRecoveringComponent>(entity.Owner).EndTime = _gameTiming.CurTime + entity.Comp.MaxDuration;
    }

    /// <summary>
    ///     Runs one update right now, whether one is due or not. For tests.
    /// </summary>
    internal void UpdateNow()
    {
        UpdateNow(_gameTiming.CurTime);
    }

    private void UpdateNow(TimeSpan now)
    {
        var enumerator = EntityQueryEnumerator<NpcRecoveringComponent, NpcRecoveryComponent>();
        while (enumerator.MoveNext(out var uid, out var recoveringComponent, out var recoveryComponent))
        {
            if (now < recoveringComponent.EndTime &&
                _mobStateSystem.IsAlive(uid) &&
                _npcSquadSystem.GetHealthFraction(uid) < recoveryComponent.RecoveredFraction)
                continue;

            RemCompDeferred<NpcRecoveringComponent>(uid);
            _npcSensorSystem.RequestReplan(uid);
        }
    }
}
