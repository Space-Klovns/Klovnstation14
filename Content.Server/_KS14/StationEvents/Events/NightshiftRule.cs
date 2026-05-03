using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Content.Server.StationEvents.Events;
using Content.Server._KS14.StationEvents.Components;
using Content.Shared.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Server.AlertLevel;
using Robust.Shared.Collections;

namespace Content.Server._KS14.StationEvents.Events;

public sealed class NightshiftRule : StationEventSystem<NightshiftRuleComponent>
{
    [Dependency] private readonly PoweredLightSystem _poweredLightSystem = default!;
    [Dependency] private readonly LightBulbSystem _bulbSystem = default!;

    private EntityQuery<StationMemberComponent> _stationMemberQuery;
    private EntityQuery<NightshiftBulbComponent> _nightshiftBulbQuery;

    public override void Initialize()
    {
        base.Initialize();

        _stationMemberQuery = GetEntityQuery<StationMemberComponent>();
        _nightshiftBulbQuery = GetEntityQuery<NightshiftBulbComponent>();

        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
        SubscribeLocalEvent<NightshiftBulbComponent, ComponentShutdown>(OnNightshiftBulbShutdown);
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

    private void OnNightshiftBulbShutdown(Entity<NightshiftBulbComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.OwningRuleUid is not { })
            return;

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

            ruleEntity.Comp.Lights.Add(bulbUid);

            var nightshiftBulbComponent = AddComp<NightshiftBulbComponent>(bulbUid);
            nightshiftBulbComponent.OwningRuleUid = ruleEntity;
            nightshiftBulbComponent.OriginalColor = bulbComponent.Color;

            _bulbSystem.SetColor(bulbUid, ruleEntity.Comp.Color, bulb: bulbComponent);
            _poweredLightSystem.UpdateLightWithBulb((lightUid, poweredLightComponent), (bulbUid, bulbComponent));
        }
    }

    private void Disable(Entity<NightshiftRuleComponent> ruleEntity)
    {
        foreach (var bulbUid in ruleEntity.Comp.Lights)
        {
            var nightshiftBulbComponent = _nightshiftBulbQuery.GetComponent(bulbUid);
            nightshiftBulbComponent.OwningRuleUid = null;

            _bulbSystem.SetColor(bulbUid, nightshiftBulbComponent.OriginalColor);
            _poweredLightSystem.UpdateLight(Transform(bulbUid).ParentUid);

            RemComp(bulbUid, nightshiftBulbComponent);
        }

        ruleEntity.Comp.Lights.Clear();
        ruleEntity.Comp.Enabled = false;
    }

    protected override void Started(EntityUid ruleUid, NightshiftRuleComponent ruleComponent, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(ruleUid, ruleComponent, gameRule, args);

        var ineligibleStations = new HashSet<EntityUid>();
        var ruleQuery = EntityQueryEnumerator<NightshiftRuleComponent>();
        while (ruleQuery.MoveNext(out var otherRuleComponent))
        {
            if (ruleComponent.StationUid == EntityUid.Invalid)
                continue;

            ineligibleStations.Add(otherRuleComponent.StationUid);
        }

        if (!TryGetRandomStation(out var stationUid, filter: (uid) => ineligibleStations.Contains(uid)))
            return;

        ruleComponent.StationUid = stationUid.Value;
        Enable((ruleUid, ruleComponent));
    }

    protected override void Ended(EntityUid ruleUid, NightshiftRuleComponent ruleComponent, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(ruleUid, ruleComponent, gameRule, args);

        if (ruleComponent.Enabled)
            Disable((ruleUid, ruleComponent));
    }
}
