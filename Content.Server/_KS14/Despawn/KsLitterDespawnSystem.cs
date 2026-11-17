using Content.Server._KS14.Dissolve;
using Content.Server._KS14.FieldCommander;
using Content.Server.GameTicking.Rules;
using Content.Shared._KS14.Despawn;
using Content.Shared.GameTicking.Components;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Placeable;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Despawn;

/// <summary>
///     Tidies a battlefield while a <see cref="KsLitterDespawnRuleComponent"/> is active: starts a timer on spent
///         casings, loose guns and magazines and dead Field Commander units, and dissolves them when it runs out.
/// </summary>
public sealed partial class KsLitterDespawnSystem : GameRuleSystem<KsLitterDespawnRuleComponent>
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsDissolveSystem _dissolveSystem = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsLitterTimerComponent> _timerQuery = default!;
    [Dependency] private EntityQuery<ItemComponent> _itemQuery = default!;
    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;
    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticQuery = default!;
    [Dependency] private EntityQuery<MagazineAmmoProviderComponent> _magazineQuery = default!;
    [Dependency] private EntityQuery<CartridgeAmmoComponent> _cartridgeQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] private EntityQuery<PlaceableSurfaceComponent> _surfaceQuery = default!;
    [Dependency] private EntityQuery<KsFieldCommanderUnitComponent> _unitQuery = default!;
    [Dependency] private EntityQuery<ActorComponent> _actorQuery = default!;

    protected override void ActiveTick(EntityUid uid, KsLitterDespawnRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        var curTime = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<KsLitterTimerComponent>();
        while (enumerator.MoveNext(out var timerUid, out var timerComponent))
        {
            // One taken off already this tick is shut down, but still stored.
            if (curTime < timerComponent.DissolveAt || timerComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            if (timerComponent.Kind != KsLitterKind.Body)
            {
                // Lying on a table or a rack, where it was put to be kept. Looked at again later rather than
                //      forgotten: dragged off again, no change of parent says so.
                if (IsLoose(timerUid) && IsOnSurface(timerUid))
                {
                    timerComponent.DissolveAt = curTime + timerComponent.Delay;
                    continue;
                }

                // Picked up since the timer started, by something that left it on: nothing to tidy.
                if (!IsLoose(timerUid))
                {
                    RemCompDeferred<KsLitterTimerComponent>(timerUid);
                    continue;
                }
            }

            RemCompDeferred<KsLitterTimerComponent>(timerUid);
            _dissolveSystem.Dissolve(timerUid, timerComponent.DissolveDuration);
        }
    }

    /// <summary>
    ///     The last litter rule to end takes its timers with it: nothing is left counting down to a tidy-up that will
    ///         never come, or waiting to dissolve the moment another rule starts.
    /// </summary>
    protected override void Ended(EntityUid uid, KsLitterDespawnRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        // Another one still running keeps them: this one is no longer counted as active by now.
        if (TryGetActiveRule(out _))
            return;

        var timerEnumerator = AllEntityQuery<KsLitterTimerComponent>();
        while (timerEnumerator.MoveNext(out var timerUid, out _))
        {
            RemCompDeferred<KsLitterTimerComponent>(timerUid);
        }
    }

    [SubscribeLocalEvent]
    private void OnCartridgeEjected(ref KsCartridgeEjectedEvent args)
    {
        if (!_cartridgeQuery.TryComp(args.Cartridge, out var cartridgeComponent) ||
            !cartridgeComponent.Spent ||
            !TryGetActiveRule(out var rule))
            return;

        StartTimer(args.Cartridge, KsLitterKind.Casing, rule.Comp.CasingDelay, rule.Comp);
    }

    /// <summary>
    ///     A gun or magazine hits the floor (a new parent that is a grid) and its timer starts; any other parent, a
    ///         hand or a bag, takes the timer off it again.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnParentChanged(ref EntParentChangedMessage args)
    {
        var uid = args.Entity;
        // Only something that can be carried is litter: a mob or a machine with a gun of its own (a hivebot, a mech,
        //      a turret off its mount) lies on the floor all its life, and is not to dissolve for it.
        var isLitterItem = _itemQuery.HasComp(uid) &&
            (_gunQuery.HasComp(uid) || _ballisticQuery.HasComp(uid) || _magazineQuery.HasComp(uid));
        var hasTimer = _timerQuery.TryComp(uid, out var timerComponent) && timerComponent.Kind == KsLitterKind.Item;

        if (!isLitterItem && !hasTimer)
            return;

        if (IsFloorParent(args.Transform.ParentUid) && !args.Transform.Anchored)
        {
            if (isLitterItem && TryGetActiveRule(out var rule))
                StartTimer(uid, KsLitterKind.Item, rule.Comp.ItemDelay, rule.Comp);

            return;
        }

        if (hasTimer)
            RemCompDeferred<KsLitterTimerComponent>(uid);
    }

    /// <summary>
    ///     By-value, as the event is (<c>RA0054</c> only allows <c>Entity&lt;T&gt;</c> handlers for <c>ref</c> events),
    ///         and broadcast, as no subscription for it exists on <c>KsFieldCommanderUnitComponent</c> to share.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (!_unitQuery.HasComp(args.Target) || _actorQuery.HasComp(args.Target))
            return;

        if (args.NewMobState == MobState.Dead)
        {
            if (TryGetActiveRule(out var rule))
                StartTimer(args.Target, KsLitterKind.Body, rule.Comp.BodyDelay, rule.Comp);

            return;
        }

        if (_timerQuery.TryComp(args.Target, out var timerComponent) && timerComponent.Kind == KsLitterKind.Body)
            RemCompDeferred<KsLitterTimerComponent>(args.Target);
    }

    private void StartTimer(EntityUid uid, KsLitterKind kind, TimeSpan delay, KsLitterDespawnRuleComponent rule)
    {
        var timerComponent = EnsureComp<KsLitterTimerComponent>(uid);
        timerComponent.Kind = kind;
        timerComponent.Delay = delay;
        timerComponent.DissolveAt = _gameTiming.CurTime + delay;
        timerComponent.DissolveDuration = rule.DissolveDuration;
    }

    private bool TryGetActiveRule(out Entity<KsLitterDespawnRuleComponent> rule)
    {
        var query = QueryActiveRules();
        if (query.MoveNext(out var ruleUid, out _, out var ruleComponent, out _))
        {
            rule = (ruleUid, ruleComponent);
            return true;
        }

        rule = default;
        return false;
    }

    /// <summary>
    ///     Whether the parent is the ground: a grid, or a map when there is no grid.
    /// </summary>
    private bool IsFloorParent(EntityUid parentUid)
        => _gridQuery.HasComp(parentUid) || _mapQuery.HasComp(parentUid);

    private bool IsLoose(EntityUid uid)
    {
        var transformComponent = Transform(uid);
        return !transformComponent.Anchored && IsFloorParent(transformComponent.ParentUid);
    }

    /// <summary>
    ///     Whether something that things can be put on, a table or a rack, is anchored on the tile it lies on.
    /// </summary>
    private bool IsOnSurface(EntityUid uid)
    {
        var transformComponent = Transform(uid);
        if (transformComponent.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var gridComponent))
            return false;

        var anchoredEnumerator = _mapSystem.GetAnchoredEntities((gridUid, gridComponent), _transformSystem.GetMapCoordinates(uid, transformComponent));
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (_surfaceQuery.HasComp(anchoredUid))
                return true;
        }

        return false;
    }
}
