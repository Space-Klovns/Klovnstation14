using Content.Server._KS14.StationEvents.Components;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared.Light.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.AutomaticNightshift;

// This is ass

public sealed class AutomaticNightshiftSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(15d);
    private TimeSpan _nextUpdate = TimeSpan.MinValue;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + UpdateInterval;
        var eqe = EntityQueryEnumerator<AutomaticNightshiftComponent, LightCycleComponent>();
        while (eqe.MoveNext(out var uid, out var autoComponent, out var lightCycleComponent))
        {
            if (_gameTiming.CurTime < autoComponent.NextAllowedStart)
                continue;

            if (!TryComp<MapComponent>(uid, out var mapComponent) ||
                _stationSystem.GetStationInMap(mapComponent.MapId) is not { } stationUid)
                continue;

            var time = (float)_gameTiming.CurTime
                .Add(lightCycleComponent.Offset)
                .Subtract(_gameTicker.RoundStartTimeSpan)
                .TotalSeconds;

            var cycleDuration = (float)lightCycleComponent.Duration.TotalSeconds;
            var localTime = time % (float)cycleDuration;
            var localTimeFrac = localTime / cycleDuration;
            if (localTimeFrac < autoComponent.StartTimeFraction)
                continue;

            var deoffset = localTime - (autoComponent.StartTimeFraction * cycleDuration);
            autoComponent.NextAllowedStart = TimeSpan.FromSeconds(_gameTiming.CurTime.TotalSeconds - deoffset + cycleDuration); // exact time next day

            var ruleUid = _gameTicker.AddGameRule(autoComponent.Proto);
            var nightshiftComponent = Comp<NightshiftRuleComponent>(ruleUid);
            nightshiftComponent.StationUid = stationUid;

            _gameTicker.StartGameRule(ruleUid);
        }
    }
}
