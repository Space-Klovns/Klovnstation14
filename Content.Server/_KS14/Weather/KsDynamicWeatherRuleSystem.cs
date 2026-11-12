using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Weather;
using Content.Shared.GameTicking.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Weather;

/// <summary>
///     Runs <see cref="KsDynamicWeatherRuleComponent"/>: walks its weather from state to state for as long as the rule
///         is active, fading one weather out as the next fades in.
/// </summary>
public sealed partial class KsDynamicWeatherRuleSystem : GameRuleSystem<KsDynamicWeatherRuleComponent>
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private WeatherSystem _weatherSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;

    protected override void ActiveTick(EntityUid uid, KsDynamicWeatherRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        // Waits for the round's map.
        if (component.MapUid == null)
        {
            if (!_mapSystem.TryGetMap(GameTicker.DefaultMap, out var defaultMapUid))
                return;

            component.MapUid = defaultMapUid;
        }

        if (component.StateIndex < 0)
        {
            EnterState((uid, component), PickInitialState(component));
            return;
        }

        if (_gameTiming.CurTime >= component.NextChange)
            EnterState((uid, component), PickNextState(component));
    }

    protected override void Ended(EntityUid uid, KsDynamicWeatherRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        // Clears up, rather than leaving the last weather on for good.
        if (component.MapUid is { } mapUid)
            ApplyWeathers(mapUid, []);
    }

    /// <summary>
    ///     Moves the weather to another map, starting over from a state picked as at the start of a round.
    /// </summary>
    public void SetMap(Entity<KsDynamicWeatherRuleComponent> rule, EntityUid mapUid)
    {
        if (rule.Comp.MapUid is { } oldMapUid && oldMapUid != mapUid)
            ApplyWeathers(oldMapUid, []);

        rule.Comp.MapUid = mapUid;
        rule.Comp.StateIndex = -1;
    }

    /// <summary>
    ///     Changes the weather to the state named <paramref name="stateName"/> now. False if there is no such state.
    /// </summary>
    public bool TrySetState(Entity<KsDynamicWeatherRuleComponent> rule, string stateName)
    {
        if (FindState(rule.Comp, stateName) is not { } stateIndex)
            return false;

        EnterState(rule, stateIndex);
        return true;
    }

    private void EnterState(Entity<KsDynamicWeatherRuleComponent> rule, int stateIndex)
    {
        if (stateIndex < 0 || stateIndex >= rule.Comp.States.Count || rule.Comp.MapUid is not { } mapUid)
            return;

        var state = rule.Comp.States[stateIndex];
        rule.Comp.StateIndex = stateIndex;

        var duration = state.MaxDuration > state.MinDuration
            ? _robustRandom.Next(state.MinDuration, state.MaxDuration)
            : state.MinDuration;
        rule.Comp.NextChange = _gameTiming.CurTime + duration;

        ApplyWeathers(mapUid, state.Weathers);
    }

    /// <summary>
    ///     Leaves exactly <paramref name="weathers"/> on the map: any other weather fades out, and each of these is put on,
    ///         or kept, with no end of its own. The rule decides when it ends.
    /// </summary>
    private void ApplyWeathers(EntityUid mapUid, List<EntProtoId> weathers)
    {
        if (_statusEffectsSystem.TryEffectsWithComp<WeatherStatusEffectComponent>(mapUid, out var current))
        {
            foreach (var effect in current)
            {
                if (Prototype(effect.Owner) is { } effectPrototype && !weathers.Contains(effectPrototype.ID))
                    _weatherSystem.TryRemoveWeather(mapUid, effectPrototype.ID);
            }
        }

        foreach (var weather in weathers)
            _weatherSystem.TryAddWeather(mapUid, weather, out _, duration: null);
    }

    private int PickInitialState(KsDynamicWeatherRuleComponent component)
    {
        if (component.InitialState is { } initialState && FindState(component, initialState) is { } initialIndex)
            return initialIndex;

        return PickByWeight(component, exceptIndex: -1);
    }

    private int PickNextState(KsDynamicWeatherRuleComponent component)
    {
        var state = component.States[component.StateIndex];
        if (state.Next.Count == 0)
            return PickByWeight(component, exceptIndex: component.StateIndex);

        var totalWeight = 0f;
        foreach (var weight in state.Next.Values)
            totalWeight += MathF.Max(0f, weight);

        var roll = _robustRandom.NextFloat() * totalWeight;
        foreach (var (name, weight) in state.Next)
        {
            roll -= MathF.Max(0f, weight);
            if (roll <= 0f && FindState(component, name) is { } nextIndex)
                return nextIndex;
        }

        // Every weight was zero, or named nothing: stay as it is for another while.
        return component.StateIndex;
    }

    /// <summary>
    ///     A state by <see cref="KsWeatherState.Weight"/>, other than <paramref name="exceptIndex"/> unless it's the only
    ///         one.
    /// </summary>
    private int PickByWeight(KsDynamicWeatherRuleComponent component, int exceptIndex)
    {
        var totalWeight = 0f;
        for (var i = 0; i < component.States.Count; i++)
        {
            if (i != exceptIndex)
                totalWeight += MathF.Max(0f, component.States[i].Weight);
        }

        if (totalWeight <= 0f)
            return Math.Max(0, exceptIndex);

        var roll = _robustRandom.NextFloat() * totalWeight;
        for (var i = 0; i < component.States.Count; i++)
        {
            if (i == exceptIndex)
                continue;

            roll -= MathF.Max(0f, component.States[i].Weight);
            if (roll <= 0f)
                return i;
        }

        return component.States.Count - 1;
    }

    private static int? FindState(KsDynamicWeatherRuleComponent component, string name)
    {
        for (var i = 0; i < component.States.Count; i++)
        {
            if (string.Equals(component.States[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    /// <summary>
    ///     Every dynamic weather running now, for the admin command.
    /// </summary>
    public List<Entity<KsDynamicWeatherRuleComponent>> GetActiveRules()
    {
        var rules = new List<Entity<KsDynamicWeatherRuleComponent>>();
        var query = QueryActiveRules();
        while (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
            rules.Add((ruleUid, ruleComponent));

        return rules;
    }
}
