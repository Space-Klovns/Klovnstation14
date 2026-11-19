using Content.Server._KS14.Mortar;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Mortar crews

    /// <summary>
    ///     A crewman that has set its mortar up stays with it: no more orders to go anywhere else.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnUnitMortarDeployed(Entity<KsFieldCommanderUnitComponent> entity, ref KsMortarDeployedEvent args)
    {
        entity.Comp.GiveOrders = false;
        entity.Comp.OrderCoordinates = Transform(args.MortarUid).Coordinates;
    }

    /// <summary>
    ///     One whose mortar is gone fights on as a plain operative: sent at the defenders again, and right up to them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnUnitMortarLost(Entity<KsFieldCommanderUnitComponent> entity, ref KsMortarLostEvent args)
    {
        entity.Comp.IgnoreStandOff = true;
        entity.Comp.GiveOrders = !TryComp<KsFieldCommanderRuleComponent>(entity.Comp.RuleUid, out var ruleComponent) ||
                                 entity.Comp.AssetIndex >= ruleComponent.Assets.Count ||
                                 ruleComponent.Assets[entity.Comp.AssetIndex].GiveOrders;
    }

    #endregion
}
