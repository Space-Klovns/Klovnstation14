using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Alert.Components;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     Fills in the cultivation counter alerts: qi, a body cultivator's exhaustion and banked tempering, and the steps
///         left before a Five-Point Exploding Heart bursts.
/// </summary>
public sealed partial class CultivationAlertSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnGetCounterAmount(Entity<CultivatorComponent> entity, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled || args.Alert.ID != entity.Comp.QiAlert)
            return;

        args.Amount = (int)entity.Comp.Qi;
    }

    [SubscribeLocalEvent]
    private void OnGetBodyCounterAmount(Entity<BodyCultivatorComponent> entity, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled)
            return;

        if (args.Alert.ID == entity.Comp.ExhaustionAlert)
            args.Amount = (int)entity.Comp.Exhaustion;
        else if (args.Alert.ID == entity.Comp.TemperingAlert)
            args.Amount = (int)entity.Comp.Tempering;
    }

    [SubscribeLocalEvent]
    private void OnGetHeartCounterAmount(Entity<ExplodingHeartComponent> entity, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled || args.Alert.ID != entity.Comp.Alert)
            return;

        args.Amount = entity.Comp.StepsLeft;
    }
}
