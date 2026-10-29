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

    /// <summary>
    ///     <see cref="Content.Shared._KS14.CCVar.KsCCVars.AtmosPuddleTileEffectInterval"/>.
    /// </summary>
    private TimeSpan _tileEffectUpdateInterval = TimeSpan.FromSeconds(0.25d);

    /// <summary>
    ///     Never shorter than this, whatever the cvar says: below it, every puddle would update every tick.
    /// </summary>
    private static readonly TimeSpan MinimumTileEffectUpdateInterval = TimeSpan.FromSeconds(0.05d);

    /// <summary>
    ///     Set while the scheduled pass runs every puddle's tile effects. Only then is the flame spreading from nearby
    ///         fires up to date: it is worked out at the start of the pass, and a run outside it (a spill, an explosion's
    ///         spark) would otherwise read the last pass's, from fires that may have gone out since.
    /// </summary>
    private bool _runningScheduledTileEffects;
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
        Subs.CVar(_configurationManager, Content.Shared._KS14.CCVar.KsCCVars.AtmosPuddleTileEffectInterval,
            value => _tileEffectUpdateInterval = TimeSpan.FromSeconds(Math.Max(value, MinimumTileEffectUpdateInterval.TotalSeconds)),
            invokeImmediately: true);

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

        _nextTileEffectUpdate = curTime + _tileEffectUpdateInterval;

        UpdateFlameSpread(curTime);

        _runningScheduledTileEffects = true;
        try
        {
            var eqe = EntityQueryEnumerator<TileEffectPuddleComponent, PuddleComponent>();
            while (eqe.MoveNext(out var uid, out _, out var puddleComponent))
                TryUpdateTileEffects((uid, puddleComponent), curTime);
        }
        finally
        {
            _runningScheduledTileEffects = false;
        }
    }

    /// <param name="flameTemperature">
    ///     An open flame in the puddle for this one update, such as an explosion passing over it, on top of any it already
    ///         has (<see cref="GetFlameTemperature"/>).
    /// </param>
    public void TryUpdateTileEffects(Entity<PuddleComponent> entity, TimeSpan curTime, bool careAboutTime = true, float? flameTemperature = null)
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
        var refreshedWithin = careAboutTime ? _tileEffectUpdateInterval + _gameTiming.TickPeriod * 2 : (TimeSpan?)null;

        if (!_tileEffectSystem.TryUpdateTileEffects(entity.Owner, null, solution, scale: scale,
                flameTemperature: MaxFlame(GetFlameTemperature(entity.Owner), flameTemperature), refreshedWithin: refreshedWithin))
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
    ///     The hottest open flame in the puddle: something hot lying in it, or, during the scheduled pass, fire spreading
    ///         from a burning tile nearby (<see cref="_runningScheduledTileEffects"/>).
    /// </summary>
    private float? GetFlameTemperature(EntityUid puddleUid)
    {
        var hotThingTemperature = _ksHotPuddleSystem.GetFlameTemperature(puddleUid);
        return _runningScheduledTileEffects
            ? MaxFlame(hotThingTemperature, GetSpreadFlameTemperature(puddleUid))
            : hotThingTemperature;
    }

    private static float? MaxFlame(float? a, float? b)
    {
        if (a is not { } first)
            return b;

        return b is { } second ? MathF.Max(first, second) : first;
    }

    /// <summary>
    ///     Runs the puddle's tile effects now, with an open flame of this temperature in it, so whatever a flame lights
    ///         catches at once. It stands in for the puddle's next scheduled update: it covers the time since the last
    ///         one, as that would have.
    /// </summary>
    public void KsSparkPuddle(EntityUid puddleUid, float flameTemperature)
    {
        if (!TryComp(puddleUid, out PuddleComponent? puddleComponent))
            return;

        TryUpdateTileEffects((puddleUid, puddleComponent), _gameTiming.CurTime, flameTemperature: flameTemperature);
    }
}
