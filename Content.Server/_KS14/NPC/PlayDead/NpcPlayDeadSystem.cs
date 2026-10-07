using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.PlayDead;

/// <summary>
///     Decides when an NPC with <see cref="NpcPlayDeadComponent"/> plays dead, and when it stops. It starts, on a
///         chance, once its caution is high and it has nobody left - no squad, or a squad of one - and stops the
///         moment it sees a hostile, or after lying there long enough. HTN does the lying down and getting up.
/// </summary>
public sealed partial class NpcPlayDeadSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [Dependency] private EntityQuery<NpcPlayDeadComponent> _playDeadQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private static readonly List<NpcContactState> SeenStates = [NpcContactState.Visible];

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;

        var enumerator = EntityQueryEnumerator<NpcPlayDeadComponent, ActiveNPCComponent>();
        while (enumerator.MoveNext(out var uid, out var playDeadComponent, out _))
        {
            UpdateNow((uid, playDeadComponent), now);
        }
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> should be playing dead right now.
    /// </summary>
    public bool IsPlayingDead(EntityUid uid)
    {
        return _playDeadQuery.TryComp(uid, out var playDeadComponent) && playDeadComponent.Playing;
    }

    /// <summary>
    ///     Runs one decision for <paramref name="uid"/> right now, whether one is due or not, awake or not. For tests.
    /// </summary>
    internal void UpdateNow(EntityUid uid)
    {
        if (_playDeadQuery.TryComp(uid, out var playDeadComponent))
            UpdateNow((uid, playDeadComponent), _gameTiming.CurTime);
    }

    private void UpdateNow(Entity<NpcPlayDeadComponent> entity, TimeSpan now)
    {
        if (!_mobStateSystem.IsAlive(entity.Owner))
        {
            SetPlaying(entity, false, now);
            return;
        }

        if (entity.Comp.Playing)
        {
            // Someone has come into view - get up and take them by surprise - or nobody is coming.
            if (_npcPerceptionSystem.HasContact(entity.Owner, SeenStates, maxAge: null) ||
                now - entity.Comp.PlayingSince >= entity.Comp.MaxDuration)
                SetPlaying(entity, false, now);

            return;
        }

        if (_npcMeterSystem.GetValue(entity.Owner, entity.Comp.Meter) < entity.Comp.Threshold)
        {
            // Calmed down: next time it gets this bad is a fresh roll.
            entity.Comp.Rolled = false;
            return;
        }

        // Not with a hostile in plain view - it would only be up again at once. The roll waits until there is none.
        if (entity.Comp.Rolled ||
            !IsAlone(entity.Owner) ||
            _npcPerceptionSystem.HasContact(entity.Owner, SeenStates, maxAge: null))
            return;

        entity.Comp.Rolled = true;

        if (_robustRandom.Prob(entity.Comp.Chance))
            SetPlaying(entity, true, now);
    }

    /// <summary>
    ///     No squad, or a squad with nobody else left in it.
    /// </summary>
    private bool IsAlone(EntityUid uid)
    {
        return !_npcSquadSystem.TryGetSquad(uid, out var squadEntity) || squadEntity.Value.Comp.Members.Count <= 1;
    }

    private void SetPlaying(Entity<NpcPlayDeadComponent> entity, bool playing, TimeSpan now)
    {
        if (entity.Comp.Playing == playing)
            return;

        entity.Comp.Playing = playing;
        entity.Comp.PlayingSince = now;
        _npcSensorSystem.RequestReplan(entity.Owner);
    }
}
