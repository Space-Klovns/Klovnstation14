using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Content.Server.StationEvents.Events;
using Content.Server._KS14.StationEvents.Components;
using Content.Shared.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Server.AlertLevel;
using Robust.Shared.Containers;
using Content.Shared.Light.EntitySystems;

namespace Content.Server._KS14.StationEvents.Events;

public sealed class NightshiftRule : StationEventSystem<NightshiftRuleComponent>
{
    [Dependency] private readonly PoweredLightSystem _poweredLightSystem = default!;
    [Dependency] private readonly LightBulbSystem _bulbSystem = default!;

    [Dependency] private readonly EntityQuery<StationMemberComponent> _stationMemberQuery = default!;
    [Dependency] private readonly EntityQuery<NightshiftBulbComponent> _nightshiftBulbQuery = default!;
    [Dependency] private readonly EntityQuery<NightshiftLightComponent> _nightshiftLightQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NightshiftLightComponent, EntRemovedFromContainerMessage>(OnRemoved, before: [typeof(SharedPoweredLightSystem)]);
        SubscribeLocalEvent<NightshiftLightComponent, EntInsertedIntoContainerMessage>(OnInserted, before: [typeof(SharedPoweredLightSystem)]);

        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);

        SubscribeLocalEvent<NightshiftLightComponent, ComponentShutdown>(OnNightshiftLightShutdown);
        SubscribeLocalEvent<NightshiftBulbComponent, ComponentShutdown>(OnNightshiftBulbShutdown);
    }

    private void OnRemoved(Entity<NightshiftLightComponent> light, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != SharedPoweredLightSystem.LightBulbContainer)
            return;

        RemComp<NightshiftBulbComponent>(args.Entity);
    }

    private void OnInserted(Entity<NightshiftLightComponent> light, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != SharedPoweredLightSystem.LightBulbContainer)
            return;

        var nightshiftLightComponent = AddComp<NightshiftBulbComponent>(args.Entity);
        nightshiftLightComponent.OwningRuleUid = light.Comp.OwningRuleUid;
        if (nightshiftLightComponent.OwningRuleUid is not { })
            return;

        var ruleComponent = Comp<NightshiftRuleComponent>(nightshiftLightComponent.OwningRuleUid!.Value);
        ruleComponent.Bulbs.Add(args.Entity);
        _bulbSystem.SetColor(args.Entity, ruleComponent.Color);
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent args)
    {
        var ruleQuery = EntityQueryEnumerator<NightshiftRuleComponent>();
        while (ruleQuery.MoveNext(out var ruleUid, out var ruleComponent))
        {
            if (args.Station != ruleComponent.StationUid)
                continue;

            if (ruleComponent.DangerousAlertLevels.Contains(args.AlertLevel))
            {
                if (ruleComponent.Enabled)
                    Disable((ruleUid, ruleComponent));
            }
            else if (!ruleComponent.Enabled)
                Enable((ruleUid, ruleComponent));
        }
    }

    private void OnNightshiftLightShutdown(Entity<NightshiftLightComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.OwningRuleUid is not { })
            return;

        Comp<NightshiftRuleComponent>(entity.Comp.OwningRuleUid.Value).Lights.Remove(entity);
        if (Terminating(entity))
            return;

        if (_poweredLightSystem.GetBulb(entity.Owner) is { } bulbUid)
            RemComp<NightshiftBulbComponent>(bulbUid);
    }

    private void OnNightshiftBulbShutdown(Entity<NightshiftBulbComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.OwningRuleUid is not { })
            return;

        Comp<NightshiftRuleComponent>(entity.Comp.OwningRuleUid.Value).Bulbs.Remove(entity);
        if (Terminating(entity))
            return;

        _bulbSystem.SetColor(entity.Owner, entity.Comp.OriginalColor);
        _poweredLightSystem.UpdateLight(Transform(entity.Owner).ParentUid);
    }

    private void Enable(Entity<NightshiftRuleComponent> ruleEntity)
    {
        ruleEntity.Comp.Enabled = true;
        var stationUid = ruleEntity.Comp.StationUid;
        var lightQuery = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();

        while (lightQuery.MoveNext(out var lightUid, out var poweredLightComponent, out var transformComponent))
        {
            // let's not arm the nuke if it isn't on station
            if (_stationMemberQuery.CompOrNull(transformComponent.ParentUid)?.Station != stationUid ||
                _poweredLightSystem.GetBulb(lightUid, poweredLightComponent) is not { } bulbUid ||
                !TryComp<LightBulbComponent>(bulbUid, out var bulbComponent) ||
                bulbComponent.State != LightBulbState.Normal)
                continue;

            ruleEntity.Comp.Lights.Add(lightUid);
            var nightshiftLightComponent = AddComp<NightshiftLightComponent>(lightUid);
            nightshiftLightComponent.OwningRuleUid = ruleEntity;

            ruleEntity.Comp.Bulbs.Add(bulbUid);
            var nightshiftBulbComponent = AddComp<NightshiftBulbComponent>(bulbUid);
            nightshiftBulbComponent.OwningRuleUid = ruleEntity;
            nightshiftBulbComponent.OriginalColor = bulbComponent.Color;

            _bulbSystem.SetColor(bulbUid, ruleEntity.Comp.Color, bulb: bulbComponent);
            _poweredLightSystem.UpdateLightWithBulb((lightUid, poweredLightComponent), (bulbUid, bulbComponent));
        }
    }

    private void Disable(Entity<NightshiftRuleComponent> ruleEntity)
    {
        foreach (var bulbUid in ruleEntity.Comp.Bulbs)
        {
            var nightshiftBulbComponent = _nightshiftBulbQuery.GetComponent(bulbUid);
            nightshiftBulbComponent.OwningRuleUid = null;

            _bulbSystem.SetColor(bulbUid, nightshiftBulbComponent.OriginalColor);
            _poweredLightSystem.UpdateLight(Transform(bulbUid).ParentUid);

            RemComp(bulbUid, nightshiftBulbComponent);
        }
        ruleEntity.Comp.Bulbs.Clear();

        foreach (var lightUid in ruleEntity.Comp.Lights)
        {
            var nightshiftLightComponent = _nightshiftLightQuery.GetComponent(lightUid);
            nightshiftLightComponent.OwningRuleUid = null;

            RemComp(lightUid, nightshiftLightComponent);
        }
        ruleEntity.Comp.Lights.Clear();

        ruleEntity.Comp.Enabled = false;
    }

    protected override void Started(EntityUid ruleUid, NightshiftRuleComponent ruleComponent, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(ruleUid, ruleComponent, gameRule, args);

        // Allow overwriting it
        EntityUid? stationUid = ruleComponent.StationUid;
        if (stationUid == EntityUid.Invalid)
        {
            var ineligibleStations = new HashSet<EntityUid>();
            var ruleQuery = EntityQueryEnumerator<NightshiftRuleComponent>();
            while (ruleQuery.MoveNext(out var otherRuleComponent))
            {
                if (otherRuleComponent.StationUid == EntityUid.Invalid)
                    continue;

                ineligibleStations.Add(otherRuleComponent.StationUid);
            }

            if (!TryGetRandomStation(out stationUid, filter: (uid) => !ineligibleStations.Contains(uid)))
                return;
        }

        ruleComponent.StationUid = stationUid.Value;
        Enable((ruleUid, ruleComponent));
    }

    protected override void Ended(EntityUid ruleUid, NightshiftRuleComponent ruleComponent, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(ruleUid, ruleComponent, gameRule, args);

        if (ruleComponent.Enabled)
            Disable((ruleUid, ruleComponent));

        ruleComponent.StationUid = EntityUid.Invalid;
    }
}
