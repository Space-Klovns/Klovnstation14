using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Alert.Components;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     Shows how much qi a cultivator holds on their qi alert.
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
}
