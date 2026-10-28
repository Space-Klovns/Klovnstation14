using Content.Shared._KS14.Fluids.Components;
using Content.Shared._KS14.TileEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Timing;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsTileEffectSystem _tileEffectSystem = default!;

    private static readonly TimeSpan TileEffectUpdateInterval = TimeSpan.FromSeconds(0.5d);
    // Not static: every server in the process (the integration test pool runs several) has its own clock, and a
    //     shared next-update time set by one held up the others' tile effects until their clocks caught up.
    private TimeSpan _nextTileEffectUpdate = TimeSpan.MinValue;

    private void InitialiseKlovn()
    {
        InitialisePuddleTileCache();

        Subs.CVar(_configurationManager, Content.Shared._KS14.CCVar.KsCCVars.AtmosPuddleEmissivity,
            value => _puddleEmissivity = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, Content.Shared._KS14.CCVar.KsCCVars.AtmosFlameSpreadRate,
            value => _flameSpreadRate = value, invokeImmediately: true);

        SubscribeLocalEvent<PuddleComponent, MapInitEvent>(OnPuddleMapInit);
    }

    private void OnPuddleMapInit(Entity<PuddleComponent> entity, ref MapInitEvent args)
    {
        entity.Comp.LastTileEffectUpdate = _gameTiming.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateAirHeatExchange(); // KS14
        ResolvePendingPuddleMerges();

        var curTime = _gameTiming.CurTime;
        if (curTime < _nextTileEffectUpdate)
            return;

        _nextTileEffectUpdate = curTime + TileEffectUpdateInterval;

        UpdateFlameSpread();

        var eqe = EntityQueryEnumerator<TileEffectPuddleComponent, PuddleComponent>();
        while (eqe.MoveNext(out var uid, out _, out var puddleComponent))
            TryUpdateTileEffects((uid, puddleComponent), curTime);
    }

    public void TryUpdateTileEffects(Entity<PuddleComponent> entity, TimeSpan curTime, bool careAboutTime = true)
    {
        if (entity.Comp.Solution is not { } solutionEntity)
            return;

        var deltaTime = TimeSpan.Zero;
        if (careAboutTime)
        {
            deltaTime = curTime - entity.Comp.LastTileEffectUpdate;
            if (deltaTime < TimeSpan.Zero)
                return;
        }

        var solution = solutionEntity.Comp.Solution;
        var scale = careAboutTime ? (float)deltaTime.TotalSeconds : 1f;
        entity.Comp.LastTileEffectUpdate = curTime;

        // On the schedule, the next update is an interval away, and can land a tick late: the interval is no whole number
        //     of ticks. One more tick, so an effect that expires then isn't left to the order the systems update in.
        var refreshedWithin = careAboutTime ? TileEffectUpdateInterval + _gameTiming.TickPeriod * 2 : (TimeSpan?)null;

        if (!_tileEffectSystem.TryUpdateTileEffects(entity.Owner, null, solution, scale: scale,
                flameTemperature: GetFlameTemperature(entity.Owner), refreshedWithin: refreshedWithin))
            return;

        if (solution.Volume == FixedPoint2.Zero)
        {
            QueueDel(entity);
            return;
        }

        _solutionContainerSystem.UpdateChemicals(solutionEntity);
        Dirty(solutionEntity);
    }

    /// <summary>
    ///     The hottest open flame in the puddle: something hot lying in it, or fire spreading from a burning tile nearby.
    /// </summary>
    private float? GetFlameTemperature(EntityUid puddleUid)
    {
        var itemFlame = _ksHotPuddleSystem.GetFlameTemperature(puddleUid);
        var spreadFlame = GetSpreadFlameTemperature(puddleUid);
        if (itemFlame is not { } item)
            return spreadFlame;

        return spreadFlame is { } spread ? MathF.Max(item, spread) : item;
    }
}
