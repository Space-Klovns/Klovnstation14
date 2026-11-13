using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.NPC;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Pinning;

/// <summary>
///     The sense layer of being pinned down: every <see cref="NpcPinnableComponent.Interval"/>, asks whether an NPC stands in
///         anyone's recent line of fire (<see cref="NpcGunfireSystem.IsUnderFire"/>, hostile fire only) and raises its meter
///         if so. Time lowers it again (<see cref="NpcMeterPrototype.DecayPerSecond"/>), so an NPC is pinned for as long as
///         the fire keeps coming and a couple of seconds after.
/// </summary>
/// <remarks>
///     Read by the operative HTN through <c>MeterPrecondition</c>: <c>gun.yml</c> (go to real cover and hold it longer) and
///         <c>root.yml</c>'s support and peek branches (peek less). Lines of fire come from <see cref="NpcGunfireSystem"/>,
///         which is also what <c>DiveOperator</c> reads. Only NPCs that are awake and opted in are visited, a few times a
///         second each, spread across the interval at map init.
/// </remarks>
public sealed partial class NpcPinnedDownSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private NpcGunfireSystem _npcGunfireSystem = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<NpcPinnableComponent, ActiveNPCComponent>();

        while (enumerator.MoveNext(out var uid, out var pinnableComponent, out _))
        {
            if (!NpcThrottle.TryConsume(ref pinnableComponent.NextCheck, now, pinnableComponent.Interval))
                continue;

            Check((uid, pinnableComponent));
        }
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<NpcPinnableComponent> entity, ref MapInitEvent args)
    {
        // Spread across the interval, so a squad spawned together does not look on the same tick.
        entity.Comp.NextCheck = NpcThrottle.Spread(_gameTiming.CurTime, entity.Comp.Interval, _robustRandom);
    }

    /// <summary>
    ///     Looks right now, whether or not a check is due and whether or not the NPC is awake. For tests.
    /// </summary>
    internal void CheckNow(Entity<NpcPinnableComponent> entity)
    {
        Check(entity);
    }

    private void Check(Entity<NpcPinnableComponent> entity)
    {
        if (entity.Comp.Gain <= 0f ||
            !_npcGunfireSystem.IsUnderFire(entity.Owner, NpcFireSide.Hostile, entity.Comp.Clearance, entity.Comp.Memory))
            return;

        var before = _npcMeterSystem.GetValue(entity.Owner, entity.Comp.Meter);
        _npcMeterSystem.Add(entity.Owner, entity.Comp.Meter, entity.Comp.Gain);

        // Crossing the line is news to the planner; being above it already is not.
        if (before < entity.Comp.ReplanThreshold && _npcMeterSystem.GetValue(entity.Owner, entity.Comp.Meter) >= entity.Comp.ReplanThreshold)
            _npcSensorSystem.RequestReplan(entity.Owner);
    }
}
