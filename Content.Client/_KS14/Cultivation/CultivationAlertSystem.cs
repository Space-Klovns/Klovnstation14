using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Alert.Components;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     Fills in the cultivation counter alerts: qi, and a body cultivator's exhaustion and banked tempering.
/// </summary>
public sealed partial class CultivationAlertSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnGetCounterAmount(Entity<CultivatorComponent> entity, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled || args.Alert.ID != SharedCultivationSystem.QiAlert)
            return;

        args.Amount = (int)entity.Comp.Qi;
    }

    [SubscribeLocalEvent]
    private void OnGetBodyCounterAmount(Entity<BodyCultivatorComponent> entity, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled)
            return;

        if (args.Alert.ID == SharedBodyCultivationSystem.ExhaustionAlert)
            args.Amount = (int)entity.Comp.Exhaustion;
        else if (args.Alert.ID == SharedBodyCultivationSystem.TemperingAlert)
            args.Amount = (int)entity.Comp.Tempering;
    }
}
